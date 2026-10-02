using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Core.Rules;

namespace Confluence.Arena.Modules.Blueprints
{
    // Center modules (MOD-061..MOD-072). Durability text: "Edge cover 60; prisms 75; pillars 80; central floor anchored.
    // Use stricter prop-specific values where applicable." Fairness: "Clear anchored eight-meter final floor. Soft cover
    // and elevated camping pockets retire at 85 seconds with a two-second public warning. Both players have equal access."
    // Every center module is point-symmetric under a 180-degree turn (the spawns are mirrored through the center) and
    // lays an anchored 8 m final floor (radius 4 m, DECISIONS D-027). Soft cover standing on that floor is flagged
    // RetiresAtFinal (spec L678); everything else stands outside the 4 m radius.

    internal static class CenterKit
    {
        public const float FloorRadius = GlobalRules.Boundary.FinalFloorRadius;

        /// <summary>The anchored final duel floor, flush with the main floor (spec L171 "cannot remove the central final duel floor").</summary>
        public static void FinalFloor(ModuleBuilder b)
        {
            ModulePiece floor = b.Block(-FloorRadius, -FloorRadius, FloorRadius, FloorRadius, -0.2f, 0f, SolidKind.Floor, ArenaMaterials.Stone, 0f, "final-floor");
            floor.Walkable = true;
            b.IsPointSymmetric = true;
        }

        /// <summary>Map-owned soft cover on the final floor: destructible and retired at 85 s.</summary>
        public static ModulePiece SoftCover(ModuleBuilder b, float x0, float z0, float x1, float z1, float height, float durability, string material, string role)
        {
            ModulePiece piece = b.Cover(x0, z0, x1, z1, height, durability, material, role);
            piece.RetiresAtFinal = true;
            return piece;
        }
    }

