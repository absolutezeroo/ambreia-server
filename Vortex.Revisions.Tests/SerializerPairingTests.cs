using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests;

/// <summary>
///     <see cref="EmittedComposerRegistrationTests"/> checks that every composer the server builds
///     has <i>a</i> serializer. This checks it has the <i>right</i> one.
///     <para>
///     <see cref="AbstractSerializer{T}"/> casts the composer to its own type parameter, so a map
///     entry that pairs one composer with another's serializer throws an
///     <c>InvalidCastException</c> the first time that message is sent. Two such pairings were live
///     in the tree when this test was written - <c>CampaignCalendarDoorOpened</c> registered against
///     the calendar-data serializer, and <c>UnseenItemsEvent</c> against the account-preferences
///     one - and both had survived review because neither serializer wrote anything yet: an empty
///     <c>Serialize</c> body reaches the cast but does nothing with the result, so the mistake only
///     surfaces once someone fills the body in.
///     </para>
///     <para>
///     The dedicated serializer existed in both cases. Nothing was missing; the wrong name was
///     simply typed into the map.
///     </para>
/// </summary>
public sealed class SerializerPairingTests
{
    private static readonly Rev Revision = new();

    [Fact]
    public void EverySerializerIsRegisteredAgainstTheComposerItActuallySerializes()
    {
        List<string> mismatches = [];

        foreach ((Type composerType, ISerializer serializer) in Revision.Serializers)
        {
            Type? handled = HandledComposerType(serializer.GetType());

            if (handled is null)
            {
                // Not an AbstractSerializer<T>; nothing to compare against.
                continue;
            }

            if (handled != composerType)
            {
                mismatches.Add(
                    $"{composerType.Name} is registered against "
                        + $"{serializer.GetType().Name}, which serializes {handled.Name}"
                );
            }
        }

        mismatches.Should().BeEmpty();
    }

    /// <summary>The T of the AbstractSerializer&lt;T&gt; this type ultimately derives from.</summary>
    private static Type? HandledComposerType(Type serializerType)
    {
        for (Type? type = serializerType; type is not null; type = type.BaseType)
        {
            if (
                type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(AbstractSerializer<>)
            )
            {
                return type.GetGenericArguments().Single();
            }
        }

        return null;
    }
}
