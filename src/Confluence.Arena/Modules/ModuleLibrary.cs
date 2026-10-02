using System;
using System.Collections.Generic;
using Confluence.Arena.Modules.Blueprints;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;

namespace Confluence.Arena.Modules
{
    /// <summary>
    /// Authored geometry for every catalogue module (spec L656-L660, L686). Blueprints are listed explicitly in
    /// catalogue order so lookups and generator candidate lists never depend on reflection or hash order.
    /// Geometry is built once per catalogue and is immutable afterward; the library is safe to share across threads.
    /// </summary>
    public sealed class ModuleLibrary
    {
        private static readonly Func<ModuleBlueprint>[] Factories =
        {
            () => new SplitGate(), () => new DoglegLane(), () => new ForkedApproach(), () => new CrossingCourt(),
            () => new OffsetAlley(), () => new RingWalk(), () => new TJunction(), () => new DoubleBend(),
            () => new DiagonalCut(), () => new BridgePair(), () => new BraidedPaths(), () => new OuterArcade(),
            () => new LowBarricade(), () => new TallSlab(), () => new WindowScreen(), () => new LCornerCover(),
            () => new BrokenColumn(), () => new TwinScreens(), () => new PrismShield(), () => new ConductiveGrille(),
            () => new PorousWall(), () => new HedgeBend(), () => new SlidingShield(), () => new SegmentedParapet(),
            () => new ShortRamp(), () => new TwinStairs(), () => new BroadSlope(), () => new SunkenCourt(),
            () => new RaisedSpine(), () => new TerracedSteps(), () => new OverlookPair(), () => new RampSwitchback(),
            () => new HopShortcut(), () => new SteppedGallery(), () => new LandingBowl(), () => new WatchDeck(),
            () => new WaterChannel(), () => new IceCrossing(), () => new CopperContact(), () => new FuelBed(),
            () => new DustPocket(), () => new PrismCorner(), () => new GroundingNiche(), () => new AcousticArch(),
            () => new HeatSinkLane(), () => new PorousStep(), () => new BrittleFringe(), () => new DampSandShelf(),
            () => new ShieldedAdvance(), () => new FlankTunnel(), () => new PeekGallery(), () => new WideSweep(),
            () => new PressureFunnel(), () => new CrossoverRamp(), () => new BlindPivot(), () => new WindowBypass(),
            () => new PropEscort(), () => new SteppingCover(), () => new OpenChallenge(), () => new RetreatLoop(),
            () => new FinalCircle(), () => new FourPillarCenter(), () => new CrossWellCenter(), () => new PrismCenter(),
            () => new DryIslandCenter(), () => new SplitHeightCenter(), () => new AcousticCenter(), () => new FractureCenter(),
            () => new ConductorCenter(), () => new GardenCenter(), () => new WindCenter(), () => new BalancedCoverCenter(),
        };

        private readonly Dictionary<string, ModuleGeometry> _byId = new Dictionary<string, ModuleGeometry>(StringComparer.Ordinal);
        private readonly List<ModuleGeometry> _ordered = new List<ModuleGeometry>();

        public ModuleLibrary(Catalogue catalogue)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            for (int i = 0; i < Factories.Length; i++)
            {
                ModuleBlueprint blueprint = Factories[i]();
                if (!catalogue.ModulesById.TryGetValue(blueprint.ModuleId, out ModuleDef def))
                {
                    continue;
                }

                var builder = new ModuleBuilder(def);
                blueprint.Build(builder);
                ModuleGeometry geometry = builder.Geometry;
                _byId[def.Id] = geometry;
                _ordered.Add(geometry);
            }

            _ordered.Sort((a, b) => string.CompareOrdinal(a.ModuleId, b.ModuleId));
        }

        /// <summary>Number of module ids that have an authored blueprint.</summary>
        public static int BlueprintCount => Factories.Length;

        /// <summary>Authored modules in id order.</summary>
        public IReadOnlyList<ModuleGeometry> All => _ordered;

        public bool TryGet(string moduleId, out ModuleGeometry geometry) => _byId.TryGetValue(moduleId ?? string.Empty, out geometry);

        public ModuleGeometry Get(string moduleId)
        {
            if (!TryGet(moduleId, out ModuleGeometry geometry))
            {
                throw new ArgumentException("No authored geometry for module " + moduleId, nameof(moduleId));
            }

            return geometry;
        }

