using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Client.Arena
{
    /// <summary>
    /// TEMPORARY DEBUG TOOL, to be replaced by Confluence.Arena's seeded generator (IMPLEMENTATION_PLAN Stage 1
    /// "Arena"). Hand-builds one plausible 32 m layout so the renderer, cameras, and walk preview can be
    /// exercised before the generator exists. The layout is NOT validated: it only imitates the recipe of
    /// spec L308-L312 and L660 (two spawn alcoves, a central final floor, cover, a ramp to a platform, water
    /// beds, props in pairs, an irregular contour). Functional symmetry is a 180 degree rotation about the
    /// center (every piece has a rotated twin with MirrorId), and the contour uses only even harmonics so
    /// r(yaw) = r(yaw + 180).
    /// </summary>
    public static class SampleLayouts
    {
        public const string SampleBiomeId = "BIO-001"; // Raincourt: water and metal (catalogue)
        public const int SampleDiameter = 32;          // spec L306 tier
        public const string NotValidatedNote = "Client sample layout (debug fixture): not generated or validated by Confluence.Arena.";

        public static ArenaLayout CreatePreviewLayout(Catalogue catalogue = null)
        {
            var b = new Builder();
            var layout = b.Layout;
            layout.Seed = 0;
            layout.Round = 1;
            layout.Diameter = SampleDiameter;
            layout.BiomeId = SampleBiomeId;
            layout.EnvironmentRule = catalogue != null && catalogue.BiomesById.TryGetValue(SampleBiomeId, out BiomeDef biome)
                ? biome.EnvironmentRule
                : "Every 8 s a 0.5 s visible rainfall pulse adds one water unit to the two mirrored gutter beds.";
            layout.Center = Vec3.Zero;
            layout.FinalFloorRadius = GlobalRules.Boundary.FinalFloorRadius;
            layout.Report = new ArenaValidationReport { Valid = false, AttemptsUsed = 0 };
            layout.Report.Failures.Add(NotValidatedNote);

            // Ground: one anchored stone slab under the whole contour, and the slate final floor (8 m, spec L48).
            b.Single(SolidKind.Floor, YawBox.OnFloor(new Vec3(0f, -0.5f, 0f), new Vec3(38f, 0.5f, 38f), 0f), "stone", MaterialFlags.Anchored, walkable: true, blocksSight: false);
            b.Single(SolidKind.Floor, YawBox.OnFloor(Vec3.Zero, new Vec3(9f, 0.05f, 9f), 0f), "slate", MaterialFlags.Anchored, walkable: true, blocksSight: false);

            // Spawn alcoves (spec L46-L47): back and side walls open toward the center; a screen in front of each
            // spawn breaks the direct initial sightline (spec L310).
            b.Pair(SolidKind.AlcoveWall, YawBox.OnFloor(new Vec3(0f, 0f, -15.3f), new Vec3(5.2f, 3f, 0.6f), 0f), "stone", MaterialFlags.Anchored);
            b.Pair(SolidKind.AlcoveWall, YawBox.OnFloor(new Vec3(-2.3f, 0f, -13.5f), new Vec3(0.6f, 3f, 3f), 0f), "stone", MaterialFlags.Anchored);
            b.Pair(SolidKind.AlcoveWall, YawBox.OnFloor(new Vec3(2.3f, 0f, -13.5f), new Vec3(0.6f, 3f, 3f), 0f), "stone", MaterialFlags.Anchored);
            b.Pair(SolidKind.Cover, YawBox.OnFloor(new Vec3(0f, 0f, -9.6f), new Vec3(5f, 2.6f, 0.6f), 0f), "stone", MaterialFlags.Anchored);

            // Side approach walls with a gap toward each flank.
            b.Pair(SolidKind.Wall, YawBox.OnFloor(new Vec3(-6.2f, 0f, -2.5f), new Vec3(0.6f, 2.8f, 6f), 0f), "stone", MaterialFlags.Anchored);

            // Center pillars and retiring soft cover inside the final floor (spec L678).
            b.Pair(SolidKind.Pillar, YawBox.OnFloor(new Vec3(-3.2f, 0f, -3.2f), new Vec3(0.9f, 3.2f, 0.9f), 0f), "stone", MaterialFlags.Anchored);
            b.Pair(SolidKind.Pillar, YawBox.OnFloor(new Vec3(3.2f, 0f, -3.2f), new Vec3(0.9f, 3.2f, 0.9f), 0f), "stone", MaterialFlags.Anchored);
            b.Pair(SolidKind.Cover, YawBox.OnFloor(new Vec3(-1.6f, 0.05f, -0.9f), new Vec3(1.8f, 1.2f, 0.35f), 0f), "growth", MaterialFlags.Combustible | MaterialFlags.Growth,
                durability: 60f, retiresAtFinal: true);

            // Material cover pieces: conductive grille, prism shield, wooden barricade, porous wall.
            b.Pair(SolidKind.Cover, YawBox.OnFloor(new Vec3(5.2f, 0f, -5.8f), new Vec3(3f, 1.8f, 0.3f), 25f), "metal", MaterialFlags.Metal | MaterialFlags.Conductive, durability: 90f);
            b.Pair(SolidKind.Cover, YawBox.OnFloor(new Vec3(-5.4f, 0f, -6.2f), new Vec3(2.6f, 2f, 0.3f), -30f), "glass", MaterialFlags.Glass | MaterialFlags.Reflective | MaterialFlags.Brittle, durability: 60f);
            b.Pair(SolidKind.Cover, YawBox.OnFloor(new Vec3(-11f, 0f, -7f), new Vec3(2.4f, 1.1f, 0.4f), 60f), "wood", MaterialFlags.Combustible, durability: 80f);
            b.Pair(SolidKind.Cover, YawBox.OnFloor(new Vec3(9.8f, 0f, -9.2f), new Vec3(2.8f, 2.2f, 0.5f), -50f), "porous", MaterialFlags.Porous | MaterialFlags.Absorbent, durability: 100f);

            // Elevation: a 1.5 m platform reached by a 23 degree ramp (spec L312: at most 30 degrees).
            b.Pair(SolidKind.Platform, YawBox.OnFloor(new Vec3(10.5f, 0f, 2f), new Vec3(4f, 1.5f, 5f), 0f), "stone", MaterialFlags.Anchored, walkable: true);
            b.PairRamp(RampShape.Wedge(new Vec3(10.5f, 0f, -2.25f), 1.4f, 1.75f, 0f, 1.5f), "stone", MaterialFlags.Anchored);
            b.Pair(SolidKind.Cover, YawBox.OnFloor(new Vec3(10.5f, 1.5f, 4f), new Vec3(3f, 1f, 0.35f), 0f), "metal", MaterialFlags.Metal | MaterialFlags.Conductive, durability: 80f);

            // Natural sources (spec L668): the mirrored Raincourt gutter beds, plus sand, growth, and a conductor strip.
            b.PairPatch(new MaterialPatch { Center = new Vec2(-5f, -10.8f), HalfSize = new Vec2(3f, 1f), Water = 2, Flags = MaterialFlags.Water | MaterialFlags.Conductive, NaturalSource = true, RefillRule = "BIO-001 rainfall pulse" });
            b.PairPatch(new MaterialPatch { Center = new Vec2(9f, -6f), Radius = 1.4f, Sand = 2, Water = 1, Flags = MaterialFlags.Sand, NaturalSource = true });
            b.PairPatch(new MaterialPatch { Center = new Vec2(-11.5f, 4f), Radius = 1.3f, Growth = 2, Flags = MaterialFlags.Growth | MaterialFlags.Combustible, NaturalSource = true });
            b.PairPatch(new MaterialPatch { Center = new Vec2(0f, -6.2f), HalfSize = new Vec2(2.2f, 0.35f), Flags = MaterialFlags.Metal | MaterialFlags.Conductive, NaturalSource = true });

            // Props in mirrored pairs (spec L662).
            b.PairProp("PROP-001", new Vec3(3.6f, 0f, -7.6f), 0f);   // Copper Barrel (conductive)
            b.PairProp("PROP-019", new Vec3(-7.2f, 0f, 3.2f), 45f);  // Ray Prism (optical)
            b.PairProp("PROP-025", new Vec3(11.4f, 1.5f, 1.2f), 0f); // Rolling Boulder (kinetic) on the platform
            b.PairProp("PROP-013", new Vec3(-9.2f, 0f, -11f), 0f);   // Briar Bundle (fuel)
            b.PairProp("PROP-031", new Vec3(6.2f, 0f, -12f), 20f);   // Resonant Gong (acoustic)

            // Spawns face the center (yaw 0 faces +Z).
            layout.Spawns = new[]
            {
                new SpawnPoint { Side = 0, Position = new Vec3(0f, 0f, -13.2f), YawDeg = 0f, Alcove = YawBox.OnFloor(new Vec3(0f, 0f, -13.4f), new Vec3(4f, 3f, 3f), 0f) },
                new SpawnPoint { Side = 1, Position = new Vec3(0f, 0f, 13.2f), YawDeg = 180f, Alcove = YawBox.OnFloor(new Vec3(0f, 0f, 13.4f), new Vec3(4f, 3f, 3f), 180f) },
            };

            // Standardized overhead light shafts (spec L320).
            layout.LightShafts.Add(new LightShaft { Position = Vec3.Zero, Radius = 4f });
            layout.LightShafts.Add(new LightShaft { Position = new Vec3(8.5f, 0f, -8.5f), Radius = 2f, MirrorId = 2 });
            layout.LightShafts.Add(new LightShaft { Position = new Vec3(-8.5f, 0f, 8.5f), Radius = 2f, MirrorId = 1 });

            layout.Boundary = CreateContour(Vec3.Zero, 64);
            layout.Modules.Add(new ModuleInstance { ModuleId = "MOD-062", Position = Vec3.Zero, YawDeg = 0f, Side = -1 });
            return layout;
        }

        /// <summary>Irregular, point-symmetric contour (even harmonics only) around a 15.4 m mean radius.</summary>
        public static BoundaryContour CreateContour(Vec3 center, int samples)
        {
            var contour = new BoundaryContour { Center = center, InitialRadii = new float[samples] };
            for (int i = 0; i < samples; i++)
            {
                float a = i * 2f * MathF.PI / samples;
                float r = 15.4f + 1.0f * MathF.Cos(2f * a) + 0.5f * MathF.Sin(4f * a + 0.6f) + 0.35f * MathF.Cos(6f * a + 1.3f);
                contour.InitialRadii[i] = MathF.Max(r, contour.FinalRadius);
            }

            return contour;
        }

        /// <summary>Accumulates solids, patches, and props, adding each piece's 180 degree twin.</summary>
        private sealed class Builder
        {
            private const float MirrorYaw = 180f;
            private int _nextSolidId = 1;
            private int _nextPropId = 1;

            public ArenaLayout Layout { get; } = new ArenaLayout();

            public Solid Single(SolidKind kind, YawBox box, string material, MaterialFlags flags, bool walkable = false, bool blocksSight = true, float durability = 0f, bool retiresAtFinal = false)
            {
                var solid = new Solid
                {
                    Id = _nextSolidId++,
                    Kind = kind,
                    Box = box,
                    Material = material,
                    Flags = flags,
                    Durability = durability,
                    Anchored = durability <= 0f,
                    Walkable = walkable,
                    BlocksSight = blocksSight,
                    RetiresAtFinal = retiresAtFinal,
                };
                Layout.Solids.Add(solid);
                return solid;
            }

            public void Pair(SolidKind kind, YawBox box, string material, MaterialFlags flags, bool walkable = false, float durability = 0f, bool retiresAtFinal = false)
            {
                Solid a = Single(kind, box, material, flags, walkable, true, durability, retiresAtFinal);
                Solid b = Single(kind, box.RotatedAbout(Vec3.Zero, MirrorYaw), material, flags, walkable, true, durability, retiresAtFinal);
                Link(a, b);
            }

            public void PairRamp(RampShape ramp, string material, MaterialFlags flags)
            {
                Solid a = Single(SolidKind.Ramp, ramp.Box, material, flags, walkable: true, blocksSight: true);
                a.RampRise = ramp.Rise;
                Solid b = Single(SolidKind.Ramp, ramp.Box.RotatedAbout(Vec3.Zero, MirrorYaw), material, flags, walkable: true, blocksSight: true);
                b.RampRise = ramp.Rise;
                Link(a, b);
            }

            public void PairPatch(MaterialPatch patch)
            {
                MaterialPatch twin = patch.Clone();
                twin.Center = -patch.Center;
                twin.YawDeg = CMath.WrapAngle(patch.YawDeg + MirrorYaw);
                int index = Layout.Materials.Count;
                patch.MirrorId = index + 1;
                twin.MirrorId = index;
                Layout.Materials.Add(patch);
                Layout.Materials.Add(twin);
            }

            public void PairProp(string propId, Vec3 position, float yawDeg)
            {
                var a = new PropPlacement { Id = _nextPropId++, PropId = propId, Position = position, YawDeg = yawDeg };
                var b = new PropPlacement
                {
                    Id = _nextPropId++,
                    PropId = propId,
                    Position = new Vec3(-position.X, position.Y, -position.Z),
                    YawDeg = CMath.WrapAngle(yawDeg + MirrorYaw),
                };
                a.MirrorId = b.Id;
                b.MirrorId = a.Id;
                Layout.Props.Add(a);
                Layout.Props.Add(b);
            }

            private static void Link(Solid a, Solid b)
            {
                a.MirrorId = b.Id;
                b.MirrorId = a.Id;
            }
        }

        /// <summary>All solids of a kind (test and debug helper).</summary>
        public static List<Solid> SolidsOfKind(ArenaLayout layout, SolidKind kind)
        {
            var result = new List<Solid>();
            foreach (Solid s in layout.Solids)
            {
                if (s.Kind == kind)
                {
                    result.Add(s);
                }
            }

            return result;
        }
    }
}
