using System;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Vortex.Primitives.Signals;
using Vortex.Signals.Translators;
using Xunit;

namespace Vortex.Signals.Tests;

/// <summary>
/// The vocabulary is append-only, and this is what enforces it.
/// </summary>
/// <remarks>
/// <para>
/// Fact keys live in <c>reward_track_step_filters.fact_key</c> and action codes in the task rows
/// beside them. Renaming either silently stops every filter already written on it from matching —
/// no build error, no failing test, no log line, just content that quietly never completes.
/// </para>
/// <para>
/// So the lists are frozen here, by hand, on purpose. Adding a key updates the list in the same
/// commit; removing one has to be typed out deliberately, under a reviewer's eyes. That friction is
/// the feature.
/// </para>
/// </remarks>
public sealed class VocabularyGovernanceTests
{
    /// <summary>Every fact key content may already be written against.</summary>
    private static readonly ImmutableArray<string> FrozenFacts =
    [
        "target",
        "item",
        "def",
        "kind",
        "room",
        "name",
        "desc",
        "category",
        "model",
        "player",
        "offer",
        // "habbicon" and "collection" were here, and "nft_product" further down: the RPG fork (M0)
        // removed Habbicons, the collectibles store and reward tracks, so nothing emits them and the
        // only content that named them -- reward-track step filters -- is gone with the feature.
        "pet",
        "badge",
        "message",
        "points",
        "group",
        "thread",
        "price",
        "quantity",
        // "currency" was here and is deliberately gone, for the same reason and under the same
        // exemption as "code" below: no translator ever emitted it, so no content can name it. No
        // event carries a currency — every price is in the one currency its action spends — so the
        // fact could only ever have been a constant, and a filter on a constant filters nothing.
        // "code" was here and is deliberately gone. It named a poll code, a quiz code, a campaign,
        // a voucher, two different product codes and a vault category -- one key meaning six
        // things, which is the ambiguity that makes a filter unreadable and a picker impossible.
        // Removing a key is normally forbidden by this very list; it is safe exactly once, because
        // the actions that carried it were added in the same unreleased change and no content can
        // name them yet. The seven keys below replace it, and none of them may ever be removed.
        "poll",
        "quiz",
        "campaign",
        "voucher",
        "club_gift",
        "vault_category",
        "targeted_offer",
        "pet_type",
        "pet_command",
        "relation",
        "given_name",
        "effect",
        "seconds",
        "colour",
        "months",
        "serial",
        "figure",
        "figure_set",
        "section",
        "dance",
        "sign",
        "posture",
    ];

    /// <summary>Every action code content may already name.</summary>
    private static readonly ImmutableArray<string> FrozenActions =
    [
        "enter_other_users_room",
        "create_room",
        "place_item",
        "move_item",
        "rotate_item",
        "pick_up_item",
        "walk_on_furni",
        "teleport",
        "chat_with_someone",
        "request_friend",
        "give_respect",
        "send_messenger_message",
        "dance",
        "wave",
        "show_sign",
        "change_figure",
        "change_motto",
        "wear_badge",
        "buy_from_catalogue",
        "pet_level",
        // The Habbicon, relic, minting, NFT-store and NFT-avatar codes were removed with their
        // features in the RPG fork (M0); their only content was reward-track tasks, also removed.
        "complete_trade",
        "spend_credits",
        "complete_quest",
        "achievement_level",
        "wired",
        "rate_room",
        "leave_room",
        "update_room_settings",
        "answer_doorbell",
        "accept_friend",
        "receive_respect",
        "login",
        "change_name",
        "earn_badge",
        "accept_quest",
        "activate_effect",
        "complete_poll",
        "submit_quiz",
        "claim_daily_task",
        "save_outfit",
        "redeem_clothing",
        "change_preference",
        "create_group",
        "join_group",
        "favourite_group",
        "create_forum_thread",
        "create_forum_post",
        "adopt_pet",
        "place_pet",
        "pick_up_pet",
        "list_on_marketplace",
        "buy_on_marketplace",
        "redeem_marketplace_credits",
        "buy_club",
        "claim_club_gift",
        "open_present",
        "open_mystery_box",
        "open_mystery_trophy",
        "buy_gift",
        "buy_targeted_offer",
        "enter_raffle",
        "win_raffle",
        "redeem_voucher",
        "claim_vault_income",
        "respect_pet",
        "pet_respected",
        "feed_pet",
        "command_pet",
        "stand_near_furni",
        "receive_gift",
    ];

