using System;
using System.Collections.Generic;
using System.Linq;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Hashing;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.Spells;
using Confluence.Sim.World;
using InputButtons = Confluence.Casting.InputButtons;

namespace Confluence.Sim.Tests.Spells
{
    /// <summary>A test grammar: a primary press opens a preparation that commits after a fixed number of ticks.</summary>
    internal sealed class TapGrammar : GrammarBase
    {
        private readonly int _commitAfter;

        public TapGrammar(int commitAfterTicks = 0, MasteryResult? result = null)
            : base("TEST", GrammarFamily.ButtonTiming, InputRoute.Standard)
        {
            _commitAfter = commitAfterTicks;
            Result = result ?? MasteryResult.Normal(MissReason.NotApplicable);
        }

        public MasteryResult Result { get; set; }

        /// <summary>Every frame the grammar observed, for server-generated combat event checks.</summary>
        public List<CastInputFrame> Frames { get; } = new List<CastInputFrame>();

        public override void Observe(in CastInputFrame frame) => Frames.Add(frame);

        public override bool WantsToBegin(in CastInputFrame frame, CastableSpell spell) => frame.WasPressed(InputButtons.Primary);

        public override void Begin(in CastInputFrame frame, in GrammarContext context)
        {
        }

        public override GrammarStep Advance(in CastInputFrame frame, in GrammarContext context) =>
            context.Elapsed >= _commitAfter ? GrammarStep.Commit(Result) : GrammarStep.Continue;
    }

    /// <summary>Records every deposit the spell runtime queues through the materials seam.</summary>
    internal sealed class RecordingMaterials : IWorldMaterials
    {
        public List<WorldDeposit> Deposits { get; } = new List<WorldDeposit>();

        public float Obscuring { get; set; }

        public void QueueDeposit(in WorldDeposit deposit) => Deposits.Add(deposit);

        public void Step(IMaterialsContext context)
        {
        }

        public float ObscuringPathLength(Vec3 from, Vec3 to) => Obscuring;

        public SurfaceInfo QuerySurface(Vec3 feet) => SurfaceInfo.Normal;

        public void Hash(ref StateHasher hasher) => hasher.Add(Deposits.Count);
    }

    /// <summary>Duel worlds with a spell runtime and helpers to cast, run, and inspect.</summary>
    internal static class SpellTestKit
    {
        private static readonly Lazy<Catalogue> LazyCatalogue = new Lazy<Catalogue>(() => Catalogue.Load(Catalogue.FindDefaultPath()));

        public static Catalogue Catalogue => LazyCatalogue.Value;

        /// <summary>A synthetic form: no record, an id with the SPL- prefix so hits count as spell hits.</summary>
        public static SpellFormBuilder Form(string id = "SPL-T01", float mana = 10f, float cooldownSec = 1f, float castTimeSec = 0f, float range = 30f,
            DamageChannel channel = DamageChannel.Thermal, params string[] tags)
        {
            SpellFormBuilder b = SpellFormBuilder.Synthetic(id, "WIZ-002");
            b.Mana = mana;
            b.CooldownSec = cooldownSec;
            b.CastTimeSec = castTimeSec;
            b.Range = range;
            b.Channel = channel;
            b.Payload.Channel = channel;
            b.Tags.AddRange(tags);
            return b;
        }

        public static SpellLoadout Loadout(IGrammar grammar, params SpellForm[] forms) => new SpellLoadout(grammar, forms);

        public static SpellLoadout Loadout(params SpellForm[] forms) => new SpellLoadout(new TapGrammar(), forms);

        /// <summary>A flat duel in combat with player 0 holding the given forms (player 1 has an empty kit).</summary>
        public static DuelWorld Duel(SpellLoadout loadout0, SpellLoadout loadout1 = null, ArenaLayout layout = null, IWorldMaterials materials = null,
            int alcoveTicks = 0)
        {
            loadout1 = loadout1 ?? Loadout(new TapGrammar());
            var runtime = SpellRuntime.WithLoadouts(loadout0, loadout1);
            return TestWorlds.Duel(layout, null, null, alcoveTicks, -1, runtime, materials);
        }

