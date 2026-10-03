using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Client.App;
using Confluence.Client.Arena;
using Confluence.Client.Input;
using Confluence.Client.Laboratory;
using Confluence.Client.Presentation;
using Confluence.Client.Settings;
using Confluence.Client.UI;
using Confluence.Client.ViewModels;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Status;
using NUnit.Framework;
using UnityEngine.UIElements;
using PlayerInput = Confluence.Sim.Input.PlayerInput;

namespace Confluence.Client.Tests
{
    /// <summary>Shop, draft, and build view models over local Confluence.Economy objects, and the screens built from them.</summary>
    public sealed class EconomyViewModelTests
    {
        private static Catalogue Cat => TestCatalogue.Instance;

        private static long _requestId;

        private static long NextId() => ++_requestId;

        private static EconomyShopModel OpenShop(PlayerEconomy player, int round = 1, ulong seed = 7919)
        {
            var shop = new EquipmentShop(player, new RoundInfo(MatchMode.Conclave, round), seed, ContentAvailability.All);
            return new EconomyShopModel(shop, new PhaseTimer(GlobalRules.Round.ShopSec), NextId, "test shop");
        }

        [Test]
        public void ShopCardsShowNameTierPriceSlotEffectAndReroll()
        {
            var player = new PlayerEconomy(Cat, "WIZ-002", 0);
            player.GrantCrowns(GlobalRules.Economy.FirstShopIncome);
            EconomyShopModel shop = OpenShop(player);

            Assert.AreEqual(GlobalRules.Economy.ShopOffers, shop.Cards.Count);
            foreach (ShopCardView card in shop.Cards)
            {
                Assert.AreEqual(ShopCardStatus.Offered, card.Status);
                Assert.AreEqual(1, card.Tier, "round 1 offers tier 1 only (spec L197)");
                Assert.AreEqual(GlobalRules.Economy.Tier1Price, card.Price);
                Assert.IsFalse(string.IsNullOrEmpty(card.Name));
                Assert.IsFalse(string.IsNullOrEmpty(card.SlotText));
                Assert.IsFalse(string.IsNullOrEmpty(card.Effect));
                Assert.IsTrue(card.RerollAvailable && card.CanBuy && card.CanReroll);
            }

            Assert.AreEqual(600, shop.Crowns);
            Assert.AreEqual(35f, shop.SecondsRemaining, 1e-3f);
        }

        [Test]
        public void BuyRerollAndSellGoThroughTheEconomyAndUpdateTheBuild()
        {
            var player = new PlayerEconomy(Cat, "WIZ-002", 0);
            player.GrantCrowns(600);
            EconomyShopModel shop = OpenShop(player);
            string bought = shop.Cards[0].DefinitionId;
            int changes = 0;
            shop.Changed += () => changes++;

            shop.Buy(0);
            Assert.IsFalse(shop.MessageIsError, shop.Message);
            Assert.AreEqual(400, shop.Crowns);
            Assert.AreEqual(ShopCardStatus.Purchased, shop.Cards[0].Status);
            Assert.IsFalse(shop.Cards[0].CanBuy);
            Assert.Greater(changes, 0);
            OwnedEquipment item = player.Loadout.AllItems.Single();
            Assert.AreEqual(bought, item.Id);
            bool shown = shop.Build.Slots.Any(s => s.InstanceId == item.InstanceId) || shop.Build.Reserve.Any(r => r.InstanceId == item.InstanceId);
            Assert.IsTrue(shown, "the bought item appears in a slot or the reserve");

            shop.Reroll(1);
            Assert.IsFalse(shop.MessageIsError, shop.Message);
            Assert.IsFalse(shop.Cards[1].RerollAvailable, "one free reroll per card (spec L206)");
            shop.Reroll(1);
            Assert.IsTrue(shop.MessageIsError, "a second reroll is refused");

            shop.Sell(item.InstanceId);
            Assert.IsFalse(shop.MessageIsError, shop.Message);
            Assert.AreEqual(500, shop.Crowns, "selling refunds 50% of the paid price (spec L212)");
            Assert.AreEqual(0, player.Loadout.AllItems.Count);

            shop.Sell(987654);
            Assert.IsTrue(shop.MessageIsError, "a refused request reports why and changes nothing");
            Assert.AreEqual(500, shop.Crowns);
        }

        [Test]
        public void SpellSwapKeepsSixOfTheEightKitSpells()
        {
            var player = new PlayerEconomy(Cat, "WIZ-002", 0);
            EconomyShopModel shop = OpenShop(player);
            Assert.AreEqual(8, shop.Build.Spells.Count);
            Assert.AreEqual(6, shop.Build.Spells.Count(s => s.Equipped));
            SpellView equipped = shop.Build.Spells.First(s => s.SlotNumber == 2);
            SpellView bench = shop.Build.Spells.First(s => !s.Equipped);

            shop.SwapSpells(equipped.Id, bench.Id);

            Assert.IsFalse(shop.MessageIsError, shop.Message);
            IReadOnlyList<string> spells = player.Loadout.EquippedSpellIds;
            Assert.AreEqual(6, spells.Count);
            Assert.AreEqual(bench.Id, spells[1], "the swapped-in spell keeps the slot number");
            Assert.IsFalse(spells.Contains(equipped.Id));
            Assert.AreEqual(2, shop.Build.Spells.First(s => s.Id == bench.Id).SlotNumber);
        }

