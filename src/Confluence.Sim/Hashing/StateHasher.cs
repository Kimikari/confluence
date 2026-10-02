using System;
using Confluence.Core.Geometry;
using Confluence.Core.Random;

namespace Confluence.Sim.Hashing
{
    /// <summary>
    /// Order-dependent 64-bit hash over authoritative state (spec L427: physics state is deterministic at the rule
    /// level). Floats hash by their exact bit pattern, so two worlds agree only if every value matches exactly.
    /// </summary>
    public struct StateHasher
    {
        private ulong _hash;
        private int _count;

        public static StateHasher Create(ulong seed = 0x434F4E464C55454EUL)
        {
            return new StateHasher { _hash = SeedHash.Mix(seed) };
        }

        public ulong Value => SeedHash.Combine(_hash, (ulong)_count);

        public void Add(ulong value)
        {
            unchecked
            {
                _hash = SeedHash.Combine(_hash, value);
                _count++;
            }
        }

        public void Add(long value) => Add(unchecked((ulong)value));

        public void Add(int value) => Add(unchecked((ulong)(long)value));

        public void Add(uint value) => Add((ulong)value);

        public void Add(bool value) => Add(value ? 1UL : 0UL);

        public void Add(float value) => Add(unchecked((ulong)(uint)BitConverter.SingleToInt32Bits(value)));

        public void Add(Vec3 value)
        {
            Add(value.X);
            Add(value.Y);
            Add(value.Z);
        }

        public void Add(Vec2 value)
        {
            Add(value.X);
            Add(value.Y);
        }

        public void Add(string value) => Add(value == null ? 0UL : SeedHash.Of(value));
    }
}
