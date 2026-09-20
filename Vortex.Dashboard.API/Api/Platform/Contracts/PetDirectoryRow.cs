namespace Vortex.Dashboard.API.Api.Platform.Contracts;

/// <summary>
/// One pet species in the picker.
/// </summary>
/// <remarks>
/// The hotel has no name for a species — there is no table for one and the client's texts carry
/// none — so until the picture loads, "Species 17" is genuinely everything that can be said about
/// it. That is what <paramref name="ImageUrl"/> is for, and why this row exists instead of the
/// shared one.
/// </remarks>
/// <param name="ImageUrl">Null when the hotel has no pet renderer configured.</param>
public sealed record PetDirectoryRow(int Id, string Value, string Name, string? ImageUrl);
