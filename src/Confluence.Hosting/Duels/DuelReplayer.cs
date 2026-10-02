using System;
using System.Collections.Generic;
using Confluence.Arena;
using Confluence.Arena.Generation;
using Confluence.Core.Arena;
using Confluence.Core.Content;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;

namespace Confluence.Hosting.Duels
{
    /// <summary>Outcome of re-simulating a replay manifest.</summary>
    public sealed class ReplayResult
    {
        internal ReplayResult(DuelWorld world, ulong finalStateHash, bool matches, string mismatch)
        {
            World = world;
            FinalStateHash = finalStateHash;
            Matches = matches;
            Mismatch = mismatch;
        }

        /// <summary>The re-simulated world after the last recorded step.</summary>
        public DuelWorld World { get; }

        public ulong FinalStateHash { get; }

        /// <summary>The replay reproduced the recorded final tick and state hash (L427).</summary>
        public bool Matches { get; }

        /// <summary>Why it did not, or null.</summary>
        public string Mismatch { get; }
    }

    /// <summary>
    /// Re-simulates a recorded duel from its manifest (spec L419, L427): the same layout (regenerated from the arena
    /// recipe and checked against the recorded layout hash, or supplied), the recorded setups and spawn sides, every
    /// recorded command, and the between-step actions, then compares the final tick and state hash. A ruleset
    /// mismatch is reported instead of producing a misleading reconstruction (L427).
    /// </summary>
    public static class DuelReplayer
    {
        public static ReplayResult Replay(ReplayManifest manifest, ArenaGenerator generator, ISimWorldFactory factory = null,
            string expectedRulesetFingerprint = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            Catalogue catalogue = generator.Content.Catalogue;
            GeneratedArena generated = generator.Generate(manifest.Arena.ToGeneratorRequest(catalogue));
            return Replay(manifest, generated.Layout, factory, expectedRulesetFingerprint);
        }

        public static ReplayResult Replay(ReplayManifest manifest, ArenaLayout layout, ISimWorldFactory factory = null,
            string expectedRulesetFingerprint = null)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (!manifest.IsComplete) throw new InvalidOperationException("The manifest's duel has not finished.");
            if (expectedRulesetFingerprint != null && !string.Equals(expectedRulesetFingerprint, manifest.RulesetFingerprint, StringComparison.Ordinal))
            {
                return new ReplayResult(null, 0UL, false, "unsupported version: the replay was recorded under ruleset " +
                    manifest.RulesetFingerprint + ", this build runs " + expectedRulesetFingerprint);
            }

            ulong layoutHash = ArenaLayoutHash.Compute(layout);
            if (layoutHash != manifest.ArenaLayoutHash)
            {
                return new ReplayResult(null, 0UL, false, "the arena layout hash " + layoutHash.ToString("X16") + " differs from the recorded " +
                    manifest.ArenaLayoutHash.ToString("X16"));
            }

            factory = factory ?? SimWorldFactory.Default;
            DuelWorld world = factory.Create(layout.Clone(), ReplayManifest.CloneSetup(manifest.Setups[0]), ReplayManifest.CloneSetup(manifest.Setups[1]),
                manifest.AlcoveTicks, manifest.CombatTicks, true);
            IReadOnlyList<ReplayAction> actions = manifest.Actions;
            int nextAction = 0;
            using (IEnumerator<PlayerInput> side0 = manifest.Inputs[0].Expand().GetEnumerator())
            using (IEnumerator<PlayerInput> side1 = manifest.Inputs[1].Expand().GetEnumerator())
            {
                while (side0.MoveNext() && side1.MoveNext())
                {
                    while (nextAction < actions.Count && actions[nextAction].BeforeTick <= world.Tick + 1)
                    {
                        ApplyAction(world, actions[nextAction]);
                        nextAction++;
                    }

                    world.Step(side0.Current, side1.Current);
                }
            }

            ulong hash = world.StateHash();
            if (world.Tick != manifest.FinalTick)
            {
                return new ReplayResult(world, hash, false, "the replay ended at tick " + world.Tick + ", the duel at " + manifest.FinalTick);
            }

            return hash == manifest.FinalStateHash
                ? new ReplayResult(world, hash, true, null)
                : new ReplayResult(world, hash, false, "final state hash " + hash.ToString("X16") + " differs from the recorded " + manifest.FinalStateHash.ToString("X16"));
        }

        private static void ApplyAction(DuelWorld world, ReplayAction action)
        {
            switch (action.Kind)
            {
                case ReplayActionKind.Surrender:
                    world.Surrender(action.Side);
                    break;
                default:
                    throw new InvalidOperationException("Unknown replay action " + action.Kind + ".");
            }
        }
    }
}
