using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Tests.Support;

namespace Vortex.PacketHandlers.Tests;

/// <summary>
/// What every word-filter handler test needs: a session that records what it was sent, a grain
/// factory whose grains record what they were asked, and a filter that answers as told.
/// </summary>
public abstract class WordFilterHandlerFixture
{
    /// <summary>The player every <see cref="Context"/> belongs to.</summary>
    protected const int PlayerId = 501;

    protected List<object> Sent { get; } = [];

    protected List<ProxyCall> GrainCalls { get; } = [];

    protected List<ProxyCall> FilterCalls { get; } = [];

    protected MessageContext Context() =>
        new(
            FakeProxy.Create<ISessionContext>(call =>
            {
                if (call.Method.Name == nameof(ISessionContext.SendComposerAsync))
                {
                    Sent.Add(call.Args![0]!);
                }

                return null;
            }),
            Primitives.Players.PlayerId.Parse(PlayerId),
            new RoomId(0)
        );

    /// <summary>
    /// Every grain the handler asks for is a proxy recording its calls into <see cref="GrainCalls"/>.
    /// <paramref name="answer"/> answers the calls a test cares about by method name.
    /// </summary>
    protected IGrainFactory Grains(Func<ProxyCall, object?>? answer = null) =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain) && call.Method.IsGenericMethod
                ? FakeProxy.CreateFor(
                    call.Method.GetGenericArguments()[0],
                    inner =>
                    {
                        GrainCalls.Add(inner);

                        return answer?.Invoke(inner);
                    }
                )
                : null
        );

    protected static IConfiguration Config() => new ConfigurationBuilder().Build();

    protected IWordFilter Refusing() => Filter(text => new WordFilterResult(text, true, false));

    protected IWordFilter Replacing(string with) =>
        Filter(_ => new WordFilterResult(with, false, true));

    /// <summary>Refuses exactly <paramref name="word"/>, passes anything else untouched.</summary>
    protected IWordFilter RefusingOnly(string word) =>
        Filter(text => new WordFilterResult(text, text == word, false));

    protected IWordFilter PassingEverything() =>
        Filter(text => new WordFilterResult(text, false, false));

    protected IWordFilter Filter(Func<string, WordFilterResult> answer) =>
        FakeProxy.Create<IWordFilter>(call =>
        {
            FilterCalls.Add(call);

            return call.Method.Name switch
            {
                nameof(IWordFilter.Apply) or nameof(IWordFilter.Test) => answer(
                    (string)call.Args![0]!
                ),
                "get_RefusedMessage" => "Interdit.",
                _ => null,
            };
        });
}
