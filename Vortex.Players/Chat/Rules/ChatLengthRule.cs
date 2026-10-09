using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Vortex.Players.Configuration;
using Vortex.Primitives.Chat;

namespace Vortex.Players.Chat.Rules;

/// <summary>At most <see cref="ChatConfig.MaxLength"/> characters once cleaned (spec F3c §5);
/// nothing is cut silently.</summary>
internal sealed class ChatLengthRule(IOptions<ChatConfig> config) : IChatRule
{
    public const string TooLong = "too_long";

    private readonly int _maxLength = config.Value.MaxLength;

    public int Priority => 100;

    public ValueTask<string?> CheckAsync(
        ChatSend send,
        ChatSenderState state,
        CancellationToken ct
    ) => ValueTask.FromResult(send.Text.Length > _maxLength ? TooLong : null);
}
