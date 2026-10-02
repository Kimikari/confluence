using System;
using System.Collections.Generic;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Sim.Hashing;

namespace Confluence.Sim.Duel
{
    /// <summary>
    /// Server-verified counters for the 24 completed-duel feats of one player in one duel (spec L251-L280). Thresholds
    /// come from the typed <see cref="FeatDef"/> values; nothing here copies a literal. Counters that need spells,
    /// reactions, or casting are fed through world APIs and stay zero until those stages call them
    /// (<see cref="AwaitingLaterStages"/>).
    /// </summary>
    public sealed class FeatTracker
    {
        /// <summary>Feats whose qualifying events come from stages that do not exist yet (spells, reactions, casting, props).</summary>
        public static readonly IReadOnlyList<string> AwaitingLaterStages = Array.AsReadOnly(new[]
        {
            "FEAT-002", // spell hits (fed by ApplyDamage once spells deal damage)
            "FEAT-006", // world reactions (IMaterialsContext.RecordReaction)
            "FEAT-009", // direct spell hits from 15 m (fed by ApplyDamage)
            "FEAT-011", // clouds cleared (IMaterialsContext.RecordCloudCleared)
            "FEAT-013", // mana commitments (DuelWorld.TryCommitMana)
            "FEAT-014", // mastery casts (DuelWorld.RecordMasteryCast)
            "FEAT-015", // hostile summons destroyed (needs summon creators)
            "FEAT-019", // interrupts (ISpellRuntime.Interrupt)
            "FEAT-020", // conductivity reaction damage (reaction contact damage)
            "FEAT-021", // moved-prop hits (prop simulation)
            "FEAT-022", // spell then weapon combos (needs spell hits)
            "FEAT-023", // hostile statuses removed (needs dispel sources)
        });

        private static readonly DamageChannel[] ChannelOrder =
        {
            DamageChannel.Physical, DamageChannel.Thermal, DamageChannel.Electrical, DamageChannel.Corrosive, DamageChannel.Arcane,
        };

        private readonly List<string> _spellsLanded = new List<string>();
        private readonly List<string> _reactions = new List<string>();
        private readonly float[] _damageByChannel = new float[6];
        private int _lastDodgeTick = int.MinValue / 2;
        private int _lastSpellHitTick = int.MinValue / 2;
        private bool _spellHitAvailableForCombo;

        public float DamageDealt { get; private set; }              // FEAT-001
        public int DistinctSpellsLanded => _spellsLanded.Count;    // FEAT-002
        public int PerfectGuards { get; private set; }              // FEAT-003
        public int MeleeHits { get; private set; }                  // FEAT-004
        public float DistanceTravelled { get; private set; }        // FEAT-005
        public int DistinctReactions => _reactions.Count;          // FEAT-006
        public int CoversBroken { get; private set; }               // FEAT-007
        public int HitsAfterDodge { get; private set; }             // FEAT-008
        public int LongRangeDirectHits { get; private set; }        // FEAT-009
        public int CloudsCleared { get; private set; }              // FEAT-011
        public int MasteryCasts { get; private set; }               // FEAT-014
        public int SummonsDestroyed { get; private set; }           // FEAT-015
        public int ConsumablesUsed { get; private set; }            // FEAT-016
        public float ShieldAbsorbed { get; private set; }           // FEAT-017
        public float HealthRestored { get; private set; }           // FEAT-018
        public int Interrupts { get; private set; }                 // FEAT-019
        public float ConductivityDamage { get; private set; }       // FEAT-020
        public int PropHits { get; private set; }                   // FEAT-021
        public int SpellWeaponCombos { get; private set; }          // FEAT-022
        public int HostileStatusesRemoved { get; private set; }     // FEAT-023
        public int BoundaryReturns { get; private set; }            // FEAT-024

        public float DamageInChannel(DamageChannel channel) => _damageByChannel[(int)channel];

        // ----- feeds -----------------------------------------------------------------------------------------

        /// <summary>Damage this player dealt to the opponent (shield absorption plus health loss), per channel.</summary>
        public void OnDamageDealt(float dealt, DamageChannel channel, float channelAmount)
        {
            if (dealt > 0f)
            {
                DamageDealt += dealt;
            }

            if (channel != DamageChannel.None && channelAmount > 0f)
            {
                _damageByChannel[(int)channel] += channelAmount;
            }
        }

        /// <summary>An original hit on the opponent: dodge follow-ups, spell-then-weapon combos, spells landed, range.</summary>
        public void OnOriginalHit(int tick, bool spellHit, bool melee, bool directSpell, string spellId, float range)
        {
            int window = Ticks.FromSeconds(Feats.Get("FEAT-008").WindowSec);
            if (tick - _lastDodgeTick <= window)
            {
                HitsAfterDodge++;
            }

            if (spellHit && spellId != null && !_spellsLanded.Contains(spellId))
            {
                _spellsLanded.Add(spellId);
            }

            if (directSpell && range >= Feats.Get("FEAT-009").MinRangeMeters)
            {
                LongRangeDirectHits++;
            }

            if (melee)
            {
                MeleeHits++;
                int comboWindow = Ticks.FromSeconds(Feats.Get("FEAT-022").WindowSec);
                if (_spellHitAvailableForCombo && tick - _lastSpellHitTick <= comboWindow)
                {
                    SpellWeaponCombos++;
                    _spellHitAvailableForCombo = false;
                }
            }

            if (spellHit)
            {
                _lastSpellHitTick = tick;
                _spellHitAvailableForCombo = true;
            }
        }

