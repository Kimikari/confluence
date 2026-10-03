using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.AugmentsA
{
    /// <summary>
    /// Duels for the augments-a records: real <see cref="DuelWorld"/>s with the catalogue, the spell runtime over each
    /// player's catalogue loadout (WIZ-002's crest-release grammar, WIZ-010's pressure meter), reaction materials, props,
    /// and an explicit registry that holds only the behaviors under test plus the implemented spells — so wizard
    /// passives and equipment behaviors registered by other content never change these numbers.
    /// </summary>
    internal static class AKit
    {
        public const string Bram = "WIZ-002";
        public const string Halen = "WIZ-010";

        public static Catalogue Catalogue => WorldDuels.Catalogue;

        /// <summary>The implemented spell behaviors from the shipped registry plus the listed records' behaviors.</summary>
        public static ContentRegistry Registry(params string[] ids) => Registry(ids, null);

        public static ContentRegistry Registry(IEnumerable<string> ids, IEnumerable<KeyValuePair<string, object>> extra)
        {
            var entries = new List<KeyValuePair<string, object>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> e in extra ?? Enumerable.Empty<KeyValuePair<string, object>>())
            {
                entries.Add(e);
                seen.Add(e.Key);
            }

            foreach (string id in ContentRegistry.Default.Ids)
            {
                if (id.StartsWith("SPL-", StringComparison.Ordinal) && seen.Add(id))
                {
                    entries.Add(new KeyValuePair<string, object>(id, Activator.CreateInstance(ContentRegistry.Default.TypeOf(id))));
                }
            }

            foreach (string id in ids ?? Array.Empty<string>())
            {
                if (seen.Add(id))
                {
                    Type type = ContentRegistry.Default.TypeOf(id);
                    Assert.True(type != null, id + " has no registered behavior");
                    entries.Add(new KeyValuePair<string, object>(id, Activator.CreateInstance(type)));
                }
            }

            return ContentRegistry.FromBehaviors(entries);
        }

        public static ActiveAugment[] Aug(string id, int rank = 1) => new[] { new ActiveAugment(id, rank) };

        /// <summary>A catalogue setup: the wizard's default six spells, the listed equipment, the augments.</summary>
        public static DuelPlayerSetup Setup(string playerId, string wizardId, IEnumerable<ActiveAugment> augments = null,
            params KeyValuePair<string, EquipmentSlot>[] equipment)
        {
            return DuelPlayerSetup.FromCatalogue(Catalogue, playerId, wizardId, equipment: equipment, augments: augments);
        }

        /// <summary>
        /// A duel in combat from tick 1 (unless an alcove is given): player 0 as configured, player 1 a plain WIZ-002 setup
        /// with no spells unless given. The registry holds the augments of both setups plus the implemented spells.
        /// </summary>
        public static DuelWorld Duel(DuelPlayerSetup a, DuelPlayerSetup b = null, ArenaLayout layout = null, ContentRegistry registry = null, int alcoveTicks = 0)
        {
            b = b ?? TestWorlds.Setup("B");
            if (registry == null)
            {
                var ids = new List<string>();
                foreach (ActiveAugment aug in a.Augments.Concat(b.Augments)) ids.Add(aug.Id);
                registry = Registry(ids.ToArray());
            }

            var options = new DuelWorldOptions
            {
                AlcoveTicks = alcoveTicks,
                Content = registry,
                Catalogue = Catalogue,
                SpellRuntime = SpellRuntime.FromCatalogue(Catalogue, registry),
            };
            return new DuelWorld(layout ?? TestWorlds.Flat(), a, b, options);
        }

        /// <summary>WIZ-002 (or another wizard) with one augment at a rank against the plain dummy.</summary>
        public static DuelWorld With(string augmentId, int rank, string wizardId = Bram, ArenaLayout layout = null,
            params KeyValuePair<string, EquipmentSlot>[] equipment)
        {
            return Duel(Setup("A", wizardId, Aug(augmentId, rank), equipment), layout: layout);
        }

        /// <summary>The same duel without the augment (the control case).</summary>
        public static DuelWorld Without(string wizardId = Bram, ArenaLayout layout = null, params KeyValuePair<string, EquipmentSlot>[] equipment)
        {
            return Duel(Setup("A", wizardId, null, equipment), layout: layout);
        }

        public static KeyValuePair<string, EquipmentSlot> Item(string id, EquipmentSlot slot = EquipmentSlot.MainFocus) =>
            new KeyValuePair<string, EquipmentSlot>(id, slot);

        /// <summary>Light blade (EQP-031 Apprentice Dagger), spear (EQP-034 Gutter Pike), heavy (EQP-035 Basalt Maul).</summary>
        public const string LightBlade = "EQP-031";
        public const string Spear = "EQP-034";
        public const string Heavy = "EQP-035";

        // ----- positions and inputs ------------------------------------------------------------------------

        /// <summary>Puts player 0 at the origin facing +Z and player 1 at distance d ahead facing back.</summary>
        public static void FaceOff(DuelWorld world, float distance)
        {
            TestWorlds.Place(world, 0, Vec3.Zero, 0f);
            TestWorlds.Place(world, 1, new Vec3(0f, 0f, distance), 180f);
        }

        public static PlayerInput A => TestWorlds.A;
        public static PlayerInput B => TestWorlds.B;

        public static PlayerInput Press(PlayerInput input, InputButtons button) => input.With(button);

        public static PlayerInput Walk(float yawDeg = 0f, float x = 0f, float y = 1f) => WorldDuels.Walk(yawDeg, x, y);

        public static void Step(DuelWorld world, int ticks, PlayerInput? a = null, PlayerInput? b = null)
        {
            PlayerInput ia = a ?? A;
            PlayerInput ib = b ?? B;
            for (int i = 0; i < ticks && !world.IsOver; i++) world.Step(ia, ib);
        }

        /// <summary>One step with player 0 pressing melee (player 1 idle); returns the strike's start event or null.</summary>
        public static MeleeEvent Strike(DuelWorld world, PlayerInput? a = null, PlayerInput? b = null, int player = 0)
        {
            int before = world.EventLog.Count;
            PlayerInput ia = a ?? A;
            PlayerInput ib = b ?? B;
            if (player == 0) ia = ia.With(InputButtons.Melee); else ib = ib.With(InputButtons.Melee);
            world.Step(ia, ib);
            return world.EventLog.Skip(before).OfType<MeleeEvent>().FirstOrDefault(m => m.Player == player && m.Change == MeleeChange.Started);
        }

        /// <summary>Steps until the player's current strike cycle ends (idle inputs).</summary>
        public static void FinishStrike(DuelWorld world, int player = 0, PlayerInput? a = null, PlayerInput? b = null)
        {
            for (int i = 0; i < 200 && world.Player(player).Melee.Busy; i++) Step(world, 1, a, b);
        }

        /// <summary>WIZ-002 crest release: hold primary for holdTicks (press tick included), then release.</summary>
        public static void HoldAndRelease(DuelWorld world, int holdTicks, PlayerInput? a = null, PlayerInput? b = null)
        {
            PlayerInput ia = a ?? A;
            PlayerInput ib = b ?? B;
            for (int i = 0; i < holdTicks; i++) world.Step(ia.With(InputButtons.Primary), ib);
            world.Step(ia, ib);
        }

        /// <summary>A normal WIZ-002 release (0.25 s hold, outside the 0.40-0.50 s crest).</summary>
        public const int NormalHold = 15;

        /// <summary>A crest release (0.45 s): the mastery reward (+12% direct damage on damaging spells).</summary>
        public const int CrestHold = 27;

        public static PlayerInput SelectSlot(PlayerInput input, int slot)
        {
            PlayerInput s = input;
            s.SelectedSlot = slot;
            return s;
        }

        // ----- inspection ------------------------------------------------------------------------------------

        public static IEnumerable<T> Events<T>(DuelWorld world) where T : DuelEvent => world.EventLog.OfType<T>();

        public static List<DamageEvent> Hits(DuelWorld world, int target, DamageKind? kind = null) =>
            world.EventLog.OfType<DamageEvent>().Where(d => d.Target == target && (kind == null || d.Kind == kind.Value)).ToList();

        public static IEnumerable<CastDuelEvent> Casts(DuelWorld world, CastEventKind kind, int player = 0) =>
            world.EventLog.OfType<CastDuelEvent>().Where(e => e.Kind == kind && e.Player == player);

        public static IEnumerable<EffectEvent> Activations(DuelWorld world, string sourceId, int player = 0) =>
            world.EventLog.OfType<EffectEvent>().Where(e => e.Player == player && e.SourceId == sourceId && e.Change == EffectChange.Activated);

        public static float Speed(DuelWorld world, int player) => new Vec3(world.Player(player).Motor.Velocity.X, 0f, world.Player(player).Motor.Velocity.Z).Length();

        /// <summary>The catalogue record text the behavior's numbers come from (D-011: each quoted number is in it).</summary>
        public static AugmentDef Record(string id) => Catalogue.Augment(id);

        /// <summary>Asserts that every phrase appears in the record's condition, rank texts, or drawback.</summary>
        public static void AssertQuotes(string id, params string[] phrases)
        {
            AugmentDef def = Record(id);
            string text = def.Condition + " | " + def.Rank1 + " | " + def.Rank2 + " | " + def.Drawback;
            foreach (string p in phrases)
            {
                Assert.True(text.Contains(p), id + " text does not contain \"" + p + "\": " + text);
            }
        }

        public static int Ticks(float seconds) => Confluence.Core.Time.Ticks.FromSeconds(seconds);
    }
}