        [Test]
        public void ReadyIsReportedToTheHost()
        {
            var player = new PlayerEconomy(Cat, "WIZ-002", 0);
            EconomyShopModel shop = OpenShop(player);
            bool? ready = null;
            shop.ReadyChanged += r => ready = r;
            shop.SetReady(true);
            Assert.AreEqual(true, ready);
            Assert.IsTrue(shop.IsReady);
        }

        [Test]
        [Verifies("AUG-079", "AUG-093")]
        public void SellingAPrerequisiteShowsDormantAugmentsWithMissingRequirements()
        {
            var player = new PlayerEconomy(Cat, "WIZ-006", 0);
            player.GrantCrowns(1000);
            Loadout l = player.Loadout;
            OwnedEquipment rapier = Give(l, "EQP-038");
            Give(l, "EQP-031");
            Grant(l, "AUG-079", 1);
            Grant(l, "AUG-093", 9);
            EconomyShopModel shop = OpenShop(player, 5);
            Assert.IsTrue(shop.Build.Augments.Where(a => !a.Empty).All(a => !a.Dormant));

            shop.Sell(rapier.InstanceId);

            StringAssert.Contains("Now dormant: AUG-079, AUG-093", shop.Message);
            AugmentSlotView child = shop.Build.Augments.Single(a => a.Id == "AUG-093");
            Assert.IsTrue(child.Dormant);
            CollectionAssert.AreEqual(new[] { "AUG-079" }, child.DormantParents);
            Assert.IsTrue(child.Missing.Any(m => m.Contains("AUG-079")));

            var panel = new BuildPanel(shop.Build);
            List<string> texts = panel.Root.Query<Label>().ToList().Select(x => x.text).ToList();
            Assert.IsTrue(texts.Contains("DORMANT"), "the build panel labels dormant augments");
            Assert.IsTrue(texts.Any(t => t.StartsWith("Missing: ", StringComparison.Ordinal)), "and lists what they miss (spec L641)");
        }

        [Test]
        public void DraftCardsShowRarityRankTargetEligibilityAndTransformation()
        {
            var session = new LaboratorySession(Cat, new LaboratoryConfig());
            session.OpenShop();
            session.CloseShop();
            Assert.AreEqual(LabPhase.Draft, session.Phase, "Conclave round 1 has a draft (spec L216)");
            EconomyDraftModel draft = session.DraftModel;

            Assert.AreEqual(GlobalRules.Draft.Candidates, draft.Cards.Count);
            Assert.AreEqual("Silver", draft.Rarity);
            Assert.IsTrue(draft.Cards.Any(c => c.IsTransformation), "a current-wizard transformation is guaranteed when eligible (spec L227)");
            foreach (DraftCardView card in draft.Cards)
            {
                Assert.AreEqual("Silver", card.Rarity);
                Assert.AreEqual(1, card.Rank);
                Assert.IsTrue(card.RerollAvailable);
                Assert.IsFalse(string.IsNullOrEmpty(card.Eligibility));
                StringAssert.StartsWith("Rank 1: ", card.Transformation);
                if (card.IsTransformation)
                {
                    StringAssert.StartsWith("Transforms ", card.TargetSpell);
                }
            }

            var screen = new DraftScreen(draft, LaboratorySession.Label);
            Assert.AreEqual(5, screen.Root.Query(className: "cf-card--augment").ToList().Count);
            screen.Detach();
        }

        [Test]
        public void DraftSelectionEndsTheDraftAndFillsASlot()
        {
            var session = new LaboratorySession(Cat, new LaboratoryConfig());
            session.OpenShop();
            session.CloseShop();
            EconomyDraftModel draft = session.DraftModel;
            bool closed = false;
            draft.Closed += () => closed = true;
            string id = draft.Cards[0].AugmentId;

            draft.Select(0);

            Assert.IsTrue(closed);
            Assert.AreEqual(DraftStep.Closed, draft.Step);
            Assert.AreEqual(id, session.Player.Loadout.AugmentSlots[0].Id);
            Assert.AreEqual(id, draft.Build.Augments[0].Id);
        }

