using Confluence.Core.Arena;
using Confluence.Core.Content;

namespace Confluence.Arena.Modules.Blueprints
{
    // Topology modules (spec catalogue MOD-001..MOD-012). Durability text: "Load-bearing terrain anchored; attached
    // low cover 80 and center cover 120." Walls and pillars are anchored; attached cover reads its value from the text.
    // Fairness: two or more attached exits and 2.5 m of route width at every turn.

    /// <summary>MOD-001 Split Gate: two 2.8 m doors divided by an anchored center pillar under a gate lintel.</summary>
    [ContentBehavior("MOD-001")]
    public sealed class SplitGate : ModuleBlueprint
    {
        public override string ModuleId => "MOD-001";

        protected internal override void Build(ModuleBuilder b)
        {
            // 6 m gate line = 2.8 m door + 0.4 m pillar + 2.8 m door.
            b.Block(-0.2f, -0.5f, 0.2f, 0.5f, 0f, 3.2f, SolidKind.Pillar, b.PrimaryMaterial, 0f, "center-pillar");
            b.Block(-b.HalfW, -0.25f, b.HalfW, 0.25f, 2.7f, 3.2f, SolidKind.Wall, b.PrimaryMaterial, 0f, "lintel");
            b.Socket(ModuleEdge.North, -1.6f, 2.8f).Socket(ModuleEdge.North, 1.6f, 2.8f)
             .Socket(ModuleEdge.South, -1.6f, 2.8f).Socket(ModuleEdge.South, 1.6f, 2.8f);
        }
    }

    /// <summary>MOD-002 Dogleg Lane: one 90-degree corner with 2.8 m of clear route width.</summary>
    [ContentBehavior("MOD-002")]
    public sealed class DoglegLane : ModuleBlueprint
    {
        public override string ModuleId => "MOD-002";

        protected internal override void Build(ModuleBuilder b)
        {
            // Lane enters from the west across z -1.4..1.4 and turns north through x 0.6..3.4.
            b.Wall(-4f, -2f, 4f, -1.4f, 3f, "outer-wall");
            b.Wall(3.4f, -1.4f, 4f, 2f, 3f, "outer-wall");
            b.Wall(-4f, 1.4f, 0.6f, 2f, 3f, "inner-corner");
            b.Socket(ModuleEdge.West, 0f, 2.8f).Socket(ModuleEdge.North, 2.0f, 2.8f);
        }
    }