        public void OnDodge(int tick) => _lastDodgeTick = tick;

        public void OnPerfectGuard() => PerfectGuards++;

        public void OnMoved(float meters)
        {
            if (meters > 0f)
            {
                DistanceTravelled += meters;
            }
        }

        public void OnReaction(string reactionId)
        {
            if (reactionId != null && !_reactions.Contains(reactionId))
            {
                _reactions.Add(reactionId);
            }
        }

        public void OnCoverBroken() => CoversBroken++;
        public void OnCloudCleared() => CloudsCleared++;
        public void OnMasteryCast() => MasteryCasts++;
        public void OnSummonDestroyed() => SummonsDestroyed++;
        public void OnConsumableUsed() => ConsumablesUsed++;
        public void OnShieldAbsorbed(float amount) => ShieldAbsorbed += Math.Max(0f, amount);
        public void OnHealthRestored(float amount) => HealthRestored += Math.Max(0f, amount);
        public void OnInterrupt() => Interrupts++;
        public void OnConductivityDamage(float amount) => ConductivityDamage += Math.Max(0f, amount);
        public void OnPropHit() => PropHits++;
        public void OnHostileStatusRemoved() => HostileStatusesRemoved++;
        public void OnBoundaryReturn() => BoundaryReturns++;

        // ----- evaluation ------------------------------------------------------------------------------------

        /// <summary>Whether this duel met a feat's definition (spec L253-L278).</summary>
        public bool IsMet(FeatDef feat, bool won, float lowestHealthFraction, float manaCommitted)
        {
            if (feat == null)
            {
                throw new ArgumentNullException(nameof(feat));
            }

            if (feat.RequiresWin && !won)
            {
                return false;
            }

            switch (feat.Id)
            {
                case "FEAT-001": return DamageDealt >= feat.Amount;
                case "FEAT-002": return DistinctSpellsLanded >= feat.Count;
                case "FEAT-003": return PerfectGuards >= feat.Count;
                case "FEAT-004": return MeleeHits >= feat.Count;
                case "FEAT-005": return DistanceTravelled >= feat.Amount;
                case "FEAT-006": return DistinctReactions >= feat.Count;
                case "FEAT-007": return CoversBroken >= feat.Count;
                case "FEAT-008": return HitsAfterDodge >= feat.Count;
                case "FEAT-009": return LongRangeDirectHits >= feat.Count;
                case "FEAT-010": return lowestHealthFraction < feat.HealthFractionBelow;
                case "FEAT-011": return CloudsCleared >= feat.Count;
                case "FEAT-012": return ChannelsAtLeast(feat.Amount) >= feat.Count;
                case "FEAT-013": return manaCommitted >= feat.Amount;
                case "FEAT-014": return MasteryCasts >= feat.Count;
                case "FEAT-015": return SummonsDestroyed >= feat.Count;
                case "FEAT-016": return ConsumablesUsed == 0;
                case "FEAT-017": return ShieldAbsorbed >= feat.Amount;
                case "FEAT-018": return HealthRestored >= feat.Amount;
                case "FEAT-019": return Interrupts >= feat.Count;
                case "FEAT-020": return ConductivityDamage >= feat.Amount;
                case "FEAT-021": return PropHits >= feat.Count;
                case "FEAT-022": return SpellWeaponCombos >= feat.Count;
                case "FEAT-023": return HostileStatusesRemoved >= feat.Count;
                case "FEAT-024": return BoundaryReturns >= feat.Count;
                default: throw new ArgumentException("Unknown feat " + feat.Id);
            }
        }

        /// <summary>Feat ids met in this duel, in FEAT order.</summary>
        public List<string> Completed(bool won, float lowestHealthFraction, float manaCommitted)
        {
            var ids = new List<string>();
            foreach (FeatDef feat in Feats.All)
            {
                if (IsMet(feat, won, lowestHealthFraction, manaCommitted))
                {
                    ids.Add(feat.Id);
                }
            }

            return ids;
        }

        public void Hash(ref StateHasher h)
        {
            h.Add(DamageDealt);
            h.Add(_spellsLanded.Count);
            h.Add(PerfectGuards);
            h.Add(MeleeHits);
            h.Add(DistanceTravelled);
            h.Add(_reactions.Count);
            h.Add(CoversBroken);
            h.Add(HitsAfterDodge);
            h.Add(LongRangeDirectHits);
            h.Add(CloudsCleared);
            h.Add(MasteryCasts);
            h.Add(SummonsDestroyed);
            h.Add(ConsumablesUsed);
            h.Add(ShieldAbsorbed);
            h.Add(HealthRestored);
            h.Add(Interrupts);
            h.Add(ConductivityDamage);
            h.Add(PropHits);
            h.Add(SpellWeaponCombos);
            h.Add(HostileStatusesRemoved);
            h.Add(BoundaryReturns);
            for (int i = 0; i < _damageByChannel.Length; i++)
            {
                h.Add(_damageByChannel[i]);
            }
        }

        private int ChannelsAtLeast(float amount)
        {
            int count = 0;
            for (int i = 0; i < ChannelOrder.Length; i++)
            {
                if (_damageByChannel[(int)ChannelOrder[i]] >= amount)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