        public static SpellRuntime Runtime(DuelWorld world) => (SpellRuntime)world.Spells;

        public static CastMachine Machine(DuelWorld world, int player) => world.Player(player).Caster.Machine;

        /// <summary>Steps once with player 0 pressing primary (aiming as given) and player 1 idle facing player 0.</summary>
        public static void Cast(DuelWorld world, PlayerInput? input = null, PlayerInput? other = null)
        {
            PlayerInput a = input ?? TestWorlds.A;
            PlayerInput b = other ?? TestWorlds.B;
            world.Step(a.With(InputButtons.Primary), b);
        }

        /// <summary>Steps until a condition holds or the limit passes; returns the ticks stepped.</summary>
        public static int RunUntil(DuelWorld world, Func<bool> done, int maxTicks, PlayerInput? a = null, PlayerInput? b = null)
        {
            PlayerInput ia = a ?? TestWorlds.A;
            PlayerInput ib = b ?? TestWorlds.B;
            for (int i = 0; i < maxTicks; i++)
            {
                if (done()) return i;
                world.Step(ia, ib);
            }

            return maxTicks;
        }

        public static void Run(DuelWorld world, int ticks, PlayerInput? a = null, PlayerInput? b = null)
        {
            PlayerInput ia = a ?? TestWorlds.A;
            PlayerInput ib = b ?? TestWorlds.B;
            for (int i = 0; i < ticks && !world.IsOver; i++) world.Step(ia, ib);
        }

        public static IEnumerable<T> Events<T>(DuelWorld world) where T : DuelEvent => world.EventLog.OfType<T>();

        public static IEnumerable<CastDuelEvent> CastEvents(DuelWorld world, CastEventKind kind, int player = 0) =>
            world.EventLog.OfType<CastDuelEvent>().Where(e => e.Kind == kind && e.Player == player);

        public static IEnumerable<SpellEvent> SpellEvents(DuelWorld world, SpellEventKind kind) =>
            world.EventLog.OfType<SpellEvent>().Where(e => e.Kind == kind);

        public static List<DamageEvent> DamageTo(DuelWorld world, int target, string sourceId = null) =>
            world.EventLog.OfType<DamageEvent>().Where(d => d.Target == target && (sourceId == null || d.SourceId == sourceId)).ToList();

        public static float TotalDealt(DuelWorld world, int target, string sourceId = null) => DamageTo(world, target, sourceId).Sum(d => d.Dealt);

        /// <summary>Health lost by a player from its duel maximum.</summary>
        public static float HealthLost(DuelWorld world, int player) => world.Player(player).Resources.MaxHealth - world.Player(player).Resources.Health;

        /// <summary>Input facing a point from a player's eye.</summary>
        public static PlayerInput Aim(DuelWorld world, int player, Vec3 point)
        {
            Vec3 d = point - world.Player(player).Eye;
            return PlayerInput.Idle(CMath.YawOf(d)).Facing(CMath.YawOf(d), CMath.PitchOf(d));
        }

        /// <summary>Input aiming at another player's chest.</summary>
        public static PlayerInput AimAt(DuelWorld world, int player, int target)
        {
            DuelPlayer t = world.Player(target);
            return Aim(world, player, t.Position + new Vec3(0f, 1.1f, 0f));
        }

        public static Solid Wall(int id, Vec3 floorCenter, Vec3 size, float yawDeg = 0f, MaterialFlags flags = MaterialFlags.None)
        {
            Solid s = TestWorlds.Box(id, floorCenter, size, SolidKind.Wall, yawDeg);
            s.Flags = flags;
            return s;
        }

        /// <summary>The default flat layout plus extra solids.</summary>
        public static ArenaLayout FlatWith(params Solid[] solids)
        {
            ArenaLayout layout = TestWorlds.Flat();
            layout.Solids.AddRange(solids);
            return layout;
        }
    }
}
