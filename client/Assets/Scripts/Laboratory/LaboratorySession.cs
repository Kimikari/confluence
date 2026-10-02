using System;
using System.Collections.Generic;
using Confluence.Arena.Generation;
using Confluence.Client.ViewModels;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Sim.Duel;
using Confluence.Sim.Players;

namespace Confluence.Client.Laboratory
{
    /// <summary>Where a Laboratory session is in the round sequence (spec L42-L49).</summary>
    public enum LabPhase
    {
        Reveal,
        Shop,
        Draft,
        Duel,
        Results,
        MatchOver,
    }

    /// <summary>Choices made on the Laboratory setup screen.</summary>
    public sealed class LaboratoryConfig
    {
        public const ulong DefaultSeed = 7919;

        public string PlayerWizardId { get; set; } = "WIZ-002";
        public string OpponentWizardId { get; set; } = "WIZ-010";
        public ulong MatchSeed { get; set; } = DefaultSeed;
        public int StartRound { get; set; } = 1;
        public DummyMode Dummy { get; set; } = DummyMode.Idle;

        /// <summary>Laboratory modification: start both fighters a few meters apart near the center instead of the duel spawns.</summary>
        public bool Sparring { get; set; }
    }

    /// <summary>
    /// The offline Laboratory (spec L69: a solo sandbox with any catalogue content; modified sessions are clearly
    /// labeled and have no ranked rewards). It runs the real round sequence on local Confluence.Economy objects —
    /// reveal, 35 s shop, 30 s draft on augment rounds, 5 s alcove and up to 90 s duel, results with Resolve and
    /// income (spec L42-L49, L36, L193) — against a training dummy that never shops or drafts. Modified rules: every
    /// catalogue record is offered regardless of implementation status, the opponent is a dummy, and timers may be
    /// paused. Plain C#: the Unity host (<c>LaboratoryController</c>) only renders it, so EditMode tests drive it.
    /// </summary>
    public sealed class LaboratorySession
    {
        public const string PlayerName = "You";
        public const string OpponentName = "Training dummy";
        public const MatchMode Mode = MatchMode.Conclave;

        private long _nextRequestId;
        private int _shopsOpened;
        private bool _wonPreviousRound;
        private IReadOnlyList<string> _opponentPreviousLoadout = new[] { "No previous round: issued gear and default spells." };

        public LaboratorySession(Catalogue catalogue, LaboratoryConfig config)
        {
            Catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            Config = config ?? new LaboratoryConfig();
            Player = new PlayerEconomy(catalogue, Config.PlayerWizardId, 0);
            Opponent = new PlayerEconomy(catalogue, Config.OpponentWizardId, 1);
            Round = Math.Max(1, Math.Min(GlobalRules.Match.RegularRounds, Config.StartRound));
            _shopsOpened = Round - 1; // starting later counts the earlier shops as held (spec L193 income)
            PlayerResolve = GlobalRules.Match.StartingResolve;
            OpponentResolve = GlobalRules.Match.StartingResolve;
            Phase = LabPhase.Reveal;
        }

        public Catalogue Catalogue { get; }
        public LaboratoryConfig Config { get; }
        public PlayerEconomy Player { get; }
        public PlayerEconomy Opponent { get; }
        public int Round { get; private set; }
        public int PlayerResolve { get; private set; }
        public int OpponentResolve { get; private set; }
        public LabPhase Phase { get; private set; }
        public RoundInfo RoundInfo => new RoundInfo(Mode, Round);

        public EquipmentShop Shop { get; private set; }
        public EconomyShopModel ShopModel { get; private set; }
        public PhaseTimer ShopTimer { get; private set; }
        public AugmentDraft Draft { get; private set; }
        public EconomyDraftModel DraftModel { get; private set; }
        public PhaseTimer DraftTimer { get; private set; }
        public RoundResultsModel Results { get; private set; }
        public DuelResult LastDuel { get; private set; }

        /// <summary>The Laboratory label every screen shows (spec L69 "clearly labeled").</summary>
        public static string Label => "LABORATORY (offline) - unranked, modified rules: all catalogue content offered, training dummy opponent";

        /// <summary>The arena request of the current round: the session seed and the round's scheduled diameter (spec L306).</summary>
        public ArenaRequest ArenaRequest => new ArenaRequest { MatchSeed = Config.MatchSeed, Round = Round };

        public bool RoundHasDraft => RoundInfo.TryGetDraftRarity(out _);

        public string RoundTitle => "Conclave round " + Round + " of " + GlobalRules.Match.RegularRounds;

