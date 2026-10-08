namespace Vortex.Permissions.Configuration;

public sealed class PermissionsConfig
{
    public const string SECTION_NAME = "Vortex:Permissions";

    /// <summary>
    /// Optional account id granted the <c>owner</c> role at startup, so the first administrator
    /// exists before anyone can assign a role. 0 disables it; the grant is idempotent and
    /// additive-only.
    /// </summary>
    /// <remarks>
    /// An id, not an e-mail: registration is open and addresses are not verified, so whoever
    /// registered the configured address first -- before its real owner -- became owner. An id is
    /// read off an account that already exists, after its owner created it.
    /// </remarks>
    public int BootstrapOwnerAccountId { get; init; }
}
