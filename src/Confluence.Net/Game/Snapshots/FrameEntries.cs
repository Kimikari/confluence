using System;
using System.Collections.Generic;
using Confluence.Core.Geometry;
using Confluence.Net.Game.Wire;
using Confluence.Net.Serialization;

namespace Confluence.Net.Game.Snapshots
{
    /// <summary>
    /// A keyed, quantized entry of a snapshot section (spell entity, field, volume, cell, prop, solid). Entries are
    /// immutable after capture and compare by their quantized wire content, so delta encoding against a baseline sends
    /// exactly the entries whose replicated state changed (spec L421: snapshots, "changed cells (delta)").
    /// </summary>
    public abstract class FrameEntry
    {
        /// <summary>Stable key inside its section (runtime id, cell index, prop id, solid id).</summary>
        public int Key { get; protected set; }

        public abstract void WriteBody(NetWriter writer);

        public abstract bool SameAs(FrameEntry other);
    }

    /// <summary>Keyed sections encoded as removals plus upserts against the baseline section (both sorted by key).</summary>
    public static class KeyedSection
    {
        public static void Sort<T>(List<T> entries) where T : FrameEntry
        {
            entries.Sort((a, b) => a.Key.CompareTo(b.Key));
        }

        /// <summary>
        /// Writes <paramref name="current"/> relative to <paramref name="baseline"/> (null = full): removed keys, then the
        /// entries that are new or differ. Keys are written as ascending gaps.
        /// </summary>
        public static void WriteDelta<T>(NetWriter writer, List<T> current, List<T> baseline) where T : FrameEntry
        {
            var removed = new List<int>();
            var upserts = new List<T>();
            int i = 0;
            int j = 0;
            int nb = baseline == null ? 0 : baseline.Count;
            while (i < current.Count || j < nb)
            {
                if (j >= nb || (i < current.Count && current[i].Key < baseline[j].Key))
                {
                    upserts.Add(current[i]);
                    i++;
                }
                else if (i >= current.Count || baseline[j].Key < current[i].Key)
                {
                    removed.Add(baseline[j].Key);
                    j++;
                }
                else
                {
                    if (!current[i].SameAs(baseline[j])) upserts.Add(current[i]);
                    i++;
                    j++;
                }
            }

            writer.WriteVarUInt((uint)removed.Count);
            int last = -1;
            foreach (int key in removed)
            {
                writer.WriteVarUInt((uint)(key - last - 1));
                last = key;
            }

            writer.WriteVarUInt((uint)upserts.Count);
            last = -1;
            foreach (T entry in upserts)
            {
                writer.WriteVarUInt((uint)(entry.Key - last - 1));
                last = entry.Key;
                entry.WriteBody(writer);
            }
        }

        /// <summary>Reads a section written by <see cref="WriteDelta"/> on top of <paramref name="baseline"/>.</summary>
        public static List<T> ReadDelta<T>(NetReader reader, List<T> baseline, int max, Func<NetReader, int, T> readEntry) where T : FrameEntry
        {
            var byKey = new SortedDictionary<int, T>();
            if (baseline != null)
                foreach (T e in baseline) byKey[e.Key] = e;

            int removed = reader.ReadCount(max, 1);
            long key = -1;
            for (int k = 0; k < removed; k++)
            {
                key = NextKey(reader, key);
                if (!byKey.Remove((int)key)) throw new NetReadException(NetReadError.InvalidValue, "removal of an absent entry");
            }

            int upserts = reader.ReadCount(max, 1);
            key = -1;
            for (int k = 0; k < upserts; k++)
            {
                key = NextKey(reader, key);
                byKey[(int)key] = readEntry(reader, (int)key);
            }

            if (byKey.Count > max) throw new NetReadException(NetReadError.CountOutOfRange, "section exceeds " + max + " entries");
            return new List<T>(byKey.Values);
        }

