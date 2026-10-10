using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Pipeline.Attributes;
using Vortex.Pipeline.Delegates;
using Vortex.Pipeline.Tests.Fixtures;
using Vortex.Runtime;
using Xunit;

namespace Vortex.Pipeline.Tests;

/// <summary>
/// TEST-01 safety net for the generic dispatch engine in <see cref="EnvelopeHost{TEnvelope,TMeta,TContext}"/>
/// (shared by Vortex.Messages and Vortex.Events), exercised directly with locally-defined
/// envelope/context types rather than any real Vortex message.
/// </summary>
public sealed class EnvelopeHostTests
{
    private static ServiceProvider NewProvider(
        Recorder recorder,
        ConcurrencyTracker? tracker = null
    )
    {
        ServiceCollection services = new();
        services.AddSingleton(recorder);

        if (tracker is not null)
        {
            services.AddSingleton(tracker);
        }

        return services.BuildServiceProvider();
    }

    private static EnvelopeHost<TestEnvelope, string, TestContext> NewHost(
        IServiceProvider sp,
        bool enableInheritance = true,
        HandlerExecutionMode mode = HandlerExecutionMode.Parallel,
        int? maxDop = null,
        Action<Exception, object>? onHandlerActivationError = null,
        Action<Exception, object>? onHandlerInvokeError = null,
        Action<TestEnvelope>? onNoHandlerRegistered = null,
        Action<Exception, object>? onBehaviorError = null,
        bool behaviorErrorsFailClosed = false
    )
    {
        EnvelopeHostOptions<TestEnvelope, string, TestContext> options = new()
        {
            CreateContextAsync = (env, meta) => Task.FromResult(new TestContext()),
            EnableInheritanceDispatch = enableInheritance,
            HandlerMode = mode,
            MaxHandlerDegreeOfParallelism = maxDop,
            OnHandlerActivationError = onHandlerActivationError,
            OnHandlerInvokeError = onHandlerInvokeError,
            OnBehaviorActivationError = onBehaviorError,
            OnBehaviorInvokeError = onBehaviorError,
            OnNoHandlerRegistered = onNoHandlerRegistered,
            BehaviorErrorsFailClosed = behaviorErrorsFailClosed,
        };

        return new EnvelopeHost<TestEnvelope, string, TestContext>(sp, options);
    }

    private static IDisposable RegisterHandler<THandler>(
        EnvelopeHost<TestEnvelope, string, TestContext> host,
        IServiceProvider sp,
        Type envType
    )
        where THandler : class
    {
        EnvelopeInvokerFactory<TestContext> factory = new();
        HandlerInvoker<TestContext> invoker = factory.CreateHandlerInvoker(
            typeof(THandler),
            envType
        );
        Func<IServiceProvider, object> activator = ActivatorHelpers.BuildActivator(
            typeof(THandler)
        );

        return host.RegisterHandler(envType, sp, activator, invoker);
    }

    private static IDisposable RegisterBehavior<TBehavior>(
        EnvelopeHost<TestEnvelope, string, TestContext> host,
        IServiceProvider sp,
        Type envType
    )
        where TBehavior : class
    {
        EnvelopeInvokerFactory<TestContext> factory = new();
        BehaviorInvoker<TestContext> invoker = factory.CreateBehaviorInvoker(
            typeof(TBehavior),
            envType
        );
        Func<IServiceProvider, object> activator = ActivatorHelpers.BuildActivator(
            typeof(TBehavior)
        );
        int order = typeof(TBehavior).GetCustomAttribute<OrderAttribute>()?.Value ?? 0;

        return host.RegisterBehavior(envType, sp, activator, invoker, order);
    }

    [Fact]
    public async Task RegisterHandler_PublishedEnvelope_IsReceivedByHandler()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(sp);
        using IDisposable reg = RegisterHandler<RecordingHandlerA>(host, sp, typeof(TestEnvelope));

        await host.PublishAsync(
                new TestEnvelope { Payload = "hello" },
                null,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        recorder.Entries.Should().Equal("A:hello");
    }

    [Fact]
    public void DefaultHandlerMode_IsParallel()
    {
        EnvelopeHostOptions<TestEnvelope, string, TestContext> options = new()
        {
            CreateContextAsync = (env, meta) => Task.FromResult(new TestContext()),
        };

        options.HandlerMode.Should().Be(HandlerExecutionMode.Parallel);
    }

    [Fact]
    public async Task MultipleHandlersForSameEnvelopeType_AllRun_UnderTheDefaultParallelMode()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(sp);
        using IDisposable regA = RegisterHandler<RecordingHandlerA>(host, sp, typeof(TestEnvelope));
        using IDisposable regB = RegisterHandler<RecordingHandlerB>(host, sp, typeof(TestEnvelope));

