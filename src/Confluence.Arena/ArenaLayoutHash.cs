using System;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Random;

namespace Confluence.Arena
{
    /// <summary>
    /// Stable 64-bit fingerprint of a layout's gameplay content: every module, solid, patch, prop, spawn, light
    /// shaft, gust, and the contour, with floats hashed by their exact bits (FNV-1a). Equal hashes mean
    /// byte-identical layouts for replay and support records (spec L427, L664); the validation report is excluded.
    /// </summary>
    public static class ArenaLayoutHash
    {
        public static ulong Compute(ArenaLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var h = new Hasher();
            h.U64(layout.Seed);
            h.I32(layout.Round);
            h.I32(layout.Diameter);
            h.Str(layout.BiomeId);
            h.Str(layout.EnvironmentRule);
            h.Str(layout.FallbackId);
            h.V3(layout.Center);
            h.F(layout.FinalFloorRadius);

            h.I32(layout.Modules.Count);
            foreach (ModuleInstance m in layout.Modules)
            {
                h.Str(m.ModuleId);
                h.V3(m.Position);
                h.F(m.YawDeg);
                h.I32(m.Side);
                h.I32(m.MirrorIndex);
            }

            h.I32(layout.Solids.Count);
            foreach (Solid s in layout.Solids)
            {
                h.I32(s.Id);
                h.I32((int)s.Kind);
                h.Box(s.Box);
                h.F(s.RampRise);
                h.Str(s.Material);
                h.I32((int)s.Flags);
                h.F(s.Durability);
                h.Bool(s.Anchored);
                h.Bool(s.RetiresAtFinal);
                h.I32(s.ModuleIndex);
                h.I32(s.MirrorId);
                h.Bool(s.BlocksSight);
                h.Bool(s.Walkable);
            }

            h.I32(layout.Materials.Count);
            foreach (MaterialPatch p in layout.Materials)
            {
                h.V2(p.Center);
                h.V2(p.HalfSize);
                h.F(p.YawDeg);
                h.F(p.Radius);
                h.F(p.SurfaceY);
                h.I32(p.Water);
                h.I32(p.Heat);
                h.I32(p.Frost);
                h.I32(p.Dust);
                h.I32(p.Sand);
                h.I32(p.Growth);
                h.I32((int)p.Flags);
                h.Bool(p.NaturalSource);
                h.Str(p.RefillRule);
                h.I32(p.MirrorId);
            }

            h.I32(layout.Props.Count);
            foreach (PropPlacement p in layout.Props)
            {
                h.I32(p.Id);
                h.Str(p.PropId);
                h.V3(p.Position);
                h.F(p.YawDeg);
                h.I32(p.MirrorId);
            }

            h.I32(layout.Spawns == null ? -1 : layout.Spawns.Length);
            if (layout.Spawns != null)
            {
                foreach (SpawnPoint s in layout.Spawns)
                {
                    h.I32(s.Side);
                    h.V3(s.Position);
                    h.F(s.YawDeg);
                    h.Box(s.Alcove);
                }
            }

            BoundaryContour b = layout.Boundary;
            h.Bool(b != null);
            if (b != null)
            {
                h.V3(b.Center);
                h.F(b.FinalRadius);
                h.F(b.ContractStartSec);
                h.F(b.ContractEndSec);
                h.I32(b.SampleCount);
                for (int i = 0; i < b.SampleCount; i++)
                {
                    h.F(b.InitialRadii[i]);
                }
            }

            h.I32(layout.LightShafts.Count);
            foreach (LightShaft l in layout.LightShafts)
            {
                h.V3(l.Position);
                h.F(l.Radius);
                h.I32(l.MirrorId);
            }

            h.I32(layout.Gusts.Count);
            foreach (GustEvent g in layout.Gusts)
            {
                h.F(g.TimeSec);
                h.F(g.DurationSec);
                h.V2(g.Direction);
                h.I32(g.Strength);
                h.V2(g.Center);
                h.F(g.Radius);
            }

            return SeedHash.Mix(h.Value);
        }

        private sealed class Hasher
        {
            public ulong Value = SeedHash.FnvOffsetBasis;

            public void Byte(byte b)
            {
                unchecked
                {
                    Value = (Value ^ b) * SeedHash.FnvPrime;
                }
            }

            public void U64(ulong v)
            {
                for (int i = 0; i < 8; i++)
                {
                    Byte((byte)(v >> (8 * i)));
                }
            }

            public void I32(int v)
            {
                unchecked
                {
                    uint u = (uint)v;
                    for (int i = 0; i < 4; i++)
                    {
                        Byte((byte)(u >> (8 * i)));
                    }
                }
            }

            public void F(float v) => I32(BitConverter.SingleToInt32Bits(v));

            public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);

            public void Str(string s)
            {
                if (s == null)
                {
                    I32(-1);
                    return;
                }

                I32(s.Length);
                U64(SeedHash.Fnv1a64(s));
            }

            public void V2(Vec2 v)
            {
                F(v.X);
                F(v.Y);
            }

            public void V3(Vec3 v)
            {
                F(v.X);
                F(v.Y);
                F(v.Z);
            }

            public void Box(YawBox b)
            {
                V3(b.Center);
                V3(b.HalfExtents);
                F(b.YawDeg);
            }
        }
    }
}
