using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Economy;
using Confluence.Economy.Augments;
using Confluence.Economy.Loadouts;
using Confluence.Economy.Shop;
using Confluence.Hosting.Bots;
using Confluence.Hosting.Commands;
using Confluence.Hosting.Content;
using Confluence.Hosting.Duels;
using Confluence.Hosting.Economics;
using Confluence.Hosting.Messages;
using Confluence.Match;
using Confluence.Reactions.Events;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World.Materials;
using Xunit.Abstractions;
using CastEventKind = Confluence.Casting.CastEventKind;
using MatchDuelEndReason = Confluence.Match.DuelEndReason;

namespace Confluence.Hosting.Tests
{
    /// <summary>
    /// Stage 2 + 3 batch 1 merge (augments-a, bram-kit, halen-kit, net-protocol) meeting in hosted matches. The
    /// equipment, augments-b and props-biomes branches are not merged yet, so the standard rules still refuse on the
    /// equipment floor alone (three tier-1 items implemented, six needed); the match below therefore runs with exactly
    /// one Laboratory override, <c>bypass-economy-floors</c>. Shop and draft pools still hold implemented records only
    /// (no <c>pools-whole-catalogue</c>), so every drafted augment has a duel behavior.
    /// </summary>
    public sealed class MergedContentIntegrationTests
    {
        private static readonly string[] M1Wizards = { "WIZ-002", "WIZ-010" };

        /// <summary>The only override the merged batch still needs (the equipment floor, D-010, D-082).</summary>
        public static LaboratoryOverrides FloorsOnly { get; } = new LaboratoryOverrides(bypassEconomyFloors: true);

        private readonly ITestOutputHelper _output;

        public MergedContentIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static Catalogue Cat => HostFixture.Catalogue;

        [Fact]
        public void Merged_Registry_Meets_The_M1_Draft_Floors_Without_Overrides_Leaving_Only_The_Equipment_Floor()
        {
            ImplementedContent implemented = ImplementedContent.FromShippedAssemblies();
            MatchHost host = HostFixture.Host(HostFixture.DuelSettings(HostFixture.SmallDuelSeed, overrides: LaboratoryOverrides.None));
            HostStartResult start = host.Start();
            foreach (DraftFloorReport w in start.Floors.Wizards) _output.WriteLine(w.ToString());
            _output.WriteLine(start.Floors.Equipment.ToString());

            // Standard pools are the implemented records only (D-010).
            Assert.NotEmpty(host.Content.PoolIds);
            Assert.All(host.Content.PoolIds, id => Assert.True(implemented.IsAvailable(id), id));

            // Spec L233: at least ten legal alternatives in every rarity for both M1 wizards at their default loadouts.
            Assert.Equal(M1Wizards, start.Floors.Wizards.Select(w => w.WizardId).OrderBy(x => x, StringComparer.Ordinal));
            foreach (DraftFloorReport wizard in start.Floors.Wizards)
            {
                Assert.True(wizard.MeetsAll, wizard.ToString());
                Assert.All(wizard.Rarities, r => Assert.True(r.LegalAlternatives >= GlobalRules.Draft.MinLegalAlternativesPerRarity, wizard.WizardId + " " + r));
            }

            // The equipment floor is the one remaining gap until the equipment branch lands (then the match starts).
            if (start.Floors.Equipment.Meets)
            {
                Assert.True(start.Started, string.Join("; ", start.Gaps));
            }
            else
            {
                Assert.Equal(HostStartCode.RefusedEconomyFloors, start.Code);
                Assert.Equal(new[] { start.Floors.Equipment.ToString() }, start.Gaps);
            }
        }