        await host.PublishAsync(new TestEnvelope { Payload = "x" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        recorder.Entries.Should().BeEquivalentTo(["A:x", "B:x"]);
    }

    [Fact]
    public async Task SequentialMode_HandlersRunInRegistrationOrder()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            mode: HandlerExecutionMode.Sequential
        );
        using IDisposable regB = RegisterHandler<RecordingHandlerB>(host, sp, typeof(TestEnvelope));
        using IDisposable regA = RegisterHandler<RecordingHandlerA>(host, sp, typeof(TestEnvelope));

        await host.PublishAsync(new TestEnvelope { Payload = "y" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        recorder.Entries.Should().Equal("B:y", "A:y");
    }

    [Fact]
    public async Task Behaviors_RunInOrderAttributeSequence_BeforeHandlers()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(sp);

        // Registered deliberately out of numeric order to prove the pipeline sorts by
        // [Order], not by registration order.
        using IDisposable regLow = RegisterBehavior<LowPriorityBehavior>(
            host,
            sp,
            typeof(TestEnvelope)
        );
        using IDisposable regUnordered = RegisterBehavior<UnorderedBehavior>(
            host,
            sp,
            typeof(TestEnvelope)
        );
        using IDisposable regHigh = RegisterBehavior<HighPriorityBehavior>(
            host,
            sp,
            typeof(TestEnvelope)
        );
        using IDisposable regHandler = RegisterHandler<RecordingHandlerA>(
            host,
            sp,
            typeof(TestEnvelope)
        );

        await host.PublishAsync(new TestEnvelope { Payload = "z" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        recorder
            .Entries.Should()
            .Equal(
                "Behavior(-5):before",
                "Behavior(0):before",
                "Behavior(10):before",
                "A:z",
                "Behavior(10):after",
                "Behavior(0):after",
                "Behavior(-5):after"
            );
    }

    [Fact]
    public async Task DisposingTheRegistration_UnregistersTheHandler_StopsFurtherInvocations()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(sp);
        IDisposable reg = RegisterHandler<RecordingHandlerA>(host, sp, typeof(TestEnvelope));

        await host.PublishAsync(new TestEnvelope { Payload = "1" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        reg.Dispose();

        await host.PublishAsync(new TestEnvelope { Payload = "2" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        recorder.Entries.Should().Equal("A:1");
    }

    [Fact]
    public async Task EnableInheritanceDispatch_True_BaseHandlerReceivesDerivedEnvelope()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(sp, enableInheritance: true);
        using IDisposable reg = RegisterHandler<RecordingHandlerA>(host, sp, typeof(TestEnvelope));

        await host.PublishAsync(
                new DerivedTestEnvelope { Payload = "d" },
                null,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        recorder.Entries.Should().Equal("A:d");
    }

    [Fact]
    public async Task EnableInheritanceDispatch_False_BaseHandlerDoesNotReceiveDerivedEnvelope()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            enableInheritance: false
        );
        using IDisposable reg = RegisterHandler<RecordingHandlerA>(host, sp, typeof(TestEnvelope));

        await host.PublishAsync(
                new DerivedTestEnvelope { Payload = "d" },
                null,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        recorder.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task OnHandlerActivationError_Fires_AndOtherHandlersStillRun()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        List<Exception> activationErrors = [];
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            onHandlerActivationError: (ex, env) => activationErrors.Add(ex)
        );

        EnvelopeInvokerFactory<TestContext> factory = new();
        HandlerInvoker<TestContext> invoker = factory.CreateHandlerInvoker(
            typeof(RecordingHandlerA),
            typeof(TestEnvelope)
        );
        Func<IServiceProvider, object> throwingActivator = _ =>
            throw new InvalidOperationException("cannot activate");
        using IDisposable badReg = host.RegisterHandler(
            typeof(TestEnvelope),
            sp,
            throwingActivator,
            invoker
        );
        using IDisposable goodReg = RegisterHandler<RecordingHandlerB>(
            host,
            sp,
            typeof(TestEnvelope)
        );

        await host.PublishAsync(new TestEnvelope { Payload = "e" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        activationErrors.Should().ContainSingle();
        activationErrors[0].Should().BeOfType<InvalidOperationException>();
        recorder.Entries.Should().Equal("B:e");
    }

    [Fact]
    public async Task OnHandlerInvokeError_Fires_AndOtherHandlersStillRun()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        List<Exception> invokeErrors = [];
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            onHandlerInvokeError: (ex, env) => invokeErrors.Add(ex)
        );

        using IDisposable throwingReg = RegisterHandler<ThrowingHandler>(
            host,
            sp,
            typeof(TestEnvelope)
        );
        using IDisposable goodReg = RegisterHandler<RecordingHandlerA>(
            host,
            sp,
            typeof(TestEnvelope)
        );

        await host.PublishAsync(new TestEnvelope { Payload = "f" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        invokeErrors.Should().ContainSingle();
        invokeErrors[0].Should().BeOfType<ThrowingHandler.BoomException>();
        recorder.Entries.Should().Equal("A:f");
    }

    [Fact]
    public async Task OnNoHandlerRegistered_FiresWhenTheBucketExistsButHasNoHandlers()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        List<TestEnvelope> noHandlerEnvelopes = [];
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            onNoHandlerRegistered: env => noHandlerEnvelopes.Add(env)
        );

        // Registering only a behavior still creates the envelope-type bucket, with zero handlers.
        using IDisposable behaviorReg = RegisterBehavior<UnorderedBehavior>(
            host,
            sp,
            typeof(TestEnvelope)
        );

        TestEnvelope published = new() { Payload = "none" };

        await host.PublishAsync(published, null, CancellationToken.None).ConfigureAwait(true);

        noHandlerEnvelopes.Should().ContainSingle();
        noHandlerEnvelopes[0].Should().BeSameAs(published);
        recorder.Entries.Should().Equal("Behavior(0):before", "Behavior(0):after");
    }

    [Fact]
    public async Task Publish_WithNothingRegisteredForTheEnvelopeType_ReturnsContext_WithoutThrowing()
    {
        using ServiceProvider sp = NewProvider(new Recorder());
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(sp);

        TestContext ctx = await host.PublishWithContextAsync(
                new TestEnvelope { Payload = "none" },
                null,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        ctx.Should().NotBeNull();
    }

    [Fact]
    public async Task MaxHandlerDegreeOfParallelism_BoundsConcurrentHandlerExecutions()
    {
        Recorder recorder = new();
        ConcurrencyTracker tracker = new();
        using ServiceProvider sp = NewProvider(recorder, tracker);
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(sp, maxDop: 2);

        CompositeDisposable regs = new();
        for (int i = 0; i < 6; i++)
        {
            regs.Add(RegisterHandler<ConcurrencyTrackingHandler>(host, sp, typeof(TestEnvelope)));
        }

        await host.PublishAsync(new TestEnvelope { Payload = "p" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        tracker.Max.Should().BeLessThanOrEqualTo(2);
        tracker
            .Max.Should()
            .BeGreaterThan(1, "degree 2 should allow real overlap, not fully serialize the work");

        regs.Dispose();
    }

    [Fact]
    public async Task ByDefault_AThrowingBehavior_IsSkipped_AndTheHandlerStillRuns()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        List<Exception> errors = [];
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            onBehaviorError: (ex, env) => errors.Add(ex)
        );

        using IDisposable behaviorReg = RegisterBehavior<ThrowingBehavior>(
            host,
            sp,
            typeof(TestEnvelope)
        );
        using IDisposable handlerReg = RegisterHandler<RecordingHandlerA>(
            host,
            sp,
            typeof(TestEnvelope)
        );

        await host.PublishAsync(new TestEnvelope { Payload = "open" }, null, CancellationToken.None)
            .ConfigureAwait(true);

        errors.Should().ContainSingle().Which.Should().BeOfType<ThrowingBehavior.BoomException>();
        recorder.Entries.Should().Equal("A:open");
    }

    [Fact]
    public async Task FailClosed_AThrowingBehavior_RefusesTheEnvelope()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        List<Exception> errors = [];
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            onBehaviorError: (ex, env) => errors.Add(ex),
            behaviorErrorsFailClosed: true
        );

        using IDisposable behaviorReg = RegisterBehavior<ThrowingBehavior>(
            host,
            sp,
            typeof(TestEnvelope)
        );
        using IDisposable handlerReg = RegisterHandler<RecordingHandlerA>(
            host,
            sp,
            typeof(TestEnvelope)
        );

        await host.PublishAsync(
                new TestEnvelope { Payload = "closed" },
                null,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        errors.Should().ContainSingle().Which.Should().BeOfType<ThrowingBehavior.BoomException>();
        recorder.Entries.Should().BeEmpty("a gate that cannot decide refuses the command");
    }

    [Fact]
    public async Task FailClosed_ABehaviorThatCannotActivate_RefusesTheEnvelope()
    {
        Recorder recorder = new();
        using ServiceProvider sp = NewProvider(recorder);
        List<Exception> errors = [];
        EnvelopeHost<TestEnvelope, string, TestContext> host = NewHost(
            sp,
            onBehaviorError: (ex, env) => errors.Add(ex),
            behaviorErrorsFailClosed: true
        );

        EnvelopeInvokerFactory<TestContext> factory = new();
        BehaviorInvoker<TestContext> invoker = factory.CreateBehaviorInvoker(
            typeof(UnorderedBehavior),
            typeof(TestEnvelope)
        );
        Func<IServiceProvider, object> throwingActivator = _ =>
            throw new InvalidOperationException("cannot activate");
        using IDisposable behaviorReg = host.RegisterBehavior(
            typeof(TestEnvelope),
            sp,
            throwingActivator,
            invoker,
            0
        );
        using IDisposable handlerReg = RegisterHandler<RecordingHandlerA>(
            host,
            sp,
            typeof(TestEnvelope)
        );

        await host.PublishAsync(
                new TestEnvelope { Payload = "closed" },
                null,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        errors.Should().ContainSingle().Which.Should().BeOfType<InvalidOperationException>();
        recorder.Entries.Should().BeEmpty();
    }
}
