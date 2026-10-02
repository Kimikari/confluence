using System;
using System.IO;
using Confluence.Core.Random;

namespace Confluence.Reactions
{
    /// <summary>
    /// Compact, deterministic serialization of a reaction world's dynamic state: active cells (only non-empty
    /// fields), volumes, hazards, conductor states, pending conductor releases, active gusts, schedule progress,
    /// field layers, ignited solids, gravity deflection history, and id counters (spec L419 "The server owns ... field states, reactions"; L664 replay records). Static arena data
    /// (solids, flags of inactive cells, light shafts) and Sim registrations (conductor shapes, prisms, gravity fields)
    /// are not included: a client or replay rebuilds the world from the same layout and registrations, then restores.
    /// Little-endian, IEEE-754 bit-exact floats, every collection in ascending id order.
    /// </summary>
    public sealed class ReactionSnapshot
    {
        internal const uint Magic = 0x58524643; // "CFRX"
        internal const ushort Version = 1;

        private readonly byte[] _data;

        internal ReactionSnapshot(byte[] data, int tick, int activeCells, int volumes)
        {
            _data = data;
            Tick = tick;
            ActiveCellCount = activeCells;
            VolumeCount = volumes;
            Hash = ComputeHash(data);
        }

        public int Tick { get; }
        public int ActiveCellCount { get; }
        public int VolumeCount { get; }

        /// <summary>Deterministic 64-bit hash of the serialized state.</summary>
        public ulong Hash { get; }

        public int SizeBytes => _data.Length;

        public byte[] ToBytes() => (byte[])_data.Clone();

        internal byte[] Data => _data;

        /// <summary>Parses serialized bytes (header only; the body is read by <see cref="ReactionWorld.RestoreSnapshot"/>).</summary>
        public static ReactionSnapshot FromBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using (var reader = new BinaryReader(new MemoryStream(data, false)))
            {
                if (reader.ReadUInt32() != Magic)
                {
                    throw new InvalidDataException("Not a reaction snapshot.");
                }

                ushort version = reader.ReadUInt16();
                if (version != Version)
                {
                    throw new InvalidDataException("Unsupported reaction snapshot version " + version + ".");
                }

                int tick = reader.ReadInt32();
                int activeCells = reader.ReadInt32();
                int volumes = reader.ReadInt32();
                return new ReactionSnapshot((byte[])data.Clone(), tick, activeCells, volumes);
            }
        }

        private static ulong ComputeHash(byte[] data)
        {
            ulong hash = SeedHash.FnvOffsetBasis;
            unchecked
            {
                for (int i = 0; i < data.Length; i++)
                {
                    hash = (hash ^ data[i]) * SeedHash.FnvPrime;
                }
            }

            return SeedHash.Mix(hash ^ (ulong)data.Length);
        }
    }
}
