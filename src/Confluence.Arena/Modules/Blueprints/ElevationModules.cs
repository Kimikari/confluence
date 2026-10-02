using Confluence.Core.Arena;
using Confluence.Core.Content;

namespace Confluence.Arena.Modules.Blueprints
{
    // Elevation modules (MOD-025..MOD-036). Durability text: "Load-bearing floor, stairs, and ramps anchored.
    // Separate edge cover has 80 durability." Fairness: at least two access routes, a safe descent, 2.5 m routes.
    // Ramps stay at or below 30 degrees and stairs use the standard 0.25 m riser / 0.45 m tread (spec L312).
    // Drops of 3 m or less are safe (fall damage begins above 3 m, spec L175); 1 m ledges take the common jump (D-024).

    /// <summary>MOD-025 Short Ramp: a gentle ramp reaches a 1 m deck with two return routes (the ramp and a safe drop).</summary>
    [ContentBehavior("MOD-025")]
    public sealed class ShortRamp : ModuleBlueprint
    {
        public override string ModuleId => "MOD-025";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM;
            b.Ramp(-3f, -1.5f, 0f, 1.5f, ModuleEdge.East, 0f, top); // 1 m over 3 m: 18.4 degrees
            b.Deck(0f, -1.5f, 2.75f, 1.5f, top);
            b.Socket(ModuleEdge.West, 0f, 3f).Socket(ModuleEdge.East, 0f, 3f);
        }
    }

    /// <summary>MOD-026 Twin Stairs: two 2.5 m stairs reach a 2 m platform and an intermediate 1 m landing.</summary>
    [ContentBehavior("MOD-026")]
    public sealed class TwinStairs : ModuleBlueprint
    {
        public override string ModuleId => "MOD-026";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 2 m
            float landing = top * 0.5f;
            b.Deck(-0.5f, 0.6f, 3f, 3f, top);
            b.Stair(0.5f, -3f, 3f, 0.6f, ModuleEdge.North, 0f, top);
            b.Deck(-3f, 0.6f, -0.5f, 3f, landing);
            b.Stair(-3f, -1.2f, -0.5f, 0.6f, ModuleEdge.North, 0f, landing);
            b.Socket(ModuleEdge.South, -1.75f, 2.5f).Socket(ModuleEdge.South, 1.75f, 2.5f);
        }
    }

    /// <summary>MOD-027 Broad Slope: a 20-degree incline five meters wide, with side cover on its deck.</summary>
    [ContentBehavior("MOD-027")]
    public sealed class BroadSlope : ModuleBlueprint
    {
        public override string ModuleId => "MOD-027";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 2 m over 5.5 m: 19.98 degrees
            b.Ramp(-4f, -2.5f, 1.5f, 2.5f, ModuleEdge.East, 0f, top);
            b.Deck(1.5f, -2.5f, 4f, 2.5f, top);
            b.Block(1.5f, 2.1f, 4f, 2.5f, top, top + 1f, SolidKind.Cover, b.PrimaryMaterial, b.Durability.Get("edge cover"), "edge-cover");
            b.Socket(ModuleEdge.West, 0f, 5f);
            b.Socket(ModuleEdge.North, -1.25f, 5.5f).Socket(ModuleEdge.South, -1.25f, 5.5f);
        }
    }

    /// <summary>MOD-028 Sunken Court: a 1 m lower court with two broad access ramps.</summary>
    [ContentBehavior("MOD-028")]
    public sealed class SunkenCourt : ModuleBlueprint
    {
        public override string ModuleId => "MOD-028";

        protected internal override void Build(ModuleBuilder b)
        {
            float depth = (float)b.Def.HeightM; // -1 m
            b.SunkenFloor(-4f, -4f, 4f, 4f, depth);
            b.Ramp(-1.5f, 1.5f, 1.5f, 4f, ModuleEdge.North, depth, 0f);
            b.Ramp(-1.5f, -4f, 1.5f, -1.5f, ModuleEdge.South, depth, 0f);
            b.Socket(ModuleEdge.North, 0f, 3f).Socket(ModuleEdge.South, 0f, 3f);
        }
    }

    /// <summary>MOD-029 Raised Spine: a 1 m ridge with walkable ramps at both ends.</summary>
    [ContentBehavior("MOD-029")]
    public sealed class RaisedSpine : ModuleBlueprint
    {
        public override string ModuleId => "MOD-029";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM;
            b.Deck(-1.5f, -1.5f, 1.5f, 1.5f, top);
            b.Ramp(-4f, -1.5f, -1.5f, 1.5f, ModuleEdge.East, 0f, top);
            b.Ramp(1.5f, -1.5f, 4f, 1.5f, ModuleEdge.West, 0f, top);
            b.Socket(ModuleEdge.West, 0f, 3f).Socket(ModuleEdge.East, 0f, 3f);
        }
    }

    /// <summary>MOD-030 Terraced Steps: three 1 m terraces with alternating edge cover and safe descents.</summary>
    [ContentBehavior("MOD-030")]
    public sealed class TerracedSteps : ModuleBlueprint
    {
        public override string ModuleId => "MOD-030";

        protected internal override void Build(ModuleBuilder b)
        {
            float edge = b.Durability.Get("edge cover");
            b.Deck(-4f, -1.5f, 4f, 0f, 1f);
            b.Deck(-4f, 0f, 4f, 1.5f, 2f);
            b.Deck(-4f, 1.5f, 4f, 3f, 3f);
            b.Block(-4f, -1.5f, -1f, -1.1f, 1f, 2f, SolidKind.Cover, b.PrimaryMaterial, edge, "edge-cover");
            b.Block(1f, 0f, 4f, 0.4f, 2f, 3f, SolidKind.Cover, b.PrimaryMaterial, edge, "edge-cover");
            b.Block(-4f, 1.5f, -1f, 1.9f, 3f, 4f, SolidKind.Cover, b.PrimaryMaterial, edge, "edge-cover");
            b.Socket(ModuleEdge.South, 0f, 7.5f);
        }
    }

    /// <summary>MOD-031 Overlook Pair: two 3 m platforms connected through a ground route and safe stairs.</summary>
    [ContentBehavior("MOD-031")]
    public sealed class OverlookPair : ModuleBlueprint
    {
        public override string ModuleId => "MOD-031";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 3 m: 5.4 m of standard stair
            b.Stair(-5f, -4f, -2.5f, 1.4f, ModuleEdge.North, 0f, top);
            b.Deck(-5f, 1.4f, -2.5f, 4f, top);
            b.Stair(2.5f, -4f, 5f, 1.4f, ModuleEdge.North, 0f, top);
            b.Deck(2.5f, 1.4f, 5f, 4f, top);
            b.Socket(ModuleEdge.South, -3.75f, 2.5f).Socket(ModuleEdge.South, 0f, 5f).Socket(ModuleEdge.South, 3.75f, 2.5f)
             .Socket(ModuleEdge.North, 0f, 5f);
        }
    }

    /// <summary>MOD-032 Ramp Switchback: two broad ramps fold around a low center cover up to a 3 m deck.</summary>
    [ContentBehavior("MOD-032")]
    public sealed class RampSwitchback : ModuleBlueprint
    {
        public override string ModuleId => "MOD-032";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 3 m in two 1.5 m flights of 4 m (20.6 degrees)
            float mid = top * 0.5f;
            b.Ramp(-2f, -3f, 2f, -0.25f, ModuleEdge.West, 0f, mid);
            b.Deck(-5f, -3f, -2f, 3f, mid);
            b.Ramp(-2f, 0.25f, 2f, 3f, ModuleEdge.East, mid, top);
            b.Deck(2f, 0.25f, 5f, 3f, top);
            b.Block(-2f, -0.25f, 2f, 0.25f, 0f, 2.5f, SolidKind.Cover, b.PrimaryMaterial, b.Durability.Get("edge cover"), "center-cover");
            b.Socket(ModuleEdge.East, -1.625f, 2.75f).Socket(ModuleEdge.South, 3.5f, 3f);
        }
    }

    /// <summary>MOD-033 Hop Shortcut: a 1 m gap between two 1 m platforms is a common-jump shortcut beside a walking route.</summary>
    [ContentBehavior("MOD-033")]
    public sealed class HopShortcut : ModuleBlueprint
    {
        public override string ModuleId => "MOD-033";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM;
            b.Deck(-4f, 0f, -0.5f, 3f, top);
            b.Deck(0.5f, 0f, 4f, 3f, top);
            b.Socket(ModuleEdge.West, -1.5f, 3f).Socket(ModuleEdge.East, -1.5f, 3f);
        }
    }

    /// <summary>MOD-034 Stepped Gallery: a U-shaped 2 m balcony with stairs at both ends.</summary>
    [ContentBehavior("MOD-034")]
    public sealed class SteppedGallery : ModuleBlueprint
    {
        public override string ModuleId => "MOD-034";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 2 m
            b.Deck(-5f, 1.5f, 5f, 4f, top, ArenaMaterials.Metal);
            b.Deck(-5f, -0.4f, -2.5f, 1.5f, top, ArenaMaterials.Metal);
            b.Deck(2.5f, -0.4f, 5f, 1.5f, top, ArenaMaterials.Metal);
            b.Stair(-5f, -4f, -2.5f, -0.4f, ModuleEdge.North, 0f, top);
            b.Stair(2.5f, -4f, 5f, -0.4f, ModuleEdge.North, 0f, top);
            b.Socket(ModuleEdge.South, 0f, 5f).Socket(ModuleEdge.South, -3.75f, 2.5f).Socket(ModuleEdge.South, 3.75f, 2.5f);
        }
    }

    /// <summary>MOD-035 Landing Bowl: a 2 m bowl with three broad exit ramps.</summary>
    [ContentBehavior("MOD-035")]
    public sealed class LandingBowl : ModuleBlueprint
    {
        public override string ModuleId => "MOD-035";

        protected internal override void Build(ModuleBuilder b)
        {
            float depth = (float)b.Def.HeightM; // -2 m: 3.6 m ramps at 29.1 degrees
            b.SunkenFloor(-4f, -4f, 4f, 4f, depth);
            b.Ramp(-1.5f, 0.4f, 1.5f, 4f, ModuleEdge.North, depth, 0f);
            b.Ramp(0.4f, -4f, 4f, -1f, ModuleEdge.East, depth, 0f);
            b.Ramp(-4f, -4f, -0.4f, -1f, ModuleEdge.West, depth, 0f);
            b.Socket(ModuleEdge.North, 0f, 3f).Socket(ModuleEdge.East, -2.5f, 3f).Socket(ModuleEdge.West, -2.5f, 3f);
        }
    }

    /// <summary>MOD-036 Watch Deck: an optional 4 m vantage with two stairs and a safe 2 m lower landing.</summary>
    [ContentBehavior("MOD-036")]
    public sealed class WatchDeck : ModuleBlueprint
    {
        public override string ModuleId => "MOD-036";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM; // 4 m: 7.2 m of standard stair per access route
            b.IsVantage = true;
            b.Deck(-5f, 2.5f, 5f, 5f, top);
            b.Stair(-5f, -4.7f, -2.5f, 2.5f, ModuleEdge.North, 0f, top);
            b.Stair(2.5f, -4.7f, 5f, 2.5f, ModuleEdge.North, 0f, top);
            // Safe descent without the stairs: 4 m deck -> 2 m landing -> floor, each drop at most 3 m.
            b.Deck(-1.5f, 0.5f, 1.5f, 2.5f, top * 0.5f);
            b.Socket(ModuleEdge.South, -3.75f, 2.5f).Socket(ModuleEdge.South, 0f, 5f).Socket(ModuleEdge.South, 3.75f, 2.5f);
        }
    }
}