        [Fact]
        [Verifies("WIZ-002")]
        [Verifies("WIZ-010")]
        [Verifies("SPL-009")]
        [Verifies("SPL-076")]
        public void Laboratory_Duel_Match_Plays_The_Merged_Kits_With_Drafted_Augments_Dormancy_And_Steam()
        {
            MatchHostSettings settings = HostFixture.DuelSettings(HostFixture.SmallDuelSeed, overrides: FloorsOnly, prefetchRounds: 8);
            MatchHost host = HostFixture.Host(settings);
            var probes = new DormancyProbe[2];
            for (int p = 0; p < 2; p++)
            {
                BotPlayer bot = host.AddBot(p, BotProfile.For(p == 0 ? BotCompetence.Expert : BotCompetence.Adept));
                probes[p] = new DormancyProbe(bot.Economy, host);
                host.SetEconomyAgent(p, probes[p]);
            }

            ImplementedContent implemented = ImplementedContent.FromShippedAssemblies();
            var recorder = new MatchRecorder(host);

            HostStartResult start = host.Start();
            Assert.True(start.Started);
            Assert.Equal(new[] { start.Floors.Equipment.ToString() }, start.Gaps); // bypassed, still reported
            Assert.Equal("laboratory:bypass-economy-floors", host.Overrides.CanonicalText());
            Assert.False(host.CountsForRating);
            host.RunUntilFinished(60 * 60 * 60);
            Assert.True(host.IsFinished);
            foreach (string line in recorder.Summary()) _output.WriteLine(line);

            DuelMatch duel = host.Duel;
            MatchEventLog events = duel.Events;
            List<DuelResolvedEvent> resolved = events.OfType<DuelResolvedEvent>();

            // Every round completed: a duel per pass, resolved by death, time or a simultaneous lethal outcome, reported
            // with a complete replay; the best of seven ended (L65).
            Assert.Empty(events.OfType<RoundVoidedEvent>());
            Assert.True(resolved.Count >= GlobalRules.Match.DuelWinsToClinch);
            Assert.All(resolved, e => Assert.Contains(e.Outcome.Reason, new[] { MatchDuelEndReason.Death, MatchDuelEndReason.Time, MatchDuelEndReason.SimultaneousLethal }));
            Assert.Contains(resolved, e => e.Outcome.Reason == MatchDuelEndReason.Death);
            Assert.Equal(resolved.Count, host.Duels.Reports.Count);
            Assert.All(host.Duels.Reports, r => Assert.True(r.Replay.IsComplete));
            Assert.True(duel.Score(0) >= GlobalRules.Match.DuelWinsToClinch || duel.Score(1) >= GlobalRules.Match.DuelWinsToClinch || duel.IsRecordedDraw || duel.IsDecider);

            // Shops every round, drafts exactly on the Duel draft rounds with the scheduled rarity (L65, L225).
            var rounds = events.OfType<PairingsAnnouncedEvent>().Where(p => p.PassKind == PassKind.Original).Select(p => p.Round).Distinct().ToList();
            foreach (int round in rounds)
            {
                Assert.Equal(new[] { 0, 1 }, events.OfType<ShopOpenedEvent>().Where(s => s.Round == round).Select(s => s.Opening.Player).OrderBy(x => x));
                List<DraftOpenedEvent> drafts = events.OfType<DraftOpenedEvent>().Where(d => d.Round == round).ToList();
                if (Schedules.DraftRarity(MatchMode.Duel, round, out Rarity rarity))
                {
                    Assert.Equal(new[] { 0, 1 }, drafts.Select(d => d.Player).OrderBy(x => x));
                    Assert.All(drafts, d => Assert.Equal(rarity, d.Rarity));
                }
                else
                {
                    Assert.Empty(drafts);
                }
            }

            // Round-1 shops fill with implemented equipment only, and every offer of the match is an implemented record.
            Assert.All(recorder.ShopCards, c => Assert.True(c.Definition == null || implemented.IsAvailable(c.Definition.Id), c.ToString()));
            Assert.All(recorder.DraftCards, c => Assert.True(implemented.IsAvailable(c.AugmentId), c.AugmentId));
            Assert.Equal(2, recorder.RoundOneShops.Count);
            Assert.All(recorder.RoundOneShops.Values, cards =>
            {
                Assert.Equal(GlobalRules.Economy.ShopOffers, cards.Count(c => c.Definition != null));
                Assert.All(cards.Where(c => c.Definition != null), c => Assert.Equal(1, c.Definition.Tier));
            });
            Assert.All(events.OfType<LoadoutFinalizedEvent>(), l => Assert.All(l.Loadout.EquipmentIds, id => Assert.True(implemented.IsAvailable(id), id)));
            Assert.Contains(events.OfType<LoadoutFinalizedEvent>(), l => l.Loadout.EquipmentIds.Count > 0);

            // Both bots drafted through the command API: shared augments and wizard transformations, upgrades included.
            var owned = events.OfType<LoadoutFinalizedEvent>().SelectMany(l => l.Loadout.AugmentIds).Distinct().ToList();
            Assert.Contains(owned, id => Cat.Augment(id).IsTransformation);
            Assert.Contains(owned, id => !Cat.Augment(id).IsTransformation && AugmentsAShared(id));
            Assert.Contains(events.OfType<LoadoutFinalizedEvent>(), l => l.Loadout.AugmentRanks.Any(r => r == 2));

            // Dormancy at finalization (L249, L641): every duel received exactly the augments the economy resolved active
            // for the round snapshot. A probe's spell swap left an owned augment's equipped-spell condition unmet for one
            // round (dormant: owned, in its slot, no effect, L247) and the next shop restored it (active again).
            Assert.All(recorder.Duels, d =>
            {
                for (int side = 0; side < 2; side++) Assert.Equal(d.EconomyActive[side], d.Setups[side].Augments.Select(a => a.Id + "r" + a.Rank));
            });
            DormancyProbe swapped = probes.FirstOrDefault(p => p.DormantRound > 0);
            Assert.True(swapped != null, "no owned augment could be made dormant by a spell swap");
            int player = Array.IndexOf(probes, swapped);
            _output.WriteLine("dormancy probe: player " + player + " " + swapped.Augment + " dormant in round " + swapped.DormantRound + " (equipped " +
                string.Join(",", swapped.DormantSpells) + "), restored in round " + swapped.RestoredRound);
            Assert.True(swapped.RestoredRound > swapped.DormantRound);
            DuelRecord dormantDuel = recorder.Duels.First(d => d.Round == swapped.DormantRound);
            int dormantSide = dormantDuel.SideOf(player);
            Assert.Contains(swapped.Augment, dormantDuel.Owned[dormantSide]);
            Assert.DoesNotContain(dormantDuel.Setups[dormantSide].Augments, a => a.Id == swapped.Augment);
            Assert.DoesNotContain(swapped.Augment, dormantDuel.Attached[dormantSide]);
            Assert.Equal(swapped.DormantSpells, dormantDuel.Setups[dormantSide].EquippedSpellIds);
            DuelRecord restoredDuel = recorder.Duels.First(d => d.Round == swapped.RestoredRound);
            int restoredSide = restoredDuel.SideOf(player);
            Assert.Contains(restoredDuel.Setups[restoredSide].Augments, a => a.Id == swapped.Augment);
            Assert.Contains(swapped.Augment, restoredDuel.Attached[restoredSide]);
            Assert.Equal(swapped.RestoredSpells, restoredDuel.Setups[restoredSide].EquippedSpellIds);

            // Transformations are resolved into the duel's spell forms (L286, L510) and shared augments attach to the
            // effect host (or edit forms) wherever they are active.
            foreach (DuelRecord d in recorder.Duels)
            {
                for (int side = 0; side < 2; side++)
                {
                    foreach (ActiveAugment a in d.Setups[side].Augments)
                    {
                        AugmentDef def = Cat.Augment(a.Id);
                        if (def.IsTransformation)
                        {
                            string target = def.TargetSpellIds[0];
                            if (d.Setups[side].EquippedSpellIds.Contains(target)) Assert.Contains(a.Id, d.Transforms[side][target]);
                        }
                        else
                        {
                            Assert.True(d.Attached[side].Contains(a.Id), "duel " + d.DuelId + " side " + side + ": " + a.Id + " attached nothing");
                        }
                    }
                }
            }

            List<DuelEvent> log = recorder.Duels.SelectMany(d => d.World.EventLog).ToList();
            var drafted = new HashSet<string>(owned);
            foreach (var g in log.OfType<EffectEvent>().GroupBy(e => e.SourceId + ":" + e.Change)) _output.WriteLine("effect " + g.Key + " " + g.Count());
            // A drafted Fieldcraft record prices WIZ-002's Ash Curtain cloud (a kit field, Sim FieldShapes.cs): with AUG-053
            // or AUG-065 owned, some commitment paid more than the listed 20 (or the crest's 17.6) for a near or covered cloud.
            var pricedClouds = new List<float>();
            foreach (DuelRecord d in recorder.Duels)
            {
                foreach (CastDuelEvent c in d.World.EventLog.OfType<CastDuelEvent>().Where(c => c.SpellId == "SPL-012" && c.Kind == CastEventKind.Committed))
                {
                    _output.WriteLine("ash duel " + d.DuelId + " mana " + c.Cast.Mana + " augs " + string.Join(",", d.Setups[c.Player].Augments.Select(a => a.Id)));
                    if (d.Setups[c.Player].Augments.Any(a => a.Id == "AUG-053" || a.Id == "AUG-065")) pricedClouds.Add(c.Cast.Mana);
                }
            }

            Assert.Contains(pricedClouds, mana => Math.Abs(mana - 20f) > 1e-3f && Math.Abs(mana - 20f * 0.88f) > 1e-3f);
            Assert.Contains(log.OfType<EffectEvent>(), e => e.Change == EffectChange.Activated && drafted.Contains(e.SourceId) && AugmentsAShared(e.SourceId));

            // A drafted projectile-family augment acts on a kit's own projectile (Sim ProjectileShapes.cs): AUG-038 First
            // Contact adds 5% (rank 1) or 8% (rank 2) to WIZ-010 Pressure Jet's 32 on a full-health enemy.
            float jet = float.Parse(Cat.Spell("SPL-073").Damage, System.Globalization.CultureInfo.InvariantCulture);
            var boosted = new List<string>();
            foreach (DuelRecord d in recorder.Duels)
            {
                for (int side = 0; side < 2; side++)
                {
                    ActiveAugment first = d.Setups[side].Augments.FirstOrDefault(a => a.Id == Confluence.Sim.Content.Augments.AugmentsA.FirstContact.Id);
                    if (first.Id == null) continue;
                    float bonus = first.Rank >= 2 ? Confluence.Sim.Content.Augments.AugmentsA.FirstContact.Rank2Bonus : Confluence.Sim.Content.Augments.AugmentsA.FirstContact.Rank1Bonus;
                    foreach (DamageEvent e in d.World.EventLog.OfType<DamageEvent>())
                        if (e.Attacker == side && e.SourceId == "SPL-073" && e.Resolution != null && Math.Abs(e.Resolution.Raw - jet * (1f + bonus)) < 1e-3f)
                            boosted.Add("duel " + d.DuelId + " rank " + first.Rank + " raw " + e.Resolution.Raw);
                }
            }

            foreach (string b in boosted) _output.WriteLine("AUG-038 on SPL-073: " + b);
            Assert.Contains(boosted, b => b.Contains("rank 1"));
            Assert.Contains(boosted, b => b.Contains("rank 2"));
            Assert.Contains(recorder.TransformedCommits, c => c.Length > 0);

            // Both kits really fought with their own spells, signatures and passives (WIZ-002 Kiln Vent / Tempered Hands,
            // WIZ-010's signature and passive) through their grammars.
            var commits = log.OfType<CastDuelEvent>().Where(c => c.Cast.Kind == CastEventKind.Committed).Select(c => c.Cast).ToList();
            List<string> bram = commits.Where(c => c.SpellId.StartsWith("SPL-", StringComparison.Ordinal) && Cat.Spell(c.SpellId).WizardId == "WIZ-002").Select(c => c.SpellId).Distinct().ToList();
            List<string> tide = commits.Where(c => c.SpellId.StartsWith("SPL-", StringComparison.Ordinal) && Cat.Spell(c.SpellId).WizardId == "WIZ-010").Select(c => c.SpellId).Distinct().ToList();
            Assert.True(bram.Count >= 4, "WIZ-002 spells cast: " + string.Join(",", bram));
            Assert.True(tide.Count >= 4, "WIZ-010 spells cast: " + string.Join(",", tide));
            Assert.Contains(commits, c => c.SpellId == "WIZ-002");
            Assert.Contains(commits, c => c.SpellId == "WIZ-010");
            List<string> damaging = log.OfType<DamageEvent>().Where(e => e.Dealt > 0f && e.Attacker != e.Target && e.SourceId.StartsWith("SPL-", StringComparison.Ordinal))
                .Select(e => e.SourceId).Distinct().ToList();
            Assert.Contains(damaging, id => Cat.Spell(id).WizardId == "WIZ-002");
            Assert.Contains(damaging, id => Cat.Spell(id).WizardId == "WIZ-010");

            // Reactions (L144, L163): Bram's Fireball heat met Halen's Water Pool water and became steam in a duel.
            Assert.NotEmpty(recorder.Steam);
            Assert.Contains(recorder.Steam, s => s.SourceSpell == "SPL-009" && s.InsideLivePool);
        }

