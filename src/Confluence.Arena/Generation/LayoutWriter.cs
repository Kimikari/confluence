using System;
using System.Collections.Generic;
using System.Globalization;
using Confluence.Arena.Modules;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Generation
{
    /// <summary>
    /// Emits world elements into an <see cref="ArenaLayout"/> and keeps the half-turn bookkeeping: every element
    /// authored on side 0 is written together with its image on side 1 and both carry each other's MirrorId.
    /// Ids follow creation order, so equal generation choices give byte-identical layouts.
    /// </summary>
    public sealed class LayoutWriter
    {
        private int _nextSolidId;
        private int _nextPropId;

        public LayoutWriter(ArenaLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            for (int i = 0; i < layout.Solids.Count; i++)
            {
                _nextSolidId = Math.Max(_nextSolidId, layout.Solids[i].Id + 1);
            }

            for (int i = 0; i < layout.Props.Count; i++)
            {
                _nextPropId = Math.Max(_nextPropId, layout.Props[i].Id + 1);
            }
        }

        public ArenaLayout Layout { get; }

        // ---- Solids -----------------------------------------------------------------------------------

        public Solid NewSolid(SolidKind kind, YawBox box, string material, float durability, bool walkable = false, float rampRise = 0f)
        {
            bool anchored = durability <= 0f;
            return new Solid
            {
                Kind = kind,
                Box = box,
                RampRise = rampRise,
                Material = material,
                Flags = ArenaMaterials.FlagsFor(material) | (anchored ? MaterialFlags.Anchored : MaterialFlags.None),
                Durability = anchored ? 0f : durability,
                Anchored = anchored,
                Walkable = walkable,
            };
        }

        /// <summary>Adds a solid that is its own image under the half turn (it is centered on the arena center).</summary>
        public Solid AddSelfMirrored(Solid solid)
        {
            solid.Id = _nextSolidId++;
            solid.MirrorId = solid.Id;
            Layout.Solids.Add(solid);
            return solid;
        }

        /// <summary>Adds a side-0 solid and its side-1 image; returns the side-0 solid.</summary>
        public Solid AddMirroredPair(Solid solid, int mirrorModuleIndex = -1)
        {
            Solid image = solid.Clone();
            image.Box = MirrorMath.Box(solid.Box);
            if (mirrorModuleIndex >= 0)
            {
                image.ModuleIndex = mirrorModuleIndex;
            }

            solid.Id = _nextSolidId++;
            image.Id = _nextSolidId++;
            solid.MirrorId = image.Id;
            image.MirrorId = solid.Id;
            Layout.Solids.Add(solid);
            Layout.Solids.Add(image);
            return solid;
        }

        public void RemoveSolidPair(Solid solid)
        {
            Solid image = Layout.FindSolid(solid.MirrorId);
            Layout.Solids.Remove(solid);
            if (image != null && image != solid)
            {
                Layout.Solids.Remove(image);
            }
        }

        // ---- Patches ----------------------------------------------------------------------------------

        public MaterialPatch AddPatchPair(MaterialPatch patch)
        {
            MaterialPatch image = patch.Clone();
            image.Center = MirrorMath.Point(patch.Center);
            image.YawDeg = MirrorMath.Yaw(patch.YawDeg);
            int a = Layout.Materials.Count;
            patch.MirrorId = a + 1;
            image.MirrorId = a;
            Layout.Materials.Add(patch);
            Layout.Materials.Add(image);
            return patch;
        }

        public MaterialPatch AddSelfMirroredPatch(MaterialPatch patch)
        {
            patch.MirrorId = Layout.Materials.Count;
            Layout.Materials.Add(patch);
            return patch;
        }

        // ---- Props ------------------------------------------------------------------------------------

        public PropPlacement AddPropPair(string propId, Vec3 position, float yawDeg)
        {
            var a = new PropPlacement { Id = _nextPropId++, PropId = propId, Position = position, YawDeg = CMath.WrapAngle360(yawDeg) };
            var b = new PropPlacement { Id = _nextPropId++, PropId = propId, Position = MirrorMath.Point(position), YawDeg = MirrorMath.Yaw(yawDeg) };
            a.MirrorId = b.Id;
            b.MirrorId = a.Id;
            Layout.Props.Add(a);
            Layout.Props.Add(b);
            return a;
        }

        // ---- Modules ----------------------------------------------------------------------------------

        /// <summary>World pose of a module placed at a ground position with a yaw of quarterTurns * 90 degrees.</summary>
        public static Vec3 ToWorld(Vec3 local, Vec2 position, int quarterTurns)
        {
            Vec2 r = new Vec2(local.X, local.Z).RotatedByYaw(quarterTurns * 90f);
            return new Vec3(position.X + r.X, local.Y, position.Y + r.Y);
        }

        /// <summary>World footprint of a module at a position and quarter-turn rotation.</summary>
        public static Rect2 Footprint(ModuleGeometry g, Vec2 position, int quarterTurns)
        {
            g.RotatedSize(quarterTurns, out float sx, out float sz);
            return Rect2.FromCenter(position, sx, sz);
        }

        /// <summary>Places a module on side 0 and its image on side 1. Returns the side-0 module index.</summary>
        public int PlaceModulePair(ModuleGeometry g, Vec2 position, int quarterTurns)
        {
            int a = Layout.Modules.Count;
            int b = a + 1;
            Layout.Modules.Add(new ModuleInstance { ModuleId = g.ModuleId, Position = position.ToXZ(), YawDeg = quarterTurns * 90f, Side = 0, MirrorIndex = b });
            Layout.Modules.Add(new ModuleInstance
            {
                ModuleId = g.ModuleId,
                Position = MirrorMath.Point(position).ToXZ(),
                YawDeg = CMath.WrapAngle360((quarterTurns + 2) * 90f),
                Side = 1,
                MirrorIndex = a,
            });

            for (int i = 0; i < g.Pieces.Count; i++)
            {
                Solid s = PieceToSolid(g.Pieces[i], position, quarterTurns, a);
                AddMirroredPair(s, b);
            }

            for (int i = 0; i < g.Patches.Count; i++)
            {
                AddPatchPair(PatchToWorld(g.ModuleId, g.Patches[i], position, quarterTurns));
            }

            return a;
        }

        /// <summary>Places the point-symmetric center module at the arena center; its pieces pair among themselves.</summary>
        public int PlaceCenterModule(ModuleGeometry g, int quarterTurns)
        {
            int index = Layout.Modules.Count;
            Layout.Modules.Add(new ModuleInstance { ModuleId = g.ModuleId, Position = Vec3.Zero, YawDeg = quarterTurns * 90f, Side = -1, MirrorIndex = index });
            int first = Layout.Solids.Count;
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                Solid s = PieceToSolid(g.Pieces[i], Vec2.Zero, quarterTurns, index);
                s.Id = _nextSolidId++;
                Layout.Solids.Add(s);
            }

            for (int i = first; i < Layout.Solids.Count; i++)
            {
                Solid s = Layout.Solids[i];
                for (int j = first; j < Layout.Solids.Count; j++)
                {
                    Solid t = Layout.Solids[j];
                    if (MirrorMath.IsPointMirror(s.Box, t.Box, 1e-3f, s.IsRamp) && s.Material == t.Material)
                    {
                        s.MirrorId = t.Id;
                        break;
                    }
                }
            }

            int firstPatch = Layout.Materials.Count;
            for (int i = 0; i < g.Patches.Count; i++)
            {
                Layout.Materials.Add(PatchToWorld(g.ModuleId, g.Patches[i], Vec2.Zero, quarterTurns));
            }

            for (int i = firstPatch; i < Layout.Materials.Count; i++)
            {
                MaterialPatch p = Layout.Materials[i];
                for (int j = firstPatch; j < Layout.Materials.Count; j++)
                {
                    MaterialPatch q = Layout.Materials[j];
                    if (Vec2.Distance(MirrorMath.Point(p.Center), q.Center) < 1e-3f && q.Flags == p.Flags && q.Radius == p.Radius)
                    {
                        p.MirrorId = j;
                        break;
                    }
                }
            }

            return index;
        }

        private static Solid PieceToSolid(ModulePiece p, Vec2 position, int quarterTurns, int moduleIndex)
        {
            Vec3 center = ToWorld(p.Box.Center, position, quarterTurns);
            return new Solid
            {
                Kind = p.Kind,
                Box = new YawBox(center, p.Box.HalfExtents, CMath.WrapAngle360(p.Box.YawDeg + quarterTurns * 90f)),
                RampRise = p.RampRise,
                Material = p.Material,
                Flags = p.Flags,
                Durability = p.Durability,
                Anchored = p.Anchored,
                RetiresAtFinal = p.RetiresAtFinal,
                BlocksSight = p.BlocksSight,
                Walkable = p.Walkable,
                ModuleIndex = moduleIndex,
            };
        }

        private static MaterialPatch PatchToWorld(string moduleId, ModulePatch p, Vec2 position, int quarterTurns)
        {
            Vec3 c = ToWorld(p.Center.ToXZ(), position, quarterTurns);
            string rule = p.Rule;
            if (p.Durability > 0f)
            {
                rule = (rule ?? moduleId) + ";durability=" + p.Durability.ToString("0.###", CultureInfo.InvariantCulture);
            }

            return new MaterialPatch
            {
                Center = new Vec2(c.X, c.Z),
                HalfSize = p.HalfSize,
                YawDeg = CMath.WrapAngle360(quarterTurns * 90f),
                Radius = p.Radius,
                SurfaceY = p.SurfaceY,
                Water = p.Water,
                Heat = p.Heat,
                Frost = p.Frost,
                Dust = p.Dust,
                Sand = p.Sand,
                Growth = p.Growth,
                Flags = p.Flags,
                NaturalSource = p.NaturalSource,
                RefillRule = rule,
            };
        }

        /// <summary>World-space clone of a module's pieces at a pose (used for previews and feasibility checks).</summary>
        public static List<YawBox> PieceBoxes(ModuleGeometry g, Vec2 position, int quarterTurns)
        {
            var boxes = new List<YawBox>(g.Pieces.Count);
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                ModulePiece p = g.Pieces[i];
                boxes.Add(new YawBox(ToWorld(p.Box.Center, position, quarterTurns), p.Box.HalfExtents, p.Box.YawDeg + quarterTurns * 90f));
            }

            return boxes;
        }
    }
}
