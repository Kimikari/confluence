using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Reactions
{
    /// <summary>
    /// A body Sim passes to <see cref="ReactionWorld.Step"/> so neutral reactions can request contact damage. Both
    /// fighters, summons, and decoys are bodies; ownership grants no exemption (spec L128, L670).
    /// </summary>
    public readonly struct ReactionBody
    {
        public readonly int Id;

        /// <summary>Feet position.</summary>
        public readonly Vec3 Position;

        public readonly float Radius;
        public readonly float Height;

        /// <summary>Grounded bodies touch ground hazards; an airborne body clears them (L148 "jump the short pulse").</summary>
        public readonly bool IsGrounded;

        public ReactionBody(int id, Vec3 position, float radius, float height, bool isGrounded)
        {
            Id = id;
            Position = position;
            Radius = radius;
            Height = height;
            IsGrounded = isGrounded;
        }

        /// <summary>A standing player capsule (D-024 radius and height).</summary>
        public static ReactionBody Player(int id, Vec3 feet, bool isGrounded = true) =>
            new ReactionBody(id, feet, GlobalRules.Movement.CapsuleRadius, GlobalRules.Movement.StandingHeight, isGrounded);

        public float Top => Position.Y + Height;
    }
}