        /// <summary>A shared augment implemented by the augments-a batch (not a proof record or a wizard transformation).</summary>
        private static bool AugmentsAShared(string id) =>
            Cat.AugmentsById.TryGetValue(id, out AugmentDef def) && !def.IsTransformation &&
            ContentIds.TryParse(id, out ContentType type, out int number) && type == ContentType.Augment && number <= 144;

        /// <summary>
        /// Delegates to the bot's own heuristics, plus one scripted spell change: in the first shop where some owned, active
        /// shared augment's ongoing equipped-spell condition (L247) fails after swapping one or two equipped spells for
        /// unequipped kit spells, it submits that selection, so the augment is dormant at finalization (L249, L641); in the
        /// next shop it equips the original spells again, which reactivates the augment at the next finalization.
        /// </summary>
        private sealed class DormancyProbe : IEconomyAgent
        {
            private readonly IEconomyAgent _inner;
            private readonly MatchHost _host;
            private long _nextId = 1000000; // never collides with the bot's own request ids
            private int _lastShopRound = -1;

            public DormancyProbe(IEconomyAgent inner, MatchHost host)
            {
                _inner = inner;
                _host = host;
            }

            public int DormantRound { get; private set; } = -1;
            public int RestoredRound { get; private set; } = -1;
            public string Augment { get; private set; }
            public List<string> DormantSpells { get; private set; }
            public List<string> RestoredSpells { get; private set; }

