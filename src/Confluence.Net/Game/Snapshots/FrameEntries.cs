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

        /// <summary>
        /// Writes this entry as a change of <paramref name="previous"/>, the receiver's entry of the same key (research
        /// §2.4.4 field groups: unchanged groups cost nothing). The default writes the full body.
        /// </summary>
        public virtual void WriteBodyAgainst(NetWriter writer, FrameEntry previous) => WriteBody(writer);

        public abstract bool SameAs(FrameEntry other);

        /// <summary>Reads a field-group mask and rejects undefined bits.</summary>
        protected static byte ReadMask(NetReader r, byte defined)
        {
            byte mask = r.ReadByte();
            if ((mask & ~defined) != 0) throw new NetReadException(NetReadError.InvalidValue, "unknown field group");
            return mask;
        }

        protected static void WriteVelocityDelta(NetWriter w, QuantizedVec3 value, QuantizedVec3 previous)
        {
            w.WriteVarInt(value.X - previous.X);
            w.WriteVarInt(value.Y - previous.Y);
            w.WriteVarInt(value.Z - previous.Z);
        }

        protected static QuantizedVec3 ReadVelocityDelta(NetReader r, QuantizedVec3 previous)
        {
            return new QuantizedVec3(VelocityAxis((long)previous.X + r.ReadVarInt()), VelocityAxis((long)previous.Y + r.ReadVarInt()), VelocityAxis((long)previous.Z + r.ReadVarInt()));
        }

        private static int VelocityAxis(long v)
        {
            if (v < -(1 << 20) || v > 1 << 20) throw new NetReadException(NetReadError.InvalidValue, "velocity out of range");
            return (int)v;
        }
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
            Diff(current, baseline, removed, upserts);
            Write(writer, removed, upserts, baseline);
        }

        /// <summary>The keys removed since <paramref name="baseline"/> and the entries that are new or differ (both ascending).</summary>
        public static void Diff<T>(List<T> current, List<T> baseline, List<int> removed, List<T> upserts) where T : FrameEntry
        {
            removed.Clear();
            upserts.Clear();
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
        }

        /// <summary>Writes removals and upserts (each ascending by key) in the layout <see cref="ReadDelta"/> reads.</summary>
        public static void Write<T>(NetWriter writer, List<int> removed, List<T> upserts, List<T> baseline = null) where T : FrameEntry
        {
            writer.WriteVarUInt((uint)removed.Count);
            int last = -1;
            foreach (int key in removed)
            {
                writer.WriteVarUInt((uint)(key - last - 1));
                last = key;
            }

            writer.WriteVarUInt((uint)upserts.Count);
            last = -1;
            int b = 0;
            int nb = baseline == null ? 0 : baseline.Count;
            foreach (T entry in upserts)
            {
                writer.WriteVarUInt((uint)(entry.Key - last - 1));
                last = entry.Key;
                while (b < nb && baseline[b].Key < entry.Key) b++;
                if (b < nb && baseline[b].Key == entry.Key) entry.WriteBodyAgainst(writer, baseline[b]);
                else entry.WriteBody(writer);
            }
        }

        /// <summary>The entry of <paramref name="key"/> in a sorted section, or null.</summary>
        public static T Find<T>(List<T> section, int key) where T : FrameEntry
        {
            if (section == null) return null;
            int lo = 0;
            int hi = section.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int k = section[mid].Key;
                if (k == key) return section[mid];
                if (k < key) lo = mid + 1;
                else hi = mid - 1;
            }

            return null;
        }

        /// <summary>Reads a section written by <see cref="WriteDelta"/> on top of <paramref name="baseline"/>.</summary>
        public static List<T> ReadDelta<T>(NetReader reader, List<T> baseline, int max, Func<NetReader, int, T> readEntry) where T : FrameEntry
        {
            return ReadDelta(reader, baseline, max, readEntry, null);
        }

        /// <summary>
        /// As <see cref="ReadDelta{T}(NetReader, List{T}, int, Func{NetReader, int, T})"/>; upserts of keys the baseline
        /// holds are read with <paramref name="readAgainst"/> on top of the held entry when given.
        /// </summary>
        public static List<T> ReadDelta<T>(NetReader reader, List<T> baseline, int max, Func<NetReader, int, T> readEntry, Func<NetReader, int, T, T> readAgainst) where T : FrameEntry
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
                if (readAgainst != null && byKey.TryGetValue((int)key, out T previous)) byKey[(int)key] = readAgainst(reader, (int)key, previous);
                else byKey[(int)key] = readEntry(reader, (int)key);
            }

            if (byKey.Count > max) throw new NetReadException(NetReadError.CountOutOfRange, "section exceeds " + max + " entries");
            return new List<T>(byKey.Values);
        }

        /// <summary>
        /// The section a receiver holds after applying <paramref name="removed"/> and <paramref name="upserts"/> to
        /// <paramref name="baseline"/> (null = empty): exactly what <see cref="ReadDelta"/> rebuilds.
        /// </summary>
        public static List<T> Apply<T>(List<T> baseline, List<int> removed, List<T> upserts) where T : FrameEntry
        {
            var result = new List<T>((baseline?.Count ?? 0) + upserts.Count);
            int b = 0;
            int u = 0;
            int r = 0;
            int nb = baseline == null ? 0 : baseline.Count;
            while (b < nb || u < upserts.Count)
            {
                int bk = b < nb ? baseline[b].Key : int.MaxValue;
                int uk = u < upserts.Count ? upserts[u].Key : int.MaxValue;
                if (uk <= bk)
                {
                    result.Add(upserts[u++]);
                    if (uk == bk) b++;
                    continue;
                }

                while (r < removed.Count && removed[r] < bk) r++;
                if (r < removed.Count && removed[r] == bk)
                {
                    b++;
                    continue;
                }

                result.Add(baseline[b++]);
            }

            return result;
        }

        /// <summary>Bytes a varint takes.</summary>
        public static int VarUIntSize(uint value)
        {
            int n = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                n++;
            }

            return n;
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

        /// <summary>
        /// Kinds that ride on their owner (movement, buff, other): they carry the origin as their position and are drawn
        /// on the owner's replicated pose, so they can never locate a hidden owner (spec L120).
        /// </summary>
        public static bool IsOwnerAttached(EntityKind kind) => kind == EntityKind.Movement || kind == EntityKind.Buff || kind == EntityKind.Other;

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

        private const byte EPos = 1, EVel = 2, EIdentity = 4, EExtra = 8, EAll = 15;

        public override void WriteBodyAgainst(NetWriter w, FrameEntry previous)
        {
            if (!(previous is EntityEntry p))
            {
                WriteBody(w);
                return;
            }

            byte mask = 0;
            if (Position != p.Position) mask |= EPos;
            if (Velocity != p.Velocity) mask |= EVel;
            if (Kind != p.Kind || Owner != p.Owner || SpellId != p.SpellId || RadiusCm != p.RadiusCm) mask |= EIdentity;
            if (Extra != p.Extra) mask |= EExtra;
            w.WriteByte(mask);
            if ((mask & EPos) != 0) w.WritePositionDelta(Position, p.Position);
            if ((mask & EVel) != 0) WriteVelocityDelta(w, Velocity, p.Velocity);
            if ((mask & EIdentity) != 0)
            {
                w.WriteByte((byte)Kind);
                w.WriteSeat(Owner);
                w.WriteId(SpellId);
                w.WriteVarUInt((uint)Math.Max(0, RadiusCm));
            }

            if ((mask & EExtra) != 0) w.WriteVarInt(Extra);
        }

        public static EntityEntry ReadAgainst(NetReader r, int key, EntityEntry p)
        {
            byte mask = ReadMask(r, EAll);
            QuantizedVec3 pos = (mask & EPos) != 0 ? r.ReadPositionDelta(p.Position) : p.Position;
            QuantizedVec3 vel = (mask & EVel) != 0 ? ReadVelocityDelta(r, p.Velocity) : p.Velocity;
            EntityKind kind = p.Kind;
            int owner = p.Owner;
            string spell = p.SpellId;
            int radius = p.RadiusCm;
            if ((mask & EIdentity) != 0)
            {
                kind = (EntityKind)r.ReadByteInRange(0, (byte)EntityKind.Buff);
                owner = r.ReadSeat();
                spell = r.ReadId();
                radius = (int)Math.Min(1 << 20, r.ReadVarUInt());
            }

            int extra = (mask & EExtra) != 0 ? r.ReadVarInt() : p.Extra;
            return new EntityEntry(key, kind, owner, spell, pos, vel, radius, extra);
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

        private const byte OPos = 1, OIdentity = 2, OExpire = 4, OHealth = 8, OAll = 15;

        public override void WriteBodyAgainst(NetWriter w, FrameEntry previous)
        {
            if (!(previous is OwnedEntry p))
            {
                WriteBody(w);
                return;
            }

            byte mask = 0;
            if (Position != p.Position) mask |= OPos;
            if (Kind != p.Kind || Owner != p.Owner || DefinitionId != p.DefinitionId || RadiusCm != p.RadiusCm) mask |= OIdentity;
            if (ExpireTick != p.ExpireTick) mask |= OExpire;
            if (HealthDeci != p.HealthDeci) mask |= OHealth;
            w.WriteByte(mask);
            if ((mask & OPos) != 0) w.WritePositionDelta(Position, p.Position);
            if ((mask & OIdentity) != 0)
            {
                w.WriteByte(Kind);
                w.WriteSeat(Owner);
                w.WriteId(DefinitionId);
                w.WriteVarUInt((uint)Math.Max(0, RadiusCm));
            }

            if ((mask & OExpire) != 0) w.WriteVarInt(ExpireTick);
            if ((mask & OHealth) != 0) w.WriteVarInt(HealthDeci);
        }

        public static OwnedEntry ReadAgainst(NetReader r, int key, OwnedEntry p)
        {
            byte mask = ReadMask(r, OAll);
            QuantizedVec3 pos = (mask & OPos) != 0 ? r.ReadPositionDelta(p.Position) : p.Position;
            byte kind = p.Kind;
            int owner = p.Owner;
            string def = p.DefinitionId;
            int radius = p.RadiusCm;
            if ((mask & OIdentity) != 0)
            {
                kind = r.ReadByteInRange(0, 1);
                owner = r.ReadSeat();
                def = r.ReadId();
                radius = (int)Math.Min(1 << 20, r.ReadVarUInt());
            }

            int expire = (mask & OExpire) != 0 ? r.ReadVarInt() : p.ExpireTick;
            int health = (mask & OHealth) != 0 ? r.ReadVarInt() : p.HealthDeci;
            return new OwnedEntry(key, kind, owner, def, pos, radius, expire, health);
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

        private const byte VOffset = 1, VExtent = 2, VShape = 4, VAll = 7;

        /// <summary>Wind drift changes only the offset group; the bitmap is resent only when the columns change (§2.4.6).</summary>
        public override void WriteBodyAgainst(NetWriter w, FrameEntry previous)
        {
            if (!(previous is VolumeEntry p))
            {
                WriteBody(w);
                return;
            }

            byte mask = 0;
            if (OffsetXmm != p.OffsetXmm || OffsetZmm != p.OffsetZmm) mask |= VOffset;
            if (Kind != p.Kind || Owner != p.Owner || BaseYCm != p.BaseYCm || HeightCm != p.HeightCm || BlocksSight != p.BlocksSight || ExpiresTick != p.ExpiresTick) mask |= VExtent;
            if (!SameShape(p)) mask |= VShape;
            w.WriteByte(mask);
            if ((mask & VOffset) != 0)
            {
                w.WriteVarInt(OffsetXmm - p.OffsetXmm);
                w.WriteVarInt(OffsetZmm - p.OffsetZmm);
            }

            if ((mask & VExtent) != 0)
            {
                w.WriteByte(Kind);
                w.WriteSeat(Owner);
                w.WriteVarInt(BaseYCm);
                w.WriteVarUInt((uint)Math.Max(0, HeightCm));
                w.WriteBool(BlocksSight);
                w.WriteVarInt(ExpiresTick);
            }

            if ((mask & VShape) != 0)
            {
                w.WriteVarInt(MinX);
                w.WriteVarInt(MinZ);
                w.WriteVarUInt((uint)SizeX);
                w.WriteVarUInt((uint)SizeZ);
                w.WriteBlob(Bits, 0, Bits.Length);
            }
        }

        public static VolumeEntry ReadAgainst(NetReader r, int key, VolumeEntry p)
        {
            byte mask = ReadMask(r, VAll);
            int ox = p.OffsetXmm;
            int oz = p.OffsetZmm;
            if ((mask & VOffset) != 0)
            {
                ox = OffsetAxis((long)p.OffsetXmm + r.ReadVarInt());
                oz = OffsetAxis((long)p.OffsetZmm + r.ReadVarInt());
            }

            byte kind = p.Kind;
            int owner = p.Owner;
            int baseY = p.BaseYCm;
            int height = p.HeightCm;
            bool blocks = p.BlocksSight;
            int expires = p.ExpiresTick;
            if ((mask & VExtent) != 0)
            {
                kind = r.ReadByteInRange(0, 15);
                owner = r.ReadSeat();
                baseY = r.ReadVarIntInRange(-100000, 100000);
                height = (int)Math.Min(100000, r.ReadVarUInt());
                blocks = r.ReadBool();
                expires = r.ReadVarInt();
            }

            int minX = p.MinX;
            int minZ = p.MinZ;
            int sizeX = p.SizeX;
            int sizeZ = p.SizeZ;
            byte[] bits = p.Bits;
            if ((mask & VShape) != 0)
            {
                minX = r.ReadVarIntInRange(-4096, 4096);
                minZ = r.ReadVarIntInRange(-4096, 4096);
                sizeX = r.ReadCountValue(0, 1024);
                sizeZ = r.ReadCountValue(0, 1024);
                bits = r.ReadBlob(WireLimits.MaxVolumeBitmapBytes);
                if (bits.Length != (sizeX * sizeZ + 7) / 8) throw new NetReadException(NetReadError.InvalidValue, "volume bitmap size mismatch");
            }

            return new VolumeEntry(key, kind, owner, baseY, height, ox, oz, blocks, expires, minX, minZ, sizeX, sizeZ, bits);
        }

        private static int OffsetAxis(long v)
        {
            if (v < -1000000 || v > 1000000) throw new NetReadException(NetReadError.InvalidValue, "volume offset out of range");
            return (int)v;
        }

        private bool SameShape(VolumeEntry o)
        {
            if (o.MinX != MinX || o.MinZ != MinZ || o.SizeX != SizeX || o.SizeZ != SizeZ || o.Bits.Length != Bits.Length) return false;
            for (int i = 0; i < Bits.Length; i++)
                if (Bits[i] != o.Bits[i]) return false;
            return true;
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
            int sizeX = r.ReadCountValue(0, 1024);
            int sizeZ = r.ReadCountValue(0, 1024);
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

        private const byte PPos = 1, PYaw = 2, PDurability = 4, PFlags = 8, PDefinition = 16, PAll = 31;

        public override void WriteBodyAgainst(NetWriter w, FrameEntry previous)
        {
            if (!(previous is PropEntry p))
            {
                WriteBody(w);
                return;
            }

            byte mask = 0;
            if (Position != p.Position) mask |= PPos;
            if (Yaw != p.Yaw) mask |= PYaw;
            if (DurabilityDeci != p.DurabilityDeci) mask |= PDurability;
            if (Destroyed != p.Destroyed || Moving != p.Moving) mask |= PFlags;
            if (DefinitionId != p.DefinitionId) mask |= PDefinition;
            w.WriteByte(mask);
            if ((mask & PPos) != 0) w.WritePositionDelta(Position, p.Position);
            if ((mask & PYaw) != 0) w.WriteUShort(Yaw);
            if ((mask & PDurability) != 0) w.WriteVarInt(DurabilityDeci);
            if ((mask & PFlags) != 0) w.WriteByte((byte)((Destroyed ? 1 : 0) | (Moving ? 2 : 0)));
            if ((mask & PDefinition) != 0) w.WriteId(DefinitionId);
        }

        public static PropEntry ReadAgainst(NetReader r, int key, PropEntry p)
        {
            byte mask = ReadMask(r, PAll);
            QuantizedVec3 pos = (mask & PPos) != 0 ? r.ReadPositionDelta(p.Position) : p.Position;
            ushort yaw = (mask & PYaw) != 0 ? r.ReadUShort() : p.Yaw;
            int durability = (mask & PDurability) != 0 ? r.ReadVarInt() : p.DurabilityDeci;
            bool destroyed = p.Destroyed;
            bool moving = p.Moving;
            if ((mask & PFlags) != 0)
            {
                byte flags = r.ReadByteInRange(0, 3);
                destroyed = (flags & 1) != 0;
                moving = (flags & 2) != 0;
            }

            string def = (mask & PDefinition) != 0 ? r.ReadId() : p.DefinitionId;
            return new PropEntry(key, def, pos, yaw, durability, destroyed, moving);
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
