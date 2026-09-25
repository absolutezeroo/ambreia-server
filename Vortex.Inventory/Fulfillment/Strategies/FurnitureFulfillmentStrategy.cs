using System;
using System.Collections.Generic;
using Vortex.Furniture;
using Vortex.Logging;
using Vortex.Primitives;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Groups;
using Vortex.Primitives.Groups.Snapshots;

namespace Vortex.Inventory.Fulfillment.Strategies;

/// <summary>
/// Floor and wall furniture: one planned row per copy, with the guild layout baked in where it
/// belongs.
/// </summary>
/// <remarks>
/// The two rules here have each been a bug. The copies granted are the purchase quantity times the
/// product's own count — ignoring the second collapsed every bundle to one of each item. And only
/// string-array furni may carry a guild layout: stamping it onto anything else overwrites that
/// item's own stuff data with a badge.
/// </remarks>
internal sealed class FurnitureFulfillmentStrategy(IFurnitureDefinitionProvider definitions)
    : IFulfillmentStrategy
{
    public IReadOnlyList<ProductType> Handles { get; } = [ProductType.Floor, ProductType.Wall];

    public void Contribute(in FulfillmentRequest request, FulfillmentPlanBuilder into)
    {
        FurnitureDefinitionSnapshot def =
            definitions.TryGetDefinition(request.Product.FurniDefinitionId)
            ?? throw new VortexException(VortexErrorCodeEnum.FurnitureDefinitionNotFound);

        string? guildExtraData =
            request.GuildIdentity is not null && def.StuffDataType == StuffDataType.StringKey
                ? BuildGuildExtraData(request.GuildIdentity)
                : BuildDecorationExtraData(def.FurniCategory, request.Product.ExtraParam);

        int copies = request.Quantity * Math.Max(1, request.Product.Quantity);

        for (int i = 0; i < copies; i++)
        {
            into.AddFurniture(new PlannedFurniture(def.Id, guildExtraData));
        }
    }

    /// <summary>
    /// A wallpaper, floor or landscape carries its pattern code ("101", "1.10") as legacy stuff
    /// data: the inventory shows it and the room applies it from there (<c>FurniView.as</c> reads
    /// <c>stuffData.getLegacyString()</c> for categories 2/3/4). Without it every bought decoration
    /// was the same blank item.
    /// </summary>
    internal static string? BuildDecorationExtraData(FurnitureCategory category, string? extraParam)
    {
        if (
            category
                is not (
                    FurnitureCategory.WallPaper
                    or FurnitureCategory.Floor
                    or FurnitureCategory.Landscape
                )
            || string.IsNullOrEmpty(extraParam)
        )
        {
            return null;
        }

        ExtraData extraData = new(null);

        extraData.UpdateSection(ExtraDataSectionType.STUFF, new { Data = extraParam });

        return extraData.GetJsonString();
    }

    /// <summary>
    /// Serializes the guild layout into the item's extra-data blob, in the same "stuff" section
    /// shape the stuff-data factory reads back.
    /// </summary>
    /// <remarks>
    /// Baked at plan time because the client renders a guild furni's badge and recolours straight
    /// from this and never asks the server again — stamping it later would mean stamping it never.
    /// </remarks>
    internal static string BuildGuildExtraData(GuildFurniIdentitySnapshot identity)
    {
        ExtraData extraData = new(null);

        extraData.UpdateSection(
            ExtraDataSectionType.STUFF,
            new
            {
                Data = GuildFurniStuffData.Build(
                    identity.GroupId,
                    identity.BadgeCode,
                    identity.ColorOneHex,
                    identity.ColorTwoHex
                ),
            }
        );

        return extraData.GetJsonString();
    }
}