    /// <summary>MOD-003 Forked Approach: one 3 m route forks around a 4 m central cover.</summary>
    [ContentBehavior("MOD-003")]
    public sealed class ForkedApproach : ModuleBlueprint
    {
        public override string ModuleId => "MOD-003";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-4f, -4f, -3.6f, 4f, 2.5f, "side-wall");
            b.Wall(3.6f, -4f, 4f, 4f, 2.5f, "side-wall");
            // The 4 m cover runs along the route, leaving two 2.9 m branches.
            b.Block(-0.7f, -2f, 0.7f, 2f, 0f, 1.8f, SolidKind.Cover, b.PrimaryMaterial, b.Durability.Get("center cover"), "center-cover");
            b.Socket(ModuleEdge.South, 0f, 3f).Socket(ModuleEdge.North, 0f, 3f);
        }
    }

    /// <summary>MOD-004 Crossing Court: a four-way crossing with one offset waist-high cover.</summary>
    [ContentBehavior("MOD-004")]
    public sealed class CrossingCourt : ModuleBlueprint
    {
        public override string ModuleId => "MOD-004";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-4f, -4f, -2f, -2f, 3f, "corner-block");
            b.Wall(2f, -4f, 4f, -2f, 3f, "corner-block");
            b.Wall(-4f, 2f, -2f, 4f, 3f, "corner-block");
            b.Wall(2f, 2f, 4f, 4f, 3f, "corner-block");
            // Offset against one corner so both 4 m crossing directions keep 3 m or more.
            b.Cover(1.0f, -2.0f, 2.0f, -1.6f, 1.0f, b.Durability.Get("low cover"), null, "low-cover");
            b.OpenEdges(ModuleEdge.North, ModuleEdge.East, ModuleEdge.South, ModuleEdge.West);
        }
    }

    /// <summary>MOD-005 Offset Alley: three staggered screens make two peek pockets beside a continuous side route.</summary>
    [ContentBehavior("MOD-005")]
    public sealed class OffsetAlley : ModuleBlueprint
    {
        public override string ModuleId => "MOD-005";

        protected internal override void Build(ModuleBuilder b)
        {
            float screen = b.Durability.Get("low cover");
            b.Cover(-3.65f, 0.6f, -3.35f, 2.0f, 2.0f, screen, ArenaMaterials.Wood, "screen");
            b.Cover(-0.15f, 0.9f, 0.15f, 2.0f, 2.0f, screen, ArenaMaterials.Wood, "screen");
            b.Cover(3.35f, 0.6f, 3.65f, 2.0f, 2.0f, screen, ArenaMaterials.Wood, "screen");
            // Side route along z -2..0.6.
            b.Socket(ModuleEdge.West, -0.7f, 2.6f).Socket(ModuleEdge.East, -0.7f, 2.6f);
        }
    }

    /// <summary>MOD-006 Ring Walk: a 3 m ring walkway surrounds a 4 m anchored planter.</summary>
    [ContentBehavior("MOD-006")]
    public sealed class RingWalk : ModuleBlueprint
    {
        public override string ModuleId => "MOD-006";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Block(-2f, -2f, 2f, 2f, 0f, 1.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "planter");
            // Corner walls bound the ring (2.7 m between planter and walls) and leave a 6 m opening on every side.
            float t = 0.3f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float xOuter = sx * 5f;
                    float zOuter = sz * 5f;
                    b.Wall(sx * 3f, zOuter - sz * t, xOuter, zOuter, 2.5f, "ring-corner");
                    b.Wall(xOuter - sx * t, sz * 3f, xOuter, zOuter - sz * t, 2.5f, "ring-corner");
                }
            }

            b.Socket(ModuleEdge.North, 0f, 6f).Socket(ModuleEdge.East, 0f, 6f).Socket(ModuleEdge.South, 0f, 6f).Socket(ModuleEdge.West, 0f, 6f);
        }
    }

    /// <summary>MOD-007 T Junction: three connected lanes share an outer peek angle.</summary>
    [ContentBehavior("MOD-007")]
    public sealed class TJunction : ModuleBlueprint
    {
        public override string ModuleId => "MOD-007";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-4f, 2.5f, 4f, 3f, 3f, "outer-wall");
            b.Wall(-4f, -3f, -1.5f, -0.5f, 3f, "peek-block");
            b.Wall(1.5f, -3f, 4f, -0.5f, 3f, "peek-block");
            b.Socket(ModuleEdge.West, 1.0f, 3f).Socket(ModuleEdge.East, 1.0f, 3f).Socket(ModuleEdge.South, 0f, 3f);
        }
    }

    /// <summary>MOD-008 Double Bend: two opposing doglegs separated by a 3 m open pocket.</summary>
    [ContentBehavior("MOD-008")]
    public sealed class DoubleBend : ModuleBlueprint
    {
        public override string ModuleId => "MOD-008";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-5f, 2.6f, 5f, 3f, 3f, "outer-wall");
            b.Wall(-5f, -3f, 5f, -2.6f, 3f, "outer-wall");
            b.Wall(-2.0f, -2.6f, -1.5f, 0f, 3f, "baffle");
            b.Wall(1.5f, 0f, 2.0f, 2.6f, 3f, "baffle");
            b.Socket(ModuleEdge.West, 0f, 5.2f).Socket(ModuleEdge.East, 0f, 5.2f);
        }
    }

    /// <summary>MOD-009 Diagonal Cut: one open diagonal lane connects two covered perimeter routes.</summary>
    [ContentBehavior("MOD-009")]
    public sealed class DiagonalCut : ModuleBlueprint
    {
        public override string ModuleId => "MOD-009";

        protected internal override void Build(ModuleBuilder b)
        {
            // Two 5 m walls parallel to the SW-NE diagonal; the lane between them is about 4.1 m wide and each wall
            // screens one perimeter route around its corner.
            b.RotatedBlock(-1.75f, 1.75f, 5f, 0.8f, -45f, 0f, 2.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "perimeter-wall");
            b.RotatedBlock(1.75f, -1.75f, 5f, 0.8f, -45f, 0f, 2.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "perimeter-wall");
            b.Socket(ModuleEdge.South, -2f, 3.5f).Socket(ModuleEdge.South, 2f, 3.5f)
             .Socket(ModuleEdge.North, -2f, 3.5f).Socket(ModuleEdge.North, 2f, 3.5f)
             .Socket(ModuleEdge.West, -2f, 3.5f).Socket(ModuleEdge.West, 2f, 3.5f)
             .Socket(ModuleEdge.East, -2f, 3.5f).Socket(ModuleEdge.East, 2f, 3.5f);
        }
    }

    /// <summary>MOD-010 Bridge Pair: two 3 m anchored bridges with a ground crossover and safe descent.</summary>
    [ContentBehavior("MOD-010")]
    public sealed class BridgePair : ModuleBlueprint
    {
        public override string ModuleId => "MOD-010";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 1 m
            for (int side = -1; side <= 1; side += 2)
            {
                float x0 = side < 0 ? -4.5f : 1.5f;
                float x1 = x0 + 3f;
                b.Deck(x0, -1.5f, x1, 1.5f, top, ArenaMaterials.Metal);
                b.Ramp(x0, 1.5f, x1, 4f, ModuleEdge.South, 0f, top);
                b.Ramp(x0, -4f, x1, -1.5f, ModuleEdge.North, 0f, top);
            }

            // Ground crossover between the bridges (x -1.5..1.5); 1 m deck sides are safe drops.
            b.Socket(ModuleEdge.North, -3f, 3f).Socket(ModuleEdge.North, 0f, 3f).Socket(ModuleEdge.North, 3f, 3f)
             .Socket(ModuleEdge.South, -3f, 3f).Socket(ModuleEdge.South, 0f, 3f).Socket(ModuleEdge.South, 3f, 3f);
        }
    }

    /// <summary>MOD-011 Braided Paths: three weaving routes reconnect at two separate crossover points.</summary>
    [ContentBehavior("MOD-011")]
    public sealed class BraidedPaths : ModuleBlueprint
    {
        public override string ModuleId => "MOD-011";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-1.9f, -5f, -1.4f, -3.5f, 2.5f, "divider");
            b.Wall(-1.9f, -1.0f, -1.4f, 5f, 2.5f, "divider");
            b.Wall(1.4f, -5f, 1.9f, 1.0f, 2.5f, "divider");
            b.Wall(1.4f, 3.5f, 1.9f, 5f, 2.5f, "divider");
            b.Socket(ModuleEdge.North, -3.45f, 3.1f).Socket(ModuleEdge.North, 0f, 2.8f).Socket(ModuleEdge.North, 3.45f, 3.1f)
             .Socket(ModuleEdge.South, -3.45f, 3.1f).Socket(ModuleEdge.South, 0f, 2.8f).Socket(ModuleEdge.South, 3.45f, 3.1f);
        }
    }

    /// <summary>MOD-012 Outer Arcade: three wide arch openings connect to a continuous outer walkway.</summary>
    [ContentBehavior("MOD-012")]
    public sealed class OuterArcade : ModuleBlueprint
    {
        public override string ModuleId => "MOD-012";

        protected internal override void Build(ModuleBuilder b)
        {
            const float z0 = -0.75f;
            const float z1 = -0.25f;
            b.Wall(-6f, z0, -5.3f, z1, 3.5f, "pier");
            b.Wall(-2.7f, z0, -1.3f, z1, 3.5f, "pier");
            b.Wall(1.3f, z0, 2.7f, z1, 3.5f, "pier");
            b.Wall(5.3f, z0, 6f, z1, 3.5f, "pier");
            b.Block(-5.3f, z0, -2.7f, z1, 2.6f, 3.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "arch");
            b.Block(-1.3f, z0, 1.3f, z1, 2.6f, 3.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "arch");
            b.Block(2.7f, z0, 5.3f, z1, 2.6f, 3.5f, SolidKind.Wall, b.PrimaryMaterial, 0f, "arch");
            // Outer walkway z -0.25..2.5 (2.75 m); three 2.6 m arches open to the inner side.
            b.Socket(ModuleEdge.West, 1.125f, 2.75f).Socket(ModuleEdge.East, 1.125f, 2.75f)
             .Socket(ModuleEdge.South, -4f, 2.6f).Socket(ModuleEdge.South, 0f, 2.6f).Socket(ModuleEdge.South, 4f, 2.6f);
        }
    }
}