            public void Act(EconomyTurn turn)
            {
                if (turn.Phase == EconomyPhase.Shop && turn.Round != _lastShopRound && !turn.IsReady)
                {
                    _lastShopRound = turn.Round;
                    if (DormantRound < 0) TrySwapOut(turn);
                    else if (RestoredRound < 0) Restore(turn);
                }

                _inner.Act(turn);
            }

            private void TrySwapOut(EconomyTurn turn)
            {
                Loadout loadout = turn.Loadout;
                List<string> equipped = loadout.EquippedSpellIds.ToList();
                List<string> spare = turn.Catalogue.Wizard(loadout.WizardId).SpellIds.Where(s => !equipped.Contains(s)).ToList();
                foreach (List<string> candidate in Swaps(equipped, spare))
                {
                    Loadout trial = loadout.Clone();
                    if (trial.SetEquippedSpells(candidate) != TransactionCode.Success) continue;
                    DormancyResolver.Resolve(trial);
                    OwnedAugment lost = trial.Augments.FirstOrDefault(a => !a.IsTransformation && a.IsDormant && loadout.Augments.Any(o => o.Id == a.Id && o.IsActive));
                    if (lost == null) continue;

                    CommandResult result = turn.Submit(PlayerCommand.SelectSpells(turn.Player, _nextId++, candidate));
                    Assert.True(result.Succeeded, result.Code.ToString());
                    Loadout live = _host.Economy.Player(turn.Player).Loadout;
                    Assert.True(live.Augments.Single(a => a.Id == lost.Id).IsDormant); // the shop resolves dormancy at once (L249)
                    DormantRound = turn.Round;
                    Augment = lost.Id;
                    DormantSpells = candidate;
                    RestoredSpells = equipped;
                    return;
                }
            }