        [Test]
        [Verifies("AUG-079", "AUG-093")]
        public void ReplacingAPrerequisiteAsksForASlotAndListsDependentsThatBecomeDormant()
        {
            var player = new PlayerEconomy(Cat, "WIZ-006", 0);
            Loadout l = player.Loadout;
            Give(l, "EQP-038");
            Give(l, "EQP-031");
            Grant(l, "AUG-079", 1);
            Grant(l, "AUG-093", 9);
            FillAugmentSlots(l);
            Assert.AreEqual(-1, l.FirstFreeAugmentSlot);

            var round = new RoundInfo(MatchMode.Conclave, 11);
            Assert.IsTrue(AugmentDraft.TryOpen(player, round, 7919, ContentAvailability.All, out AugmentDraft opened));
            var draft = new EconomyDraftModel(opened, player, new PhaseTimer(GlobalRules.Round.DraftSec), NextId, "test draft");
            int position = draft.Cards.ToList().FindIndex(c => !c.IsUpgrade);
            Assert.GreaterOrEqual(position, 0, "the draft offers at least one new definition");
            string chosen = draft.Cards[position].AugmentId;

            draft.Select(position);
            Assert.AreEqual(DraftStep.ChoosingReplacement, draft.Step, "six slots are full: name the augment to replace (spec L231)");

            draft.ChooseReplacement(l.AugmentSlotOf("AUG-079"));
            Assert.AreEqual(DraftStep.Confirming, draft.Step);
            Assert.AreEqual(Cat.Augment("AUG-079").Name, draft.Pending.ReplacedName);
            Assert.IsTrue(draft.Pending.BecomeDormant.Any(line => line.Contains("AUG-093")), "the dependent is listed before confirming (spec L645)");
            Assert.IsNull(l.FindAugment(chosen), "nothing changes before the confirmation");

            draft.Confirm();
            Assert.AreEqual(DraftStep.Closed, draft.Step);
            Assert.IsNotNull(l.FindAugment(chosen));
            Assert.IsNull(l.FindAugment("AUG-079"));
            Assert.IsTrue(l.FindAugment("AUG-093").IsDormant);
        }

        [Test]
        public void CancellingAPendingSelectionKeepsTheDraftOpen()
        {
            var player = new PlayerEconomy(Cat, "WIZ-006", 0);
            Loadout l = player.Loadout;
            FillAugmentSlots(l);
            Assert.IsTrue(AugmentDraft.TryOpen(player, new RoundInfo(MatchMode.Conclave, 3), 7919, ContentAvailability.All, out AugmentDraft opened));
            var draft = new EconomyDraftModel(opened, player, new PhaseTimer(GlobalRules.Round.DraftSec), NextId, "test draft");
            int position = draft.Cards.ToList().FindIndex(c => !c.IsUpgrade);
            draft.Select(position);
            Assert.AreEqual(DraftStep.ChoosingReplacement, draft.Step);
            draft.CancelPending();
            Assert.AreEqual(DraftStep.Choosing, draft.Step);
            Assert.IsTrue(opened.IsOpen);
            draft.Pass();
            Assert.AreEqual(DraftStep.Closed, draft.Step);
            Assert.IsFalse(opened.IsOpen);
        }

        [Test]
        public void BuildModelShowsSlotsReserveSpellsAugmentsAndBaseStatistics()
        {
            var player = new PlayerEconomy(Cat, "WIZ-002", 0);
            var build = new LoadoutBuildModel(player);
            Assert.AreEqual(GlobalRules.Loadout.EquipmentSlots, build.Slots.Count);
            Assert.IsTrue(build.Slots.First().Issued, "the Main Focus holds the issued plain focus (spec L202)");
            Assert.AreEqual(GlobalRules.Economy.ReserveSlots, build.ReserveCapacity);
            Assert.AreEqual(GlobalRules.Draft.AugmentSlots, build.Augments.Count);
            Assert.IsTrue(build.Augments.All(a => a.Empty));
            StatView health = build.Stats.First(s => s.Name == "Health");
            Assert.AreEqual("300", health.Effective);
            Assert.IsTrue(build.Stats.Any(s => s.Name == "Mana regeneration" && s.Effective == "10/s"));
            Assert.IsTrue(build.Stats.All(s => !s.Capped), "nothing is capped without modifiers");
            Assert.AreEqual("None", build.EnabledActive);
        }

        // Fills the free augment slots with unconditional shared Silver augments through the production path.
        private static void FillAugmentSlots(Loadout l)
        {
            foreach (AugmentDef def in Cat.Augments)
            {
                if (l.FirstFreeAugmentSlot < 0)
                {
                    return;
                }

                if (!def.IsShared || !def.IsUnconditional || def.RarityEnum != Rarity.Silver || l.FindAugment(def.Id) != null)
                {
                    continue;
                }

                AugmentAcquisition.AcquireNew(l, def, new RoundInfo(MatchMode.Conclave, 1), null, null);
            }

            Assert.AreEqual(-1, l.FirstFreeAugmentSlot, "six unconditional Silver augments exist");
        }

        private static OwnedEquipment Give(Loadout loadout, string equipmentId)
        {
            EquipmentDef def = Cat.Equipment(equipmentId);
            Assert.AreEqual(TransactionCode.Success, loadout.AddItem(def, def.Price, null, out OwnedEquipment item), equipmentId);
            return item;
        }

