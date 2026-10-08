using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;

namespace Vortex.Messages.Registry;

public sealed class MessageContext(
    ISessionContext session,
    PlayerId playerId,
    RoomId roomId,
    int accountId = 0
)
{
    private PlayerId _playerId = playerId;
    private RoomId _roomId = roomId;
    private ISessionContext _session = session;

    public PlayerId PlayerId => _playerId;
    public RoomId RoomId => _roomId;

    /// <summary>The account of a socket in the account phase (spec F3a §5.2), 0 otherwise; the
    /// <see cref="PlayerId"/> is then invalid.</summary>
    public int AccountId { get; } = accountId;
    public SessionKey SessionKey => _session.SessionKey;
    public string? RemoteIpAddress => _session.RemoteIpAddress;

    public ActionContext AsActionContext() =>
        new()
        {
            Origin = ActionOrigin.Player,
            SessionKey = SessionKey,
            PlayerId = PlayerId,
            RoomId = RoomId,
        };

    public async Task CloseSessionAsync() =>
        await _session.CloseSessionAsync().ConfigureAwait(false);

    public async Task SendComposerAsync(IComposer composer, CancellationToken ct) =>
        await _session.SendComposerAsync(composer, ct).ConfigureAwait(false);

    public void SetRevisionId(string production) => _session.SetRevisionId(production);

    public void SetupEncryption(byte[] key, bool setCryptoOut = false) =>
        _session.SetupEncryption(key, setCryptoOut);
}
