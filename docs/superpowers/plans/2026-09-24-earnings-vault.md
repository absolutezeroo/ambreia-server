# Earnings Vault — Slice 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the client's Earnings window real: a single idempotent `EarnAsync` entry point, operator-routable per category, with Achievements and HC payday paying through it, and a working Claim / Claim All.

**Architecture:** Sources build an `IncomeEarning` (category, `CurrencyKind`, amount, source key) and call `IPlayerVaultGrain.EarnAsync`. The vault decides — from a live config switch and the currency — whether to keep it as an immutable row or pay the wallet directly, both idempotent on the source key. Claims hand each row to the `IVaultRewardSettler` registered for its reward type and mark it claimed. The client ping travels as a domain event to a protocol-aware handler, because `Vortex.Collectibles` is protocol-free.

**Tech Stack:** .NET 10, Orleans, EF Core (Pomelo MySQL; InMemory in tests), xUnit + FluentAssertions, `Vortex.Tests.Support` (`GrainActivationContext`, `FakeProxy`).

**Spec:** `docs/superpowers/specs/2026-09-24-earnings-vault-design.md`

## Global Constraints

- Wire truth is the AS3 client (`vortex-modern-client/sources/WIN63-202607011411-782849652/src/com/sulake/habbo/catalog/earnings/`). Category and reward type are single bytes; Claim All is the byte `0xFF` (= `-1`).
- `Vortex.Collectibles` stays protocol-free: no reference to `Vortex.Protocol`.
- Handlers stay orchestration-only; no DB access from handlers; composers go through `PlayerPresenceGrain.SendComposerAsync`.
- One type per file for new types (existing multi-record files such as `CollectibleEvents.cs` keep their convention).
- `source_key` ≤ 64 chars (it doubles as a `commerce_receipts.step_key`, `MaxLength(64)`).
- Rows are never deleted; claimed rows get `claimed_at`.
- A config key is declared only for a category that has a live source: this slice declares `earnings.achievements.to_vault` and `earnings.habbo_club.to_vault`, default `true`, group `Earnings`.
- `.cs` edits are reformatted by csharpier via the post-edit hook — re-read a file before the next `Edit`.
- Commit with `git commit -o <paths>` only (shared index). Never push.
- Never stop the running `Vortex.Main`.

## Review Focus

1. **Deposit replayed after claim** (a payday retried after the player already claimed it) → no second row, no second payment. Pinned in Task 6.
2. **Claim All with rows in several categories plus one product row** → all currency rows paid and marked, product row untouched, response `-1`/`true`. Pinned in Task 6.
3. **Operator flips a category to direct while rows are pending** → pending rows still claimable, new earnings go to the wallet, no notification. Pinned in Task 6.
4. **Non-vault currency (diamonds) from an achievement** → paid directly and exactly once across a retried level-up. Pinned in Task 6 (direct path idempotency) and Task 8.
5. **Claim with nothing pending / unknown category byte** → `false`, no exception, client button re-enabled. Pinned in Task 6.

---

### Task 1: Wire — Claim All and the full category enum

**Files:**
- Modify: `Vortex.Primitives/Vault/Enums/VaultRewardCategoryType.cs`
- Modify: `Vortex.Revisions/Revision20260701/Parsers/Vault/IncomeRewardClaimMessageParser.cs`
- Modify: `Vortex.Revisions/Revision20260701/Serializers/Vault/IncomeRewardClaimResponseMessageComposerSerializer.cs`
- Modify: `Vortex.Revisions/Revision20260701/Serializers/Vault/IncomeRewardStatusMessageComposerSerializer.cs` (same signed byte write for the category)
- Test: `Vortex.Revisions.Tests/Notifications/IncomeRewardClaimWireTests.cs`

**Interfaces:**
- Produces: `VaultRewardCategoryType.All = -1`, `Games = 11`, `WiredChest = 12`, `Agency = 13`.

- [ ] **Step 1: Write the failing test**

Look at a parser test for the harness shape first (`Vortex.Revisions.Tests/Avatar/PostureAndSignWireLayoutTests.cs:40-70` builds a client packet and calls `Revision.Parsers[header].Parse(packet)`); reuse its packet-building helper exactly.

```csharp
using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Vortex.Primitives.Vault.Enums;
using Vortex.Protocol.Messages.Incoming.Vault;
using Vortex.Protocol.Messages.Outgoing.Vault;
using Vortex.Revisions.Configuration;
using Xunit;
using Rev = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Tests.Notifications;

/// <summary>
/// The client's Claim All sends category -1 as one byte (EarningsView.windowProcedure,
/// _SafeCls_2320: new Byte(param1)) and reads the response category back with a signed readByte.
/// Read unsigned, it arrived as 255, matched no row, and the button re-enabled with nothing claimed.
/// </summary>
public sealed class IncomeRewardClaimWireTests
{
    private static readonly Rev Revision = new(Options.Create(new ProtocolLimitsConfig()));

    [Theory]
    [InlineData(0xFF, VaultRewardCategoryType.All)]
    [InlineData(0x02, VaultRewardCategoryType.Achievements)]
    [InlineData(0x0D, VaultRewardCategoryType.Agency)]
    public void Parser_ReadsTheCategorySigned(byte wire, VaultRewardCategoryType expected)
    {
        IncomeRewardClaimMessage message = (IncomeRewardClaimMessage)
            Revision
                .Parsers[Vortex.Revisions.Revision20260701.MessageEvent.IncomeRewardClaimMessageEvent]
                .Parse(ClientPacketWithBody(wire)); // helper copied from PostureAndSignWireLayoutTests

        message.Category.Should().Be(expected);
    }

    [Theory]
    [InlineData(VaultRewardCategoryType.All, "ff01")]
    [InlineData(VaultRewardCategoryType.HabboClub, "0400")]
    public void Response_WritesTheCategoryAsOneSignedByte(
        VaultRewardCategoryType category,
        string expectedBody
    )
    {
        bool result = category == VaultRewardCategoryType.All;

        byte[] frame = Revision
            .Serializers[typeof(IncomeRewardClaimResponseMessageComposer)]
            .Serialize(new IncomeRewardClaimResponseMessageComposer { RewardCategory = category, Result = result })
            .ToArray();

        Convert.ToHexString(frame.Skip(6).ToArray()).ToLowerInvariant().Should().Be(expectedBody);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Vortex.Revisions.Tests --filter FullyQualifiedName~IncomeRewardClaimWireTests`
Expected: compile error — `VaultRewardCategoryType.All` / `Agency` do not exist.

- [ ] **Step 3: Implement**

`VaultRewardCategoryType.cs`:

```csharp
namespace Vortex.Primitives.Vault.Enums;

/// <summary>
/// The Earnings window's rows, numbered as the client's EarningsView sends them. <see cref="All"/>
/// is its Claim All button: one byte, 0xFF, read back signed.
/// </summary>
public enum VaultRewardCategoryType
{
    All = -1,
    Tutorial = 0,
    DailyGift = 1,
    Achievements = 2,
    Marketplace = 3,
    HabboClub = 4,
    LevelProgression = 5,
    RoomBundleSales = 6,
    BonusBag = 7,
    Donation = 8,
    Suprise = 9,
    SnowStorm = 10,
    Games = 11,
    WiredChest = 12,
    Agency = 13,
}
```

Parser:

```csharp
internal class IncomeRewardClaimMessageParser : IParser
{
    // Signed: Claim All is -1 on the wire (0xFF), and every real category fits in 0..127.
    public IMessageEvent Parse(IClientPacket packet) =>
        new IncomeRewardClaimMessage
        {
            Category = (VaultRewardCategoryType)unchecked((sbyte)packet.PopByte()),
        };
}
```

Response serializer body:

```csharp
packet
    .WriteByte(unchecked((byte)(sbyte)message.RewardCategory))
    .WriteBoolean(message.Result);
```

