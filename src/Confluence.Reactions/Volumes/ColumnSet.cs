using System;
using System.Collections.Generic;

namespace Confluence.Reactions.Volumes
{
    /// <summary>
    /// A set of 0.5 m grid columns, stored as a growable bitmap over its bounding rectangle. Coordinates are
    /// integer cell coordinates of the grid the volume was created on; a volume's continuous wind offset is applied
    /// on top. Iteration is row-major (z outer, x inner), matching grid index order.
    /// </summary>
    public sealed class ColumnSet
    {
        private bool[] _bits = new bool[0];

        public int MinX { get; private set; }
        public int MinZ { get; private set; }
        public int SizeX { get; private set; }
        public int SizeZ { get; private set; }
        public int Count { get; private set; }

        public int MaxX => MinX + SizeX - 1;
        public int MaxZ => MinZ + SizeZ - 1;

        public bool IsEmpty => Count == 0;

        public bool Contains(int x, int z)
        {
            int lx = x - MinX;
            int lz = z - MinZ;
            if (lx < 0 || lz < 0 || lx >= SizeX || lz >= SizeZ)
            {
                return false;
            }

            return _bits[lz * SizeX + lx];
        }

        public bool Add(int x, int z)
        {
            EnsureCovers(x, z);
            int i = (z - MinZ) * SizeX + (x - MinX);
            if (_bits[i])
            {
                return false;
            }

            _bits[i] = true;
            Count++;
            return true;
        }

        public bool Remove(int x, int z)
        {
            int lx = x - MinX;
            int lz = z - MinZ;
            if (lx < 0 || lz < 0 || lx >= SizeX || lz >= SizeZ)
            {
                return false;
            }

            int i = lz * SizeX + lx;
            if (!_bits[i])
            {
                return false;
            }

            _bits[i] = false;
            Count--;
            return true;
        }

        public void Clear()
        {
            _bits = new bool[0];
            MinX = MinZ = SizeX = SizeZ = 0;
            Count = 0;
        }

        /// <summary>Appends occupied columns in row-major order as (x, z) pairs.</summary>
        public void GetColumns(List<int> xs, List<int> zs)
        {
            for (int lz = 0; lz < SizeZ; lz++)
            {
                for (int lx = 0; lx < SizeX; lx++)
                {
                    if (_bits[lz * SizeX + lx])
                    {
                        xs.Add(MinX + lx);
                        zs.Add(MinZ + lz);
                    }
                }
            }
        }

        public ColumnSet Clone()
        {
            var copy = new ColumnSet
            {
                MinX = MinX,
                MinZ = MinZ,
                SizeX = SizeX,
                SizeZ = SizeZ,
                Count = Count,
            };
            copy._bits = (bool[])_bits.Clone();
            return copy;
        }

        private void EnsureCovers(int x, int z)
        {
            if (SizeX == 0)
            {
                MinX = x;
                MinZ = z;
                SizeX = 1;
                SizeZ = 1;
                _bits = new bool[1];
                return;
            }

            if (x >= MinX && z >= MinZ && x <= MaxX && z <= MaxZ)
            {
                return;
            }

            int newMinX = Math.Min(MinX, x);
            int newMinZ = Math.Min(MinZ, z);
            int newMaxX = Math.Max(MaxX, x);
            int newMaxZ = Math.Max(MaxZ, z);
            int newSizeX = newMaxX - newMinX + 1;
            int newSizeZ = newMaxZ - newMinZ + 1;
            var bits = new bool[newSizeX * newSizeZ];
            for (int lz = 0; lz < SizeZ; lz++)
            {
                for (int lx = 0; lx < SizeX; lx++)
                {
                    if (_bits[lz * SizeX + lx])
                    {
                        bits[(MinZ + lz - newMinZ) * newSizeX + (MinX + lx - newMinX)] = true;
                    }
                }
            }

            _bits = bits;
            MinX = newMinX;
            MinZ = newMinZ;
            SizeX = newSizeX;
            SizeZ = newSizeZ;
        }
    }
}