        public static bool SameSection<T>(List<T> a, List<T> b) where T : FrameEntry
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Key != b[i].Key || !a[i].SameAs(b[i])) return false;
            return true;
        }

        private static long NextKey(NetReader reader, long previous)
        {
            long key = previous + 1 + reader.ReadVarUInt();
            if (key > int.MaxValue) throw new NetReadException(NetReadError.InvalidValue, "section key out of range");
            return key;
        }
    }

    /// <summary>Kinds of replicated spell deliveries (Sim.Spells entities).</summary>
    public enum EntityKind : byte
    {
        Other = 0,
        Projectile,
        Field,
        Deployable,
        Summon,
        Wave,
        Tether,
        Movement,
        Buff,
    }

    /// <summary>A live spell delivery: kind, owner, spell, position, velocity, size (spec L419 "spell entities").</summary>
    public sealed class EntityEntry : FrameEntry
    {
        public EntityEntry(int id, EntityKind kind, int owner, string spellId, QuantizedVec3 position, QuantizedVec3 velocity, int radiusCm, int extra)
        {
            Key = id;
            Kind = kind;
            Owner = owner;
            SpellId = spellId;
            Position = position;
            Velocity = velocity;
            RadiusCm = radiusCm;
            Extra = extra;
        }

        public EntityKind Kind { get; }
        public int Owner { get; }
        public string SpellId { get; }
        public QuantizedVec3 Position { get; }

        /// <summary>Velocity in 1/64 m/s.</summary>
        public QuantizedVec3 Velocity { get; }

        public int RadiusCm { get; }

        /// <summary>Kind-specific: wave front (cm), buff end tick; 0 otherwise.</summary>
        public int Extra { get; }

        public Vec3 PositionMeters => Quantization.DequantizePosition(Position);
        public Vec3 VelocityMetersPerSecond => new Vec3(Velocity.X / 64f, Velocity.Y / 64f, Velocity.Z / 64f);

        public override void WriteBody(NetWriter w)
        {
            w.WriteByte((byte)Kind);
            w.WriteSeat(Owner);
            w.WriteId(SpellId);
            w.WriteQuantizedPosition(Position);
            w.WriteVarInt(Velocity.X);
            w.WriteVarInt(Velocity.Y);
            w.WriteVarInt(Velocity.Z);
            w.WriteVarUInt((uint)Math.Max(0, RadiusCm));
            w.WriteVarInt(Extra);
        }

        public static EntityEntry Read(NetReader r, int key)
        {
            var kind = (EntityKind)r.ReadByteInRange(0, (byte)EntityKind.Buff);
            int owner = r.ReadSeat();
            string spell = r.ReadId();
            QuantizedVec3 pos = r.ReadQuantizedPosition();
            var vel = new QuantizedVec3(r.ReadVarIntInRange(-1 << 20, 1 << 20), r.ReadVarIntInRange(-1 << 20, 1 << 20), r.ReadVarIntInRange(-1 << 20, 1 << 20));
            int radius = (int)Math.Min(1 << 20, r.ReadVarUInt());
            int extra = r.ReadVarInt();
            return new EntityEntry(key, kind, owner, spell, pos, vel, radius, extra);
        }

        public override bool SameAs(FrameEntry other) =>
            other is EntityEntry o && o.Kind == Kind && o.Owner == Owner && o.SpellId == SpellId && o.Position == Position &&
            o.Velocity == Velocity && o.RadiusCm == RadiusCm && o.Extra == Extra;
    }

    /// <summary>An owned field or summon of the ownership registry (spec L128 limits; L421 "fields").</summary>
    public sealed class OwnedEntry : FrameEntry
    {
        public OwnedEntry(int id, byte kind, int owner, string definitionId, QuantizedVec3 position, int radiusCm, int expireTick, int healthDeci)
        {
            Key = id;
            Kind = kind;
            Owner = owner;
            DefinitionId = definitionId;
            Position = position;
            RadiusCm = radiusCm;
            ExpireTick = expireTick;
            HealthDeci = healthDeci;
        }

        /// <summary>Sim.Duel.OwnedEntityKind as a byte (0 field, 1 summon).</summary>
        public byte Kind { get; }

        public int Owner { get; }
        public string DefinitionId { get; }
        public QuantizedVec3 Position { get; }
        public int RadiusCm { get; }
        public int ExpireTick { get; }

        /// <summary>Summon health in tenths (0 for fields).</summary>
        public int HealthDeci { get; }

        public override void WriteBody(NetWriter w)
        {
            w.WriteByte(Kind);
            w.WriteSeat(Owner);
            w.WriteId(DefinitionId);
            w.WriteQuantizedPosition(Position);
            w.WriteVarUInt((uint)Math.Max(0, RadiusCm));
            w.WriteVarInt(ExpireTick);
            w.WriteVarInt(HealthDeci);
        }

        public static OwnedEntry Read(NetReader r, int key)
        {
            byte kind = r.ReadByteInRange(0, 1);
            int owner = r.ReadSeat();
            string def = r.ReadId();
            QuantizedVec3 pos = r.ReadQuantizedPosition();
            int radius = (int)Math.Min(1 << 20, r.ReadVarUInt());
            int expire = r.ReadVarInt();
            int health = r.ReadVarInt();
            return new OwnedEntry(key, kind, owner, def, pos, radius, expire, health);
        }

        public override bool SameAs(FrameEntry other) =>
            other is OwnedEntry o && o.Kind == Kind && o.Owner == Owner && o.DefinitionId == DefinitionId && o.Position == Position &&
            o.RadiusCm == RadiusCm && o.ExpireTick == ExpireTick && o.HealthDeci == HealthDeci;
    }

    /// <summary>
    /// A server-owned obscuration volume (steam, mist, dust...; spec L134, L167): kind, owner, vertical extent, the wind
    /// offset, and its occupied 0.5 m columns as a bitmap over their bounding rectangle in arena grid coordinates.
    /// </summary>
    public sealed class VolumeEntry : FrameEntry
    {
        public VolumeEntry(int id, byte kind, int owner, int baseYCm, int heightCm, int offsetXmm, int offsetZmm, bool blocksSight,
            int expiresTick, int minX, int minZ, int sizeX, int sizeZ, byte[] bits)
        {
            Key = id;
            Kind = kind;
            Owner = owner;
            BaseYCm = baseYCm;
            HeightCm = heightCm;
            OffsetXmm = offsetXmm;
            OffsetZmm = offsetZmm;
            BlocksSight = blocksSight;
            ExpiresTick = expiresTick;
            MinX = minX;
            MinZ = minZ;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Bits = bits ?? new byte[0];
        }

        /// <summary>Reactions VolumeKind as a byte.</summary>
        public byte Kind { get; }

        /// <summary>Owning player, or -1 for map-owned volumes.</summary>
        public int Owner { get; }

        public int BaseYCm { get; }
        public int HeightCm { get; }
        public int OffsetXmm { get; }
        public int OffsetZmm { get; }
        public bool BlocksSight { get; }
        public int ExpiresTick { get; }
        public int MinX { get; }
        public int MinZ { get; }
        public int SizeX { get; }
        public int SizeZ { get; }

        /// <summary>Row-major occupancy bitmap (bit x + z * SizeX).</summary>
        public byte[] Bits { get; }

        public bool Contains(int x, int z)
        {
            int lx = x - MinX;
            int lz = z - MinZ;
            if (lx < 0 || lz < 0 || lx >= SizeX || lz >= SizeZ) return false;
            int bit = lz * SizeX + lx;
            return (Bits[bit >> 3] & (1 << (bit & 7))) != 0;
        }

        public override void WriteBody(NetWriter w)
        {
            w.WriteByte(Kind);
            w.WriteSeat(Owner);
            w.WriteVarInt(BaseYCm);
            w.WriteVarUInt((uint)Math.Max(0, HeightCm));
            w.WriteVarInt(OffsetXmm);
            w.WriteVarInt(OffsetZmm);
            w.WriteBool(BlocksSight);
            w.WriteVarInt(ExpiresTick);
            w.WriteVarInt(MinX);
            w.WriteVarInt(MinZ);
            w.WriteVarUInt((uint)SizeX);
            w.WriteVarUInt((uint)SizeZ);
            w.WriteBlob(Bits, 0, Bits.Length);
        }

        public static VolumeEntry Read(NetReader r, int key)
        {
            byte kind = r.ReadByteInRange(0, 15);
            int owner = r.ReadSeat();
            int baseY = r.ReadVarIntInRange(-100000, 100000);
            int height = (int)Math.Min(100000, r.ReadVarUInt());
            int ox = r.ReadVarIntInRange(-1000000, 1000000);
            int oz = r.ReadVarIntInRange(-1000000, 1000000);
            bool blocks = r.ReadBool();
            int expires = r.ReadVarInt();
            int minX = r.ReadVarIntInRange(-4096, 4096);
            int minZ = r.ReadVarIntInRange(-4096, 4096);
            int sizeX = r.ReadVarIntInRange(0, 1024);
            int sizeZ = r.ReadVarIntInRange(0, 1024);
            byte[] bits = r.ReadBlob(WireLimits.MaxVolumeBitmapBytes);
            if (bits.Length != (sizeX * sizeZ + 7) / 8) throw new NetReadException(NetReadError.InvalidValue, "volume bitmap size mismatch");
            return new VolumeEntry(key, kind, owner, baseY, height, ox, oz, blocks, expires, minX, minZ, sizeX, sizeZ, bits);
        }

        public override bool SameAs(FrameEntry other)
        {
            if (!(other is VolumeEntry o)) return false;
            if (o.Kind != Kind || o.Owner != Owner || o.BaseYCm != BaseYCm || o.HeightCm != HeightCm || o.OffsetXmm != OffsetXmm ||
                o.OffsetZmm != OffsetZmm || o.BlocksSight != BlocksSight || o.ExpiresTick != ExpiresTick || o.MinX != MinX || o.MinZ != MinZ ||
                o.SizeX != SizeX || o.SizeZ != SizeZ || o.Bits.Length != Bits.Length)
            {
                return false;
            }

            for (int i = 0; i < Bits.Length; i++)
                if (Bits[i] != o.Bits[i]) return false;
            return true;
        }
    }

    /// <summary>Cell material bits replicated with a reaction cell.</summary>
    [Flags]
    public enum CellBits : byte
    {
        None = 0,
        Wet = 1,
        Ice = 2,
        Burning = 4,
        Charged = 8,
        Damp = 16,
    }

    /// <summary>
    /// One non-empty 0.5 m reaction cell (spec L134-L161): material units and visible state bits. Keyed by the arena
    /// grid index (Reactions CellGrid.ForArena), which the client rebuilds from the public layout.
    /// </summary>
    public sealed class CellEntry : FrameEntry
    {
        public CellEntry(int index, byte water, byte heat, byte frost, byte dust, byte sand, byte growth, byte venom, CellBits bits)
        {
            Key = index;
            Water = water;
            Heat = heat;
            Frost = frost;
            Dust = dust;
            Sand = sand;
            Growth = growth;
            Venom = venom;
            Bits = bits;
        }

        public byte Water { get; }
        public byte Heat { get; }
        public byte Frost { get; }
        public byte Dust { get; }
        public byte Sand { get; }
        public byte Growth { get; }
        public byte Venom { get; }
        public CellBits Bits { get; }

        public override void WriteBody(NetWriter w)
        {
            // A presence mask keeps sparse cells (usually one material) at two or three bytes.
            byte mask = 0;
            if (Water != 0) mask |= 1;
            if (Heat != 0) mask |= 2;
            if (Frost != 0) mask |= 4;
            if (Dust != 0) mask |= 8;
            if (Sand != 0) mask |= 16;
            if (Growth != 0) mask |= 32;
            if (Venom != 0) mask |= 64;
            w.WriteByte(mask);
            if (Water != 0) w.WriteByte(Water);
            if (Heat != 0) w.WriteByte(Heat);
            if (Frost != 0) w.WriteByte(Frost);
            if (Dust != 0) w.WriteByte(Dust);
            if (Sand != 0) w.WriteByte(Sand);
            if (Growth != 0) w.WriteByte(Growth);
            if (Venom != 0) w.WriteByte(Venom);
            w.WriteByte((byte)Bits);
        }

        public static CellEntry Read(NetReader r, int key)
        {
            if (key >= WireLimits.MaxCells * 8) throw new NetReadException(NetReadError.InvalidValue, "cell index out of range");
            byte mask = r.ReadByte();
            if ((mask & 0x80) != 0) throw new NetReadException(NetReadError.InvalidValue, "unknown cell material");
            byte water = (mask & 1) != 0 ? r.ReadByte() : (byte)0;
            byte heat = (mask & 2) != 0 ? r.ReadByte() : (byte)0;
            byte frost = (mask & 4) != 0 ? r.ReadByte() : (byte)0;
            byte dust = (mask & 8) != 0 ? r.ReadByte() : (byte)0;
            byte sand = (mask & 16) != 0 ? r.ReadByte() : (byte)0;
            byte growth = (mask & 32) != 0 ? r.ReadByte() : (byte)0;
            byte venom = (mask & 64) != 0 ? r.ReadByte() : (byte)0;
            byte bits = r.ReadByte();
            if ((bits & ~0x1F) != 0) throw new NetReadException(NetReadError.InvalidValue, "unknown cell bits");
            return new CellEntry(key, water, heat, frost, dust, sand, growth, venom, (CellBits)bits);
        }

        public override bool SameAs(FrameEntry other) =>
            other is CellEntry o && o.Water == Water && o.Heat == Heat && o.Frost == Frost && o.Dust == Dust && o.Sand == Sand &&
            o.Growth == Growth && o.Venom == Venom && o.Bits == Bits;
    }

    /// <summary>A prop body (spec L171-L173): position, yaw, durability, destroyed and moving flags.</summary>
    public sealed class PropEntry : FrameEntry
    {
        public PropEntry(int id, string definitionId, QuantizedVec3 position, ushort yaw, int durabilityDeci, bool destroyed, bool moving)
        {
            Key = id;
            DefinitionId = definitionId;
            Position = position;
            Yaw = yaw;
            DurabilityDeci = durabilityDeci;
            Destroyed = destroyed;
            Moving = moving;
        }

        public string DefinitionId { get; }
        public QuantizedVec3 Position { get; }
        public ushort Yaw { get; }
        public int DurabilityDeci { get; }
        public bool Destroyed { get; }
        public bool Moving { get; }

        public override void WriteBody(NetWriter w)
        {
            w.WriteId(DefinitionId);
            w.WriteQuantizedPosition(Position);
            w.WriteUShort(Yaw);
            w.WriteVarInt(DurabilityDeci);
            w.WriteByte((byte)((Destroyed ? 1 : 0) | (Moving ? 2 : 0)));
        }

        public static PropEntry Read(NetReader r, int key)
        {
            string def = r.ReadId();
            QuantizedVec3 pos = r.ReadQuantizedPosition();
            ushort yaw = r.ReadUShort();
            int durability = r.ReadVarInt();
            byte flags = r.ReadByteInRange(0, 3);
            return new PropEntry(key, def, pos, yaw, durability, (flags & 1) != 0, (flags & 2) != 0);
        }

        public override bool SameAs(FrameEntry other) =>
            other is PropEntry o && o.DefinitionId == DefinitionId && o.Position == Position && o.Yaw == Yaw &&
            o.DurabilityDeci == DurabilityDeci && o.Destroyed == Destroyed && o.Moving == Moving;
    }

    /// <summary>
    /// A collision solid that differs from the public layout: every active dynamic solid (prop bodies, walls, ice
    /// blocks) with its exact box, and every deactivated layout solid. The client rebuilds its prediction collision from
    /// the layout plus these, so its motor steps against the same geometry as the server's (spec L419).
    /// </summary>
    public sealed class SolidEntry : FrameEntry
    {
        public SolidEntry(int id, bool dynamic, bool active, byte kind, int flags, bool walkable, bool blocksSight, YawBox box, int durabilityDeci)
        {
            Key = id;
            Dynamic = dynamic;
            Active = active;
            Kind = kind;
            Flags = flags;
            Walkable = walkable;
            BlocksSight = blocksSight;
            Box = box;
            DurabilityDeci = durabilityDeci;
        }

        public bool Dynamic { get; }
        public bool Active { get; }

        /// <summary>Core SolidKind as a byte.</summary>
        public byte Kind { get; }

        /// <summary>Core MaterialFlags.</summary>
        public int Flags { get; }

        public bool Walkable { get; }
        public bool BlocksSight { get; }
        public YawBox Box { get; }
        public int DurabilityDeci { get; }

        public override void WriteBody(NetWriter w)
        {
            w.WriteByte((byte)((Dynamic ? 1 : 0) | (Active ? 2 : 0) | (Walkable ? 4 : 0) | (BlocksSight ? 8 : 0)));
            if (!Dynamic) return; // a deactivated layout solid: the client knows its geometry
            w.WriteByte(Kind);
            w.WriteVarInt(Flags);
            w.WriteYawBox(Box);
            w.WriteVarInt(DurabilityDeci);
        }

        public static SolidEntry Read(NetReader r, int key)
        {
            byte bits = r.ReadByteInRange(0, 15);
            bool dynamic = (bits & 1) != 0;
            bool active = (bits & 2) != 0;
            if (!dynamic) return new SolidEntry(key, false, active, 0, 0, (bits & 4) != 0, (bits & 8) != 0, default, 0);
            byte kind = r.ReadByteInRange(0, 31);
            int flags = r.ReadVarIntInRange(0, 1 << 20);
            YawBox box = r.ReadYawBox();
            int durability = r.ReadVarInt();
            return new SolidEntry(key, true, active, kind, flags, (bits & 4) != 0, (bits & 8) != 0, box, durability);
        }

        public override bool SameAs(FrameEntry other)
        {
            if (!(other is SolidEntry o) || o.Dynamic != Dynamic || o.Active != Active || o.Walkable != Walkable || o.BlocksSight != BlocksSight) return false;
            if (!Dynamic) return true;
            return o.Kind == Kind && o.Flags == Flags && o.DurabilityDeci == DurabilityDeci &&
                o.Box.Center == Box.Center && o.Box.HalfExtents == Box.HalfExtents && o.Box.YawDeg == Box.YawDeg;
        }
    }
}
