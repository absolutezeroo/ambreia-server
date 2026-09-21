using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Hotel.Contracts;

/// <summary>
/// The files the client downloads at boot.
/// </summary>
/// <param name="Available">False when no asset root is configured, which is why the page can show
/// "nothing to edit here" rather than an empty table that looks like a loss.</param>
public sealed record GamedataFileList(bool Available, IReadOnlyList<GamedataFileRow> Files);
