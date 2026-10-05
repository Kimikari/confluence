using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Net.Protocol;
using Confluence.Net.Serialization;

namespace Confluence.Net.Game.Wire
{
    /// <summary>
    /// Exact wire form of a generated arena layout (research §2.7.1 authoritative fallback): every field the layout hash
    /// covers (ArenaLayoutHash), floats bit for bit, deflated. A client whose own regeneration does not match the server's
    /// layout hash (a different float runtime, spec L427) downloads it instead of losing prediction and geometry; the
    /// decoded layout hashes to the same value by construction. The validation report is not sent.
    /// </summary>
    public static class LayoutCodec
    {
        public const int MaxModules = 512;
        public const int MaxSolids = 4096;
        public const int MaxPatches = 2048;
        public const int MaxProps = 512;
        public const int MaxShafts = 256;
        public const int MaxGusts = 256;
        public const int MaxContourSamples = 1024;

        /// <summary>Largest inflated layout accepted (a forged stream cannot allocate more).</summary>
        public const int MaxInflatedBytes = 1 << 20;

        public static byte[] Encode(ArenaLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            var w = new NetWriter(16384);
            w.WriteULong(layout.Seed);
            w.WriteVarInt(layout.Round);
            w.WriteVarInt(layout.Diameter);
            WriteNullable(w, layout.BiomeId);
            WriteNullable(w, layout.EnvironmentRule);
            WriteNullable(w, layout.FallbackId);
            w.WriteVec3(layout.Center);
            w.WriteFloat(layout.FinalFloorRadius);

            w.WriteVarUInt((uint)layout.Modules.Count);
            foreach (ModuleInstance m in layout.Modules)
            {
                WriteNullable(w, m.ModuleId);
                w.WriteVec3(m.Position);
                w.WriteFloat(m.YawDeg);
                w.WriteVarInt(m.Side);
                w.WriteVarInt(m.MirrorIndex);
            }

            w.WriteVarUInt((uint)layout.Solids.Count);
            foreach (Solid s in layout.Solids)
            {
                w.WriteVarInt(s.Id);
                w.WriteVarInt((int)s.Kind);
                WriteBox(w, s.Box);
                w.WriteFloat(s.RampRise);
                WriteNullable(w, s.Material);
                w.WriteVarInt((int)s.Flags);
                w.WriteFloat(s.Durability);
                w.WriteByte((byte)((s.Anchored ? 1 : 0) | (s.RetiresAtFinal ? 2 : 0) | (s.BlocksSight ? 4 : 0) | (s.Walkable ? 8 : 0)));
                w.WriteVarInt(s.ModuleIndex);
                w.WriteVarInt(s.MirrorId);
            }

            w.WriteVarUInt((uint)layout.Materials.Count);
            foreach (MaterialPatch p in layout.Materials)
            {
                w.WriteFloat(p.Center.X);
                w.WriteFloat(p.Center.Y);
                w.WriteFloat(p.HalfSize.X);
                w.WriteFloat(p.HalfSize.Y);
                w.WriteFloat(p.YawDeg);
                w.WriteFloat(p.Radius);
                w.WriteFloat(p.SurfaceY);
                w.WriteVarInt(p.Water);
                w.WriteVarInt(p.Heat);
                w.WriteVarInt(p.Frost);
                w.WriteVarInt(p.Dust);
                w.WriteVarInt(p.Sand);
                w.WriteVarInt(p.Growth);
                w.WriteVarInt((int)p.Flags);
                w.WriteBool(p.NaturalSource);
                WriteNullable(w, p.RefillRule);
                w.WriteVarInt(p.MirrorId);
            }

            w.WriteVarUInt((uint)layout.Props.Count);
            foreach (PropPlacement p in layout.Props)
            {
                w.WriteVarInt(p.Id);
                WriteNullable(w, p.PropId);
                w.WriteVec3(p.Position);
                w.WriteFloat(p.YawDeg);
                w.WriteVarInt(p.MirrorId);
                w.WriteBool(p.HasCollision);
                if (p.HasCollision) WriteBox(w, p.CollisionBox);
            }

            w.WriteVarInt(layout.Spawns == null ? -1 : layout.Spawns.Length);
            if (layout.Spawns != null)
            {
                foreach (SpawnPoint s in layout.Spawns)
                {
                    w.WriteVarInt(s.Side);
                    w.WriteVec3(s.Position);
                    w.WriteFloat(s.YawDeg);
                    WriteBox(w, s.Alcove);
                }
            }

            BoundaryContour b = layout.Boundary;
            w.WriteBool(b != null);
            if (b != null)
            {
                w.WriteVec3(b.Center);
                w.WriteFloat(b.FinalRadius);
                w.WriteFloat(b.ContractStartSec);
                w.WriteFloat(b.ContractEndSec);
                w.WriteVarUInt((uint)b.SampleCount);
                for (int i = 0; i < b.SampleCount; i++) w.WriteFloat(b.InitialRadii[i]);
            }

            w.WriteVarUInt((uint)layout.LightShafts.Count);
            foreach (LightShaft l in layout.LightShafts)
            {
                w.WriteVec3(l.Position);
                w.WriteFloat(l.Radius);
                w.WriteVarInt(l.MirrorId);
            }

            w.WriteVarUInt((uint)layout.Gusts.Count);
            foreach (GustEvent g in layout.Gusts)
            {
                w.WriteFloat(g.TimeSec);
                w.WriteFloat(g.DurationSec);
                w.WriteFloat(g.Direction.X);
                w.WriteFloat(g.Direction.Y);
                w.WriteVarInt(g.Strength);
                w.WriteFloat(g.Center.X);
                w.WriteFloat(g.Center.Y);
                w.WriteFloat(g.Radius);
            }

            return Deflate(w.Buffer, w.Length);
        }

