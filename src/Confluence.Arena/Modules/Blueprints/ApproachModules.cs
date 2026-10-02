using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Modules.Blueprints
{
    // Approach modules (MOD-049..MOD-060). Durability text: "Separate cover 80; central block 120; low islands 50;
    // load-bearing terrain anchored." Fairness: "Equal approach lengths from mirrored sockets. At least one crossover
    // and one alternative route; no field can seal every path. Optional firing lane respects the 28 meter limit."
    // Every module keeps a 2.5 m or wider through-route between paired sockets.

    /// <summary>MOD-049 Shielded Advance: three low covers spaced three meters apart beside a 3 m route.</summary>
    [ContentBehavior("MOD-049")]
    public sealed class ShieldedAdvance : ModuleBlueprint
    {
        public override string ModuleId => "MOD-049";

        protected internal override void Build(ModuleBuilder b)
        {
            float cover = b.Durability.Get("separate cover");
            for (int i = -1; i <= 1; i++)
            {
                float cx = i * 3f;
                b.Cover(cx - 0.8f, 0.5f, cx + 0.8f, 1.0f, 1.0f, cover, ArenaMaterials.Stone, "low-cover");
            }

            b.OpenAllEdges(); // route z -2.5..0.5 (3 m) with crouch pockets behind the covers
        }
    }

    /// <summary>MOD-050 Flank Tunnel: an opaque roofed passage that bends once and has two visible exits.</summary>
    [ContentBehavior("MOD-050")]
    public sealed class FlankTunnel : ModuleBlueprint
    {
        public override string ModuleId => "MOD-050";

        protected internal override void Build(ModuleBuilder b)
        {
            // Interior z -1.5..1.5 (3 m) from the west exit, turning north through x 1.5..4.5 (3 m).
            b.Wall(-5f, -2f, 5f, -1.5f, 3f, "tunnel-wall");
            b.Wall(4.5f, -1.5f, 5f, 2f, 3f, "tunnel-wall");
            b.Wall(-5f, 1.5f, 1.5f, 2f, 3f, "tunnel-wall");
            b.Block(-5f, -1.5f, 4.5f, 1.5f, 3f, 3.3f, SolidKind.Wall, b.PrimaryMaterial, 0f, "roof");
            b.Socket(ModuleEdge.West, 0f, 3f).Socket(ModuleEdge.North, 3f, 3f);
        }
    }

    /// <summary>MOD-051 Peek Gallery: a wall with three alternating peek windows beside a 2.6 m outer path.</summary>
    [ContentBehavior("MOD-051")]
    public sealed class PeekGallery : ModuleBlueprint
    {
        public override string ModuleId => "MOD-051";

        protected internal override void Build(ModuleBuilder b)
        {
            const float z0 = -0.3f;
            const float z1 = -0.1f;
            // Window spans x -3.4..-2.6, -0.4..0.4, 2.6..3.4; standing (1.1-1.7 m) and crouched (0.5-1.0 m) alternate.
            b.Wall(-5f, z0, -3.4f, z1, 2.4f, "gallery-wall");
            b.Wall(-2.6f, z0, -0.4f, z1, 2.4f, "gallery-wall");
            b.Wall(0.4f, z0, 2.6f, z1, 2.4f, "gallery-wall");
            b.Wall(3.4f, z0, 5f, z1, 2.4f, "gallery-wall");
            float[] centers = { -3f, 0f, 3f };
            for (int i = 0; i < centers.Length; i++)
            {
                bool standing = i % 2 == 0;
                float sill = standing ? 1.1f : 0.5f;
                float head = standing ? 1.7f : 1.0f;
                float cx = centers[i];
                b.Block(cx - 0.4f, z0, cx + 0.4f, z1, 0f, sill, SolidKind.Wall, ArenaMaterials.Metal, 0f, "window-sill");
                b.Block(cx - 0.4f, z0, cx + 0.4f, z1, head, 2.4f, SolidKind.Wall, ArenaMaterials.Metal, 0f, "window-head");
            }

            b.Socket(ModuleEdge.West, 1.2f, 2.6f).Socket(ModuleEdge.East, 1.2f, 2.6f).Socket(ModuleEdge.North, 0f, 9.5f)
             .Socket(ModuleEdge.South, 0f, 9.5f);
        }
    }

    /// <summary>MOD-052 Wide Sweep: a curved open route follows one large anchored outer cover.</summary>
    [ContentBehavior("MOD-052")]
    public sealed class WideSweep : ModuleBlueprint
    {
        public override string ModuleId => "MOD-052";

        protected internal override void Build(ModuleBuilder b)
        {
            // Three segments approximate an arc bowing toward the outer (north) edge.
            b.RotatedBlock(-4.1f, 1.9f, 3.8f, 0.6f, 20f, 0f, 2.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "outer-cover");
            b.Block(-2.3f, 2.2f, 2.3f, 2.8f, 0f, 2.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "outer-cover");
            b.RotatedBlock(4.1f, 1.9f, 3.8f, 0.6f, -20f, 0f, 2.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "outer-cover");
            b.Socket(ModuleEdge.West, -1f, 4f).Socket(ModuleEdge.East, -1f, 4f).Socket(ModuleEdge.South, 0f, 11f);
        }
    }

    /// <summary>MOD-053 Pressure Funnel: a 6 m route narrows to 3 m, then reopens with a side exit.</summary>
    [ContentBehavior("MOD-053")]
    public sealed class PressureFunnel : ModuleBlueprint
    {
        public override string ModuleId => "MOD-053";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-1f, 1.5f, 1f, 3f, 2.5f, "funnel-wall");
            b.Wall(-1f, -3f, 4f, -1.5f, 2.5f, "funnel-wall");
            b.Socket(ModuleEdge.West, 0f, 6f).Socket(ModuleEdge.East, 0.75f, 4.5f)
             .Socket(ModuleEdge.North, 2.5f, 3f).Socket(ModuleEdge.North, -2.5f, 3f);
        }
    }

    /// <summary>MOD-054 Crossover Ramp: a 2 m bridge route crosses over a separate 3 m ground lane.</summary>
    [ContentBehavior("MOD-054")]
    public sealed class CrossoverRamp : ModuleBlueprint
    {
        public override string ModuleId => "MOD-054";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 2 m over 3.5 m ramps: 29.7 degrees
            b.Ramp(-5f, -1.5f, -1.5f, 1.5f, ModuleEdge.East, 0f, top);
            b.Ramp(1.5f, -1.5f, 5f, 1.5f, ModuleEdge.West, 0f, top);
            // A thin anchored deck keeps standing headroom (1.8 m) for the ground lane below.
            b.Bridge(-1.5f, -1.5f, 1.5f, 1.5f, top, 0.15f);
            b.Socket(ModuleEdge.West, 0f, 3f).Socket(ModuleEdge.East, 0f, 3f)
             .Socket(ModuleEdge.North, 0f, 3f).Socket(ModuleEdge.South, 0f, 3f);
        }
    }

    /// <summary>MOD-055 Blind Pivot: one large central block with three open pivot exits and an anchored back wall.</summary>
    [ContentBehavior("MOD-055")]
    public sealed class BlindPivot : ModuleBlueprint
    {
        public override string ModuleId => "MOD-055";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Block(-1.5f, -0.25f, 1.5f, 0.5f, 0f, 2.5f, SolidKind.Cover, b.PrimaryMaterial, b.Durability.Get("central block"), "central-block");
            b.Wall(-4f, -3f, 4f, -2.75f, 2.5f, "back-wall");
            b.Socket(ModuleEdge.West, -0.25f, 5.5f).Socket(ModuleEdge.East, -0.25f, 5.5f).Socket(ModuleEdge.North, 0f, 8f);
        }
    }

    /// <summary>MOD-056 Window Bypass: a wall with one firing window and a full-width covered bypass route.</summary>
    [ContentBehavior("MOD-056")]
    public sealed class WindowBypass : ModuleBlueprint
    {
        public override string ModuleId => "MOD-056";

        protected internal override void Build(ModuleBuilder b)
        {
            float cover = b.Durability.Get("separate cover");
            b.Cover(-5f, -0.15f, -0.4f, 0.15f, 2.0f, cover, ArenaMaterials.Wood, "window-wall");
            b.Cover(0.4f, -0.15f, 5f, 0.15f, 2.0f, cover, ArenaMaterials.Wood, "window-wall");
            b.Block(-0.4f, -0.15f, 0.4f, 0.15f, 0f, 1.1f, SolidKind.Cover, ArenaMaterials.Wood, cover, "window-sill");
            b.Block(-0.4f, -0.15f, 0.4f, 0.15f, 1.7f, 2.0f, SolidKind.Cover, ArenaMaterials.Wood, cover, "window-head");
            // Firing side z 0.15..3 and covered bypass z -3..-0.15 (2.85 m each).
            b.Socket(ModuleEdge.West, 1.575f, 2.85f).Socket(ModuleEdge.West, -1.575f, 2.85f)
             .Socket(ModuleEdge.East, 1.575f, 2.85f).Socket(ModuleEdge.East, -1.575f, 2.85f)
             .Socket(ModuleEdge.North, 0f, 9.5f).Socket(ModuleEdge.South, 0f, 9.5f);
        }
    }

    /// <summary>MOD-057 Prop Escort: two heavy covers slide half a meter along visible tracks beside a 3 m route.</summary>
    [ContentBehavior("MOD-057")]
    public sealed class PropEscort : ModuleBlueprint
    {
        public override string ModuleId => "MOD-057";

        protected internal override void Build(ModuleBuilder b)
        {
            float cover = b.Durability.Get("separate cover");
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = side * 2.5f;
                ModulePiece heavy = b.Cover(cx - 0.8f, 0.75f, cx + 0.8f, 1.25f, 1.4f, cover, ArenaMaterials.EscortCover, "escort-cover");
                heavy.IsMovable = true;
                heavy.TrackFrom = new Vec2(cx, 1.0f);
                heavy.TrackTo = new Vec2(cx, 1.5f); // half a meter toward the outer edge
                b.Patch(cx - 0.8f, 0.75f, cx + 0.8f, 1.75f, MaterialFlags.None, false, "MOD-057:track");
            }

            b.OpenAllEdges(); // route z -2.5..0.75 (3.25 m)
        }
    }

    /// <summary>MOD-058 Stepping Cover: four low cover islands alternate along a 3 m zigzag exposed walkway.</summary>
    [ContentBehavior("MOD-058")]
    public sealed class SteppingCover : ModuleBlueprint
    {
        public override string ModuleId => "MOD-058";

        protected internal override void Build(ModuleBuilder b)
        {
            float island = b.Durability.Get("low islands");
            float[] xs = { -3.75f, -1.25f, 1.25f, 3.75f };
            for (int i = 0; i < xs.Length; i++)
            {
                float cz = i % 2 == 0 ? 2.1f : -2.1f;
                b.Cover(xs[i] - 0.75f, cz - 0.6f, xs[i] + 0.75f, cz + 0.6f, 1.0f, island, ArenaMaterials.Stone, "low-island");
            }

            b.OpenAllEdges(); // walkway z -1.5..1.5 between the alternating islands
        }
    }

    /// <summary>MOD-059 Open Challenge: one 12 m firing lane between two staggered blocks that bend the flank routes.</summary>
    [ContentBehavior("MOD-059")]
    public sealed class OpenChallenge : ModuleBlueprint
    {
        public override string ModuleId => "MOD-059";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-5f, 1f, -2f, 3f, 2.5f, "stagger-block");
            b.Wall(2f, -3f, 5f, -1f, 2.5f, "stagger-block");
            // The lane z -1..1 is open end to end (12 m, below the 28 m limit); both flanks bend around a block.
            b.Socket(ModuleEdge.West, -1f, 4f).Socket(ModuleEdge.East, 1f, 4f)
             .Socket(ModuleEdge.North, 1f, 8f).Socket(ModuleEdge.South, -1f, 8f);
        }
    }

    /// <summary>MOD-060 Retreat Loop: a 3 m loop around two anchored walls with a 2.5 m cutoff through the middle and audible gravel.</summary>
    [ContentBehavior("MOD-060")]
    public sealed class RetreatLoop : ModuleBlueprint
    {
        public override string ModuleId => "MOD-060";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-1.75f, 1.25f, 1.75f, 2f, 2.5f, "loop-wall");
            b.Wall(-1.75f, -2f, 1.75f, -1.25f, 2.5f, "loop-wall");
            // Audible ground materials at the two chase cutoffs (both ends of the middle passage).
            b.Patch(-3.25f, -1.25f, -1.75f, 1.25f, MaterialFlags.None, false, "MOD-060:audible-gravel");
            b.Patch(1.75f, -1.25f, 3.25f, 1.25f, MaterialFlags.None, false, "MOD-060:audible-gravel");
            b.OpenAllEdges();
        }
    }
}
