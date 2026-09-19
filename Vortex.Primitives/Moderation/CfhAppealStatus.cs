namespace Vortex.Primitives.Moderation;

/// <summary>
/// Where a reporter's appeal against their own closed report has got to. Numeric values match the
/// WIN63 client's constants on the report-status entry (class <c>_SafeCls_2648</c>, 0/1/2/3) and are
/// written straight onto the wire as a byte, so this enum is the wire vocabulary, not a translation
/// of it.
/// </summary>
/// <remarks>
/// The client only offers the appeal button when the report is closed, carries
/// <see cref="None"/>, and was closed <em>without</em> a sanction: an appeal here is the reporter
/// saying "you decided nothing happened and I disagree", never a sanctioned player contesting their
/// own ban.
/// </remarks>
public enum CfhAppealStatus
{
    /// <summary>Never appealed. The client hides the whole appeal half of the window on this value.</summary>
    None = 0,

    /// <summary>Appealed, nobody has looked yet — the row reads "appealed" instead of "decided".</summary>
    Pending = 1,

    /// <summary>Looked at again, and this time the report was upheld.</summary>
    ActionTaken = 2,

    /// <summary>Looked at again, and the original decision stands.</summary>
    NoAction = 3,
}
