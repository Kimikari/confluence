using Confluence.Client.Arena;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using UnityEngine;

namespace Confluence.Client.Cameras
{
    /// <summary>
    /// PREVIEW-ONLY walking for arena inspection, to be replaced by the shared Sim.CharacterMotor once it exists
    /// (docs/ARCHITECTURE.md: the client predicts with the server's motor). It reuses the Core movement constants
    /// (GlobalRules.Movement, DECISIONS D-024: walk 5.5, sprint 7 m/s, gravity 18, jump apex 1.1 m, step 0.35 m,
    /// capsule radius 0.4 m) against the layout's solids: ground from the highest surface within a step,
    /// walls pushed out as circles against yaw-box footprints. It never decides gameplay.
    /// </summary>
    public sealed class PreviewWalker
    {
        public const float KillPlaneY = -20f;
        private const int CollisionPasses = 3;

        private readonly ArenaLayout _layout;
        private Vector3 _velocity;

        public PreviewWalker(ArenaLayout layout, Vector3 feet)
        {
            _layout = layout;
            Feet = feet;
            Respawn = feet;
        }

        /// <summary>Foot position (capsule bottom).</summary>
        public Vector3 Feet { get; private set; }

        public Vector3 Respawn { get; set; }

        public bool Grounded { get; private set; }

        public float Radius => GlobalRules.Movement.CapsuleRadius;

        public float Height => GlobalRules.Movement.StandingHeight;

        public void Teleport(Vector3 feet)
        {
            Feet = feet;
            _velocity = Vector3.zero;
        }

        /// <summary>Advances one frame. wish is the desired horizontal direction (length 0-1) in world space.</summary>
        public void Step(Vector3 wish, bool sprint, bool jump, float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            float speed = sprint ? GlobalRules.Movement.SprintSpeed : GlobalRules.Movement.WalkSpeed;
            Vector3 target = Vector3.ClampMagnitude(new Vector3(wish.x, 0f, wish.z), 1f) * speed;
            float accel = Grounded ? GlobalRules.Movement.GroundAcceleration : GlobalRules.Movement.AirAcceleration;
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), target, accel * dt);
            _velocity = new Vector3(horizontal.x, _velocity.y, horizontal.z);

            if (Grounded && jump)
            {
                _velocity.y = GlobalRules.Movement.JumpLaunchSpeed;
                Grounded = false;
            }

            _velocity.y -= GlobalRules.Movement.Gravity * dt;
            Vector3 next = Feet + _velocity * dt;
            next = ResolveWalls(next);

            // Land on (or, when already grounded, stick down to) the highest surface within a step.
            bool wasGrounded = Grounded && !jump;
            float ground = ArenaQueries.SurfaceHeightAt(_layout, next.x, next.z, Mathf.Max(Feet.y, next.y) + GlobalRules.Movement.StepHeight);
            bool snap = !float.IsNaN(ground) && (next.y <= ground || (wasGrounded && next.y - ground <= GlobalRules.Movement.StepHeight));
            if (snap && _velocity.y <= 0f)
            {
                next.y = ground;
                _velocity.y = 0f;
                Grounded = true;
            }
            else
            {
                Grounded = false;
            }

            if (next.y < KillPlaneY)
            {
                Teleport(Respawn);
                return;
            }

            Feet = next;
        }

        // Pushes the capsule's circle out of every solid whose vertical span blocks it (above step height).
        private Vector3 ResolveWalls(Vector3 p)
        {
            if (_layout?.Solids == null)
            {
                return p;
            }

            float radius = Radius;
            for (int pass = 0; pass < CollisionPasses; pass++)
            {
                bool moved = false;
                for (int i = 0; i < _layout.Solids.Count; i++)
                {
                    Solid solid = _layout.Solids[i];
                    if (solid == null || solid.IsRamp)
                    {
                        continue; // ramps are walked on, not pushed against
                    }

                    YawBox box = solid.Box;
                    if (box.Top <= p.y + GlobalRules.Movement.StepHeight || box.Bottom >= p.y + Height)
                    {
                        continue;
                    }

                    var point = new Vec2(p.x, p.z);
                    Vec2 closest = box.ClosestPointXZ(point);
                    Vec2 offset = point - closest;
                    float distSq = offset.LengthSquared();
                    if (distSq >= radius * radius)
                    {
                        continue;
                    }

                    Vec2 pushed;
                    if (distSq > 1e-8f)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        pushed = closest + offset * (radius / dist);
                    }
                    else
                    {
                        pushed = PushOutOfFootprint(box, point, radius);
                    }

                    p = new Vector3(pushed.X, p.y, pushed.Y);
                    moved = true;
                }

                if (!moved)
                {
                    break;
                }
            }

            return p;
        }

        // Center inside the footprint: leave through the nearest face.
        private static Vec2 PushOutOfFootprint(YawBox box, Vec2 point, float radius)
        {
            Vec3 local = box.ToLocal(new Vec3(point.X, box.Center.Y, point.Y));
            Vec3 h = box.HalfExtents;
            float dx = h.X - Mathf.Abs(local.X);
            float dz = h.Z - Mathf.Abs(local.Z);
            if (dx < dz)
            {
                local.X = Mathf.Sign(local.X == 0f ? 1f : local.X) * (h.X + radius);
            }
            else
            {
                local.Z = Mathf.Sign(local.Z == 0f ? 1f : local.Z) * (h.Z + radius);
            }

            return box.ToWorld(local).XZ();
        }
    }
}