            private void Restore(EconomyTurn turn)
            {
                CommandResult result = turn.Submit(PlayerCommand.SelectSpells(turn.Player, _nextId++, RestoredSpells));
                Assert.True(result.Succeeded, result.Code.ToString());
                Assert.True(_host.Economy.Player(turn.Player).Loadout.Augments.Single(a => a.Id == Augment).IsActive);
                RestoredRound = turn.Round;
            }

            /// <summary>Every equipped list with one, then two, equipped spells replaced by unequipped kit spells, in a fixed order.</summary>
            private static IEnumerable<List<string>> Swaps(List<string> equipped, List<string> spare)
            {
                for (int i = 0; i < equipped.Count; i++)
                    foreach (string s in spare)
                    {
                        var c = new List<string>(equipped);
                        c[i] = s;
                        yield return c;
                    }

                if (spare.Count < 2) yield break;
                for (int i = 0; i < equipped.Count; i++)
                    for (int j = i + 1; j < equipped.Count; j++)
                    {
                        var c = new List<string>(equipped);
                        c[i] = spare[0];
                        c[j] = spare[1];
                        yield return c;
                    }
            }
        }

        private sealed class DuelRecord
        {
            public int DuelId;
            public int Round;
            public DuelWorld World;
            public int[] Players = new int[2];
            public DuelPlayerSetup[] Setups = new DuelPlayerSetup[2];
            public List<string>[] EconomyActive = new List<string>[2];
            public List<string>[] Owned = new List<string>[2];
            public Dictionary<string, List<string>>[] Transforms = new Dictionary<string, List<string>>[2];
            public HashSet<string>[] Attached = new HashSet<string>[2];

