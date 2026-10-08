using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Vortex.Tests.Support;

/// <summary>
/// Interface stub for the wide grain interfaces. Hand-writing a fake for something like
/// <c>IPlayerGrain</c> means thirty members of which a test cares about two, so the handler answers
/// the calls it recognises and everything else falls through to a harmless default for its return
/// type (a completed task, a zeroed struct, null).
/// </summary>
// Not sealed: DispatchProxy generates a subclass at runtime.
public class FakeProxy : DispatchProxy
{
    private Func<ProxyCall, object?> _handler = _ => null;

    public static T Create<T>(Func<ProxyCall, object?> handler)
        where T : class
    {
        T proxy = Create<T, FakeProxy>();

        ((FakeProxy)(object)proxy)._handler = handler;

        return proxy;
    }

    /// <summary>Same thing when the interface is only known at runtime, which is what answering a
    /// generic lookup like <c>GetComponent&lt;T&gt;()</c> needs.</summary>
    public static object CreateFor(Type interfaceType, Func<ProxyCall, object?> handler)
    {
        // .NET ships more than one Create overload, so the two-type-parameter, no-argument one is
        // picked explicitly rather than by name.
        MethodInfo create =
            Array.Find(
                typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static),
                m =>
                    m.Name == nameof(DispatchProxy.Create)
                    && m.GetGenericArguments().Length == 2
                    && m.GetParameters().Length == 0
            ) ?? throw new InvalidOperationException("DispatchProxy.Create<T, TProxy>() moved.");

        object proxy =
            create.MakeGenericMethod(interfaceType, typeof(FakeProxy)).Invoke(null, null)
            ?? throw new InvalidOperationException($"Could not proxy {interfaceType.Name}.");

        ((FakeProxy)proxy)._handler = handler;

        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        // A null answer means "not one of the calls this test cares about", not "the result is
        // null" -- tests that need a null result never reach here because nothing asserts on it.
        return _handler(new ProxyCall(targetMethod, args))
            ?? DefaultValueFor(targetMethod.ReturnType);
    }

    private static object? DefaultValueFor(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Type resultType = returnType.GetGenericArguments()[0];

            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [GetDefault(resultType)]);
        }

        return GetDefault(returnType);
    }

    private static object? GetDefault(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? Activator.CreateInstance(type)
            : null;
}