Status serializer: replace `.WriteByte((byte)reward.RewardCategory)` with `.WriteByte(unchecked((byte)(sbyte)reward.RewardCategory))`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Vortex.Revisions.Tests --filter "FullyQualifiedName~IncomeReward"`
Expected: PASS (new tests and the existing `IncomeRewardNotificationWireTests`).

- [ ] **Step 5: Commit**

```bash
git add Vortex.Primitives/Vault/Enums/VaultRewardCategoryType.cs Vortex.Revisions/Revision20260701/Parsers/Vault/IncomeRewardClaimMessageParser.cs Vortex.Revisions/Revision20260701/Serializers/Vault/IncomeRewardClaimResponseMessageComposerSerializer.cs Vortex.Revisions/Revision20260701/Serializers/Vault/IncomeRewardStatusMessageComposerSerializer.cs Vortex.Revisions.Tests/Notifications/IncomeRewardClaimWireTests.cs
git commit -o <same paths> -m "earnings: Claim All reads -1 signed; categories 11-13"
```

---

### Task 2: Primitives — `IncomeEarning`, `VaultRewardRouting`, operation kinds

**Files:**
- Create: `Vortex.Primitives/Vault/IncomeEarning.cs`
- Create: `Vortex.Primitives/Vault/VaultRewardRouting.cs`
- Modify: `Vortex.Primitives/Commerce/CommerceOperation.cs` (enum `CommerceOperationKind`, class `CommerceStepKeys`)
- Test: `Vortex.Players.Tests/Vault/VaultRewardRoutingTests.cs`

**Interfaces:**
- Produces:
  - `sealed record IncomeEarning { VaultRewardCategoryType Category; CurrencyKind Currency; int Amount; string SourceKey; }` (Orleans-serializable, `MaxSourceKeyLength = 64`).
  - `static bool VaultRewardRouting.TryGetRewardType(CurrencyKind currency, out VaultRewardType type)`
  - `static CurrencyKind VaultRewardRouting.ToCurrencyKind(VaultRewardType type)` (throws for `Product`).
  - `CommerceOperationKind.VaultIncome = 22`, `CommerceOperationKind.VaultIncomeDirect = 23`.
  - `CommerceStepKeys.VAULT_CLAIM = "vault-claim"`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using FluentAssertions;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;
using Xunit;

namespace Vortex.Players.Tests.Vault;

public sealed class VaultRewardRoutingTests
{
    private static readonly CurrencyKind Credits = new() { CurrencyType = CurrencyType.Credits };
    private static readonly CurrencyKind Duckets = new()
    {
        CurrencyType = CurrencyType.ActivityPoints,
        ActivityPointType = 0,
    };
    private static readonly CurrencyKind Diamonds = new()
    {
        CurrencyType = CurrencyType.ActivityPoints,
        ActivityPointType = 5,
    };

    [Fact]
    public void Credits_AreRewardTypeOne()
    {
        VaultRewardRouting.TryGetRewardType(Credits, out VaultRewardType type).Should().BeTrue();
        type.Should().Be(VaultRewardType.Credits);
        ((int)type).Should().Be(1);
    }

    [Fact]
    public void Duckets_AreRewardTypeZero()
    {
        VaultRewardRouting.TryGetRewardType(Duckets, out VaultRewardType type).Should().BeTrue();
        type.Should().Be(VaultRewardType.Duckets);
    }

    [Fact]
    public void AnyOtherCurrency_HasNoVaultType()
    {
        VaultRewardRouting.TryGetRewardType(Diamonds, out _).Should().BeFalse();
        VaultRewardRouting
            .TryGetRewardType(new CurrencyKind { CurrencyType = CurrencyType.Emeralds }, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void RoundTrips_BackToTheWalletCurrency()
    {
        VaultRewardRouting.ToCurrencyKind(VaultRewardType.Credits).Should().Be(Credits);
        VaultRewardRouting.ToCurrencyKind(VaultRewardType.Duckets).Should().Be(Duckets);
    }

    [Fact]
    public void Product_IsNotACurrency()
    {
        Action act = () => VaultRewardRouting.ToCurrencyKind(VaultRewardType.Product);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~VaultRewardRoutingTests`
Expected: compile error — `VaultRewardRouting` does not exist.

- [ ] **Step 3: Implement**

`IncomeEarning.cs`:

```csharp
using Orleans;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Primitives.Vault;

/// <summary>
/// One thing a player earned, as the source that paid it describes it: which Earnings row it belongs
/// to, in the wallet's own currency vocabulary. The source never decides whether it waits in the
/// vault or lands in the wallet — <c>IPlayerVaultGrain.EarnAsync</c> does.
/// </summary>
/// <remarks>
/// <see cref="SourceKey"/> names the event that earned it (<c>club-payday:2026-09-01</c>) and is what
/// makes a retried deposit a no-op. It is also used as a commerce receipt step key, hence the length
/// cap.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record IncomeEarning
{
    public const int MaxSourceKeyLength = 64;

    [Id(0)]
    public required VaultRewardCategoryType Category { get; init; }

    [Id(1)]
    public required CurrencyKind Currency { get; init; }

    [Id(2)]
    public required int Amount { get; init; }

    [Id(3)]
    public required string SourceKey { get; init; }
}
```

`VaultRewardRouting.cs`:

```csharp
using System;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Primitives.Vault;

/// <summary>
/// Which wallet currencies the Earnings window can hold, and the reward type the client reads for
/// each. The client knows exactly two (EarningsView.onIncomeRewardDataReceived: 0 = duckets,
/// 1 = credits); every other currency is paid straight to the wallet instead.
/// </summary>
public static class VaultRewardRouting
{
    private const int DucketsActivityPointType = 0;

    public static bool TryGetRewardType(CurrencyKind currency, out VaultRewardType type)
    {
        if (currency.CurrencyType == CurrencyType.Credits)
        {
            type = VaultRewardType.Credits;
            return true;
        }

        if (
            currency.CurrencyType == CurrencyType.ActivityPoints
            && currency.ActivityPointType == DucketsActivityPointType
        )
        {
            type = VaultRewardType.Duckets;
            return true;
        }

        type = default;
        return false;
    }

    public static CurrencyKind ToCurrencyKind(VaultRewardType type) =>
        type switch
        {
            VaultRewardType.Credits => new CurrencyKind { CurrencyType = CurrencyType.Credits },
            VaultRewardType.Duckets => new CurrencyKind
            {
                CurrencyType = CurrencyType.ActivityPoints,
                ActivityPointType = DucketsActivityPointType,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a currency."),
        };
}
```

In `CommerceOperation.cs`, append to `CommerceOperationKind` after `NftClaim = 21,`:

```csharp
    /// <summary>
    /// Cashing one Earnings row. The id is derived from the row, so a claim retried after a crash
    /// between the credit and the claimed mark finds its receipt and only marks.
    /// </summary>
    VaultIncome = 22,

    /// <summary>
    /// An earning paid straight to the wallet — a currency the Earnings window cannot hold, or a
    /// category the operator routed direct. Derived from the player; the step is the source key.
    /// </summary>
    VaultIncomeDirect = 23,
```

and in `CommerceStepKeys`:

```csharp
    /// <summary>Paying one claimed Earnings row.</summary>
    public const string VAULT_CLAIM = "vault-claim";
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~VaultRewardRoutingTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git commit -o Vortex.Primitives/Vault/IncomeEarning.cs Vortex.Primitives/Vault/VaultRewardRouting.cs Vortex.Primitives/Commerce/CommerceOperation.cs Vortex.Players.Tests/Vault/VaultRewardRoutingTests.cs -m "earnings: IncomeEarning and the vault's currency routing"
```

(`git add` the two new files and the test first.)

---

### Task 3: Database — `source_key`, `claimed_at`, unique index

**Files:**
- Modify: `Vortex.Database/Entities/Players/PlayerVaultIncomeRewardEntity.cs`
- Create: `Vortex.Database/Migrations/<timestamp>_AddVaultIncomeSourceKey.cs` (+ `.Designer.cs`, snapshot) via the EF script

**Interfaces:**
- Produces: `PlayerVaultIncomeRewardEntity.SourceKey` (string, ≤64), `PlayerVaultIncomeRewardEntity.ClaimedAt` (`DateTime?`); unique index `(player_id, source_key)`.

- [ ] **Step 1: Edit the entity**