        public long NextRequestId() => ++_nextRequestId;

        public RoundRevealModel CreateReveal(Action onContinue)
        {
            string next = RoundHasDraft ? "Shop (35 s), then a free augment draft (30 s)" : "Shop (35 s)";
            return new RoundRevealModel(Catalogue, RoundTitle + ": reveal", OpponentName, ContentFormat.WizardName(Catalogue, Opponent.Loadout.WizardId),
                _opponentPreviousLoadout, PlayerResolve, OpponentResolve, next, onContinue);
        }

        /// <summary>Spec L45, L193: scheduled income (600 first, then 500) plus a 100 winner bonus, then the 35 s shop.</summary>
        public EconomyShopModel OpenShop()
        {
            RequirePhase(LabPhase.Reveal);
            int income = Schedules.ShopIncome(_shopsOpened == 0) + (_wonPreviousRound ? GlobalRules.Economy.WinnerBonus : 0);
            Player.GrantCrowns(income);
            Opponent.GrantCrowns(Schedules.ShopIncome(_shopsOpened == 0));
            _shopsOpened++;
            Shop = new EquipmentShop(Player, RoundInfo, Config.MatchSeed, ContentAvailability.All);
            ShopTimer = new PhaseTimer(GlobalRules.Round.ShopSec);
            ShopModel = new EconomyShopModel(Shop, ShopTimer, NextRequestId, RoundTitle + ": equipment shop (+" + ContentFormat.Crowns(income) + ")");
            Phase = LabPhase.Shop;
            return ShopModel;
        }

        /// <summary>Ends the shop (ready or timeout, spec L355) and opens the round's draft if it has one.</summary>
        public void CloseShop()
        {
            RequirePhase(LabPhase.Shop);
            Shop.Close();
            ShopModel.Rebuild();
            if (AugmentDraft.TryOpen(Player, RoundInfo, Config.MatchSeed, ContentAvailability.All, out AugmentDraft draft))
            {
                Draft = draft;
                DraftTimer = new PhaseTimer(GlobalRules.Round.DraftSec);
                DraftModel = new EconomyDraftModel(draft, Player, DraftTimer, NextRequestId, RoundTitle + ": " + ContentFormat.RarityName(draft.Rarity) + " augment draft");
                Phase = LabPhase.Draft;
            }
            else
            {
                Draft = null;
                DraftModel = null;
                Phase = LabPhase.Duel;
            }
        }

        /// <summary>Ends the draft; an open draft times out with nothing chosen (spec L229).</summary>
        public void CloseDraft()
        {
            RequirePhase(LabPhase.Draft);
            if (Draft.IsOpen)
            {
                DraftModel.Timeout();
            }

            Phase = LabPhase.Duel;
        }

        /// <summary>Advances the open phase timer; returns true when it expired on this call.</summary>
        public bool AdvanceTimer(int ticks)
        {
            switch (Phase)
            {
                case LabPhase.Shop:
                    bool shopExpired = ShopTimer.Advance(ticks);
                    ShopModel.Tick();
                    return shopExpired;
                case LabPhase.Draft:
                    bool draftExpired = DraftTimer.Advance(ticks);
                    DraftModel.Tick();
                    return draftExpired;
                default:
                    return false;
            }
        }

        public void SetTimersPaused(bool paused)
        {
            if (ShopTimer != null)
            {
                ShopTimer.Paused = paused;
            }

            if (DraftTimer != null)
            {
                DraftTimer.Paused = paused;
            }
        }

        public DuelPlayerSetup PlayerSetup() => LoadoutDuelSetup.Build(Player.Loadout, PlayerName);

        public DuelPlayerSetup OpponentSetup() => LoadoutDuelSetup.Build(Opponent.Loadout, OpponentName);