        private static void Grant(Loadout loadout, string augmentId, int round)
        {
            AcquisitionResult result = AugmentAcquisition.AcquireNew(loadout, Cat.Augment(augmentId), new RoundInfo(MatchMode.Conclave, round), null, null);
            Assert.IsTrue(result.Succeeded, augmentId + ": " + result.Code);
        }
    }

    /// <summary>The Laboratory round sequence on local economy objects (spec L36, L42-L49, L193).</summary>
    public sealed class LaboratorySessionTests
    {
        private static Catalogue Cat => TestCatalogue.Instance;

        [Test]
        public void RoundsPayScheduledIncomeAndTheWinnerBonus()
        {
            var session = new LaboratorySession(Cat, new LaboratoryConfig());
            Assert.AreEqual(LabPhase.Reveal, session.Phase);
            session.OpenShop();
            Assert.AreEqual(600, session.Player.Crowns, "600 before the first shop (spec L193)");
            session.ShopModel.SetReady(true);
            session.CloseShop();
            session.CloseDraft();
            Assert.AreEqual(LabPhase.Duel, session.Phase);

            DuelResult won = RunDuel(session, killOpponent: true);
            RoundResultsModel results = session.CompleteDuel(won, Ticks.FromSeconds(GlobalRules.Round.AlcoveSec), null);
            Assert.AreEqual("Victory", results.Outcome);
            Assert.AreEqual(GlobalRules.Economy.WinnerBonus, results.WinnerBonus);
            Assert.AreEqual(500, results.NextIncome);
            Assert.AreEqual(8, session.PlayerResolve);
            Assert.AreEqual(7, session.OpponentResolve, "the loser loses 1 Resolve in rounds 1-3 (spec L36)");
            Assert.IsTrue(results.DecisiveSequence.Count > 0);

            session.NextRound();
            Assert.AreEqual(2, session.Round);
            session.OpenShop();
            Assert.AreEqual(600 + 500 + 100, session.Player.Crowns, "later shops pay 500 plus the 100 winner bonus");
            session.CloseShop();
            Assert.AreEqual(LabPhase.Duel, session.Phase, "round 2 has no draft (spec L216)");
        }

        [Test]
        public void ADrawCostsBothPlayersTheScheduledResolve()
        {
            var session = new LaboratorySession(Cat, new LaboratoryConfig { StartRound = 4 });
            session.SkipToDuel();
            DuelResult draw = RunDuel(session, killOpponent: false);
            Assert.IsTrue(draw.IsDraw, "two idle fighters at full health draw on the timer (spec L53)");
            RoundResultsModel results = session.CompleteDuel(draw, Ticks.FromSeconds(GlobalRules.Round.AlcoveSec), null);
            Assert.AreEqual("Draw", results.Outcome);
            Assert.AreEqual(6, session.PlayerResolve, "rounds 4-6 cost 2 (spec L36)");
            Assert.AreEqual(6, session.OpponentResolve);
            Assert.AreEqual(0, results.WinnerBonus);
        }

        [Test]
        public void PhaseTimersCountTicksAndExpire()
        {
            var timer = new PhaseTimer(GlobalRules.Round.DraftSec);
            Assert.AreEqual(Ticks.FromSeconds(30f), timer.TotalTicks);
            timer.Paused = true;
            Assert.IsFalse(timer.Advance(5000));
            Assert.AreEqual(30f, timer.SecondsRemaining, 1e-4f);
            timer.Paused = false;
            Assert.IsFalse(timer.Advance(timer.TotalTicks - 1));
            Assert.IsTrue(timer.Advance(1));
            Assert.IsTrue(timer.Expired);
            Assert.IsFalse(timer.Advance(1), "expiry is reported once");
        }

        [Test]
        public void LoadoutBecomesTheDuelSetup()
        {
            var player = new PlayerEconomy(Cat, "WIZ-002", 0);
            EquipmentDef staff = Cat.EquipmentById.Values.First(e => e.Category == EquipmentCategory.Staff && e.Tier == 1);
            Assert.AreEqual(TransactionCode.Success, player.Loadout.AddItem(staff, staff.Price, null, out OwnedEquipment item));
            DuelPlayerSetup setup = LoadoutDuelSetup.Build(player.Loadout, "p");
            Assert.AreEqual("WIZ-002", setup.WizardId);
            CollectionAssert.AreEqual(player.Loadout.EquippedSpellIds, setup.EquippedSpellIds);
            Assert.AreEqual(staff.Id, setup.ItemIn(EquipmentSlot.MainFocus).Id);
        }