```csharp
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Vortex.Primitives.Vault;

namespace Vortex.Database.Entities.Players;

/// <summary>
/// One earning in a player's Earnings window. Immutable once written, and never deleted: a claim
/// sets <see cref="ClaimedAt"/>. Deleting would drop <see cref="SourceKey"/> from the unique index,
/// and a deposit replayed after the claim would then be paid a second time.
/// </summary>
[Table("player_vault_income_rewards")]
[Index(nameof(PlayerEntityId), nameof(SourceKey), IsUnique = true)]
public class PlayerVaultIncomeRewardEntity : VortexEntity
{
    [Column("player_id")]
    public int PlayerEntityId { get; set; }

    [Column("reward_category")]
    public int RewardCategory { get; set; }

    [Column("reward_type")]
    public int RewardType { get; set; }

    [Column("amount")]
    public int Amount { get; set; }

    [Column("product_code")]
    public string ProductCode { get; set; } = string.Empty;

    [Column("source_key")]
    [MaxLength(IncomeEarning.MaxSourceKeyLength)]
    public string SourceKey { get; set; } = string.Empty;

    [Column("claimed_at")]
    public DateTime? ClaimedAt { get; set; }

    [ForeignKey(nameof(PlayerEntityId))]
    public PlayerEntity? PlayerEntity { get; set; }
}
```