    /// <summary>MOD-061 Final Circle: an anchored 8 m final floor with two opposed low covers.</summary>
    [ContentBehavior("MOD-061")]
    public sealed class FinalCircle : ModuleBlueprint
    {
        public override string ModuleId => "MOD-061";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            float edge = b.Durability.Get("edge cover");
            CenterKit.SoftCover(b, -1f, 2.75f, 1f, 3.25f, 1.0f, edge, ArenaMaterials.Stone, "opposed-low-cover");
            CenterKit.SoftCover(b, -1f, -3.25f, 1f, -2.75f, 1.0f, edge, ArenaMaterials.Stone, "opposed-low-cover");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-062 Four Pillar Center: four thin anchored-height pillars border a clear 8 m final floor.</summary>
    [ContentBehavior("MOD-062")]
    public sealed class FourPillarCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-062";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            float pillar = b.Durability.Get("pillars");
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    b.Pillar(sx * 3.6f, sz * 3.6f, 0.6f, 3.0f, pillar, ArenaMaterials.Stone, "thin-pillar"); // 5.1 m out
                }
            }

            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-063 Cross Well Center: four routes surround flat sealed-well art (no pit, no collision).</summary>
    [ContentBehavior("MOD-063")]
    public sealed class CrossWellCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-063";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float x0 = sx * 3.6f;
                    float z0 = sz * 3.6f;
                    b.Wall(x0, z0, sx * 5f, sz * 5f, 2.5f, "corner-block"); // inner corner 5.1 m out
                }
            }

            ModulePatch well = b.Patch(-1.5f, -1.5f, 1.5f, 1.5f, MaterialFlags.None, false, "MOD-063:sealed-well-art");
            well.Radius = 1.5f;
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-064 Prism Center: two edge prisms with fixed faces border a clear 8 m center.</summary>
    [ContentBehavior("MOD-064")]
    public sealed class PrismCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-064";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            float prism = b.Durability.Get("prisms");
            b.RotatedBlock(0f, 4.65f, 0.8f, 0.8f, 225f, 0f, 1.8f, SolidKind.Pillar, ArenaMaterials.Prism, prism, "edge-prism");
            b.RotatedBlock(0f, -4.65f, 0.8f, 0.8f, 45f, 0f, 1.8f, SolidKind.Pillar, ArenaMaterials.Prism, prism, "edge-prism");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-065 Dry Island Center: a dry rough final floor with two wet beds outside its edges.</summary>
    [ContentBehavior("MOD-065")]
    public sealed class DryIslandCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-065";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            ModulePatch rough = b.Patch(-4f, -4f, 4f, 4f, MaterialFlags.None, false, "MOD-065:dry-rough-floor");
            rough.Radius = CenterKit.FloorRadius;
            for (int side = -1; side <= 1; side += 2)
            {
                float z0 = side * 4.1f;
                ModulePatch bed = b.Patch(-2f, z0, 2f, side * 5f, MaterialFlags.Water, true, "MOD-065:wet-bed");
                bed.Water = 1;
            }

            b.OpenAllEdges();
        }
    }

    /// <summary>
    /// MOD-066 Split Height Center: two 1 m ledges (common-jump height) sit outside a level 8 m final floor. Each ledge
    /// is 5 m long, leaving 2.5 m walkable corners around its ends.
    /// </summary>
    [ContentBehavior("MOD-066")]
    public sealed class SplitHeightCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-066";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            float top = (float)b.Def.HeightM;
            b.Deck(-2.5f, 4f, 2.5f, 5f, top);
            b.Deck(-2.5f, -5f, 2.5f, -4f, top);
            b.OpenAllEdges(); // around the ledge ends, or over them by common jump
        }
    }

    /// <summary>MOD-067 Acoustic Center: two anchored acoustic plates stand outside the 8 m final floor.</summary>
    [ContentBehavior("MOD-067")]
    public sealed class AcousticCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-067";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            b.Block(-1f, 4.4f, 1f, 4.7f, 0f, 2.0f, SolidKind.Wall, ArenaMaterials.AcousticPlate, 0f, "acoustic-plate");
            b.Block(-1f, -4.7f, 1f, -4.4f, 0f, 2.0f, SolidKind.Wall, ArenaMaterials.AcousticPlate, 0f, "acoustic-plate");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-068 Fracture Center: four breakable low panels border the anchored final floor and retire at 85 s.</summary>
    [ContentBehavior("MOD-068")]
    public sealed class FractureCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-068";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            float edge = b.Durability.Get("edge cover");
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    float yaw = sx * sz > 0 ? -45f : 45f; // each panel faces the center
                    ModulePiece panel = b.RotatedBlock(sx * 2.6f, sz * 2.6f, 2f, 0.3f, yaw, 0f, 1.0f, SolidKind.Cover, ArenaMaterials.Stone, edge, "breakable-panel");
                    panel.RetiresAtFinal = true;
                }
            }

            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-069 Conductor Center: two insulated metal floor strips sit outside the dry final floor.</summary>
    [ContentBehavior("MOD-069")]
    public sealed class ConductorCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-069";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            for (int side = -1; side <= 1; side += 2)
            {
                float zIn = side * 4.25f;
                float zOut = side * 4.95f;
                b.Patch(-2f, zIn, 2f, zOut, MaterialFlags.Metal | MaterialFlags.Conductive, true, "MOD-069:metal-strip");
                b.Patch(-2.2f, side * 4.05f, 2.2f, zIn, MaterialFlags.Insulating, false, "MOD-069:insulation");
            }

            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-070 Garden Center: two L-shaped growth corners stand outside the clear final floor.</summary>
    [ContentBehavior("MOD-070")]
    public sealed class GardenCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-070";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            float edge = b.Durability.Get("edge cover"); // hedges are the module's edge cover
            for (int s = -1; s <= 1; s += 2)
            {
                b.Cover(s * 3.2f, s * 4.4f, s * 5f, s * 5f, 1.6f, edge, ArenaMaterials.Hedge, "growth-corner");
                b.Cover(s * 4.4f, s * 3.2f, s * 5f, s * 4.4f, 1.6f, edge, ArenaMaterials.Hedge, "growth-corner");
            }

            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-071 Wind Center: two floor gutters guide clouds along the edges of a clear final floor.</summary>
    [ContentBehavior("MOD-071")]
    public sealed class WindCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-071";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            b.Patch(-5f, 4.1f, 5f, 4.9f, MaterialFlags.None, false, "MOD-071:wind-gutter");
            b.Patch(-5f, -4.9f, 5f, -4.1f, MaterialFlags.None, false, "MOD-071:wind-gutter");
            b.OpenAllEdges();
        }
    }

    /// <summary>MOD-072 Balanced Cover Center: opposed low covers on the floor and four matching peek posts outside it.</summary>
    [ContentBehavior("MOD-072")]
    public sealed class BalancedCoverCenter : ModuleBlueprint
    {
        public override string ModuleId => "MOD-072";

        protected internal override void Build(ModuleBuilder b)
        {
            CenterKit.FinalFloor(b);
            float edge = b.Durability.Get("edge cover");
            float pillar = b.Durability.Get("pillars");
            CenterKit.SoftCover(b, -1f, 3.0f, 1f, 3.5f, 1.0f, edge, ArenaMaterials.Stone, "opposed-low-cover");
            CenterKit.SoftCover(b, -1f, -3.5f, 1f, -3.0f, 1.0f, edge, ArenaMaterials.Stone, "opposed-low-cover");
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    b.Pillar(sx * 2.2f, sz * 4.4f, 0.5f, 2.2f, pillar, ArenaMaterials.Stone, "peek-post"); // 4.9 m out
                }
            }

            b.OpenAllEdges();
        }
    }
}
