using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;

namespace Confluence.Arena.Modules.Blueprints
{
    // Material modules (MOD-037..MOD-048). Durability text: "Ice 45 per tile; prism 75; grounding statue 80; metal
    // plate 90; growth 50; ordinary floor anchored." Fairness: "The material footprint has a rough dry bypass. Source
    // strength is visible and finite ... It never fills the entire final floor." Material modules replace ordinary
    // floor sections without blocking mandatory movement (spec L660), so every edge stays open and each patch leaves
    // a dry bypass of at least 2.5 m (3 m where the text says so). Natural sources (water, dust, growth beds and
    // conductor sections, spec L668) are flagged so the generator can keep at most eight at combat start.

    /// <summary>MOD-037 Water Channel: a 2 m wet strip beside a dry 3 m bypass.</summary>
    [ContentBehavior("MOD-037")]
    public sealed class WaterChannel : ModuleBlueprint
    {
        public override string ModuleId => "MOD-037";

        protected internal override void Build(ModuleBuilder b)
        {
            ModulePatch strip = b.Patch(-2.75f, -2f, -0.75f, 2f, MaterialFlags.Water, true, "MOD-037:wet-strip");
            strip.Water = 1; // a wet strip: one visible, finite water unit per cell
            b.Patch(-0.5f, -2f, 2.5f, 2f, MaterialFlags.None, false, "MOD-037:dry-bypass"); // 3 m dry bypass
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-038 Ice Crossing: a 2 m ice patch between two rough stopping zones, beside a dry bypass.</summary>
    [ContentBehavior("MOD-038")]
    public sealed class IceCrossing : ModuleBlueprint
    {
        public override string ModuleId => "MOD-038";

        protected internal override void Build(ModuleBuilder b)
        {
            ModulePatch ice = b.Patch(-2.75f, -1f, -0.75f, 1f, MaterialFlags.Ice | MaterialFlags.Brittle, false, "MOD-038:ice");
            ice.Durability = b.Durability.Get("ice"); // 45 per tile (spec L145)
            b.Patch(-2.75f, -2.75f, -0.75f, -1f, MaterialFlags.None, false, "MOD-038:rough-stop");
            b.Patch(-2.75f, 1f, -0.75f, 2.75f, MaterialFlags.None, false, "MOD-038:rough-stop");
            b.OpenAllEdges(); // dry bypass x -0.75..3 (3.75 m)
        }
    }

    /// <summary>MOD-039 Copper Contact: a 3 m conductor plate inside an insulated perimeter.</summary>
    [ContentBehavior("MOD-039")]
    public sealed class CopperContact : ModuleBlueprint
    {
        public override string ModuleId => "MOD-039";

        protected internal override void Build(ModuleBuilder b)
        {
            ModulePatch plate = b.Patch(-2.75f, -1.5f, 0.25f, 1.5f, MaterialFlags.Metal | MaterialFlags.Conductive, true, "MOD-039:conductor-plate");
            plate.Durability = b.Durability.Get("metal plate");
            // A 0.25 m insulated border isolates the plate from the floor around it (spec L682 border pattern).
            b.Patch(-3f, -1.75f, 0.5f, -1.5f, MaterialFlags.Insulating, false, "MOD-039:insulation");
            b.Patch(-3f, 1.5f, 0.5f, 1.75f, MaterialFlags.Insulating, false, "MOD-039:insulation");
            b.Patch(-3f, -1.5f, -2.75f, 1.5f, MaterialFlags.Insulating, false, "MOD-039:insulation");
            b.Patch(0.25f, -1.5f, 0.5f, 1.5f, MaterialFlags.Insulating, false, "MOD-039:insulation");
            b.OpenAllEdges(); // dry bypass x 0.5..3 (2.5 m)
        }
    }

    /// <summary>MOD-040 Fuel Bed: a 2 m finite growth bed beside an anchored low cover.</summary>
    [ContentBehavior("MOD-040")]
    public sealed class FuelBed : ModuleBlueprint
    {
        public override string ModuleId => "MOD-040";

        protected internal override void Build(ModuleBuilder b)
        {
            ModulePatch bed = b.Patch(-2.75f, -1.25f, -0.75f, 0.75f, MaterialFlags.Growth | MaterialFlags.Combustible, true, "MOD-040:growth-bed");
            bed.Growth = 1;
            bed.Durability = b.Durability.Get("growth");
            // Anchored cover is structural stone (the module's only listed material is the growth itself).
            b.Block(-2.75f, 1.0f, -0.75f, 1.5f, 0f, 1.2f, SolidKind.Cover, ArenaMaterials.Stone, 0f, "anchored-cover");
            b.OpenAllEdges(); // dry bypass x -0.5..3 (3.5 m)
        }
    }

    /// <summary>MOD-041 Dust Pocket: a 2 m dust bed beside a wide stone path.</summary>
    [ContentBehavior("MOD-041")]
    public sealed class DustPocket : ModuleBlueprint
    {
        public override string ModuleId => "MOD-041";

        protected internal override void Build(ModuleBuilder b)
        {
            ModulePatch bed = b.Patch(-2.75f, -1f, -0.75f, 1f, MaterialFlags.Sand, true, "MOD-041:dust-bed");
            bed.Dust = 1;
            b.Patch(-0.5f, -2f, 3f, 2f, MaterialFlags.None, false, "MOD-041:stone-path"); // 3.5 m wide path
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-042 Prism Corner: a marked prism in a right-angle wall corner beside a clear alternate route.</summary>
    [ContentBehavior("MOD-042")]
    public sealed class PrismCorner : ModuleBlueprint
    {
        public override string ModuleId => "MOD-042";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-3f, 2.5f, -0.5f, 3f, 2.5f, "corner-wall");
            b.Wall(-3f, 0.5f, -2.5f, 2.5f, 2.5f, "corner-wall");
            // The prism's fixed reflecting face looks out of the corner (south-east, yaw 135).
            b.RotatedBlock(-1.8f, 1.8f, 0.8f, 0.8f, 135f, 0f, 1.8f, SolidKind.Pillar, ArenaMaterials.Prism, b.Durability.Get("prism"), "prism");
            b.OpenAllEdges(); // the clear alternate route crosses the open half x 0..3 and z -3..0.5
        }
    }

    /// <summary>MOD-043 Grounding Niche: one grounding statue beside a short wet bed.</summary>
    [ContentBehavior("MOD-043")]
    public sealed class GroundingNiche : ModuleBlueprint
    {
        public override string ModuleId => "MOD-043";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Pillar(-2.3f, 1.1f, 0.8f, 2.0f, b.Durability.Get("grounding statue"), ArenaMaterials.GroundingStatue, "grounding-statue");
            ModulePatch bed = b.Patch(-2.75f, -1.5f, -0.75f, 0.5f, MaterialFlags.Water, true, "MOD-043:wet-bed");
            bed.Water = 1;
            b.OpenAllEdges(); // dry bypass x -0.5..3 (3.5 m)
        }
    }

