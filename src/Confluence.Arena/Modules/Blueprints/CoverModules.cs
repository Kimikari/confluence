using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Modules.Blueprints
{
    // Cover modules (MOD-013..MOD-024). Durability text: "Low wood 60; thin panels 45; stone slab 120; prism 75;
    // metal or column 90; porous wall 100; hedge 60; parapet segment 40." Fairness: two exposed flank routes remain;
    // destruction changes peek geometry with collision. Cover pieces sit in open floor, so every long edge is open.

    /// <summary>MOD-013 Low Barricade: 1 m crouch cover with two exposed flanking ends.</summary>
    [ContentBehavior("MOD-013")]
    public sealed class LowBarricade : ModuleBlueprint
    {
        public override string ModuleId => "MOD-013";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Cover(-1.5f, -0.25f, 1.5f, 0.25f, 1.0f, b.Durability.Get("low wood"), ArenaMaterials.Wood, "low-barricade");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-014 Tall Slab: a 2.5 m opaque slab between two open side passages.</summary>
    [ContentBehavior("MOD-014")]
    public sealed class TallSlab : ModuleBlueprint
    {
        public override string ModuleId => "MOD-014";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Cover(-1.0f, -0.3f, 1.0f, 0.3f, 2.5f, b.Durability.Get("stone slab"), ArenaMaterials.Stone, "tall-slab");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-015 Window Screen: a 2 m screen with one fixed 0.7 m firing window.</summary>
    [ContentBehavior("MOD-015")]
    public sealed class WindowScreen : ModuleBlueprint
    {
        public override string ModuleId => "MOD-015";

        protected internal override void Build(ModuleBuilder b)
        {
            float metal = b.Durability.Get("metal");
            const float t = 0.15f;
            b.Cover(-1.75f, -t, 0.3f, t, 2.0f, metal, ArenaMaterials.Metal, "screen");
            b.Cover(1.0f, -t, 1.75f, t, 2.0f, metal, ArenaMaterials.Metal, "screen");
            // Window x 0.3..1.0 (0.7 m) at standing chest-to-eye height 1.1..1.7 m.
            b.Block(0.3f, -t, 1.0f, t, 0f, 1.1f, SolidKind.Cover, ArenaMaterials.Metal, metal, "screen-sill");
            b.Block(0.3f, -t, 1.0f, t, 1.7f, 2.0f, SolidKind.Cover, ArenaMaterials.Metal, metal, "screen-head");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-016 L Corner Cover: two connected walls create an open-sided sheltered corner.</summary>
    [ContentBehavior("MOD-016")]
    public sealed class LCornerCover : ModuleBlueprint
    {
        public override string ModuleId => "MOD-016";

        protected internal override void Build(ModuleBuilder b)
        {
            float slab = b.Durability.Get("stone slab");
            b.Cover(-1.75f, 1.25f, 1.75f, 1.75f, 2.0f, slab, ArenaMaterials.Stone, "corner-wall");
            b.Cover(1.25f, -1.75f, 1.75f, 1.25f, 2.0f, slab, ArenaMaterials.Stone, "corner-wall");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-017 Broken Column: a 1.4 m broken column with a clearly visible top lip.</summary>
    [ContentBehavior("MOD-017")]
    public sealed class BrokenColumn : ModuleBlueprint
    {
        public override string ModuleId => "MOD-017";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Pillar(0f, 0f, 1.2f, 1.4f, b.Durability.Get("column"), ArenaMaterials.Stone, "broken-column");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-018 Twin Screens: two thin offset panels with one meter of visible separation.</summary>
    [ContentBehavior("MOD-018")]
    public sealed class TwinScreens : ModuleBlueprint
    {
        public override string ModuleId => "MOD-018";

        protected internal override void Build(ModuleBuilder b)
        {
            float thin = b.Durability.Get("thin panels");
            b.Cover(-2.0f, -0.65f, 0.2f, -0.5f, 2.0f, thin, ArenaMaterials.Wood, "thin-panel");
            b.Cover(-0.2f, 0.5f, 2.0f, 0.65f, 2.0f, thin, ArenaMaterials.Wood, "thin-panel");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-019 Prism Shield: an opaque 2 m shield beside a marked ray prism (prism faces local +Z).</summary>
    [ContentBehavior("MOD-019")]
    public sealed class PrismShield : ModuleBlueprint
    {
        public override string ModuleId => "MOD-019";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Cover(-1.75f, -0.25f, 0.5f, 0.25f, 2.0f, b.Durability.Get("stone slab"), ArenaMaterials.Stone, "shield");
            b.Block(0.9f, -0.4f, 1.7f, 0.4f, 0f, 1.8f, SolidKind.Pillar, ArenaMaterials.Prism, b.Durability.Get("prism"), "prism");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-020 Conductive Grille: a solid lower plate supports an open upper grille on insulated feet.</summary>
    [ContentBehavior("MOD-020")]
    public sealed class ConductiveGrille : ModuleBlueprint
    {
        public override string ModuleId => "MOD-020";

        protected internal override void Build(ModuleBuilder b)
        {
            float metal = b.Durability.Get("metal");
            b.Cover(-1.75f, -0.15f, 1.75f, 0.15f, 1.0f, metal, ArenaMaterials.Metal, "lower-plate");
            ModulePiece grille = b.Block(-1.75f, -0.1f, 1.75f, 0.1f, 1.0f, 2.2f, SolidKind.Cover, ArenaMaterials.Grille, metal, "upper-grille");
            grille.BlocksSight = false; // open grille: blocks movement and bodies, not sight
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-021 Porous Wall: an absorbent stone face with capacity for one visible water unit.</summary>
    [ContentBehavior("MOD-021")]
    public sealed class PorousWall : ModuleBlueprint
    {
        public override string ModuleId => "MOD-021";

        protected internal override void Build(ModuleBuilder b)
        {
            b.Cover(-1.75f, -0.3f, 1.75f, 0.3f, 2.0f, b.Durability.Get("porous wall"), ArenaMaterials.Porous, "porous-wall");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-022 Hedge Bend: an L-shaped finite hedge providing combustible sight cover.</summary>
    [ContentBehavior("MOD-022")]
    public sealed class HedgeBend : ModuleBlueprint
    {
        public override string ModuleId => "MOD-022";

        protected internal override void Build(ModuleBuilder b)
        {
            float hedge = b.Durability.Get("hedge");
            b.Cover(-1.75f, 1.2f, 1.75f, 1.75f, 1.8f, hedge, ArenaMaterials.Hedge, "hedge");
            b.Cover(1.2f, -1.75f, 1.75f, 1.2f, 1.8f, hedge, ArenaMaterials.Hedge, "hedge");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-023 Sliding Shield: a medium-mass 1.6 m cover moving along a marked 4 m straight track.</summary>
    [ContentBehavior("MOD-023")]
    public sealed class SlidingShield : ModuleBlueprint
    {
        public override string ModuleId => "MOD-023";

        protected internal override void Build(ModuleBuilder b)
        {
            // A 1.5 m shield whose center travels x -1.25..1.25 sweeps exactly the 4 m track x -2..2.
            ModulePiece shield = b.Cover(-0.75f, -0.15f, 0.75f, 0.15f, 1.6f, b.Durability.Get("metal"), ArenaMaterials.SlidingShield, "sliding-shield");
            shield.IsMovable = true;
            shield.TrackFrom = new Vec2(-1.25f, 0f);
            shield.TrackTo = new Vec2(1.25f, 0f);
            b.Patch(-2f, -0.25f, 2f, 0.25f, MaterialFlags.None, false, "MOD-023:track");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-024 Segmented Parapet: three cover segments that break separately into open peek spaces.</summary>
    [ContentBehavior("MOD-024")]
    public sealed class SegmentedParapet : ModuleBlueprint
    {
        public override string ModuleId => "MOD-024";

        protected internal override void Build(ModuleBuilder b)
        {
            float segment = b.Durability.Get("parapet segment");
            b.Cover(-2.25f, -0.2f, -0.75f, 0.2f, 1.0f, segment, ArenaMaterials.Stone, "parapet-segment");
            b.Cover(-0.75f, -0.2f, 0.75f, 0.2f, 1.0f, segment, ArenaMaterials.Stone, "parapet-segment");
            b.Cover(0.75f, -0.2f, 2.25f, 0.2f, 1.0f, segment, ArenaMaterials.Stone, "parapet-segment");
            b.OpenAllEdges();
        }
    }
}