        // Runs the session's duel with the training dummy: the demonstration pilot attacks until the dummy falls, or
        // both stand idle until the timer.
        private static DuelResult RunDuel(LaboratorySession session, bool killOpponent)
        {
            ArenaLayout layout = SparringLayout.Create(TestArenas.Raincourt32.Layout);
            var world = new DuelWorld(layout, session.PlayerSetup(), session.OpponentSetup());
            var pilot = new DemoPilot();
            var dummy = new TrainingDummyPilot(DummyMode.Idle);
            int limit = world.AlcoveTicks + world.CombatTicks + 10;
            for (int i = 0; i < limit && !world.IsOver; i++)
            {
                PlayerInput mine = killOpponent ? pilot.Next(world, 0) : PlayerInput.Idle(world.Player(0).Motor.YawDeg);
                world.Step(mine, dummy.Next(world, 1));
            }

            Assert.IsTrue(world.IsOver);
            return world.Result;
        }
    }

    /// <summary>HUD view model over a Sim duel on a generated arena (spec L347-L349, L46-L53).</summary>
    public sealed class DuelHudModelTests
    {
        private static Catalogue Cat => TestCatalogue.Instance;

        private static DuelWorld NewWorld(ArenaLayout layout)
        {
            DuelPlayerSetup a = DuelPlayerSetup.FromCatalogue(Cat, "p0", "WIZ-002");
            DuelPlayerSetup b = DuelPlayerSetup.FromCatalogue(Cat, "p1", "WIZ-010");
            return new DuelWorld(layout, a, b);
        }

        private static void Step(DuelWorld world, int ticks)
        {
            for (int i = 0; i < ticks && !world.IsOver; i++)
            {
                world.Step(PlayerInput.Idle(world.Player(0).Motor.YawDeg), PlayerInput.Idle(world.Player(1).Motor.YawDeg));
            }
        }

        [Test]
        public void AlcoveShowsTheWardTimerAndBlockedSlots()
        {
            DuelWorld world = NewWorld(TestArenas.Raincourt32.Layout);
            var hud = new DuelHudModel(Cat, 0);
            Step(world, 1);
            hud.Refresh(world);

            Assert.AreEqual(300f, hud.Health);
            Assert.AreEqual(300f, hud.MaxHealth);
            Assert.AreEqual(100f, hud.Mana);
            Assert.AreEqual(100f, hud.Stamina);
            Assert.AreEqual(6, hud.Slots.Count);
            Assert.IsTrue(hud.Slots.All(s => s.State == SlotState.Blocked), "the ward prevents casting (spec L46)");
            Assert.AreEqual(Cat.Spell("SPL-009").Name, hud.Slots[0].Name);
            Assert.IsTrue(hud.Slots[0].Selected);
            StringAssert.Contains("alcove ward", hud.SelectedSpellLine);
            StringAssert.StartsWith("Alcove", hud.PhaseText);
            Assert.AreEqual("Kiln Vent", hud.Signature.Name, "the signature's name from WIZ-002");
            Assert.AreEqual(SlotState.Empty, hud.Consumable.State);
            Assert.IsTrue(hud.Statuses.Any(s => s.Kind == StatusIconKind.HealingBudget && s.Value == "120/120"), "healing budget is always shown (spec L347)");
            Assert.IsNotNull(hud.Notice, "the HUD says that casting is inert without a spell runtime");
        }

        [Test]
        public void CombatSlotsAreReadyAndStatusIconsNameWetControlImmunityAndDamageOverTime()
        {
            DuelWorld world = NewWorld(TestArenas.Raincourt32.Layout);
            var hud = new DuelHudModel(Cat, 0);
            Step(world, world.AlcoveTicks + 1);
            hud.Refresh(world);
            Assert.IsTrue(hud.Slots.All(s => s.State == SlotState.Ready));
            Assert.AreEqual("1:30", hud.TimerText);
            Assert.AreEqual(BoundaryCue.Steady, hud.Boundary);

            world.ApplyStatus(0, StatusApplication.Of(StatusKind.Wet, "test", 1, Ticks.FromSeconds(3f)));
            world.ApplyStatus(0, StatusApplication.Of(StatusKind.Burn, "test", 1, Ticks.FromSeconds(2f), 4f));
            world.ApplyStatus(0, StatusApplication.Of(StatusKind.Stun, "test", 1, Ticks.FromSeconds(0.5f)));
            Step(world, Ticks.FromSeconds(0.6f));
            hud.Refresh(world);
            Assert.IsTrue(hud.Statuses.Any(s => s.Kind == StatusIconKind.Wet && s.Glyph == "WET"));
            Assert.IsTrue(hud.Statuses.Any(s => s.Kind == StatusIconKind.DamageOverTime && s.Glyph == "BRN"));
            Assert.IsTrue(hud.Statuses.Any(s => s.Kind == StatusIconKind.ControlImmunity && s.Seconds > 0f), "immunity follows the stun (spec L116)");
            Assert.Less(hud.Health, 300f, "the burn pulsed");
        }

