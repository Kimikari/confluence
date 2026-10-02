using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Random;
using Confluence.Core.Rules;
using Confluence.Economy.Loadouts;

namespace Confluence.Economy.Shop
{
    /// <summary>
    /// The shared equipment the shop may offer: every legal catalogue definition that is available (implemented),
    /// grouped by tier in catalogue order. Spells and augments are never part of it (spec L185, L202). Owning a wizard is
    /// never an item condition (spec L206), so the pool is the same for every player.
    /// </summary>
    public sealed class EquipmentPool
    {
        private readonly List<EquipmentDef>[] _byTier = new List<EquipmentDef>[GlobalRules.Economy.TierCount];

        public EquipmentPool(Catalogue catalogue, IContentAvailability availability)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));
            if (availability == null) throw new ArgumentNullException(nameof(availability));
            for (int t = 0; t < _byTier.Length; t++) _byTier[t] = new List<EquipmentDef>();
            foreach (var def in catalogue.EquipmentItems)
                if (SlotRules.IsLegalDefinition(def) && availability.IsAvailable(def.Id)) _byTier[def.Tier - 1].Add(def);
        }

        /// <summary>Available legal definitions of a tier (1-4), in catalogue order.</summary>
        public IReadOnlyList<EquipmentDef> Tier(int tier) => _byTier[tier - 1].AsReadOnly();

        /// <summary>
        /// DECISIONS D-020 (spec L206 "drawn from unlocked affordable tiers, then uniformly among legal definitions in a
        /// tier"): choose uniformly among tiers that are unlocked this round, cost at most the balance, and still hold a
        /// non-excluded definition; then uniformly within that tier. Null when nothing qualifies.
        /// </summary>
        public EquipmentDef Draw(Rng rng, RoundInfo round, int balance, ICollection<string> excluded)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var tiers = new List<List<EquipmentDef>>(_byTier.Length);
            for (int tier = 1; tier <= _byTier.Length; tier++)
            {
                if (!round.IsTierUnlocked(tier) || Schedules.TierPrice(tier) > balance) continue;
                var candidates = Candidates(tier, excluded);
                if (candidates.Count > 0) tiers.Add(candidates);
            }
            if (tiers.Count == 0) return null;
            var chosenTier = tiers[rng.NextInt(tiers.Count)];
            return chosenTier[rng.NextInt(chosenTier.Count)];
        }

        /// <summary>Definitions a draw could return now.</summary>
        public int CountDrawable(RoundInfo round, int balance, ICollection<string> excluded)
        {
            int n = 0;
            for (int tier = 1; tier <= _byTier.Length; tier++)
                if (round.IsTierUnlocked(tier) && Schedules.TierPrice(tier) <= balance) n += Candidates(tier, excluded).Count;
            return n;
        }

        private List<EquipmentDef> Candidates(int tier, ICollection<string> excluded)
        {
            var list = new List<EquipmentDef>();
            foreach (var def in _byTier[tier - 1])
                if (excluded == null || !excluded.Contains(def.Id)) list.Add(def);
            return list;
        }
    }
}