        /// <summary>
        /// Applies a finished duel (spec L36: the loser loses the round's Resolve, a draw costs both; L193: the winner
        /// bonus is paid before the next shop) and builds the results.
        /// </summary>
        public RoundResultsModel CompleteDuel(DuelResult result, int alcoveTicks, Action onContinue)
        {
            RequirePhase(LabPhase.Duel);
            LastDuel = result ?? throw new ArgumentNullException(nameof(result));
            int loss = Schedules.ResolveLoss(Round);
            int playerBefore = PlayerResolve;
            int opponentBefore = OpponentResolve;
            bool won = result.Won(0);
            bool lost = result.Won(1);
            if (result.IsDraw)
            {
                PlayerResolve = Math.Max(0, PlayerResolve - loss);
                OpponentResolve = Math.Max(0, OpponentResolve - loss);
            }
            else if (won)
            {
                OpponentResolve = Math.Max(0, OpponentResolve - loss);
            }
            else if (lost)
            {
                PlayerResolve = Math.Max(0, PlayerResolve - loss);
            }

            _wonPreviousRound = won;
            _opponentPreviousLoadout = LoadoutLines(Opponent.Loadout);
            bool matchOver = PlayerResolve <= 0 || OpponentResolve <= 0 || Round >= GlobalRules.Match.RegularRounds;
            string outcome = result.IsDraw ? "Draw" : won ? "Victory" : "Defeat";
            var names = new[] { PlayerName, OpponentName };
            var resolve = new List<ResolveLine>
            {
                new ResolveLine { Name = PlayerName, Before = playerBefore, After = PlayerResolve },
                new ResolveLine { Name = OpponentName, Before = opponentBefore, After = OpponentResolve },
            };

            IReadOnlyList<DuelEvent> sequence = result.DecisiveSequences != null && result.DecisiveSequences.Count > 0 ? result.DecisiveSequences[0] : null;
            Results = new RoundResultsModel(RoundTitle + ": results", outcome, ReasonText(result), resolve,
                matchOver ? 0 : Schedules.ShopIncome(false), won ? GlobalRules.Economy.WinnerBonus : 0,
                DuelEventText.Lines(sequence, alcoveTicks, names), LoadoutLines(Player.Loadout), matchOver, onContinue);
            Phase = LabPhase.Results;
            return Results;
        }

        /// <summary>Moves to the next round's reveal, or ends the session after round 12 or at zero Resolve (spec L36-L38).</summary>
        public void NextRound()
        {
            RequirePhase(LabPhase.Results);
            if (Results != null && Results.MatchOver)
            {
                Phase = LabPhase.MatchOver;
                return;
            }

            Round++;
            Shop = null;
            ShopModel = null;
            Draft = null;
            DraftModel = null;
            Results = null;
            Phase = LabPhase.Reveal;
        }

        /// <summary>Skips straight to the duel of the current round (sandbox entry).</summary>
        public void SkipToDuel()
        {
            if (Phase == LabPhase.Shop)
            {
                CloseShop();
            }

            if (Phase == LabPhase.Draft)
            {
                CloseDraft();
            }

            if (Phase == LabPhase.Reveal || Phase == LabPhase.Results)
            {
                Phase = LabPhase.Duel;
            }
        }

        /// <summary>The loadout as plain lines (spec L44 "completed previous-round loadout", L361 "remaining build").</summary>
        public static IReadOnlyList<string> LoadoutLines(Loadout loadout)
        {
            var lines = new List<string>();
            var spells = new List<string>();
            foreach (string id in loadout.EquippedSpellIds)
            {
                spells.Add(loadout.Catalogue.Spell(id).Name);
            }

            lines.Add("Spells: " + string.Join(", ", spells));
            var items = new List<string>();
            foreach (OwnedEquipment item in loadout.EquippedItems)
            {
                loadout.FindItem(item.InstanceId, out Placement placement);
                items.Add(item.Definition.Name + " (" + placement + ")");
            }

            lines.Add("Equipment: " + (items.Count > 0 ? string.Join(", ", items) : "issued gear only"));
            var augments = new List<string>();
            foreach (OwnedAugment augment in loadout.Augments)
            {
                augments.Add(augment.Definition.Name + " r" + augment.Rank + (augment.IsDormant ? " (dormant)" : string.Empty));
            }

            lines.Add("Augments: " + (augments.Count > 0 ? string.Join(", ", augments) : "none"));
            return lines;
        }

        private static string ReasonText(DuelResult result)
        {
            switch (result.Reason)
            {
                case DuelEndReason.Death: return "Defeat by health loss at " + ContentFormat.Seconds(result.CombatSeconds) + " of combat.";
                case DuelEndReason.SimultaneousDeath: return "Both fighters fell in the same simulation step: a draw (spec L53).";
                case DuelEndReason.Timer:
                    return "Timer judgement at 90 s on health fraction: " + ContentFormat.Percent(result.HealthFractions[0]) + " vs "
                        + ContentFormat.Percent(result.HealthFractions[1]) + " (below 0.5 points is a draw, spec L53).";
                case DuelEndReason.Surrender: return "Surrender: a normal loss (spec L53).";
                default: return result.Reason.ToString();
            }
        }

        private void RequirePhase(LabPhase phase)
        {
            if (Phase != phase)
            {
                throw new InvalidOperationException("The Laboratory is in " + Phase + ", not " + phase + ".");
            }
        }
    }
}