        [Test]
        public void FinalSecondsShowTheJudgementAndTheBoundaryCue()
        {
            DuelWorld world = NewWorld(TestArenas.Raincourt32.Layout);
            var hud = new DuelHudModel(Cat, 0);
            Step(world, world.AlcoveTicks + Ticks.FromSeconds(GlobalRules.Boundary.ContractionStartSec + 1f));
            hud.Refresh(world);
            Assert.AreEqual(BoundaryCue.Contracting, hud.Boundary, "the contraction is announced from 55 s (spec L48)");
            while (!world.IsOver && hud.Boundary != BoundaryCue.Outside)
            {
                Step(world, Ticks.PerSecond / 2);
                hud.Refresh(world);
            }

            Assert.AreEqual(BoundaryCue.Outside, hud.Boundary, "players left at the spawns end up outside the contracting boundary (spec L51)");
            StringAssert.Contains("OUTSIDE", hud.BoundaryText);

            DuelWorld calm = NewWorld(SparringLayout.Create(TestArenas.Raincourt32.Layout));
            var calmHud = new DuelHudModel(Cat, 0);
            Step(calm, calm.AlcoveTicks + Ticks.FromSeconds(76f));
            calmHud.Refresh(calm);
            Assert.IsTrue(calmHud.JudgementVisible, "shown throughout the final 15 s (spec L53)");
            Assert.AreEqual(0, calmHud.Judgement, "equal health is a draw: EVEN");
        }

        [Test]
        public void HudScreenRendersTheModel()
        {
            DuelWorld world = NewWorld(TestArenas.Raincourt32.Layout);
            var model = new DuelHudModel(Cat, 0);
            Step(world, world.AlcoveTicks + 1);
            model.Refresh(world);
            var hud = new DuelHud(model, LaboratorySession.Label, "keys");
            hud.Refresh();
            List<string> texts = hud.Root.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.IsTrue(texts.Contains("1:30"));
            Assert.IsTrue(texts.Any(t => t.StartsWith("Health 300/300", StringComparison.Ordinal)));
            Assert.IsTrue(texts.Contains(LaboratorySession.Label));
            Assert.AreEqual(6, hud.Root.Query(className: "cf-slot").ToList().Count);
        }
    }

    /// <summary>Generator-driven Laboratory presentation: sparring spawns, the offline duel, and world sync.</summary>
    public sealed class OfflineDuelTests
    {
        private static Catalogue Cat => TestCatalogue.Instance;

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _created)
            {
                if (o != null)
                {
                    UnityEngine.Object.DestroyImmediate(o);
                }
            }