    /// <summary>MOD-044 Acoustic Arch: a public resonant bell hangs from an arch above an ordinary walkway.</summary>
    [ContentBehavior("MOD-044")]
    public sealed class AcousticArch : ModuleBlueprint
    {
        public override string ModuleId => "MOD-044";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Wall(-3f, -0.3f, -2.5f, 0.3f, 3.2f, "pier");
            b.Wall(2.5f, -0.3f, 3f, 0.3f, 3.2f, "pier");
            b.Block(-2.5f, -0.3f, 2.5f, 0.3f, 2.8f, 3.2f, SolidKind.Wall, ArenaMaterials.Stone, 0f, "arch");
            // The bell hangs above standing and jumping head height; it is a fixed, anchored public sound source.
            b.Block(-0.35f, -0.35f, 0.35f, 0.35f, 2.2f, 2.8f, SolidKind.Wall, ArenaMaterials.Bell, 0f, "bell");
            b.OpenAllEdges(); // 5 m walkway under the arch
        }
    }

    /// <summary>MOD-045 Heat Sink Lane: two cool metal faces line a 3.4 m lane and absorb finite heat.</summary>
    [ContentBehavior("MOD-045")]
    public sealed class HeatSinkLane : ModuleBlueprint
    {
        public override string ModuleId => "MOD-045";

        protected internal override void Build(ModuleBuilder b)
        {
            float metal = b.Durability.Get("metal plate");
            b.Cover(-2.5f, -2f, 2.5f, -1.7f, 2.0f, metal, ArenaMaterials.HeatSink, "heat-sink");
            b.Cover(-2.5f, 1.7f, 2.5f, 2f, 2.0f, metal, ArenaMaterials.HeatSink, "heat-sink");
            b.OpenEdges(ModuleEdge.West, ModuleEdge.East);
        }
    }

    /// <summary>MOD-046 Porous Step: a 1 m absorbent platform with a ramp and a floor-level water-release trough.</summary>
    [ContentBehavior("MOD-046")]
    public sealed class PorousStep : ModuleBlueprint
    {
        public override string ModuleId => "MOD-046";

        protected internal override void Build(ModuleBuilder b)
        {
            float top = (float)b.Def.HeightM;
            b.Deck(-3f, -1f, 0f, 2f, top, ArenaMaterials.Porous);
            b.Ramp(0f, -1f, 3f, 2f, ModuleEdge.West, 0f, top); // 1 m over 3 m: 18.4 degrees
            b.Patch(-3f, -2f, 0f, -1f, MaterialFlags.None, false, "MOD-046:release-trough");
            b.Socket(ModuleEdge.East, 0.5f, 3f).Socket(ModuleEdge.South, 0f, 6f);
        }
    }

    /// <summary>MOD-047 Brittle Fringe: three brittle ice covers line one edge of a 3.2 m route.</summary>
    [ContentBehavior("MOD-047")]
    public sealed class BrittleFringe : ModuleBlueprint
    {
        public override string ModuleId => "MOD-047";

        protected internal override void Build(ModuleBuilder b)
        {
            float ice = b.Durability.Get("ice");
            for (int i = -1; i <= 1; i++)
            {
                float cx = i * 2f;
                b.Cover(cx - 0.6f, 1.2f, cx + 0.6f, 1.8f, 1.2f, ice, ArenaMaterials.Ice, "brittle-ice");
            }

            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-048 Damp Sand Shelf: two sand tiles each start with one water unit (damp sand, spec L154).</summary>
    [ContentBehavior("MOD-048")]
    public sealed class DampSandShelf : ModuleBlueprint
    {
        public override string ModuleId => "MOD-048";

        protected internal override void Build(ModuleBuilder b)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x0 = side < 0 ? -2.5f : 0.5f;
                ModulePatch tile = b.Patch(x0, 0.5f, x0 + 2f, 2f, MaterialFlags.Sand | MaterialFlags.Water, true, "MOD-048:damp-sand");
                tile.Sand = 1;
                tile.Water = 1;
            }

            b.OpenAllEdges(); // dry bypass z -2..0.5 (2.5 m)
        }
    }
}