        /// <summary>Decodes a layout; throws <see cref="NetReadException"/> on any malformed or oversized input.</summary>
        public static ArenaLayout Decode(byte[] compressed)
        {
            byte[] raw = Inflate(compressed);
            var r = new NetReader(raw);
            var layout = new ArenaLayout
            {
                Seed = r.ReadULong(),
                Round = r.ReadVarIntInRange(0, 1000),
                Diameter = r.ReadVarIntInRange(0, 1000),
                BiomeId = ReadNullable(r),
                EnvironmentRule = ReadNullable(r),
                FallbackId = ReadNullable(r),
                Center = RV3(r),
                FinalFloorRadius = RF(r),
            };

            int modules = r.ReadCount(MaxModules, 8);
            for (int i = 0; i < modules; i++)
            {
                layout.Modules.Add(new ModuleInstance
                {
                    ModuleId = ReadNullable(r),
                    Position = RV3(r),
                    YawDeg = RF(r),
                    Side = r.ReadVarIntInRange(-1, 1),
                    MirrorIndex = r.ReadVarIntInRange(-1, MaxModules),
                });
            }

            int solids = r.ReadCount(MaxSolids, 16);
            for (int i = 0; i < solids; i++)
            {
                var s = new Solid
                {
                    Id = r.ReadVarInt(),
                    Kind = (SolidKind)r.ReadVarIntInRange(0, (int)SolidKind.Barrier),
                    Box = ReadBox(r),
                    RampRise = RF(r),
                    Material = ReadNullable(r),
                    Flags = (MaterialFlags)r.ReadVarIntInRange(0, int.MaxValue),
                    Durability = RF(r),
                };
                byte bits = r.ReadByteInRange(0, 15);
                s.Anchored = (bits & 1) != 0;
                s.RetiresAtFinal = (bits & 2) != 0;
                s.BlocksSight = (bits & 4) != 0;
                s.Walkable = (bits & 8) != 0;
                s.ModuleIndex = r.ReadVarIntInRange(-1, MaxModules);
                s.MirrorId = r.ReadVarInt();
                layout.Solids.Add(s);
            }

            int patches = r.ReadCount(MaxPatches, 16);
            for (int i = 0; i < patches; i++)
            {
                layout.Materials.Add(new MaterialPatch
                {
                    Center = new Vec2(RF(r), RF(r)),
                    HalfSize = new Vec2(RF(r), RF(r)),
                    YawDeg = RF(r),
                    Radius = RF(r),
                    SurfaceY = RF(r),
                    Water = r.ReadVarIntInRange(0, 255),
                    Heat = r.ReadVarIntInRange(0, 255),
                    Frost = r.ReadVarIntInRange(0, 255),
                    Dust = r.ReadVarIntInRange(0, 255),
                    Sand = r.ReadVarIntInRange(0, 255),
                    Growth = r.ReadVarIntInRange(0, 255),
                    Flags = (MaterialFlags)r.ReadVarIntInRange(0, int.MaxValue),
                    NaturalSource = r.ReadBool(),
                    RefillRule = ReadNullable(r),
                    MirrorId = r.ReadVarInt(),
                });
            }

            int props = r.ReadCount(MaxProps, 8);
            for (int i = 0; i < props; i++)
            {
                var p = new PropPlacement
                {
                    Id = r.ReadVarInt(),
                    PropId = ReadNullable(r),
                    Position = RV3(r),
                    YawDeg = RF(r),
                    MirrorId = r.ReadVarInt(),
                    HasCollision = r.ReadBool(),
                };
                if (p.HasCollision) p.CollisionBox = ReadBox(r);
                layout.Props.Add(p);
            }

            int spawns = r.ReadVarIntInRange(-1, 8);
            if (spawns < 0) layout.Spawns = null;
            else
            {
                layout.Spawns = new SpawnPoint[spawns];
                for (int i = 0; i < spawns; i++)
                {
                    layout.Spawns[i] = new SpawnPoint { Side = r.ReadVarIntInRange(0, 1), Position = RV3(r), YawDeg = RF(r), Alcove = ReadBox(r) };
                }
            }

            if (r.ReadBool())
            {
                var b = new BoundaryContour
                {
                    Center = RV3(r),
                    FinalRadius = RF(r),
                    ContractStartSec = RF(r),
                    ContractEndSec = RF(r),
                };
                int samples = r.ReadCount(MaxContourSamples, 4);
                b.InitialRadii = new float[samples];
                for (int i = 0; i < samples; i++) b.InitialRadii[i] = RF(r);
                layout.Boundary = b;
            }
            else
            {
                layout.Boundary = null;
            }

            int shafts = r.ReadCount(MaxShafts, 8);
            for (int i = 0; i < shafts; i++) layout.LightShafts.Add(new LightShaft { Position = RV3(r), Radius = RF(r), MirrorId = r.ReadVarInt() });
            int gusts = r.ReadCount(MaxGusts, 8);
            for (int i = 0; i < gusts; i++)
            {
                layout.Gusts.Add(new GustEvent
                {
                    TimeSec = RF(r),
                    DurationSec = RF(r),
                    Direction = new Vec2(RF(r), RF(r)),
                    Strength = r.ReadVarIntInRange(0, 1000),
                    Center = new Vec2(RF(r), RF(r)),
                    Radius = RF(r),
                });
            }

            r.ExpectEnd();
            return layout;
        }

