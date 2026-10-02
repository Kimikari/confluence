using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Sim.Duel;
using Confluence.Sim.Effects;
using Confluence.Sim.Input;
using Confluence.Sim.Players;
using Confluence.Sim.World.Materials;

namespace Confluence.Sim.Tests.World
{
    /// <summary>
    /// Duel helpers for the world, props, stats, and effects tests: a real <see cref="DuelWorld"/> with its default
    /// reaction materials, the shipped content behaviors, and the supplied catalogue. Input builders live here so the
    /// tests do not name the input flag type directly.
    /// </summary>
    internal static class WorldDuels
    {
        private static readonly Lazy<Catalogue> SharedCatalogue = new Lazy<Catalogue>(() => Catalogue.Load(Catalogue.FindDefaultPath()));

        public static Catalogue Catalogue => SharedCatalogue.Value;

        /// <summary>A duel in combat from tick 1 (no alcove) unless alcoveTicks is given, with reactions and props.</summary>
        public static DuelWorld Duel(ArenaLayout layout = null, DuelPlayerSetup a = null, DuelPlayerSetup b = null, int alcoveTicks = 0,
            IEffectContentSource content = null, bool withCatalogue = true)
        {
            var options = new DuelWorldOptions
            {
                AlcoveTicks = alcoveTicks,
                Content = content,
                Catalogue = withCatalogue ? Catalogue : null,
            };
            return new DuelWorld(layout ?? TestWorlds.Flat(), a ?? TestWorlds.Setup("A"), b ?? TestWorlds.Setup("B"), options);
        }

        public static ReactionMaterials Materials(DuelWorld world) => (ReactionMaterials)world.Materials;

        public static ReactionWorld Reactions(DuelWorld world) => Materials(world).World;

        /// <summary>A natural map-owned water bed (spec L668) of half size halfX by halfZ meters.</summary>
        public static MaterialPatch WaterBed(Vec2 center, float halfX, float halfZ, int water = 1, string refillRule = null)
        {
            return new MaterialPatch
            {
                Center = center,
                HalfSize = new Vec2(halfX, halfZ),
                Water = water,
                NaturalSource = true,
                RefillRule = refillRule,
            };
        }

        /// <summary>A natural map ice patch (BIO-004 style), slippery from combat start.</summary>
        public static MaterialPatch IcePatch(Vec2 center, float halfX, float halfZ)
        {
            return new MaterialPatch
            {
                Center = center,
                HalfSize = new Vec2(halfX, halfZ),
                Water = 1,
                Flags = MaterialFlags.Ice,
                NaturalSource = true,
            };
        }

        /// <summary>A prop placement with an explicit collision box resting on the floor (the arena generator writes it).</summary>
        public static PropPlacement Prop(int id, string propId, Vec3 floorCenter, Vec3 size, bool hasCollision = true)
        {
            return new PropPlacement
            {
                Id = id,
                PropId = propId,
                Position = floorCenter,
                CollisionBox = YawBox.OnFloor(floorCenter, size, 0f),
                HasCollision = hasCollision,
            };
        }

        public static DuelPlayerSetup SetupWith(string id, IEnumerable<KeyValuePair<string, EquipmentSlot>> equipment = null, IEnumerable<ActiveAugment> augments = null)
        {
            return DuelPlayerSetup.FromCatalogue(Catalogue, id, "WIZ-002", equipment: equipment, augments: augments);
        }

        public static KeyValuePair<string, EquipmentSlot> Item(string id, EquipmentSlot slot) => new KeyValuePair<string, EquipmentSlot>(id, slot);

        // ----- inputs -------------------------------------------------------------------------------------------

        public static PlayerInput Idle(float yawDeg) => PlayerInput.Idle(yawDeg);

        public static PlayerInput Walk(float yawDeg, float x = 0f, float y = 1f)
        {
            PlayerInput input = PlayerInput.Idle(yawDeg);
            input.MoveX = x;
            input.MoveY = y;
            return input;
        }

        public static PlayerInput Sprint(float yawDeg) => Walk(yawDeg).With(InputButtons.Sprint);

        public static PlayerInput Jump(float yawDeg) => PlayerInput.Idle(yawDeg).With(InputButtons.Jump);

        public static PlayerInput Dodge(float yawDeg, float x = 0f, float y = 1f) => Walk(yawDeg, x, y).With(InputButtons.Dodge);

        public static PlayerInput Guard(float yawDeg) => PlayerInput.Idle(yawDeg).With(InputButtons.Guard);

        public static PlayerInput Melee(float yawDeg) => PlayerInput.Idle(yawDeg).With(InputButtons.Melee);

        public static PlayerInput Select(float yawDeg, int slot)
        {
            PlayerInput input = PlayerInput.Idle(yawDeg);
            input.SelectedSlot = slot;
            return input;
        }

        public static void Step(DuelWorld world, int ticks, PlayerInput a, PlayerInput b)
        {
            for (int i = 0; i < ticks && !world.IsOver; i++)
            {
                world.Step(a, b);
            }
        }

        public static void Idle(DuelWorld world, int ticks) => Step(world, ticks, TestWorlds.A, TestWorlds.B);

        public static void IdleSeconds(DuelWorld world, float seconds) => Idle(world, Ticks.FromSeconds(seconds));

        /// <summary>Events of a type emitted since the world was built (the event log is kept by default).</summary>
        public static List<T> Events<T>(DuelWorld world) where T : DuelEvent
        {
            var list = new List<T>();
            foreach (DuelEvent e in world.EventLog)
            {
                if (e is T typed)
                {
                    list.Add(typed);
                }
            }

            return list;
        }

        public static float Distance2(Vec3 a, Vec3 b) => Vec3.DistanceXZ(a, b);

        public static float Walkspeed => GlobalRules.Movement.WalkSpeed;
    }
}