            public int SideOf(int player) => Players[0] == player ? 0 : 1;
        }

        private sealed class SteamRecord
        {
            public int DuelId;
            public int Tick;
            public string SourceSpell;
            public bool InsideLivePool;
        }

        /// <summary>Collects per-duel setups, attachments, offers and reactions while a hosted match runs.</summary>
        private sealed class MatchRecorder
        {
            private readonly MatchHost _host;

            public MatchRecorder(MatchHost host)
            {
                _host = host;
                host.DuelStarted += OnStarted;
                host.DuelStepped += OnStepped;
                host.Outbox.Queued += OnMessage;
            }

            public List<DuelRecord> Duels { get; } = new List<DuelRecord>();
            public List<SteamRecord> Steam { get; } = new List<SteamRecord>();
            public List<ShopCard> ShopCards { get; } = new List<ShopCard>();
            public List<DraftCard> DraftCards { get; } = new List<DraftCard>();
            public Dictionary<int, IReadOnlyList<ShopCard>> RoundOneShops { get; } = new Dictionary<int, IReadOnlyList<ShopCard>>();
            public List<string> TransformedCommits { get; } = new List<string>();

            private void OnMessage(HostMessage m)
            {
                if (m is ShopStateMessage shop)
                {
                    ShopCards.AddRange(shop.Cards);
                    if (_host.Flow.Round == 1 && !RoundOneShops.ContainsKey(shop.Player))
                        RoundOneShops.Add(shop.Player, shop.Cards); // the opening offer, before any purchase
                }
                else if (m is DraftStateMessage draft)
                {
                    DraftCards.AddRange(draft.Cards.Where(c => c != null));
                }
            }

            private void OnStarted(RunningDuel d)
            {
                var record = new DuelRecord { DuelId = d.DuelId, Round = _host.Flow.Round, World = d.World };
                record.Players[0] = d.Setup.Side0Player;
                record.Players[1] = d.Setup.Side1Player;
                for (int side = 0; side < 2; side++)
                {
                    int player = record.Players[side];
                    DuelPlayer p = d.World.Player(side);
                    record.Setups[side] = p.Setup;
                    Loadout snapshot = _host.Economy.RoundLoadout(player);
                    record.EconomyActive[side] = snapshot.Augments.Where(a => a.IsActive).Select(a => a.Id + "r" + a.Rank).ToList();
                    record.Owned[side] = snapshot.Augments.Select(a => a.Id).ToList();
                    record.Transforms[side] = p.Caster.Loadout.Spells.Where(f => f != null).ToDictionary(f => f.SpellId, f => f.AppliedTransforms.ToList());
                    EffectHost host = d.World.Effects.Host(side);
                    var attached = new HashSet<string>(StringComparer.Ordinal);
                    foreach (EffectHook h in host.Hooks) attached.Add(h.SourceId);
                    foreach (Effect e in host.Effects) attached.Add(e.SourceId);
                    foreach (ConditionalModifier c in host.ConditionalModifiers) attached.Add(c.SourceId);
                    foreach (StatModifier s in host.Stats.Modifiers) attached.Add(s.SourceId);
                    foreach (ActiveFormStage s in AugmentFormStages.For(p.Setup, Cat, ContentRegistry.Default)) attached.Add(s.AugmentId);
                    record.Attached[side] = attached;
                }

                Duels.Add(record);
            }

