using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Sim.Casting;
using Confluence.Sim.Content.Wizards.HalenTide;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.World.Materials;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Halen
{
    /// <summary>
    /// A duel with WIZ-010 Halen Tide as player 0 (real catalogue kit, pressure-metering grammar, registered behaviors,
    /// the duel's own reaction world) and a target as player 1. Every input of player 0 carries the selected slot, so a
    /// cast is never cancelled by an accidental slot change.
    /// </summary>
    internal sealed class HalenDuel
    {
        /// <summary>The bot script's mastery timing: hold to 51, vent ticks 22-27, release at tick 33 inside 45-55.</summary>
        public const int MasteryRelease = 33;
        public const int VentFrom = 22;
        public const int VentTo = 28;

        /// <summary>A plain normal release: pressure 54 at tick 24 inside the safe band.</summary>
        public const int NormalRelease = 24;

        public HalenDuel(DuelWorld world)
        {
            World = world;
        }

        public DuelWorld World { get; }

        /// <summary>Player 0's view and movement (the slot is added on every step).</summary>
        public PlayerInput Halen { get; set; } = TestWorlds.A;

        /// <summary>Player 1's input.</summary>
        public PlayerInput Other { get; set; } = TestWorlds.B;

        public int Slot { get; set; }

        public DuelPlayer P0 => World.Player(0);
        public DuelPlayer P1 => World.Player(1);
        public SpellRuntime Runtime => (SpellRuntime)World.Spells;
        public ReactionWorld Reactions => ((ReactionMaterials)World.Materials).World;
        public ReactionMaterials Materials => (ReactionMaterials)World.Materials;
        public CastMachine Machine => P0.Caster.Machine;

        public void Step(InputButtons extra = InputButtons.None, InputButtons other = InputButtons.None) =>
            World.Step(Halen.Selecting(Slot).With(extra), Other.With(other));

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks && !World.IsOver; i++) Step();
        }

        public void RunSeconds(double seconds) => Run(Ticks.FromSeconds(seconds));

        public int RunUntil(Func<bool> done, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (done()) return i;
                Step();
            }

            return maxTicks;
        }

        /// <summary>Aims player 0 at a point.</summary>
        public void AimAt(Vec3 point)
        {
            Vec3 d = point - P0.Eye;
            PlayerInput a = Halen;
            a.YawDeg = CMath.YawOf(d);
            a.PitchDeg = CMath.PitchOf(d);
            Halen = a;
        }

        /// <summary>Aims player 0 at player 1's chest.</summary>
        public void AimAtOther() => AimAt(P1.Position + new Vec3(0f, 1.1f, 0f));

        /// <summary>
        /// Casts the spell in a slot through WIZ-010's pressure-metering grammar: Primary held for the given ticks (the
        /// press tick included), then released; a mastery attempt vents with Modifier A on the bot's ticks. Returns the
        /// commitment event, or null when nothing committed.
        /// </summary>
        public CastDuelEvent Cast(int slot, int hold = NormalRelease, bool mastery = false)
        {
            Slot = slot;
            int before = Commits().Count;
            if (mastery) hold = MasteryRelease;
            for (int k = 0; k < hold; k++)
            {
                InputButtons buttons = InputButtons.Primary;
                if (mastery && k >= VentFrom && k < VentTo) buttons |= InputButtons.ModifierA;
                Step(buttons);
            }

            Step(); // release: the grammar commits on this tick
            List<CastDuelEvent> commits = Commits();
            return commits.Count > before ? commits[commits.Count - 1] : null;
        }

        /// <summary>Activates the signature (its own binding, no grammar, spec L472).</summary>
        public CastDuelEvent Signature()
        {
            int before = Commits().Count;
            Step(InputButtons.Signature);
            List<CastDuelEvent> commits = Commits();
            return commits.Count > before ? commits[commits.Count - 1] : null;
        }

        /// <summary>Presses the spell-recast binding once on the selected slot.</summary>
        public void Recast() => Step(InputButtons.Recast);

        public List<CastDuelEvent> Commits() => SpellTestKit.CastEvents(World, CastEventKind.Committed, 0).ToList();

        public List<DamageEvent> DamageTo(int target, string source = null) => SpellTestKit.DamageTo(World, target, source);

        public List<T> Events<T>() where T : DuelEvent => World.EventLog.OfType<T>().ToList();

        public List<TideEvent> Tide(TideEventKind kind) => World.EventLog.OfType<TideEvent>().Where(e => e.Kind == kind).ToList();

        public List<SpellEvent> SpellEvents(SpellEventKind kind) => SpellTestKit.SpellEvents(World, kind).ToList();

        public CellUnits Units(Vec3 point) => Reactions.UnitsAt(point);

        /// <summary>Material placed straight into the duel's reaction world (applied at the next step's direct-impact phase).</summary>
        public void Deposit(Material material, int strength, Footprint footprint, float lifetimeSec = 6f, int owner = 1)
        {
            Reactions.Deposit(new MaterialDeposit
            {
                Material = material,
                Strength = strength,
                Footprint = footprint,
                SurfaceY = 0f,
                LifetimeTicks = Ticks.FromSeconds(lifetimeSec),
                Owner = owner,
                SourceEventId = 0,
            });
        }

        /// <summary>Total units of a material over a footprint.</summary>
        public int Total(Material material, Footprint footprint)
        {
            var cells = new List<int>();
            Reactions.Grid.Rasterize(footprint, cells);
            int total = 0;
            foreach (int c in cells)
            {
                CellUnits u = Reactions.UnitsAtCell(c);
                total += material == Material.Water ? u.Water : material == Material.Heat ? u.Heat : material == Material.Frost ? u.Frost : u.Venom;
            }

            return total;
        }
    }

    /// <summary>Setups and worlds for the WIZ-010 tests.</summary>
    internal static class HalenTestKit
    {
        public const string Wizard = "WIZ-010";

        public static Catalogue Catalogue => SpellTestKit.Catalogue;

        /// <summary>Halen with the given spells (default kit when null) and transformation ranks.</summary>
        public static DuelPlayerSetup Halen(IEnumerable<string> spells = null, params ActiveAugment[] augments) =>
            DuelPlayerSetup.FromCatalogue(Catalogue, "H", Wizard, spells, null, augments);

        public static ActiveAugment Aug(string id, int rank = 1) => new ActiveAugment(id, rank);

        /// <summary>A target without spells (Bram's grammar, empty kit).</summary>
        public static DuelPlayerSetup Dummy() => TestWorlds.Setup("B");

        /// <summary>Bram Cinder with his default kit (SPL-009 Fireball in slot 0).</summary>
        public static DuelPlayerSetup Bram() => DuelPlayerSetup.FromCatalogue(Catalogue, "B", "WIZ-002");

        public static HalenDuel Duel(DuelPlayerSetup halen = null, DuelPlayerSetup other = null, ArenaLayout layout = null, int combatTicks = -1)
        {
            var runtime = SpellRuntime.FromCatalogue(Catalogue);
            DuelWorld world = TestWorlds.Duel(layout, halen ?? Halen(), other ?? Dummy(), 0, combatTicks, runtime, null);
            return new HalenDuel(world);
        }

        /// <summary>Halen with one spell in slot 0 (and the rest of the default kit after it).</summary>
        public static HalenDuel With(string spellId, params ActiveAugment[] augments)
        {
            var spells = new List<string> { spellId };
            foreach (string id in Catalogue.Wizard(Wizard).SpellIds)
            {
                if (spells.Count >= GlobalRules.Loadout.EquippedSpellSlots) break;
                if (!spells.Contains(id)) spells.Add(id);
            }

            return Duel(Halen(spells, augments));
        }

        /// <summary>The resolved form of a spell for a set of transformations.</summary>
        public static SpellForm Form(string spellId, params ActiveAugment[] augments)
        {
            HalenDuel d = With(spellId, augments);
            return d.P0.Caster.Loadout.FormFor(spellId);
        }

        /// <summary>The flat test layout plus solids.</summary>
        public static ArenaLayout Layout(params Solid[] solids) => SpellTestKit.FlatWith(solids);
    }
}