            _created.Clear();
        }

        [Test]
        public void SparringSpawnsStandNearTheCenterInSight()
        {
            ArenaLayout source = TestArenas.Raincourt32.Layout;
            ArenaLayout layout = SparringLayout.Create(source);
            Assert.AreNotSame(source, layout);
            Assert.AreEqual(source.Solids.Count, layout.Solids.Count, "only the spawns change");
            var world = Sim.World.CollisionWorld.FromLayout(layout);
            float apart = Core.Geometry.Vec3.DistanceXZ(layout.Spawns[0].Position, layout.Spawns[1].Position);
            Assert.That(apart, Is.InRange(2.3f, 3.7f));
            Assert.LessOrEqual(Core.Geometry.Vec3.DistanceXZ(layout.Spawns[1].Position, layout.Center), 1.5f);
            Assert.LessOrEqual(Core.Geometry.Vec3.DistanceXZ(layout.Spawns[0].Position, layout.Center), layout.FinalFloorRadius,
                "both stand on the final floor, so the boundary never reaches them (spec L48)");
            Assert.IsTrue(SparringLayout.CanSpar(world, layout.Spawns[0].Position, layout.Spawns[1].Position), "clear sight and a clear walking line");
        }

        [Test]
        public void DemonstrationDuelLandsConfirmedHitsOnTheGuardingDummy()
        {
            OfflineDuel duel = CreateDuel(DummyMode.Guard, out ArenaRenderer _);
            duel.LocalPilot = new DemoPilot();
            duel.FastForward(duel.World.AlcoveTicks + Ticks.FromSeconds(6f));

            DuelPlayer dummy = duel.World.Player(1);
            DuelPlayer me = duel.World.Player(0);
            Assert.Less(dummy.Resources.Health, 300f, "player " + me.Position + " grounded " + me.Motor.Grounded + " velocity " + me.Motor.Velocity
                + ", dummy " + dummy.Position + ", spawns " + duel.World.Layout.Spawns[0].Position + " / " + duel.World.Layout.Spawns[1].Position
                + ", melee events " + duel.World.EventLog.OfType<MeleeEvent>().Count());
            Assert.Greater(duel.DamageNumbersShown, 0, "every landed hit is an authoritative DamageEvent");
            int hits = duel.World.EventLog.OfType<DamageEvent>().Count(e => e.Attacker == 0 && e.Target == 1);
            Assert.AreEqual(hits, duel.DamageNumbersShown, "numbers only for confirmed hits");
            Assert.IsTrue(duel.World.EventLog.OfType<GuardEvent>().Any(g => g.Player == 1), "the dummy guards");
            Assert.Less(duel.HudModel.Stamina, 100f, "melee and movement spent stamina");

            // The guard arc and the melee sweep follow the authoritative state tick by tick.
            bool sawGuard = false;
            bool sawSwing = false;
            for (int i = 0; i < Ticks.FromSeconds(6f) && !(sawGuard && sawSwing); i++)
            {
                duel.FastForward(1);
                sawGuard |= duel.World.Player(1).Guard.Active && duel.Fighter(1).GuardShown;
                sawSwing |= duel.World.Player(0).Melee.Busy && duel.Fighter(0).SwingShown;
                Assert.AreEqual(duel.World.Player(1).Guard.Active, duel.Fighter(1).GuardShown);
            }

            Assert.IsTrue(sawGuard, "the dummy's guard arc is drawn while it guards");
            Assert.IsTrue(sawSwing, "the player's committed sweep is drawn during windup and strike");
        }

        [Test]
        public void KeyboardDefaultsFollowTheSpecification()
        {
            using (var input = new DuelInput())
            {
                Assert.AreEqual("<Mouse>/leftButton", input.BindingOf(InputButtons.Primary));
                Assert.AreEqual("<Mouse>/rightButton", input.BindingOf(InputButtons.Guard));
                Assert.AreEqual("<Keyboard>/leftShift", input.BindingOf(InputButtons.Sprint));
                Assert.AreEqual("<Keyboard>/space", input.BindingOf(InputButtons.Jump));
                Assert.AreEqual("<Keyboard>/leftCtrl", input.BindingOf(InputButtons.Crouch));
                Assert.AreEqual("<Keyboard>/leftAlt", input.BindingOf(InputButtons.Dodge));
                Assert.AreEqual("<Keyboard>/f", input.BindingOf(InputButtons.Melee));
                Assert.AreEqual("<Keyboard>/q", input.BindingOf(InputButtons.Signature));
                Assert.AreEqual("<Keyboard>/e", input.BindingOf(InputButtons.ActiveItem));
                Assert.AreEqual("<Keyboard>/x", input.BindingOf(InputButtons.Consumable));
                Assert.AreEqual("<Keyboard>/t", input.BindingOf(InputButtons.Recast));
                Assert.AreEqual("<Keyboard>/r", input.BindingOf(InputButtons.CastModifier));
                Assert.AreEqual("<Keyboard>/z", input.BindingOf(InputButtons.ModifierA));
                Assert.AreEqual("<Keyboard>/c", input.BindingOf(InputButtons.ModifierB));
                input.SelectedSlot = 4;
                PlayerInput command = input.Sample(12, 30f, -5f);
                Assert.AreEqual(4, command.SelectedSlot);
                Assert.AreEqual(30f, command.YawDeg);
                Assert.AreEqual(-5f, command.PitchDeg);
                Assert.AreEqual(12, command.Tick);
                Assert.AreEqual(1u, command.Sequence);
            }
        }

        [Test]
        public void RendererFollowsDestroyedAndTemporarySolids()
        {
            OfflineDuel duel = CreateDuel(DummyMode.Idle, out ArenaRenderer renderer);
            duel.FastForward(duel.World.AlcoveTicks + 1);
            DuelWorld world = duel.World;
            Sim.World.CollisionSolid breakable = world.Collision.Solids.First(s => s.Destructible && !s.Dynamic);
            Assert.IsTrue(renderer.FindSolidObject(breakable.Id).activeSelf);

            world.DamageSolid(breakable.Id, breakable.Durability + 1f, 0);
            var spec = new Sim.World.TemporarySolidSpec
            {
                OwnerPlayer = 0,
                SourceId = "test-wall",
                Box = Core.Geometry.YawBox.OnFloor(world.Player(0).Position + new Core.Geometry.Vec3(0f, 0f, 3f), new Core.Geometry.Vec3(1f, 1f, 0.2f), 0f),
                LifetimeTicks = Ticks.FromSeconds(2f),
                Durability = 50f,
            };
            int temporary = world.AddTemporarySolid(spec);
            Assert.GreaterOrEqual(temporary, 0);
            Assert.AreEqual(1, renderer.SyncSolids(world.Collision));
            Assert.IsFalse(renderer.FindSolidObject(breakable.Id).activeSelf, "a destroyed solid is hidden");
            Assert.IsNotNull(renderer.FindSolidObject(temporary));

            duel.FastForward(Ticks.FromSeconds(2.5f));
            Assert.AreEqual(0, renderer.SyncSolids(world.Collision), "an expired temporary solid disappears");
            Assert.IsNull(renderer.FindSolidObject(temporary));
        }

        [Test]
        public void OwnedFieldsAndSummonsAreRendered()
        {
            OfflineDuel duel = CreateDuel(DummyMode.Idle, out ArenaRenderer _);
            duel.FastForward(duel.World.AlcoveTicks + 1);
            var spec = new Sim.Fields.OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Field,
                Owner = 0,
                DefinitionId = "test-field",
                LifetimeTicks = Ticks.FromSeconds(1f),
                Position = duel.World.Layout.Center,
                Radius = 2f,
            };
            Assert.IsNotNull(duel.World.CreateOwnedEntity(spec));
            duel.FastForward(1);
            Assert.AreEqual(1, duel.Entities.Count);
            duel.FastForward(Ticks.FromSeconds(1.2f));
            Assert.AreEqual(0, duel.Entities.Count, "an expired field is removed");
        }

        [Test]
        public void RevealDescribesTheGeneratedArena()
        {
            var session = new LaboratorySession(Cat, new LaboratoryConfig());
            RoundRevealModel reveal = session.CreateReveal(null);
            Assert.IsFalse(reveal.CanContinue, "the shop waits for the arena");
            reveal.SetArena(TestArenas.Raincourt32);
            Assert.IsTrue(reveal.CanContinue);
            Assert.AreEqual(32, reveal.Diameter);
            StringAssert.Contains("BIO-001", reveal.Biome);
            Assert.AreEqual(Surfaces.PresentIn(TestArenas.Raincourt32.Layout).Count, reveal.Materials.Count);
            Assert.IsTrue(reveal.Materials.Any(m => m.Label == "Shallow water"));
            Assert.Greater(reveal.MajorTerrain.Count, 0);
            Assert.AreEqual(8, reveal.PlayerResolve);
            Assert.AreEqual(LaboratorySession.OpponentName, reveal.OpponentName);
        }

        private OfflineDuel CreateDuel(DummyMode mode, out ArenaRenderer renderer)
        {
            ArenaLayout layout = SparringLayout.Create(TestArenas.Raincourt32.Layout);
            renderer = ArenaRenderer.Create(layout, Cat);
            _created.Add(renderer.gameObject);
            var world = new DuelWorld(layout, DuelPlayerSetup.FromCatalogue(Cat, "p0", "WIZ-002"), DuelPlayerSetup.FromCatalogue(Cat, "p1", "WIZ-010"));
            var settings = new ClientSettings(new MemorySettingsStore());
            OfflineDuel duel = OfflineDuel.Create(null, renderer, world, Cat, settings, new TrainingDummyPilot(mode), LaboratorySession.Label, "keys");
            _created.Add(duel.gameObject);
            return duel;
        }
    }

    /// <summary>Laboratory command-line switches and the interface scale setting.</summary>
    public sealed class LaboratoryOptionTests
    {
        [Test]
        public void LaboratorySwitchesParse()
        {
            CommandLineOptions o = CommandLineOptions.Parse(new[]
            {
                "-laboratory", "-labScreen", "DRAFT", "-labRound", "3", "-labWizard", "WIZ-006", "-labDuelTime", "12.5", "-labDemo", "-arenaSeed", "42",
            });
            Assert.IsTrue(o.Laboratory);
            Assert.AreEqual(LabScreen.Draft, o.LabScreen);
            Assert.AreEqual(3, o.LabRound);
            Assert.AreEqual("WIZ-006", o.LabWizard);
            Assert.AreEqual(12.5f, o.LabDuelTimeSec);
            Assert.IsTrue(o.LabDemo);
            LaboratoryConfig config = LaboratoryController.ConfigFrom(o, TestCatalogue.Instance);
            Assert.AreEqual("WIZ-006", config.PlayerWizardId);
            Assert.AreEqual(42ul, config.MatchSeed);
            Assert.AreEqual(3, config.StartRound);
        }

        [Test]
        public void UnknownWizardsFallBackToTheDefaults()
        {
            CommandLineOptions o = CommandLineOptions.Parse(new[] { "-laboratory", "-labWizard", "WIZ-999" });
            LaboratoryConfig config = LaboratoryController.ConfigFrom(o, TestCatalogue.Instance);
            Assert.AreEqual("WIZ-002", config.PlayerWizardId);
        }

        [Test]
        public void InterfaceScaleIsClampedAndPersisted()
        {
            var store = new MemorySettingsStore();
            var settings = new ClientSettings(store) { UiScale = 3f };
            Assert.AreEqual(ClientSettings.UiScaleMax, settings.UiScale);
            settings.UiScale = 1.25f;
            settings.Save();
            var loaded = new ClientSettings(store);
            loaded.Load();
            Assert.AreEqual(1.25f, loaded.UiScale, 1e-5f);
            loaded.ResetToDefaults();
            Assert.AreEqual(1f, loaded.UiScale);
        }

        [Test]
        public void LaboratoryIsReachableFromTheMenuAndTraining()
        {
            CollectionAssert.Contains(AppStateMachine.LegalTargets(AppState.MainMenu), AppState.Laboratory);
            CollectionAssert.Contains(AppStateMachine.LegalTargets(AppState.Training), AppState.Laboratory);
            CollectionAssert.DoesNotContain(AppStateMachine.LegalTargets(AppState.Laboratory), AppState.Duel);
        }
    }
}