(The old `[Index(nameof(PlayerEntityId))]` is covered by the composite index's leading column.)

Check `Vortex.Database.csproj` references `Vortex.Primitives`; if it does not, use the literal `64` with a comment pointing to `IncomeEarning.MaxSourceKeyLength` instead.

- [ ] **Step 2: Generate the migration**

Run: `pwsh scripts/ef.ps1 migrations add AddVaultIncomeSourceKey`
Expected: three files generated under `Vortex.Database/Migrations`.

- [ ] **Step 3: Backfill before the unique index**

In the generated `Up`, between the `AddColumn` calls and the `CreateIndex` call, insert:

```csharp
// Rows written before this column existed get a key of their own, so the unique index below can
// be created over them. Nothing ever deposited into the vault before this migration, so on a real
// hotel this touches nothing — it is here so that is not an assumption.
migrationBuilder.Sql(
    "UPDATE player_vault_income_rewards SET source_key = CONCAT('legacy:', id) WHERE source_key = '';"
);
```

Confirm `DropIndex` for the old single-column index comes before `CreateIndex`, and that `Down` reverses both.

- [ ] **Step 4: Build**

Run: `dotnet build Vortex.Database/Vortex.Database.csproj`
Expected: success, no pending-model-changes warning.

- [ ] **Step 5: Commit**

```bash
git commit -o Vortex.Database/Entities/Players/PlayerVaultIncomeRewardEntity.cs Vortex.Database/Migrations/<timestamp>_AddVaultIncomeSourceKey.cs Vortex.Database/Migrations/<timestamp>_AddVaultIncomeSourceKey.Designer.cs Vortex.Database/Migrations/VortexDbContextModelSnapshot.cs -m "earnings: vault rows carry a source key and a claimed mark"
```

---

### Task 4: Routing policy — config keys and the catalogue

**Files:**
- Create: `Vortex.Collectibles/Configuration/EarningsConfig.cs`
- Create: `Vortex.Collectibles/Vault/EarningsRoutingPolicy.cs`
- Modify: `Vortex.Primitives/Server/ConfigKeyCatalog.cs` (two entries)
- Test: `Vortex.Players.Tests/Vault/EarningsRoutingPolicyTests.cs`

**Interfaces:**
- Produces:
  - `EarningsConfig.AchievementsToVaultKey = "earnings.achievements.to_vault"`, `EarningsConfig.HabboClubToVaultKey = "earnings.habbo_club.to_vault"`, `EarningsConfig.ToVaultDefault = true`.
  - `static string EarningsRoutingPolicy.KeyFor(VaultRewardCategoryType category)` — throws `InvalidOperationException` for a category with no declared key.
  - `static IReadOnlyCollection<string> EarningsRoutingPolicy.AllKeys`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using FluentAssertions;
using Vortex.Collectibles.Configuration;
using Vortex.Collectibles.Vault;
using Vortex.Primitives.Server;
using Vortex.Primitives.Vault.Enums;
using Xunit;

namespace Vortex.Players.Tests.Vault;

/// <summary>
/// The switch that decides whether a category waits in the Earnings window is an operator setting.
/// It must exist in the dashboard catalogue with the default the code falls back to, and a source
/// wired without one must fail here rather than silently pick a behaviour.
/// </summary>
public sealed class EarningsRoutingPolicyTests
{
    [Theory]
    [InlineData(VaultRewardCategoryType.Achievements, "earnings.achievements.to_vault")]
    [InlineData(VaultRewardCategoryType.HabboClub, "earnings.habbo_club.to_vault")]
    public void LiveCategories_HaveTheirKey(VaultRewardCategoryType category, string key) =>
        EarningsRoutingPolicy.KeyFor(category).Should().Be(key);

    [Fact]
    public void ACategoryWithNoSource_HasNoSwitch()
    {
        Action act = () => EarningsRoutingPolicy.KeyFor(VaultRewardCategoryType.DailyGift);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EveryKey_IsInTheCatalogueWithTheSameDefault()
    {
        EarningsRoutingPolicy.AllKeys.Should().HaveCount(2).And.OnlyHaveUniqueItems();

        foreach (string key in EarningsRoutingPolicy.AllKeys)
        {
            ConfigKeyDescriptor? descriptor = ConfigKeyCatalog.Find(key);

            descriptor.Should().NotBeNull($"'{key}' must be editable from the dashboard");
            descriptor!.Kind.Should().Be(ConfigValueKind.Bool);
            descriptor.DefaultValue.Should().Be(EarningsConfig.ToVaultDefault ? "true" : "false");
            descriptor.Group.Should().Be("Earnings");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~EarningsRoutingPolicyTests`
Expected: compile error — `EarningsRoutingPolicy` does not exist.

- [ ] **Step 3: Implement**

`EarningsConfig.cs`:

```csharp
namespace Vortex.Collectibles.Configuration;

/// <summary>
/// Operator switches for the Earnings window, served live from <c>IServerConfigGrain</c> and listed
/// in <c>ConfigKeyCatalog</c>. One per category that has a live source — a switch for a category
/// nothing pays into would be offered, flipped, and do nothing.
/// </summary>
public static class EarningsConfig
{
    /// <summary>Achievement level-up credits and duckets wait in the Earnings window (true) or land in the wallet (false).</summary>
    public const string AchievementsToVaultKey = "earnings.achievements.to_vault";

    /// <summary>The Habbo Club payday waits in the Earnings window (true) or lands in the wallet (false).</summary>
    public const string HabboClubToVaultKey = "earnings.habbo_club.to_vault";

    public const bool ToVaultDefault = true;
}
```

`EarningsRoutingPolicy.cs`:

```csharp
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Vortex.Collectibles.Configuration;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>
/// The operator switch behind each Earnings category. Wiring a new source is one entry here, one
/// constant in <see cref="EarningsConfig"/> and one line in <c>ConfigKeyCatalog</c>.
/// </summary>
internal static class EarningsRoutingPolicy
{
    private static readonly FrozenDictionary<VaultRewardCategoryType, string> Keys =
        new Dictionary<VaultRewardCategoryType, string>
        {
            [VaultRewardCategoryType.Achievements] = EarningsConfig.AchievementsToVaultKey,
            [VaultRewardCategoryType.HabboClub] = EarningsConfig.HabboClubToVaultKey,
        }.ToFrozenDictionary();

    public static IReadOnlyCollection<string> AllKeys => Keys.Values;

    public static string KeyFor(VaultRewardCategoryType category) =>
        Keys.TryGetValue(category, out string? key)
            ? key
            : throw new InvalidOperationException(
                $"Earnings category {category} has a source but no routing switch: add it to "
                    + $"{nameof(EarningsRoutingPolicy)}, {nameof(EarningsConfig)} and ConfigKeyCatalog."
            );
}
```

`ConfigKeyCatalog.cs` — add to `All`, next to the other gameplay groups:

```csharp
        new(
            "earnings.achievements.to_vault",
            "true",
            ConfigValueKind.Bool,
            "Credits and duckets from achievement levels wait in the Earnings window until the player claims them. Off: they land in the wallet immediately. Other currencies are always paid directly. Earnings already waiting stay claimable when this changes.",
            "Earnings"
        ),
        new(
            "earnings.habbo_club.to_vault",
            "true",
            ConfigValueKind.Bool,
            "The Habbo Club payday waits in the Earnings window until the player claims it. Off: it lands in the wallet on payday. Paydays already waiting stay claimable when this changes.",
            "Earnings"
        ),
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~EarningsRoutingPolicyTests` and `dotnet test Vortex.Database.Tests --filter FullyQualifiedName~ConfigKeyCatalogFitsItsColumn`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -o Vortex.Collectibles/Configuration/EarningsConfig.cs Vortex.Collectibles/Vault/EarningsRoutingPolicy.cs Vortex.Primitives/Server/ConfigKeyCatalog.cs Vortex.Players.Tests/Vault/EarningsRoutingPolicyTests.cs -m "earnings: operator switch per category, in the dashboard catalogue"
```

---

### Task 5: Settlers — one per reward type, registered by a Collectibles module

**Files:**
- Create: `Vortex.Collectibles/Vault/VaultIncomeRow.cs`
- Create: `Vortex.Collectibles/Vault/IVaultRewardSettler.cs`
- Create: `Vortex.Collectibles/Vault/CurrencyVaultRewardSettler.cs`
- Create: `Vortex.Collectibles/Vault/VaultRewardSettlerMap.cs`
- Create: `Vortex.Collectibles/CollectiblesModule.cs`
- Modify: `Vortex.Main/Program.cs` (register the module beside `RewardTracksModule`)
- Test: `Vortex.Players.Tests/Vault/VaultRewardSettlerTests.cs`

**Interfaces:**
- Consumes: `VaultRewardRouting.ToCurrencyKind`, `CommerceOperationKind.VaultIncome`, `CommerceStepKeys.VAULT_CLAIM` (Task 2).
- Produces:
  - `internal sealed record VaultIncomeRow(int Id, VaultRewardCategoryType Category, VaultRewardType RewardType, int Amount, string ProductCode)`
  - `internal interface IVaultRewardSettler { IReadOnlySet<VaultRewardType> Handles { get; } Task SettleAsync(int playerId, VaultIncomeRow row, CancellationToken ct); }` — returns normally when the row is paid (now or earlier), throws when it is not.
  - `internal sealed class VaultRewardSettlerMap(IEnumerable<IVaultRewardSettler>)` with `bool TryGet(VaultRewardType, out IVaultRewardSettler)`; constructor throws `InvalidOperationException` on two settlers for one type.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Vortex.Collectibles.Vault;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault.Enums;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Players.Tests.Vault;

public sealed class VaultRewardSettlerTests
{
    private const int PlayerId = 7;

    [Fact]
    public async Task ACurrencyRow_IsCreditedUnderAnIdDerivedFromTheRow()
    {
        List<ProxyCall> credits = [];
        CurrencyVaultRewardSettler settler = new(WalletFactory(credits, answer: true));

        await settler.SettleAsync(PlayerId, Row(41, VaultRewardType.Duckets, 15), CancellationToken.None);

        credits.Should().ContainSingle();
        ((List<WalletDebitRequest>)credits[0].Args![0]!)
            .Should()
            .ContainSingle()
            .Which.Amount.Should()
            .Be(15);
        ((CommerceOperationId)credits[0].Args![1]!)
            .Should()
            .Be(CommerceOperationId.Deterministic(CommerceOperationKind.VaultIncome, 41));
        credits[0].Args![2].Should().Be(CommerceStepKeys.VAULT_CLAIM);
    }

    [Fact]
    public async Task AlreadyCredited_StillCountsAsSettled()
    {
        // CreditOnceAsync answers false for "this receipt already exists": the row was paid by an
        // earlier claim that crashed before marking it, and marking it now is the right outcome.
        CurrencyVaultRewardSettler settler = new(WalletFactory([], answer: false));

        await settler
            .Invoking(s => s.SettleAsync(PlayerId, Row(41, VaultRewardType.Credits, 5), CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    [Fact]
    public void TwoSettlersForOneType_IsAWiringError()
    {
        IGrainFactory factory = WalletFactory([], answer: true);

        FluentActions
            .Invoking(() => new VaultRewardSettlerMap(
                [new CurrencyVaultRewardSettler(factory), new CurrencyVaultRewardSettler(factory)]))
            .Should()
            .Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void AProductRow_HasNoSettlerYet()
    {
        VaultRewardSettlerMap map = new([new CurrencyVaultRewardSettler(WalletFactory([], true))]);

        map.TryGet(VaultRewardType.Product, out _).Should().BeFalse();
        map.TryGet(VaultRewardType.Credits, out _).Should().BeTrue();
    }

    private static VaultIncomeRow Row(int id, VaultRewardType type, int amount) =>
        new(id, VaultRewardCategoryType.Achievements, type, amount, string.Empty);

    private static IGrainFactory WalletFactory(List<ProxyCall> credits, bool answer)
    {
        IPlayerWalletGrain wallet = FakeProxy.Create<IPlayerWalletGrain>(call =>
        {
            if (call.Method.Name == nameof(IPlayerWalletGrain.CreditOnceAsync))
            {
                credits.Add(call);
                return Task.FromResult(answer);
            }

            return null;
        });

        return FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod
            && call.Method.GetGenericArguments()[0] == typeof(IPlayerWalletGrain)
                ? wallet
                : null
        );
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~VaultRewardSettlerTests`
Expected: compile error — settler types do not exist.

- [ ] **Step 3: Implement**

`VaultIncomeRow.cs`:

```csharp
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>One unclaimed Earnings row, as a settler is handed it.</summary>
internal sealed record VaultIncomeRow(
    int Id,
    VaultRewardCategoryType Category,
    VaultRewardType RewardType,
    int Amount,
    string ProductCode
);
```

`IVaultRewardSettler.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>
/// Turns a claimed Earnings row into what it is worth. One implementation per reward type, resolved
/// from DI: the slice that first deposits products adds a product settler and one line in
/// <c>CollectiblesModule</c>, and the vault grain does not change.
/// </summary>
/// <remarks>
/// Returning means the row is paid — now, or by an earlier claim that crashed before the row was
/// marked. Throwing means it is not, and the row stays unclaimed for the next attempt. A settler must
/// be safe to call twice for the same row.
/// </remarks>
internal interface IVaultRewardSettler
{
    IReadOnlySet<VaultRewardType> Handles { get; }

    Task SettleAsync(int playerId, VaultIncomeRow row, CancellationToken ct);
}
```

`CurrencyVaultRewardSettler.cs`:

```csharp
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>Credits and duckets: one wallet credit per row, under a receipt derived from the row.</summary>
internal sealed class CurrencyVaultRewardSettler(IGrainFactory grainFactory) : IVaultRewardSettler
{
    public IReadOnlySet<VaultRewardType> Handles { get; } =
        new[] { VaultRewardType.Credits, VaultRewardType.Duckets }.ToFrozenSet();

    public async Task SettleAsync(int playerId, VaultIncomeRow row, CancellationToken ct)
    {
        // The result is deliberately ignored: false is "this receipt exists", i.e. an earlier claim
        // paid it and crashed before marking the row. A currency that cannot be credited throws.
        await grainFactory
            .GetPlayerWalletGrain(playerId)
            .CreditOnceAsync(
                [
                    new WalletDebitRequest
                    {
                        CurrencyKind = VaultRewardRouting.ToCurrencyKind(row.RewardType),
                        Amount = row.Amount,
                    },
                ],
                CommerceOperationId.Deterministic(CommerceOperationKind.VaultIncome, row.Id),
                CommerceStepKeys.VAULT_CLAIM,
                ct
            )
            .ConfigureAwait(false);
    }
}
```

`VaultRewardSettlerMap.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Vault;

/// <summary>
/// The registered settlers, by the reward type each handles. Two settlers for one type is a wiring
/// mistake that would pay a row by whichever registered last, so it refuses instead.
/// </summary>
internal sealed class VaultRewardSettlerMap
{
    private readonly Dictionary<VaultRewardType, IVaultRewardSettler> _byType = [];

    public VaultRewardSettlerMap(IEnumerable<IVaultRewardSettler> settlers)
    {
        foreach (IVaultRewardSettler settler in settlers)
        {
            foreach (VaultRewardType type in settler.Handles)
            {
                if (!_byType.TryAdd(type, settler))
                {
                    throw new InvalidOperationException(
                        $"Two vault settlers handle {type}: {_byType[type].GetType().Name} and {settler.GetType().Name}."
                    );
                }
            }
        }
    }

    public bool TryGet(VaultRewardType type, [NotNullWhen(true)] out IVaultRewardSettler? settler) =>
        _byType.TryGetValue(type, out settler);
}
```

`CollectiblesModule.cs` (pattern: `Vortex.RewardTracks/RewardTracksModule.cs`):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Collectibles.Vault;
using Vortex.Primitives.Hosting;

namespace Vortex.Collectibles;

/// <summary>
/// What the collectibles module registers. The vault settlers are the extension point: adding a
/// reward type the Earnings window can pay is a class implementing <see cref="IVaultRewardSettler"/>
/// and one line here.
/// </summary>
public sealed class CollectiblesModule : IHostPluginModule
{
    public string Key => "vortex-collectibles";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.AddSingleton<IVaultRewardSettler, CurrencyVaultRewardSettler>();
        services.AddSingleton<VaultRewardSettlerMap>();
    }
}
```

Check the `IHostPluginModule` namespace against `RewardTracksModule.cs` usings and copy them. In `Vortex.Main/Program.cs`, add `using Vortex.Collectibles;` and, after `AddHostPlugin<RewardTracksModule>`, `builder.Services.AddHostPlugin<CollectiblesModule>(builder);`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~VaultRewardSettlerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git commit -o Vortex.Collectibles/Vault/VaultIncomeRow.cs Vortex.Collectibles/Vault/IVaultRewardSettler.cs Vortex.Collectibles/Vault/CurrencyVaultRewardSettler.cs Vortex.Collectibles/Vault/VaultRewardSettlerMap.cs Vortex.Collectibles/CollectiblesModule.cs Vortex.Main/Program.cs Vortex.Players.Tests/Vault/VaultRewardSettlerTests.cs -m "earnings: a settler per reward type, currency first"
```

---

### Task 6: The vault grain — `EarnAsync`, `ClaimAsync`, deposit event

**Files:**
- Modify: `Vortex.Primitives/Players/Grains/IPlayerVaultGrain.cs`
- Modify: `Vortex.Primitives/Events/CollectibleEvents.cs` (add `VaultIncomeDepositedEvent`)
- Rewrite: `Vortex.Collectibles/Grains/PlayerVaultGrain.cs`
- Modify: `Vortex.PacketHandlers/Vault/IncomeRewardClaimMessageHandler.cs` (`ClaimCategoryAsync` → `ClaimAsync`)
- Test: `Vortex.Players.Tests/Vault/PlayerVaultGrainTests.cs`

**Interfaces:**
- Consumes: Tasks 2–5.
- Produces:
  - `Task EarnAsync(IncomeEarning earning, CancellationToken ct)`
  - `Task<bool> ClaimAsync(VaultRewardCategoryType category, CancellationToken ct)` — `All` claims every category.
  - `Task<List<IncomeRewardSnapshot>> GetIncomeRewardsAsync(CancellationToken ct)` — unclaimed rows only (unchanged signature).
  - `sealed record VaultIncomeDepositedEvent(PlayerId PlayerId, VaultRewardCategoryType Category) : IEvent`
  - `AddIncomeRewardAsync` and `ClaimCategoryAsync` are removed.

- [ ] **Step 1: Write the failing tests**

Harness: InMemory `VortexDbContext` + `FakeProxy` for `IGrainFactory` (wallet records `CreditOnceAsync` calls and answers from a receipt set keyed by `(operationId, stepKey)`, like the real table; server config answers `GetBoolAsync` from a dictionary, falling back to the passed default), grain built with `GrainActivationContext.CreateWithIntegerKey<PlayerVaultGrain>(PlayerId, dbFactory, grainFactory, events, settlerMap, NullLogger<PlayerVaultGrain>.Instance)`. Follow `Vortex.Players.Tests/Achievements/PlayerAchievementGrainTests.cs:220-330` for the harness shape (`CountingDbContextFactory`, `FakeProxy.CreateFor`). Call `OnActivateAsync(CancellationToken.None)` after construction.

```csharp
    private static IncomeEarning Credits(VaultRewardCategoryType category, int amount, string key) =>
        new()
        {
            Category = category,
            Currency = new CurrencyKind { CurrencyType = CurrencyType.Credits },
            Amount = amount,
            SourceKey = key,
        };

    [Fact]
    public async Task ADeposit_IsARowAndOnePing()
    {
        Harness h = await Harness.StartAsync();

        await h.Grain.EarnAsync(Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"), default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().ContainSingle().Which.Amount.Should().Be(40);
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().ContainSingle()
            .Which.Category.Should().Be(VaultRewardCategoryType.HabboClub);
        h.WalletCredits.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSameSourceKeyTwice_IsOneRowAndOnePing()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning payday = Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01");

        await h.Grain.EarnAsync(payday, default);
        await h.Grain.EarnAsync(payday, default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().ContainSingle();
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task ADepositReplayedAfterItsClaim_IsNotPaidAgain()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning payday = Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01");

        await h.Grain.EarnAsync(payday, default);
        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeTrue();
        await h.Grain.EarnAsync(payday, default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
        h.WalletCredits.Should().ContainSingle();
    }

    [Fact]
    public async Task ANonVaultCurrency_IsPaidDirectlyOnce()
    {
        Harness h = await Harness.StartAsync();
        IncomeEarning diamonds = new()
        {
            Category = VaultRewardCategoryType.Achievements,
            Currency = new CurrencyKind { CurrencyType = CurrencyType.ActivityPoints, ActivityPointType = 5 },
            Amount = 3,
            SourceKey = "achievement:12:level:2",
        };

        await h.Grain.EarnAsync(diamonds, default);
        await h.Grain.EarnAsync(diamonds, default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
        h.AppliedCredits.Should().ContainSingle(); // second call hit the same (operation, step) receipt
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ACategoryRoutedDirect_SkipsTheVault_AndPendingRowsStayClaimable()
    {
        Harness h = await Harness.StartAsync();
        await h.Grain.EarnAsync(Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-08-01"), default);

        h.Config["earnings.habbo_club.to_vault"] = false;
        await h.Grain.EarnAsync(Credits(VaultRewardCategoryType.HabboClub, 50, "club-payday:2026-09-01"), default);

        (await h.Grain.GetIncomeRewardsAsync(default)).Should().ContainSingle().Which.Amount.Should().Be(40);
        h.AppliedCredits.Should().ContainSingle().Which.Amount.Should().Be(50);
        h.Published.OfType<VaultIncomeDepositedEvent>().Should().ContainSingle();

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeTrue();
        h.AppliedCredits.Should().HaveCount(2);
    }

    [Fact]
    public async Task ClaimAll_PaysEveryCurrencyRow_AndLeavesAProductRow()
    {
        Harness h = await Harness.StartAsync(seedProductRow: true); // seeds one Product row, category BonusBag
        await h.Grain.EarnAsync(Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"), default);
        await h.Grain.EarnAsync(Credits(VaultRewardCategoryType.Achievements, 5, "achievement:1:level:1"), default);

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.All, default)).Should().BeTrue();

        h.AppliedCredits.Select(c => c.Amount).Should().BeEquivalentTo([40, 5]);
        (await h.Grain.GetIncomeRewardsAsync(default)).Should().ContainSingle()
            .Which.RewardType.Should().Be(VaultRewardType.Product);
    }

    [Fact]
    public async Task AClaimThatCrashedAfterTheCredit_IsFinishedByTheNextClaim_WithoutPayingTwice()
    {
        Harness h = await Harness.StartAsync();
        await h.Grain.EarnAsync(Credits(VaultRewardCategoryType.HabboClub, 40, "club-payday:2026-09-01"), default);

        h.ThrowAfterNextCredit = true; // wallet commits the receipt, then the call throws
        await h.Grain.Invoking(g => g.ClaimAsync(VaultRewardCategoryType.HabboClub, default))
            .Should().NotThrowAsync(); // grain answers false, row stays
        (await h.Grain.GetIncomeRewardsAsync(default)).Should().ContainSingle();

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.HabboClub, default)).Should().BeTrue();

        h.AppliedCredits.Should().ContainSingle();
        (await h.Grain.GetIncomeRewardsAsync(default)).Should().BeEmpty();
    }

    [Fact]
    public async Task NothingToClaim_IsFalse()
    {
        Harness h = await Harness.StartAsync();

        (await h.Grain.ClaimAsync(VaultRewardCategoryType.Marketplace, default)).Should().BeFalse();
        (await h.Grain.ClaimAsync((VaultRewardCategoryType)99, default)).Should().BeFalse();
    }
```

`Harness` exposes: `Grain`, `Published` (`List<IEvent>`), `Config` (`Dictionary<string,bool>`), `WalletCredits` (every `CreditOnceAsync` call), `AppliedCredits` (`List<WalletDebitRequest>` — only calls whose `(operationId, stepKey)` was not already in the receipt set), `ThrowAfterNextCredit`. Its wallet fake adds the receipt and the applied credit **before** throwing when `ThrowAfterNextCredit` is set, then clears the flag.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~PlayerVaultGrainTests`
Expected: compile error — `EarnAsync` / `ClaimAsync` / `VaultIncomeDepositedEvent` do not exist.

- [ ] **Step 3: Implement**

`IPlayerVaultGrain.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Orleans.Snapshots.Vault;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Primitives.Players.Grains;

/// <summary>A player's Earnings window: what they earned and have not cashed yet.</summary>
public interface IPlayerVaultGrain : IGrainWithIntegerKey
{
    /// <summary>Unclaimed earnings, one entry per row; the client sums them per category.</summary>
    Task<List<IncomeRewardSnapshot>> GetIncomeRewardsAsync(CancellationToken ct);

    /// <summary>
    /// The one way anything pays into the Earnings window. Kept as a row, or paid straight to the
    /// wallet when the currency has no Earnings type or the operator routed the category direct.
    /// Idempotent on <see cref="IncomeEarning.SourceKey"/> either way.
    /// </summary>
    Task EarnAsync(IncomeEarning earning, CancellationToken ct);

    /// <summary>Cashes one category, or every one for <see cref="VaultRewardCategoryType.All"/>.</summary>
    Task<bool> ClaimAsync(VaultRewardCategoryType category, CancellationToken ct);
}
```

`CollectibleEvents.cs` — add beside `VaultIncomeClaimedEvent`:

```csharp
/// <summary>
/// Something new is waiting in a player's Earnings window. The vault is protocol-free, so the client
/// ping (IncomeRewardNotification) is sent by a handler of this event, not by the grain.
/// </summary>
public sealed record VaultIncomeDepositedEvent(PlayerId PlayerId, VaultRewardCategoryType Category)
    : IEvent;
```

(add `using Vortex.Primitives.Vault.Enums;` if missing).

`PlayerVaultGrain.cs` — full replacement:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Collectibles.Vault;
using Vortex.Database.Context;
using Vortex.Database.Entities.Players;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Vault;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Collectibles.Grains;

/// <summary>
/// A player's Earnings window. Sources describe what was earned (<see cref="IncomeEarning"/>); this
/// grain alone decides whether it waits here or lands in the wallet, and pays each row through the
/// settler registered for its reward type.
/// </summary>
internal sealed class PlayerVaultGrain(
    IDbContextFactory<VortexDbContext> dbCtxFactory,
    IGrainFactory grainFactory,
    IEventPublisher events,
    VaultRewardSettlerMap settlers,
    ILogger<PlayerVaultGrain> logger
) : Grain, IPlayerVaultGrain
{
    private readonly List<PlayerVaultIncomeRewardEntity> _unclaimed = [];

    private int PlayerId => (int)this.GetPrimaryKeyLong();

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        _unclaimed.AddRange(
            await dbCtx
                .PlayerVaultIncomeRewards.AsNoTracking()
                .Where(r => r.PlayerEntityId == PlayerId && r.ClaimedAt == null)
                .ToListAsync(ct)
                .ConfigureAwait(true)
        );
    }

    public Task<List<IncomeRewardSnapshot>> GetIncomeRewardsAsync(CancellationToken ct) =>
        Task.FromResult(
            _unclaimed
                .Select(r => new IncomeRewardSnapshot
                {
                    RewardCategory = (VaultRewardCategoryType)r.RewardCategory,
                    RewardType = (VaultRewardType)r.RewardType,
                    Amount = r.Amount,
                    ProductCode = r.ProductCode,
                })
                .ToList()
        );

    public async Task EarnAsync(IncomeEarning earning, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(earning.SourceKey);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            earning.SourceKey.Length,
            IncomeEarning.MaxSourceKeyLength
        );

        if (earning.Amount <= 0)
        {
            return;
        }

        bool toVault = await grainFactory
            .GetServerConfigGrain()
            .GetBoolAsync(
                EarningsRoutingPolicy.KeyFor(earning.Category),
                Configuration.EarningsConfig.ToVaultDefault
            )
            .ConfigureAwait(true);

        if (
            !toVault
            || !VaultRewardRouting.TryGetRewardType(earning.Currency, out VaultRewardType rewardType)
        )
        {
            // Direct: same idempotency, carried by the wallet receipt instead of a row.
            await grainFactory
                .GetPlayerWalletGrain(PlayerId)
                .CreditOnceAsync(
                    [new WalletDebitRequest { CurrencyKind = earning.Currency, Amount = earning.Amount }],
                    CommerceOperationId.Deterministic(CommerceOperationKind.VaultIncomeDirect, PlayerId),
                    earning.SourceKey,
                    ct
                )
                .ConfigureAwait(true);

            return;
        }

        if (await TryInsertAsync(earning, rewardType, ct).ConfigureAwait(true))
        {
            await events
                .PublishAsync(new VaultIncomeDepositedEvent(PlayerId, earning.Category), ct)
                .ConfigureAwait(true);
        }
    }

    public async Task<bool> ClaimAsync(VaultRewardCategoryType category, CancellationToken ct)
    {
        List<PlayerVaultIncomeRewardEntity> candidates = _unclaimed
            .Where(r =>
                category == VaultRewardCategoryType.All
                || (VaultRewardCategoryType)r.RewardCategory == category
            )
            .ToList();

        List<PlayerVaultIncomeRewardEntity> settled = [];
        bool failed = false;

        foreach (PlayerVaultIncomeRewardEntity row in candidates)
        {
            if (!settlers.TryGet((VaultRewardType)row.RewardType, out IVaultRewardSettler? settler))
            {
                continue; // no settler for this type yet (products): the row waits
            }

            try
            {
                await settler
                    .SettleAsync(PlayerId, ToRow(row), ct)
                    .ConfigureAwait(true);
                settled.Add(row);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = true;
                logger.LogError(
                    ex,
                    "Earnings row {RowId} ({Category}) could not be paid to player {PlayerId}; it stays unclaimed.",
                    row.Id,
                    (VaultRewardCategoryType)row.RewardCategory,
                    PlayerId
                );
            }
        }

        if (settled.Count > 0)
        {
            await MarkClaimedAsync(settled, ct).ConfigureAwait(true);

            await events
                .PublishAsync(
                    new VaultIncomeClaimedEvent(PlayerId, category.ToString(), settled.Count),
                    ct
                )
                .ConfigureAwait(true);
        }

        return settled.Count > 0 && !failed;
    }

    private async Task<bool> TryInsertAsync(
        IncomeEarning earning,
        VaultRewardType rewardType,
        CancellationToken ct
    )
    {
        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Checked against the table, not the unclaimed cache: a key whose row was already claimed
        // must stay spent. The unique index is the backstop for a second silo racing this one.
        bool exists = await dbCtx
            .PlayerVaultIncomeRewards.AnyAsync(
                r => r.PlayerEntityId == PlayerId && r.SourceKey == earning.SourceKey,
                ct
            )
            .ConfigureAwait(true);

        if (exists)
        {
            return false;
        }

        PlayerVaultIncomeRewardEntity entity = new()
        {
            PlayerEntityId = PlayerId,
            RewardCategory = (int)earning.Category,
            RewardType = (int)rewardType,
            Amount = earning.Amount,
            ProductCode = string.Empty,
            SourceKey = earning.SourceKey,
        };

        dbCtx.PlayerVaultIncomeRewards.Add(entity);

        try
        {
            await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);
        }
        catch (DbUpdateException ex)
        {
            logger.LogInformation(
                ex,
                "Earning {SourceKey} for player {PlayerId} was already recorded.",
                earning.SourceKey,
                PlayerId
            );
            return false;
        }

        _unclaimed.Add(entity);
        return true;
    }

    private async Task MarkClaimedAsync(
        List<PlayerVaultIncomeRewardEntity> rows,
        CancellationToken ct
    )
    {
        List<int> ids = rows.Select(r => r.Id).ToList();
        DateTime now = DateTime.UtcNow;

        await using VortexDbContext dbCtx = await dbCtxFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(true);

        // Tracked update rather than ExecuteUpdate: the InMemory provider used by the tests does
        // not support bulk updates, and a claim touches a handful of rows.
        List<PlayerVaultIncomeRewardEntity> tracked = await dbCtx
            .PlayerVaultIncomeRewards.Where(r => ids.Contains(r.Id))
            .ToListAsync(ct)
            .ConfigureAwait(true);

        foreach (PlayerVaultIncomeRewardEntity row in tracked)
        {
            row.ClaimedAt = now;
        }

        await dbCtx.SaveChangesAsync(ct).ConfigureAwait(true);

        _unclaimed.RemoveAll(r => ids.Contains(r.Id));
    }

    private static VaultIncomeRow ToRow(PlayerVaultIncomeRewardEntity r) =>
        new(
            r.Id,
            (VaultRewardCategoryType)r.RewardCategory,
            (VaultRewardType)r.RewardType,
            r.Amount,
            r.ProductCode
        );
}
```

Check `VaultIncomeClaimedEvent`'s first parameter type (`PlayerId`) — if there is no implicit conversion from `int`, wrap with `new PlayerId(PlayerId)` as other grains in `Vortex.Collectibles` do. Same for `VaultIncomeDepositedEvent`.

`IncomeRewardClaimMessageHandler.cs`: replace `.ClaimCategoryAsync(message.Category, ct)` with `.ClaimAsync(message.Category, ct)`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test Vortex.Players.Tests --filter "FullyQualifiedName~Vault"`
Expected: PASS (all vault tests from Tasks 2, 4, 5, 6).

- [ ] **Step 5: Run the grain rules reviewer**

Dispatch the `grain-rules-reviewer` agent on `Vortex.Collectibles/Grains/PlayerVaultGrain.cs`. Expected: no swallowed exception (the `catch` logs and reports `false`), no sequential cross-grain awaits in a loop beyond the per-row settle (each row is its own idempotent credit — acceptable and documented), no per-event DB writes beyond one insert per deposit. Address anything it confirms.

- [ ] **Step 6: Commit**

```bash
git commit -o Vortex.Primitives/Players/Grains/IPlayerVaultGrain.cs Vortex.Primitives/Events/CollectibleEvents.cs Vortex.Collectibles/Grains/PlayerVaultGrain.cs Vortex.PacketHandlers/Vault/IncomeRewardClaimMessageHandler.cs Vortex.Players.Tests/Vault/PlayerVaultGrainTests.cs -m "earnings: one idempotent EarnAsync, claims through settlers, rows marked not deleted"
```

---

### Task 7: The client ping — a protocol-aware event handler

**Files:**
- Create: `Vortex.PacketHandlers/Vault/Events/VaultIncomeDepositedNotifier.cs`
- Modify: `Vortex.Protocol/Messages/Outgoing/Vault/IncomeRewardNotificationMessageComposer.cs` (the "nothing sends this yet" remark is now false)
- Test: `Vortex.PacketHandlers.Tests/VaultIncomeDepositedNotifierTests.cs`

**Interfaces:**
- Consumes: `VaultIncomeDepositedEvent` (Task 6).
- Produces: nothing new for later tasks.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.PacketHandlers.Vault.Events;
using Vortex.Primitives.Events;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Vault.Enums;
using Vortex.Protocol.Messages.Outgoing.Vault;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

public sealed class VaultIncomeDepositedNotifierTests
{
    [Fact]
    public async Task ADeposit_PingsThePlayerWithItsCategory()
    {
        List<IComposer> sent = [];
        IPlayerPresenceGrain presence = FakeProxy.Create<IPlayerPresenceGrain>(call =>
        {
            if (call.Method.Name == nameof(IPlayerPresenceGrain.SendComposerAsync)
                && call.Args?[0] is IComposer composer)
            {
                sent.Add(composer);
            }

            return Task.CompletedTask;
        });
        IGrainFactory factory = FakeProxy.Create<IGrainFactory>(call =>
            call.Method.IsGenericMethod ? presence : null);

        await new VaultIncomeDepositedNotifier(factory).HandleAsync(
            new VaultIncomeDepositedEvent(new PlayerId(7), VaultRewardCategoryType.HabboClub),
            null!,
            default
        );

        sent.Should().ContainSingle()
            .Which.Should().BeOfType<IncomeRewardNotificationMessageComposer>()
            .Which.RewardCategory.Should().Be(VaultRewardCategoryType.HabboClub);
    }
}
```

Check `Vortex.PacketHandlers.Tests.csproj` references `Vortex.Tests.Support`; add the `ProjectReference` if not. Match the `PlayerId` construction to however `CollectibleEvents.cs` records are built elsewhere in tests.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Vortex.PacketHandlers.Tests --filter FullyQualifiedName~VaultIncomeDepositedNotifierTests`
Expected: compile error — `VaultIncomeDepositedNotifier` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Orleans;
using Vortex.Protocol.Messages.Outgoing.Vault;

namespace Vortex.PacketHandlers.Vault.Events;

/// <summary>
/// Tells an online player that something new is waiting in their Earnings window. The client lights
/// the purse indicator and posts <c>notification.earning.new</c> (EarningsController.as:139); an
/// offline player simply sees it on their next window open.
/// </summary>
/// <remarks>
/// Here rather than in the vault grain because <c>Vortex.Collectibles</c> is protocol-free.
/// </remarks>
public sealed class VaultIncomeDepositedNotifier(IGrainFactory grainFactory)
    : IEventHandler<VaultIncomeDepositedEvent>
{
    public async ValueTask HandleAsync(
        VaultIncomeDepositedEvent e,
        EventContext ctx,
        CancellationToken ct
    ) =>
        await grainFactory
            .GetPlayerPresenceGrain(e.PlayerId)
            .SendComposerAsync(
                new IncomeRewardNotificationMessageComposer { RewardCategory = e.Category }
            )
            .ConfigureAwait(false);
}
```

(Use the `GetPlayerPresenceGrain` overload that takes a `PlayerId`; if only `long` exists, pass `e.PlayerId.Value`.)

In `IncomeRewardNotificationMessageComposer.cs`, replace the second `<para>` ("Nothing sends this yet…") with:

```csharp
/// <para>
/// Sent by <c>VaultIncomeDepositedNotifier</c> whenever <c>PlayerVaultGrain.EarnAsync</c> keeps a
/// new row. One byte, verified against the official parser and against a 2026-09-18 capture whose
/// value, 5, is this enum's <see cref="VaultRewardCategoryType.LevelProgression"/> arriving beside
/// a reward-track advance.
/// </para>
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Vortex.PacketHandlers.Tests --filter FullyQualifiedName~VaultIncomeDepositedNotifierTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -o Vortex.PacketHandlers/Vault/Events/VaultIncomeDepositedNotifier.cs Vortex.Protocol/Messages/Outgoing/Vault/IncomeRewardNotificationMessageComposer.cs Vortex.PacketHandlers.Tests/VaultIncomeDepositedNotifierTests.cs -m "earnings: ping the client when something is deposited"
```

(add the csproj to the path list if it changed.)

---

### Task 8: Source — achievement level-ups earn through the vault

**Files:**
- Create: `Vortex.Progression/Achievements/AchievementEarning.cs`
- Modify: `Vortex.Progression/Grains/PlayerAchievementGrain.cs` (the `foreach (AchievementLevelUp levelUp …)` currency grant, ~line 361-376)
- Test: `Vortex.Players.Tests/Achievements/AchievementEarningTests.cs`

**Interfaces:**
- Consumes: `IncomeEarning`, `IPlayerVaultGrain.EarnAsync`.
- Produces: `static IncomeEarning AchievementEarning.For(int achievementId, AchievementLevelUp levelUp)`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;
using Vortex.Progression.Achievements;
using Xunit;

namespace Vortex.Players.Tests.Achievements;

public sealed class AchievementEarningTests
{
    private static AchievementLevelUp LevelUp(int level, int rewardType, int amount) =>
        new()
        {
            Level = level,
            BadgeCode = "ACH_RoomEntry2",
            RemovedBadgeCode = "ACH_RoomEntry1",
            RewardAmount = amount,
            RewardType = rewardType,
            ScorePoints = 10,
        };

    [Fact]
    public void ALevelUp_IsAnAchievementsEarning_KeyedByAchievementAndLevel()
    {
        IncomeEarning earning = AchievementEarning.For(12, LevelUp(level: 2, rewardType: 0, amount: 50));

        earning.Category.Should().Be(VaultRewardCategoryType.Achievements);
        earning.Amount.Should().Be(50);
        earning.SourceKey.Should().Be("achievement:12:level:2");
        earning.Currency.CurrencyType.Should().Be(CurrencyType.ActivityPoints);
        earning.Currency.ActivityPointType.Should().Be(0);
    }

    [Fact]
    public void ACreditsReward_KeepsItsCurrency()
    {
        AchievementEarning.For(12, LevelUp(2, rewardType: -1, amount: 5))
            .Currency.CurrencyType.Should().Be(CurrencyType.Credits);
    }
}
```

Also extend `PlayerAchievementGrainTests`: add a test that seeds a definition whose level 1 has `RewardAmount = 10, RewardType = 0`, progresses past it, and asserts the fake `IPlayerVaultGrain` received exactly one `EarnAsync` with `SourceKey == "achievement:{AchievementId}:level:1"` and that the wallet fake received no `GrantCurrencyAsync`. In `BuildGrainFactory`, route `typeof(IPlayerVaultGrain)` to a recording `FakeProxy` (answering `Task.CompletedTask`) and make `Definition()` take the reward amount/type as parameters with the existing `0/0` as defaults.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Vortex.Players.Tests --filter "FullyQualifiedName~AchievementEarning|FullyQualifiedName~PlayerAchievementGrainTests"`
Expected: compile error — `AchievementEarning` does not exist.

- [ ] **Step 3: Implement**

`AchievementEarning.cs`:

```csharp
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Progression.Achievements;

/// <summary>What one achievement level pays, as an Earnings entry. The key is the level itself: a
/// level is crossed once, so a replayed payout for it is the same earning.</summary>
public static class AchievementEarning
{
    public static IncomeEarning For(int achievementId, AchievementLevelUp levelUp) =>
        new()
        {
            Category = VaultRewardCategoryType.Achievements,
            Currency = CurrencyRewardRules.KindFor(levelUp.RewardType),
            Amount = levelUp.RewardAmount,
            SourceKey = $"achievement:{achievementId}:level:{levelUp.Level}",
        };
}
```

In `PlayerAchievementGrain.cs`, inside the `RecordOwedPayoutAsync` lambda, resolve the vault next to the wallet and replace the grant:

```csharp
                        await vault
                            .EarnAsync(AchievementEarning.For(definition.Id, levelUp), innerCt)
                            .ConfigureAwait(true);
```

where `IPlayerVaultGrain vault = _grainFactory.GetPlayerVaultGrain((long)PlayerId);` is declared where `wallet` is. Remove `wallet` if it has no other use in the method. Keep the `if (levelUp.RewardAmount <= 0) continue;` guard.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Vortex.Players.Tests --filter "FullyQualifiedName~Achievement"`
Expected: PASS (new and existing achievement tests).

- [ ] **Step 5: Commit**

```bash
git commit -o Vortex.Progression/Achievements/AchievementEarning.cs Vortex.Progression/Grains/PlayerAchievementGrain.cs Vortex.Players.Tests/Achievements/AchievementEarningTests.cs Vortex.Players.Tests/Achievements/PlayerAchievementGrainTests.cs -m "earnings: achievement rewards earn through the vault"
```

---

### Task 9: Source — HC payday earns through the vault

**Files:**
- Create: `Vortex.Players/Club/ClubPaydayEarning.cs` (check the folder convention in `Vortex.Players`; put it beside `Configuration/ClubConfig.cs`'s feature code if a `Club` folder does not exist)
- Modify: `Vortex.Players/Grains/PlayerGrain.cs` (`CheckAndGrantPaydayAsync`, the `GrantCreditsAsync(totalReward, ct)` call)
- Test: `Vortex.Players.Tests/Club/ClubPaydayEarningTests.cs`

**Interfaces:**
- Consumes: `IncomeEarning`, `IPlayerVaultGrain.EarnAsync`.
- Produces: `static IncomeEarning ClubPaydayEarning.For(DateTime payday, int credits)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using FluentAssertions;
using Vortex.Players.Club;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;
using Xunit;

namespace Vortex.Players.Tests.Club;

public sealed class ClubPaydayEarningTests
{
    [Fact]
    public void APayday_IsAHabboClubCreditsEarning_KeyedByItsDate()
    {
        IncomeEarning earning = ClubPaydayEarning.For(new DateTime(2026, 9, 1, 13, 45, 0, DateTimeKind.Utc), 41);

        earning.Category.Should().Be(VaultRewardCategoryType.HabboClub);
        earning.Currency.CurrencyType.Should().Be(CurrencyType.Credits);
        earning.Amount.Should().Be(41);
        earning.SourceKey.Should().Be("club-payday:2026-09-01");
    }

    [Fact]
    public void TheKey_IsCultureIndependent()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("ar-SA");
            ClubPaydayEarning.For(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 1)
                .SourceKey.Should().Be("club-payday:2026-09-01");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~ClubPaydayEarningTests`
Expected: compile error — `ClubPaydayEarning` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Globalization;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Wallet;
using Vortex.Primitives.Vault;
using Vortex.Primitives.Vault.Enums;

namespace Vortex.Players.Club;

/// <summary>
/// One Habbo Club payday, as an Earnings entry. Keyed by the payday's date: the payday loop can be
/// re-run after a failed save, and the same date must not pay twice.
/// </summary>
public static class ClubPaydayEarning
{
    public static IncomeEarning For(DateTime payday, int credits) =>
        new()
        {
            Category = VaultRewardCategoryType.HabboClub,
            Currency = new CurrencyKind { CurrencyType = CurrencyType.Credits },
            Amount = credits,
            SourceKey = string.Create(CultureInfo.InvariantCulture, $"club-payday:{payday:yyyy-MM-dd}"),
        };
}
```

In `PlayerGrain.CheckAndGrantPaydayAsync`, replace

```csharp
                IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(
                    (int)this.GetPrimaryKeyLong()
                );
                await wallet.GrantCreditsAsync(totalReward, ct).ConfigureAwait(true);
```

with

```csharp
                // Earned on payday, cashed from the Earnings window (or paid straight to the wallet
                // when the operator routes Habbo Club direct). Keyed by the payday date, so a loop
                // re-run after a failed UpsertKickbackAsync does not pay the same payday twice.
                await _grainFactory
                    .GetPlayerVaultGrain((int)this.GetPrimaryKeyLong())
                    .EarnAsync(ClubPaydayEarning.For(payday, totalReward), ct)
                    .ConfigureAwait(true);
```

`payday` is the loop variable holding the current payday date at that point — confirm it has not been advanced yet (it is advanced after the grant, `payday = payday.AddDays(giftCycleDays)`).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Vortex.Players.Tests --filter FullyQualifiedName~ClubPaydayEarningTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -o Vortex.Players/Club/ClubPaydayEarning.cs Vortex.Players/Grains/PlayerGrain.cs Vortex.Players.Tests/Club/ClubPaydayEarningTests.cs -m "earnings: the Habbo Club payday earns through the vault"
```

---

### Task 10: Gate and live check

**Files:** none new.

- [ ] **Step 1: Wire audit**

Dispatch the `wire-truth-auditor` agent on `IncomeRewardClaimMessageParser`, `IncomeRewardClaimResponseMessageComposerSerializer`, `IncomeRewardStatusMessageComposerSerializer` against `EarningsView.as` / `_SafeCls_2320.as` / the status parser class (`_SafeCls_3503`). Expected: no drift.

- [ ] **Step 2: Specs check**

Run: `dotnet run --project Vortex.Specs.Cli -- analyze IncomeRewardClaimMessage` and `-- validate`
Expected: no new conflict; `validate` passes.

- [ ] **Step 3: Quality gate**

Run: `dotnet build Vortex.Main/Vortex.Main.csproj -t:VortexCloudQualityGate`
Expected: success. If `Vortex.Main` is running and locks outputs, do not stop it — report the lock and run the test projects individually instead.

- [ ] **Step 4: Live check** (with the `run-vortex-emulator` skill, only if the emulator can be (re)started without killing a running instance)

Set a test player's HC payday date in the past, log in, open the Earnings window: the HC payday row shows the credits, the purse indicator lights, Claim credits the wallet once, Claim All with an achievement row pays both and disables every button.

- [ ] **Step 5: Final commit of anything the gate reformatted**

```bash
git status --short
git commit -o <only files from this plan> -m "earnings: gate fixes"
```

---

## Self-review notes

- Spec coverage: wire fixes (T1), `IncomeEarning` + routing (T2), `source_key`/`claimed_at` (T3), operator policy + catalogue (T4), settlers + DI + duplicate refusal (T5), `EarnAsync`/`ClaimAsync`/idempotency/marking/notification event (T6), protocol ping (T7), achievements (T8), payday (T9), verification (T10).