        private static void WriteNullable(NetWriter w, string s)
        {
            w.WriteBool(s != null);
            if (s != null) w.WriteString(s, WireLimits.TextMaxBytes);
        }

        private static string ReadNullable(NetReader r) => r.ReadBool() ? r.ReadString(WireLimits.TextMaxBytes) : null;

        private static void WriteBox(NetWriter w, YawBox b)
        {
            w.WriteVec3(b.Center);
            w.WriteVec3(b.HalfExtents);
            w.WriteFloat(b.YawDeg);
        }

        private static YawBox ReadBox(NetReader r)
        {
            Vec3 center = RV3(r);
            Vec3 half = RV3(r);
            float yaw = RF(r);
            return new YawBox(center, half, yaw);
        }

        /// <summary>A float bit for bit (an indestructible durability may be infinite); NaN is malformed.</summary>
        private static float RF(NetReader r)
        {
            float v = r.ReadFloatRaw();
            if (float.IsNaN(v)) throw new NetReadException(NetReadError.InvalidValue, "NaN in a layout");
            return v;
        }

        private static Vec3 RV3(NetReader r) => new Vec3(RF(r), RF(r), RF(r));

        private static byte[] Deflate(byte[] buffer, int length)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(buffer, 0, length);
                return output.ToArray();
            }
        }

        private static byte[] Inflate(byte[] compressed)
        {
            if (compressed == null || compressed.Length == 0) throw new NetReadException(NetReadError.EndOfData, "empty layout");
            try
            {
                using (var input = new MemoryStream(compressed))
                using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var chunk = new byte[8192];
                    int n;
                    while ((n = inflate.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        if (output.Length + n > MaxInflatedBytes) throw new NetReadException(NetReadError.CountOutOfRange, "layout too large");
                        output.Write(chunk, 0, n);
                    }

                    return output.ToArray();
                }
            }
            catch (InvalidDataException e)
            {
                throw new NetReadException(NetReadError.InvalidValue, "corrupt layout stream: " + e.Message);
            }
        }
    }

    /// <summary>A client asks for the exact layout of an announced arena it could not regenerate (hash mismatch).</summary>
    public sealed class ArenaLayoutRequest : INetMessage
    {
        public string ArenaId { get; set; }

        public void Serialize(NetWriter w) => w.WriteId(ArenaId);

        public void Deserialize(NetReader r) => ArenaId = r.ReadId();
    }

    /// <summary>The server's exact layout of an announced arena (deflated <see cref="LayoutCodec"/> bytes).</summary>
    public sealed class ArenaLayoutMessage : INetMessage
    {
        /// <summary>Largest compressed layout accepted.</summary>
        public const int MaxBytes = 256 * 1024;

        public string ArenaId { get; set; }
        public ulong LayoutHash { get; set; }
        public byte[] Layout { get; set; } = new byte[0];

        public void Serialize(NetWriter w)
        {
            w.WriteId(ArenaId);
            w.WriteULong(LayoutHash);
            w.WriteBlob(Layout, 0, Layout.Length);
        }

        public void Deserialize(NetReader r)
        {
            ArenaId = r.ReadId();
            LayoutHash = r.ReadULong();
            Layout = r.ReadBlob(MaxBytes);
        }
    }
}
