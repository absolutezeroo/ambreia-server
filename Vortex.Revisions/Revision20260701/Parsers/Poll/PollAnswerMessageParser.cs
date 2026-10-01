using System.Collections.Immutable;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Packets;
using Vortex.Protocol.Messages.Incoming.Poll;

namespace Vortex.Revisions.Revision20260701.Parsers.Poll;

internal class PollAnswerMessageParser : IParser
{
    public IMessageEvent Parse(IClientPacket packet)
    {
        int pollId = packet.PopInt();
        int questionId = packet.PopInt();

        // The client pushes the answer count, then that many strings -- one per picked choice, or a
        // single string for a free-text question.
        int answerCount = packet.PopCount(sizeof(ushort));
        ImmutableArray<string>.Builder answers = ImmutableArray.CreateBuilder<string>(answerCount);

        for (int i = 0; i < answerCount; i++)
        {
            answers.Add(packet.PopString());
        }

        return new PollAnswerMessage
        {
            PollId = pollId,
            QuestionId = questionId,
            Answers = answers.MoveToImmutable(),
        };
    }
}
