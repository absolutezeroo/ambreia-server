using System.Collections.Generic;

namespace Vortex.Dashboard.API.Api.Catalogue.Contracts;

/// <summary>
/// Every gift-dialog choice, in the order the dialog shows them, plus whether the dialog is on at
/// all — it is off when no parcel resolves, and the page says so instead of letting an operator
/// wonder why the gift button does nothing.
/// </summary>
public sealed record GiftWrappingList(bool WrappingEnabled, IReadOnlyList<GiftWrappingRow> Items);