    /// <summary>The declared values of every closed fact, which content compares against too.</summary>
    private static readonly ImmutableArray<string> FrozenEnumValues =
    [
        "floor",
        "wall",
        "purple",
        "blue",
        "green",
        "yellow",
        "lilac",
        "orange",
        "turquoise",
        "red",
        "settings",
        "tags",
        "category_trade",
        // The nine sides of a piece of furniture. Frozen like the rest: a task stores the string,
        // so renaming one would quietly stop matching what it was written to match.
        "on",
        "front",
        "front_right",
        "right",
        "back_right",
        "back",
        "back_left",
        "left",
        "front_left",
    ];

    [Fact]
    public void No_fact_key_has_been_renamed_or_removed()
    {
        ImmutableArray<string> live = [.. Facts.All.Select(f => f.Key), Facts.TargetKey];

        live.Should()
            .Contain(
                FrozenFacts,
                "a fact key is stored in content, so renaming one silently stops every filter "
                    + "written on it from matching"
            );
    }

    [Fact]
    public void No_action_code_has_been_renamed_or_removed()
    {
        SignalActions
            .All.Should()
            .Contain(FrozenActions, "an action code is stored on every task row that names it");
    }

    [Fact]
    public void No_declared_value_of_a_closed_fact_has_gone_away()
    {
        ImmutableArray<string> live =
        [
            .. Facts
                .All.SelectMany(f => f.EnumValues.IsDefaultOrEmpty ? [] : f.EnumValues)
                .Select(v => v.Value),
        ];

        live.Should().Contain(FrozenEnumValues, "content compares against these strings");
    }

    [Fact]
    public void Every_fact_can_be_shown_to_an_operator()
    {
        // A plugin cannot add keys to the dashboard locales, and a locale can lag behind here too.
        // Without a fallback the editor renders a raw key, which tells an operator nothing.
        Facts.All.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.FallbackLabel));
        Facts.All.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.LabelKey));
    }

    [Fact]
    public void Every_fact_offers_an_operator_that_means_something()
    {
        // A fact whose kind allows no operator is a filter nobody can write, and a fact whose only
        // allowed operator is one the engine treats as exact equality on free text is a filter that
        // never fires. Contains exists precisely so Text has an answer here.
        foreach (FactKey fact in Facts.All)
        {
            FactOperators
                .For(fact.Kind)
                .Should()
                .NotBeEmpty($"'{fact.Key}' would be offered with no way to compare it");
        }

        FactOperators
            .For(FactKind.Text)
            .Should()
            .Contain(FactOperators.Contains, "exact equality on a line a player typed never fires");

        FactOperators
            .For(FactKind.RoomId)
            .Should()
            .NotContain(FactOperators.Contains, "a substring of an id is not a thing");
    }

    [Fact]
    public void Every_action_a_translator_declares_is_frozen()
    {
        // Catches the other direction: a new action shipped without being added to the frozen list
        // would otherwise be renameable a week later with nothing to stop it.
        ImmutableArray<string> declared =
        [
            .. TranslatorCatalog.All.SelectMany(t => t.Shapes).Select(s => s.Action).Distinct(),
        ];

        FrozenActions.Should().Contain(declared);
    }

    [Fact]
    public void Every_fact_in_the_catalogue_is_emitted_by_something()
    {
        // The defect this whole subsystem exists to prevent, in its last hiding place. A fact can be
        // written for a feature, documented, frozen above -- and never wired to the translator that
        // was supposed to emit it. Nothing else says so: the editor builds its list from shapes, so
        // an orphan is invisible rather than wrong, and the feature is simply missing.
        //
        // `message` was exactly that. The chat event was given its line, the fact was declared for
        // it, and the translator kept emitting only the room, so "say something containing X" could
        // not be written.
        ImmutableArray<string> emitted =
        [
            .. TranslatorCatalog
                .All.SelectMany(t => t.Shapes)
                .SelectMany(s => s.Facts)
                .Select(f => f.Key)
                .Distinct(),
        ];

        Facts
            .All.Select(f => f.Key)
            .Should()
            .BeSubsetOf(emitted, "a fact nothing emits is a filter that can never match");
    }
}
