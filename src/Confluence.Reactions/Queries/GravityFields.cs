using System;
using System.Collections.Generic;
using System.IO;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Reactions.Queries
{
    /// <summary>Projectile classes for the gravity rule (spec L160: physical projectiles bend; beams do not).</summary>
    public enum ProjectileClass
    {
        Physical,
        Magical,
        Beam,
    }

    /// <summary>A visible deflection field (spec L160; PROP-039 Gravity Pebble; BIO-023 coils).</summary>
    public sealed class GravityFieldDef
    {
        public int Id { get; set; }
        public Vec3 Center { get; set; }
        public float Radius { get; set; }

        /// <summary>Stated maximum change; clamped to the global 20 degrees (L160). Local text may be stricter.</summary>
        public float MaxDeflectionDeg { get; set; } = GlobalRules.World.GravityMaxDeflectionDeg;

        /// <summary>First tick the field deflects (BIO-023 "after a 0.25 s visible pulse").</summary>
        public int ActiveFromTick { get; set; }

        /// <summary>Tick the field ends (int.MaxValue for none).</summary>
        public int ExpiresTick { get; set; } = int.MaxValue;

        /// <summary>Pushes projectiles away from the center instead of bending them toward it.</summary>
        public bool Repels { get; set; }

        public int Owner { get; set; } = ReactionRules.MapOwner;
    }

    /// <summary>
    /// Gravity deflection query. Each field bends an eligible physical projectile once, on entry, by at most its
    /// stated angle, and a projectile's total change from all fields never exceeds 20 degrees (L160). Beams and
    /// magical projectiles are not bent unless their spell says so.
    /// </summary>
    public sealed class GravityFieldSet
    {
        private readonly List<GravityFieldDef> _fields = new List<GravityFieldDef>();
        private readonly HashSet<long> _deflectedPairs = new HashSet<long>();
        private readonly Dictionary<int, float> _totalDeflection = new Dictionary<int, float>();

        public IReadOnlyList<GravityFieldDef> Fields => _fields;

        public void Register(GravityFieldDef field)
        {
            Remove(field.Id);
            _fields.Add(field);
            _fields.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        public bool Remove(int id) => _fields.RemoveAll(f => f.Id == id) > 0;

        /// <summary>Drops a despawned projectile's deflection history.</summary>
        public void ForgetProjectile(int projectileId)
        {
            _totalDeflection.Remove(projectileId);
            _deflectedPairs.RemoveWhere(key => (int)(key & 0xFFFFFFFF) == projectileId);
        }

        internal void RemoveExpired(int tick) => _fields.RemoveAll(f => f.ExpiresTick <= tick);

        /// <summary>Total degrees a projectile has been bent so far.</summary>
        public float TotalDeflectionDeg(int projectileId) =>
            _totalDeflection.TryGetValue(projectileId, out float deg) ? deg : 0f;

        /// <summary>
        /// Bends an eligible projectile that has just entered a field. Returns true and the new velocity when a
        /// deflection applied (the speed is unchanged).
        /// </summary>
        public bool TryDeflect(int projectileId, Vec3 position, Vec3 velocity, ProjectileClass projectileClass, int tick,
            out Vec3 newVelocity, out int fieldId)
        {
            newVelocity = velocity;
            fieldId = -1;
            if (projectileClass != ProjectileClass.Physical)
            {
                return false;
            }

            float speed = velocity.Length();
            if (speed < CMath.Epsilon)
            {
                return false;
            }

            bool deflected = false;
            for (int i = 0; i < _fields.Count; i++)
            {
                GravityFieldDef field = _fields[i];
                if (tick < field.ActiveFromTick || tick >= field.ExpiresTick ||
                    Vec3.DistanceSquared(position, field.Center) > field.Radius * field.Radius)
                {
                    continue;
                }

                long key = ((long)field.Id << 32) | (uint)projectileId;
                if (!_deflectedPairs.Add(key))
                {
                    continue;
                }

                float used = TotalDeflectionDeg(projectileId);
                float budget = Math.Min(CMath.Clamp(field.MaxDeflectionDeg, 0f, GlobalRules.World.GravityMaxDeflectionDeg),
                    GlobalRules.World.GravityMaxDeflectionDeg - used);
                if (budget <= 0f)
                {
                    continue;
                }

                Vec3 toward = field.Repels ? position - field.Center : field.Center - position;
                if (!RotateToward(newVelocity, toward, budget, out Vec3 bent, out float appliedDeg))
                {
                    continue;
                }

                newVelocity = bent;
                _totalDeflection[projectileId] = used + appliedDeg;
                fieldId = field.Id;
                deflected = true;
            }

            return deflected;
        }

        /// <summary>Writes the per-projectile deflection history (dynamic state) in ascending key order.</summary>
        internal void WriteHistory(BinaryWriter writer)
        {
            var projectiles = new List<int>(_totalDeflection.Keys);
            projectiles.Sort();
            writer.Write(projectiles.Count);
            for (int i = 0; i < projectiles.Count; i++)
            {
                writer.Write(projectiles[i]);
                writer.Write(_totalDeflection[projectiles[i]]);
            }

            var pairs = new List<long>(_deflectedPairs);
            pairs.Sort();
            writer.Write(pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                writer.Write(pairs[i]);
            }
        }

        /// <summary>Replaces the deflection history with one written by <see cref="WriteHistory"/>.</summary>
        internal void ReadHistory(BinaryReader reader)
        {
            _totalDeflection.Clear();
            _deflectedPairs.Clear();
            int projectiles = reader.ReadInt32();
            for (int i = 0; i < projectiles; i++)
            {
                int id = reader.ReadInt32();
                _totalDeflection[id] = reader.ReadSingle();
            }

            int pairs = reader.ReadInt32();
            for (int i = 0; i < pairs; i++)
            {
                _deflectedPairs.Add(reader.ReadInt64());
            }
        }

        /// <summary>Rotates v toward the target direction by at most maxDeg, keeping its length.</summary>
        internal static bool RotateToward(Vec3 v, Vec3 target, float maxDeg, out Vec3 result, out float appliedDeg)
        {
            result = v;
            appliedDeg = 0f;
            float speed = v.Length();
            float targetLen = target.Length();
            if (speed < CMath.Epsilon || targetLen < CMath.Epsilon)
            {
                return false;
            }

            Vec3 u = v / speed;
            Vec3 w = target / targetLen;
            float cos = CMath.Clamp(Vec3.Dot(u, w), -1f, 1f);
            float angle = CMath.RadToDeg(MathF.Acos(cos));
            if (angle < 1e-4f)
            {
                return false;
            }

            Vec3 perp = w - u * cos;
            float perpLen = perp.Length();
            if (perpLen < CMath.Epsilon)
            {
                return false; // exactly opposite: no unique bend plane
            }

            perp = perp / perpLen;
            appliedDeg = MathF.Min(angle, maxDeg);
            float rad = CMath.DegToRad(appliedDeg);
            result = (u * MathF.Cos(rad) + perp * MathF.Sin(rad)) * speed;
            return true;
        }
    }
}
