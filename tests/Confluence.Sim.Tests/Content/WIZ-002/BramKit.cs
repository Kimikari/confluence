using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Reactions;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.Tests.Spells;
using Confluence.Sim.Tests.World;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Content.Wiz002
{
    /// <summary>Player 0's loadout from the catalogue and registered behaviors; player 1's fixed (or none).</summary>
    internal sealed class MixedLoadouts : ISpellLoadoutSource
    {
        private readonly CatalogueLoadouts _catalogue;
        private readonly SpellLoadout _opponent;

        public MixedLoadouts(CatalogueLoadouts catalogue, SpellLoadout opponent)
        {
            _catalogue = catalogue;
            _opponent = opponent;
        }

        public SpellLoadout Build(DuelWorld world, int player) => player == 1 && _opponent != null ? _opponent : _catalogue.Build(world, player);
    }

    /// <summary>
    /// WIZ-002 Bram Cinder duels driven by real per-tick input through the crest-release grammar (D-056), with the
    /// duel's own reaction world (the default <see cref="ReactionMaterials"/>), the catalogue's props, and the shipped
    /// content registry. Player 0 is Bram; player 1 is a wizard-less dummy (no passive) or a test caster.
    /// </summary>
    internal static class BramKit
    {
        public const int NormalHold = 15;   // a normal release (0.25 s)
        public const int CrestHold = 27;    // the 0.45 s crest

        public static readonly string[] DefaultKit = { "SPL-009", "SPL-010", "SPL-011", "SPL-012", "SPL-013", "SPL-014" };
        public static readonly string[] LateKit = { "SPL-015", "SPL-016", "SPL-011", "SPL-012", "SPL-013", "SPL-014" };

        public static Catalogue Catalogue => SpellTestKit.Catalogue;

        public static DuelPlayerSetup Bram(IEnumerable<string> spells = null, params ActiveAugment[] augments) =>
            DuelPlayerSetup.FromCatalogue(Catalogue, "A", "WIZ-002", spells ?? DefaultKit, augments: augments);

        public static ActiveAugment Aug(string id, int rank = 1) => new ActiveAugment(id, rank);

        public static DuelPlayerSetup Dummy() => new DuelPlayerSetup { PlayerId = "B", WizardId = null };

        /// <summary>A flat duel in combat; player 1 at (0,0,6) facing player 0 at (0,0,-6).</summary>
        public static DuelWorld Duel(DuelPlayerSetup bram = null, ArenaLayout layout = null, SpellLoadout opponent = null, DuelPlayerSetup other = null,
            IWorldMaterials materials = null)
        {
            var runtime = new SpellRuntime(new MixedLoadouts(new CatalogueLoadouts(Catalogue), opponent));
            var options = new DuelWorldOptions { AlcoveTicks = 0, SpellRuntime = runtime, Catalogue = Catalogue, Materials = materials };
            return new DuelWorld(layout ?? TestWorlds.Flat(), bram ?? Bram(), other ?? Dummy(), options);
        }

        public static SpellRuntime Runtime(DuelWorld w) => (SpellRuntime)w.Spells;

        public static ReactionWorld Reactions(DuelWorld w) => ((ReactionMaterials)w.Materials).World;

        public static ReactionMaterials Materials(DuelWorld w) => (ReactionMaterials)w.Materials;

        public static int SlotOf(DuelWorld w, string spellId) => w.Player(0).Setup.EquippedSpellIds.IndexOf(spellId);

        public static SpellForm Form(DuelWorld w, string spellId) => w.Player(0).Caster.FormFor(spellId);

        /// <summary>Bram's input aiming at a point, with a selected slot.</summary>
        public static PlayerInput AimAt(DuelWorld w, Vec3 point, int slot = 0) => SpellTestKit.Aim(w, 0, point).Selecting(slot);

        /// <summary>Holds primary for <paramref name="hold"/> ticks (press tick included) on a slot and releases.</summary>
        public static void Cast(DuelWorld w, int slot, int hold = NormalHold, PlayerInput? aim = null, PlayerInput? other = null)
        {
            PlayerInput a = (aim ?? TestWorlds.A).Selecting(slot);
            PlayerInput b = other ?? TestWorlds.B;
            for (int i = 0; i < hold; i++) w.Step(a.With(InputButtons.Primary), b);
            w.Step(a, b);
        }

        public static void Cast(DuelWorld w, string spellId, int hold = NormalHold, PlayerInput? aim = null, PlayerInput? other = null) =>
            Cast(w, SlotOf(w, spellId), hold, aim, other);

        /// <summary>One tick pressing the spell recast on a slot.</summary>
        public static void Recast(DuelWorld w, int slot, PlayerInput? aim = null, PlayerInput? other = null)
        {
            PlayerInput a = (aim ?? TestWorlds.A).Selecting(slot);
            w.Step(a.With(InputButtons.Recast), other ?? TestWorlds.B);
            w.Step(a, other ?? TestWorlds.B);
        }

        /// <summary>One tick pressing the signature.</summary>
        public static void Signature(DuelWorld w, PlayerInput? aim = null, PlayerInput? other = null)
        {
            PlayerInput a = aim ?? TestWorlds.A;
            w.Step(a.With(InputButtons.Signature), other ?? TestWorlds.B);
            w.Step(a, other ?? TestWorlds.B);
        }

        public static void Run(DuelWorld w, int ticks, PlayerInput? a = null, PlayerInput? b = null) => SpellTestKit.Run(w, ticks, a, b);

        public static int RunUntil(DuelWorld w, System.Func<bool> done, int max, PlayerInput? a = null, PlayerInput? b = null) =>
            SpellTestKit.RunUntil(w, done, max, a, b);

        public static List<DamageEvent> DamageTo(DuelWorld w, int target, string source = null) => SpellTestKit.DamageTo(w, target, source);

        public static float Dealt(DuelWorld w, int target, string source = null) => SpellTestKit.TotalDealt(w, target, source);

        public static IEnumerable<SpellEvent> Spell(DuelWorld w, SpellEventKind kind) => SpellTestKit.SpellEvents(w, kind);

        public static IEnumerable<Confluence.Sim.Casting.CastDuelEvent> Casts(DuelWorld w, CastEventKind kind, int player = 0) =>
            SpellTestKit.CastEvents(w, kind, player);

        public static T Entity<T>(DuelWorld w) where T : SpellEntity => Runtime(w).Entities.OfType<T>().FirstOrDefault();

        public static float Mana(DuelWorld w, int player = 0) => w.Player(player).Resources.Mana;

        // ----- test casters for player 1 ------------------------------------------------------------------------

        /// <summary>A test projectile (no record): instant release on a primary press.</summary>
        public static SpellForm Bolt(string id, float damage, DamageChannel channel = DamageChannel.Physical, float speed = 40f)
        {
            SpellFormBuilder b = SpellTestKit.Form(id, mana: 0f, cooldownSec: 0.5f, channel: channel, tags: new[] { "projectile", "damaging" });
            b.Delivery = new ProjectileDelivery { Speed = speed, MaxRange = 40f };
            b.Payload.Direct = damage;
            return b.Build();
        }

        /// <summary>A test cone (not a projectile): instant release on a primary press.</summary>
        public static SpellForm Swipe(string id, float damage, float reach = 6f, DamageChannel channel = DamageChannel.Physical)
        {
            SpellFormBuilder b = SpellTestKit.Form(id, mana: 0f, cooldownSec: 0.5f, range: reach, channel: channel, tags: new[] { "cone", "damaging", "melee" });
            b.Delivery = new ConeDelivery { HalfAngleDeg = 45f, Reach = reach };
            b.Payload.Direct = damage;
            return b.Build();
        }

        public static SpellLoadout Caster(params SpellForm[] forms) => SpellTestKit.Loadout(new TapGrammar(), forms);

        /// <summary>Player 1 fires its slot once (aiming at a point) while player 0 idles with the given input.</summary>
        public static void OpponentFires(DuelWorld w, int slot, Vec3 at, PlayerInput? bram = null)
        {
            PlayerInput aim = SpellTestKit.Aim(w, 1, at).Selecting(slot);
            w.Step(bram ?? TestWorlds.A, aim.With(InputButtons.Primary));
            w.Step(bram ?? TestWorlds.A, aim);
        }

        public static Vec3 Chest(DuelWorld w, int player) => w.Player(player).Position + new Vec3(0f, 1.1f, 0f);

        // ----- layouts --------------------------------------------------------------------------------------------

        public static ArenaLayout FlatWith(params MaterialPatch[] patches)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Materials.AddRange(patches);
            return layout;
        }

        public static MaterialPatch Water(Vec2 center, float halfX, float halfZ, int units = 2) => WorldDuels.WaterBed(center, halfX, halfZ, units);

        public static MaterialPatch Ice(Vec2 center, float halfX, float halfZ) => WorldDuels.IcePatch(center, halfX, halfZ);
    }
}