            private void OnStepped(RunningDuel d)
            {
                DuelWorld w = d.World;
                foreach (DuelEvent e in w.TickEvents)
                {
                    if (e is CastDuelEvent c && c.Cast.Kind == CastEventKind.Committed && c.Cast.Slot >= 0)
                    {
                        SpellFormLookup(w, c.Player, c.Cast.SpellId, out IReadOnlyList<string> transforms);
                        if (transforms != null && transforms.Count > 0) TransformedCommits.Add(c.Cast.SpellId + ":" + string.Join(",", transforms));
                    }
                }

                var materials = (ReactionMaterials)w.Materials;
                StepResult step = materials.LastStep;
                if (step == null) return;
                foreach (ReactionEvent e in step.Events)
                {
                    if (e.Kind != ReactionKind.Steam) continue;
                    var source = w.EventLog.FirstOrDefault(x => x.Id == e.SourceEventId) as CastDuelEvent;
                    ObscurationVolume volume = materials.World.Volumes.FirstOrDefault(v => v.Id == e.TargetId);
                    Steam.Add(new SteamRecord
                    {
                        DuelId = d.DuelId,
                        Tick = e.Tick,
                        SourceSpell = source?.Cast.SpellId,
                        InsideLivePool = volume != null && InsideLivePool(w, volume),
                    });
                }
            }

            private static void SpellFormLookup(DuelWorld w, int player, string spellId, out IReadOnlyList<string> transforms)
            {
                SpellForm form = w.Player(player).Caster?.Loadout.FormFor(spellId);
                transforms = form?.AppliedTransforms;
            }

            /// <summary>Some steam column lies inside a Water Pool (SPL-076, 3 m radius) that is alive at this tick.</summary>
            private static bool InsideLivePool(DuelWorld w, ObscurationVolume steam)
            {
                var pools = new List<SpellEvent>();
                var ended = new HashSet<int>();
                foreach (DuelEvent e in w.EventLog)
                {
                    if (!(e is SpellEvent s) || s.SpellId != "SPL-076") continue;
                    if (s.Kind == SpellEventKind.FieldCreated) pools.Add(s);
                    else if (s.Kind == SpellEventKind.FieldEnded) ended.Add(s.EntityId);
                }

                const float PoolRadius = 3f; // SPL-076 "Place a connected 3 m-radius shallow pool"
                ColumnSet columns = steam.Columns;
                foreach (SpellEvent pool in pools)
                {
                    if (ended.Contains(pool.EntityId)) continue;
                    for (int z = columns.MinZ; z <= columns.MaxZ; z++)
                    for (int x = columns.MinX; x <= columns.MaxX; x++)
                        if (columns.Contains(x, z) && Vec2.Distance(steam.ColumnCenter(x, z), pool.Position.XZ()) <= PoolRadius) return true;
                }

                return false;
            }

            public IEnumerable<string> Summary()
            {
                foreach (DuelRecord d in Duels)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        yield return "duel " + d.DuelId + " r" + d.Round + " side " + side + " " + d.Setups[side].WizardId + " spells=[" + string.Join(",", d.Setups[side].EquippedSpellIds) +
                            "] augments=[" + string.Join(",", d.Setups[side].Augments.Select(a => a.Id + "r" + a.Rank)) + "] owned=[" + string.Join(",", d.Owned[side]) + "] result=" + d.World.Result?.Reason;
                    }
                }

                foreach (SteamRecord s in Steam) yield return "steam duel " + s.DuelId + " t" + s.Tick + " from " + s.SourceSpell + (s.InsideLivePool ? " in a live Water Pool" : "");
                foreach (string c in TransformedCommits.Distinct()) yield return "transformed commit " + c;
            }
        }
    }
}