        /// <summary>Authored modules of one category in id order.</summary>
        public List<ModuleGeometry> OfCategory(ModuleCategory category)
        {
            var result = new List<ModuleGeometry>();
            for (int i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i].Category == category)
                {
                    result.Add(_ordered[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// Authoring checks shared by tests and the generator: pieces inside the footprint, sockets on edges and at
        /// least 2.5 m wide (spec L312, module sockets text), ramps at most 30 degrees, elevation equal to the catalogue
        /// height, durability values taken from the module text, and point symmetry for center modules.
        /// </summary>
        public static List<string> Check(ModuleGeometry g)
        {
            var issues = new List<string>();
            float hw = g.Width * 0.5f + 1e-3f;
            float hd = g.Depth * 0.5f + 1e-3f;
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                ModulePiece p = g.Pieces[i];
                for (int c = 0; c < 4; c++)
                {
                    Vec2 corner = p.Box.FootprintCorner(c);
                    if (Math.Abs(corner.X) > hw || Math.Abs(corner.Y) > hd)
                    {
                        issues.Add(g.ModuleId + " piece " + i + " (" + p.Role + ") leaves the footprint.");
                        break;
                    }
                }

                if (!p.Anchored && !g.DeclaredDurability.Contains(p.Durability))
                {
                    issues.Add(g.ModuleId + " piece " + i + " durability " + p.Durability + " is not in the module text.");
                }

                if (p.Anchored && (p.Durability != 0f || (p.Flags & MaterialFlags.Anchored) == 0))
                {
                    issues.Add(g.ModuleId + " piece " + i + " is anchored but carries durability or lacks the anchored flag.");
                }
            }

            if (g.Sockets.Count == 0)
            {
                issues.Add(g.ModuleId + " has no sockets.");
            }

            for (int i = 0; i < g.Sockets.Count; i++)
            {
                ModuleSocket s = g.Sockets[i];
                float edgeLength = s.Edge == ModuleEdge.North || s.Edge == ModuleEdge.South ? g.Width : g.Depth;
                if (s.Width + 1e-3f < GlobalRules.Arena.MinRouteWidth)
                {
                    issues.Add(g.ModuleId + " socket " + i + " is narrower than 2.5 m.");
                }

                if (Math.Abs(s.Offset) + s.Width * 0.5f > edgeLength * 0.5f + 1e-3f)
                {
                    issues.Add(g.ModuleId + " socket " + i + " extends past its edge.");
                }
            }

            if (g.MaxSlopeDeg() > GlobalRules.Arena.RampMaxSlopeDeg + 1e-3f)
            {
                issues.Add(g.ModuleId + " has a ramp steeper than 30 degrees.");
            }

            float expected = g.Height;
            if (expected > 0f && Math.Abs(g.MaxWalkableHeight() - expected) > 1e-3f)
            {
                issues.Add(g.ModuleId + " top surface " + g.MaxWalkableHeight() + " differs from catalogue height " + expected + ".");
            }

            if (expected < 0f && Math.Abs(g.MinWalkableHeight() - expected) > 1e-3f)
            {
                issues.Add(g.ModuleId + " lowest surface " + g.MinWalkableHeight() + " differs from catalogue height " + expected + ".");
            }

            float limit = g.IsVantage ? GlobalRules.Arena.VantageHeight : GlobalRules.Arena.ElevationMax;
            if (g.MaxWalkableHeight() > limit + 1e-3f)
            {
                issues.Add(g.ModuleId + " exceeds the elevation limit.");
            }

            if (g.Category == ModuleCategory.Center && !IsPointSymmetric(g))
            {
                issues.Add(g.ModuleId + " center geometry is not point-symmetric.");
            }

            return issues;
        }

        /// <summary>True when every piece and patch has a counterpart under a 180-degree turn about the module center.</summary>
        public static bool IsPointSymmetric(ModuleGeometry g)
        {
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                ModulePiece p = g.Pieces[i];
                bool found = false;
                for (int j = 0; j < g.Pieces.Count && !found; j++)
                {
                    found = MirrorMath.IsPointMirror(p.Box, g.Pieces[j].Box, 1e-3f, p.RampRise > 0f) && p.Material == g.Pieces[j].Material
                        && p.Durability == g.Pieces[j].Durability && p.RampRise == g.Pieces[j].RampRise;
                }

                if (!found)
                {
                    return false;
                }
            }

            for (int i = 0; i < g.Patches.Count; i++)
            {
                ModulePatch a = g.Patches[i];
                bool found = false;
                for (int j = 0; j < g.Patches.Count && !found; j++)
                {
                    ModulePatch b = g.Patches[j];
                    found = Vec2.Distance(a.Center, -b.Center) < 1e-3f && Vec2.Distance(a.HalfSize, b.HalfSize) < 1e-3f
                        && a.Radius == b.Radius && a.Flags == b.Flags && a.Water == b.Water && a.Growth == b.Growth;
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
