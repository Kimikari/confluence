using System;

namespace Confluence.Core.Rules
{
    /// <summary>
    /// Global rule numbers from the design (spec §2-§7, §14 and the rule chapters, lines 1-717). Content may be
    /// stricter than these values but never looser (spec L128, L300). Units: meters, seconds, degrees; fractions
    /// are 0-1. Each constant cites its source line in docs/confluence/confluence-design.md.
    /// </summary>
    public static class GlobalRules
    {
        /// <summary>Health, mana, and stamina pools and regeneration (spec §3 "Common numerical foundation").</summary>
        public static class Resources
        {
            public const float BaseHealth = 300f;              // spec L78 (also L36, L464)
            public const float BaseMana = 100f;                // spec L78
            public const float BaseStamina = 100f;             // spec L78
            public const float MaxHealthCap = 450f;            // spec L114
            public const float HealthRegenPerSecond = 0f;      // spec L78: no passive health regeneration
            public const float ManaRegenPerSecond = 10f;       // spec L78
            public const float ManaRegenDelaySec = 1.5f;       // spec L78: after 1.5 s without spending mana
            public const float ManaRegenCapPerSecond = 14f;    // spec L78: cap after modifiers
            public const float StaminaRegenPerSecond = 20f;    // spec L78
            public const float StaminaRegenDelaySec = 1f;      // spec L78: after 1 s without spending stamina
            public const bool ChannelsCountAsSpending = true;  // spec L78
            public const bool ScalesWithRound = false;         // spec L78: base pools never grow with round number
        }

        /// <summary>Locomotion (spec §3 L80-L82).</summary>
        public static class Movement
        {
            public const float WalkSpeed = 5.5f;               // spec L80, m/s
            public const float SprintSpeed = 7f;               // spec L80, m/s
            public const float SprintStaminaPerSecond = 12f;   // spec L80
            public const float JumpStaminaCost = 10f;          // spec L80
            public const int UniversalJumpCount = 1;           // spec L80: no universal double jump
            public const float DodgeStaminaCost = 25f;         // spec L82
            public const float DodgeDistance = 3f;             // spec L82
            public const float DodgeDurationSec = 0.22f;       // spec L82
            public const float DodgeCooldownSec = 1.6f;        // spec L82
            public const bool DodgeGrantsInvulnerability = false; // spec L82
        }

        /// <summary>Cast phases, interruption, recovery, and telegraphs (spec §3 L86-L96, L472, L525, L535).</summary>
        public static class Casting
        {
            public const float PreCommitCancelRecastLockSec = 0.15f; // spec L90
            public const float InterruptRefundFraction = 0.25f;      // spec L90: only with the interrupt-refund tag
            public const float VoluntaryCancelRefundFraction = 0f;   // spec L90: after commitment
            public const float RefundCapFraction = 0.25f;            // spec L90, L124: of the original paid resource
            public const int MaxRefundsPerCast = 1;                  // spec L90

            public const float RecoveryQuickProjectileSec = 0.20f;   // spec L92
            public const float RecoveryHeavyProjectileSec = 0.35f;   // spec L92
            public const float RecoveryControlSec = 0.35f;           // spec L92
            public const float RecoveryMajorFieldOrSummonSec = 0.45f; // spec L92
            public const float MaxChannelDurationSec = 3f;           // spec L92

            public const float MoveSpeedFactorHeavyCast = 0.70f;     // spec L92: heavy/control/field casts, of walk speed
            public const float MoveSpeedFactorQuickCast = 0.85f;     // spec L92

            public const int DefaultRecastCount = 1;                 // spec L94: once during the effect's remaining lifetime
            public const float RecastExtraManaCost = 0f;             // spec L94 unless the record states a payment

            public const float AnticipatoryCueMinSec = 0.25f;        // spec L96
            public const float CueDirectDamageThreshold = 70f;       // spec L96: threats above 70 direct damage need the cue

            public const float CloseRangeMaxMeters = 6f;             // spec L535: offensive range at most 6 m is "close"
            public const float RangedRangeMinMeters = 12f;           // spec L535: offensive range at least 12 m is "ranged"
        }

        /// <summary>Common melee (spec §3 L100-L102). Profiles are assembled in <see cref="MeleeProfile"/>.</summary>
        public static class Melee
        {
            public const float FocusDamage = 18f;          // spec L100: wand or staff butt strike, physical
            public const float FocusReach = 1.6f;          // spec L100
            public const float FocusCycleSec = 0.8f;       // spec L100
            public const float FocusStamina = 8f;          // spec L100
            public const float FocusWindupSec = 0.18f;     // spec L102

            public const float LightBladeDamage = 28f;     // spec L100
            public const float LightBladeReach = 1.8f;     // spec L100
            public const float LightBladeCycleSec = 0.8f;  // spec L100
            public const float LightBladeStamina = 12f;    // spec L100
            public const float LightBladeWindupSec = 0.20f; // spec L102

            public const float HeavyDamage = 42f;          // spec L100
            public const float HeavyReach = 2.2f;          // spec L100
            public const float HeavyCycleSec = 1.25f;      // spec L100
            public const float HeavyStamina = 20f;         // spec L100
            public const float HeavyWindupSec = 0.35f;     // spec L102

            public const float SpearDamage = 32f;          // spec L100
            public const float SpearReach = 2.8f;          // spec L100
            public const float SpearCycleSec = 1.05f;      // spec L100
            public const float SpearStamina = 16f;         // spec L100
            public const float SpearWindupSec = 0.25f;     // spec L102

            public const int HitsPerOpponentPerAttack = 1; // spec L102
            public const float EquipSwapSec = 0.35f;       // spec L106: cannot cancel recovery
            public const float SpellSelectSwapSec = 0f;    // spec L106: changing the selected spell has no swap windup
        }

        /// <summary>Common guard (spec §3 L104-L106).</summary>
        public static class Guard
        {
            public const float StaminaCost = 25f;               // spec L104, paid upfront
            public const float MaxHoldSec = 0.8f;               // spec L104
            public const float CooldownSec = 3f;                // spec L104, from release
            public const float ArcDeg = 120f;                   // spec L104, frontal arc (60 degrees either side)
            public const float DamageReduction = 0.50f;         // spec L104, weapon and projectile direct damage
            public const float PerfectWindowSec = 0.12f;        // spec L104, first 0.12 s
            public const float PerfectDamageReduction = 0.80f;  // spec L104
            public const float PerfectManaGain = 5f;            // spec L104, once per guard on a successful block
            public const float ReleasePunishWindowSec = 0.25f;  // spec L104
            public const bool ReducesKnockbackBySameFraction = true; // spec L104
        }

        /// <summary>Damage resolution and mitigation (spec §3 L110-L112).</summary>
        public static class Damage
        {
            public const float PrecisionMultiplierMax = 1.25f;   // spec L110
            public const float ChannelResistanceCap = 0.35f;     // spec L112
            public const float NegativeResistanceFloor = -0.15f; // spec L112
            public const float PassiveReductionCap = 0.45f;      // spec L112: all passive reductions incl. resistance, multiplicative
            public const bool GuardExemptFromPassiveCap = true;  // spec L106
        }

        /// <summary>Shields (spec §3 L114, L579).</summary>
        public static class Shields
        {
            public const float Cap = 100f;                      // spec L114, L579: temporary and reserve shields share it
            public const float MaxTimedDurationSec = 8f;        // spec L114, L579: cast and triggered shields
            public const bool ReserveShieldsRegenerate = false; // spec L114
        }

        /// <summary>Healing and lifesteal budgets (spec §3 L114).</summary>
        public static class Healing
        {
            public const float MaxPerSecond = 35f;          // spec L114, direct healing from all sources
            public const float MaxPerDuel = 120f;           // spec L114
            public const float LifestealFractionCap = 0.10f; // spec L114, direct damage only
            public const float LifestealMaxPerSecond = 20f; // spec L114, also within the 120 duel budget
        }

        /// <summary>Hard control and slows (spec §3 L116).</summary>
        public static class Control
        {
            public const float HardControlMaxSec = 0.75f;   // spec L116, single application
            public const float SilenceMaxSec = 0.6f;        // spec L116
            public const float HardControlImmunitySec = 2f; // spec L116, after any hard control ends
            public const float SlowCap = 0.30f;             // spec L116, strongest slow only
            public const float SlowMaxDurationSec = 3f;     // spec L116
        }

        /// <summary>Named statuses, reveal, and invisibility (spec §3 L118-L120).</summary>
        public static class Statuses
        {
            public const float WetDamageMultiplier = 1f;            // spec L118: wet adds no universal damage multiplier
            public const float RevealMaxSec = 2f;                   // spec L120
            public const float InvisibilityMaxSec = 1.5f;           // spec L120
            public const float InvisibilitySilhouetteRadius = 4f;   // spec L120
        }

        /// <summary>Global modifier ceilings and mastery normalization (spec §3 L124, L478-L480).</summary>
        public static class Modifiers
        {
            public const float MovementBonusCap = 0.20f;          // spec L124
            public const int MaxDashCharges = 2;                  // spec L124
            public const float MaxDashDistance = 8f;              // spec L124
            public const float MaxTeleportDistance = 10f;         // spec L124
            public const float CooldownReductionCap = 0.25f;      // spec L124, permanent CDR
            public const float MinRechargeSec = 0.5f;             // spec L124
            public const float MasteryAttributeCap = 0.15f;       // spec L124, L478-L480: damage, projectile speed, or windup
            public const float MasteryEfficiencyCap = 0.20f;      // spec L124, L478-L480: paid-resource reduction
            public const int MasteryResultsPerCast = 1;           // spec L478, L480, L508
            public const float TypicalMasteryRewardMin = 0.10f;   // spec L478: individual roster rewards are normally 10-15%
            public const float TypicalMasteryRewardMax = 0.15f;   // spec L478
            public const float CostReductionCap = 0.30f;          // spec L124: from all sources
            public const float MinPaidCostFraction = 0.70f;       // spec L124
            public const float RefundCapFraction = 0.25f;         // spec L124: of the payment, separately capped
        }

        /// <summary>Proc budgets, secondary effects, and chains (spec §3 L126).</summary>
        public static class Triggers
        {
            public const float ProcInternalCooldownSec = 0.5f; // spec L126
            public const int ProcsPerTargetHitEvent = 1;       // spec L126
            public const int SecondaryGenerations = 1;         // spec L126, L508
            public const int ChainMaxJumps = 3;                // spec L126
            public const float ChainMaxJumpDistance = 6f;      // spec L126
        }

        /// <summary>Player-owned fields and summons (spec §3 L128, L496, L670).</summary>
        public static class FieldsAndSummons
        {
            public const int MaxFieldsPerPlayer = 3;  // spec L128
            public const int MaxSummonsPerPlayer = 2; // spec L128
            public const float MaxLifetimeSec = 8f;   // spec L128
        }

        /// <summary>Cells, deposits, and the §4 core reaction table (spec L136-L167).</summary>
        public static class World
        {
            // Representation (L136)
            public const float CellSize = 0.5f;             // spec L136
            public const int MaxUnitsPerCell = 4;           // spec L136, L163: water, heat, frost, dust 0-4
            public const int MaxActiveCellRegions = 128;    // spec L136, L421

            // Default deposits (L163)
            public const int QuickProjectileDeposit = 1;          // spec L163, one local unit
            public const int FieldDepositPerCell = 1;             // spec L163, on creation
            public const int HeavyProjectileImpactDeposit = 2;    // spec L163, impact cell
            public const int HeavyProjectileAdjacentDeposit = 1;  // spec L163
            public const float HeavyProjectileAdjacentRadius = 1f; // spec L163

            // Heat and water -> steam (L144)
            public const int SteamHeatConsumed = 1;          // spec L144
            public const int SteamWaterConsumed = 1;         // spec L144
            public const float SteamLifetimeSec = 3f;        // spec L144
            public const float SteamRise = 2f;               // spec L144
            public const float SteamDamage = 0f;             // spec L144
            public const float SteamSightBlockPathLength = 2f; // spec L167: cumulative path length blocking acquisition

            // Frost and water -> ice (L145)
            public const int IceFrostConsumed = 2;           // spec L145
            public const int IceWaterConsumed = 1;           // spec L145
            public const float IceLifetimeSec = 5f;          // spec L145
            public const float IceTileDurability = 45f;      // spec L145

            // Heat and ice (L146)
            public const int MeltHeatConsumed = 2;           // spec L146
            public const int MeltWaterProduced = 1;          // spec L146

            // Force and brittle ice (L147)
            public const float ShatterMinDirectDamage = 20f; // spec L147
            public const int ShatterTilesPerImpact = 1;      // spec L147
            public const float ShardConeLength = 1f;         // spec L147
            public const float ShardDamage = 10f;            // spec L147, physical, once per impact

            // Electricity and connected wet cells (L148)
            public const float WetChargeDurationSec = 0.6f;  // spec L148
            public const float WetChargeMaxPropagation = 6f; // spec L148
            public const int WetChargeMaxCells = 12;         // spec L148

            // Electricity and metal (L149)
            public const int MetalMaxBranches = 3;           // spec L149
            public const float MetalMaxBranchLength = 6f;    // spec L149

            // Water and anchored conductor (L150)
            public const int GroundedBranchesRedirected = 1; // spec L150

            // Wind and steam or dust (L151)
            public const int WindTranslateMinStrength = 1;   // spec L151 "above strength 1"; read as >= 1 (BIO gusts of strength 1 move clouds)
            public const float WindTranslateMaxSpeed = 3f;   // spec L151, m/s
            public const int WindDisperseMinStrength = 2;    // spec L151
            public const float WindDisperseSliceDepth = 2f;  // spec L151, front slice

            // Water and burning material (L152)
            public const int ExtinguishWaterConsumed = 1;    // spec L152
            public const int ExtinguishHeatRemoved = 1;      // spec L152
            public const float BurnSuppressionSec = 2f;      // spec L152

            // Heat and combustible growth (L153)
            public const int GrowthIgniteMinHeat = 2;        // spec L153
            public const float GrowthBurnSec = 2f;           // spec L153

            // Water and sand (L154)
            public const float DampSandSec = 3f;             // spec L154, equal units

            // Force and loose props (L155)
            public const float PropCollisionDamageCap = 25f; // spec L155, L173, physical per impact

            // Light and prism props (L157, L527)
            public const int PrismMaxReflections = 1;            // spec L157, L527
            public const float PrismRetainedDamageFraction = 0.70f; // spec L157, L527

            // Shadow and bright light (L158)
            public const float ShadowLifetimeDrainPerSecond = 1f; // spec L158, seconds lost per second of exposure

            // Venom and water (L159)
            public const float VenomDilutionFactor = 0.5f;   // spec L159

            // Gravity and projectiles (L160)
            public const float GravityMaxDeflectionDeg = 20f; // spec L160

            // Arcane and active field (L161)
            public const int DispelLayersRemoved = 1;        // spec L161
        }

        /// <summary>Destruction, props, knockback, falls, and recall (spec §4 L171-L175).</summary>
        public static class Terrain
        {
            public const float WallRaiseMax = 0.5f;          // spec L171: a wall formed under a player lifts them at most 0.5 m

            public const float LightPropMass = 10f;          // spec L173
            public const float MediumPropMass = 30f;         // spec L173
            public const float HeavyPropMass = 80f;          // spec L173

            public const float LightPropPushDistance = 4f;   // spec L173, normal force attack
            public const float MediumPropPushDistance = 2f;  // spec L173
            public const float HeavyPropPushDistance = 0.5f; // spec L173
            public const float AnchoredPropPushDistance = 0f; // spec L173: anchored objects only shake or take durability damage

            public const float DamagingPropVelocityCap = 8f; // spec L173, m/s

            public const float LightPropCollisionDamage = 5f;   // spec L173
            public const float MediumPropCollisionDamage = 15f; // spec L173
            public const float HeavyPropCollisionDamage = 25f;  // spec L173
            public const float PropHitIntervalPerPlayerSec = 1f; // spec L173: each prop damages a player at most once per second

            public const float PlayerKnockbackCap = 4f;      // spec L173, per effect

            public const float FallDamageThreshold = 3f;     // spec L175, drop height in meters
            public const float FallDamagePerMeter = 10f;     // spec L175, physical, per excess meter
            public const float FallDamageCap = 50f;          // spec L175

            public const float OutOfMapRecallPenalty = 40f;  // spec L175, health
            public const float OutOfMapRecallSec = 0.5f;     // spec L175

            public static float PropMassValue(PropMass mass)
            {
                switch (mass)
                {
                    case PropMass.Light: return LightPropMass;
                    case PropMass.Medium: return MediumPropMass;
                    case PropMass.Heavy: return HeavyPropMass;
                    default: return float.PositiveInfinity; // anchored
                }
            }

            public static float PushDistance(PropMass mass)
            {
                switch (mass)
                {
                    case PropMass.Light: return LightPropPushDistance;
                    case PropMass.Medium: return MediumPropPushDistance;
                    case PropMass.Heavy: return HeavyPropPushDistance;
                    default: return AnchoredPropPushDistance;
                }
            }

            /// <summary>Collision damage of a moving prop above its threshold (anchored props do not move).</summary>
            public static float CollisionDamage(PropMass mass)
            {
                switch (mass)
                {
                    case PropMass.Light: return LightPropCollisionDamage;
                    case PropMass.Medium: return MediumPropCollisionDamage;
                    case PropMass.Heavy: return HeavyPropCollisionDamage;
                    default: return 0f;
                }
            }

            /// <summary>Fall damage for a drop height: 10 per meter beyond 3 m, capped at 50 (spec L175).</summary>
            public static float FallDamage(float dropMeters)
            {
                if (!(dropMeters > FallDamageThreshold))
                {
                    return 0f;
                }

                return Math.Min(FallDamageCap, (dropMeters - FallDamageThreshold) * FallDamagePerMeter);
            }
        }

        /// <summary>Boundary damage and contraction (spec §2 L48, L51; L674-L679).</summary>
        public static class Boundary
        {
            public const float Tier1DamagePerSecond = 15f;  // spec L51, first 2 s outside
            public const float Tier2DamagePerSecond = 30f;  // spec L51, from 2 s
            public const float Tier3DamagePerSecond = 60f;  // spec L51, after 4 s
            public const float Tier2StartSec = 2f;          // spec L51
            public const float Tier3StartSec = 4f;          // spec L51
            public const float EscalationResetInsideSec = 1f; // spec L51: returning inside resets after 1 s
            public const float EdgeImpactDamage = 5f;       // spec L51: forced displacement stopped at the edge
            public const bool BypassesMitigation = true;    // spec L51: shields, resistance, guard, healing reduction, invulnerability
            public const bool TriggersItemsOrAugments = false; // spec L51

            public const float ContractionStartSec = 55f;   // spec L48
            public const float ContractionEndSec = 85f;     // spec L48
            public const float FinalFloorDiameter = 8f;     // spec L48, L678
            public const float FinalFloorRadius = FinalFloorDiameter / 2f;
            public const float FinalCoverWarningSec = 83f;  // spec L678
            public const float FinalCoverRetireSec = 85f;   // spec L678

            /// <summary>Damage per second for a continuous time spent outside (spec L51).</summary>
            public static float DamagePerSecond(float secondsOutside)
            {
                if (secondsOutside < Tier2StartSec)
                {
                    return Tier1DamagePerSecond;
                }

                return secondsOutside < Tier3StartSec ? Tier2DamagePerSecond : Tier3DamagePerSecond;
            }
        }

        /// <summary>Round phases and duel resolution (spec §2 L44-L53, L678).</summary>
        public static class Round
        {
            public const float ShopSec = 35f;                       // spec L45
            public const float DraftSec = 30f;                      // spec L45, L227
            public const float AlcoveSec = 5f;                      // spec L46
            public const float CombatMaxSec = 90f;                  // spec L47
            public const float ContractionStartSec = Boundary.ContractionStartSec; // spec L48
            public const float ContractionEndSec = Boundary.ContractionEndSec;     // spec L48
            public const float FinalFloorDiameter = Boundary.FinalFloorDiameter;   // spec L48
            public const float ResultsSec = 8f;                     // spec L49
            public const float DrawThresholdFraction = 0.005f;      // spec L53: difference below 0.5 percentage points
            public const float JudgementIndicatorSec = 15f;         // spec L53: shown throughout the final 15 s
            public const float JudgementIndicatorStartSec = CombatMaxSec - JudgementIndicatorSec;
            public const float FinalCoverWarningSec = Boundary.FinalCoverWarningSec; // spec L678
            public const float FinalCoverRetireSec = Boundary.FinalCoverRetireSec;   // spec L678
            public const bool SurrenderAvoidsResolveLoss = false;   // spec L53
        }

        /// <summary>Crowns, shop, and selling (spec §5 L193-L212).</summary>
        public static class Economy
        {
            public const int FirstShopIncome = 600;      // spec L193
            public const int LaterShopIncome = 500;      // spec L193, each later scheduled or bracket shop
            public const int WinnerBonus = 100;          // spec L193, L61
            public const int TierCount = 4;              // spec L195-L200
            public const int Tier1Price = 200;           // spec L197
            public const int Tier2Price = 400;           // spec L198
            public const int Tier3Price = 600;           // spec L199
            public const int Tier4Price = 800;           // spec L200
            public const int ShopOffers = 3;             // spec L206
            public const int RerollsPerOffer = 1;        // spec L206
            public const int ReserveSlots = 3;           // spec L210
            public const float SellFraction = 0.5f;      // spec L212
            public const int UsedConsumableSellValue = 0; // spec L212
            public const int ConsumableCharges = 1;      // spec L212, L575
            public const int EnabledActiveItems = 1;     // spec L210, L575
            public const int EnabledConsumables = 1;     // spec L210
            public const int IssuedGearResaleValue = 0;  // spec L202
        }

        /// <summary>Free augment drafts (spec §5 L216-L233, L594).</summary>
        public static class Draft
        {
            public const int Candidates = 5;                    // spec L216-L223, L227
            public const int RerollsPerCandidate = 1;           // spec L229
            public const int ChoicesPerDraft = 1;               // spec L229
            public const int AugmentSlots = 6;                  // spec L231
            public const int MaxRank = 2;                       // spec L231
            public const int MinLegalAlternativesPerRarity = 10; // spec L233
            public const int GuaranteedTransformations = 1;     // spec L227, when one is eligible
            public const int GuaranteedBroadAlternatives = 2;   // spec L227, universal or broadly conditional
            public const int DraftsPerMatch = 6;                // spec L216-L225
        }

        /// <summary>Loadout composition (spec §6 L292, L296; L462).</summary>
        public static class Loadout
        {
            public const int SpellsPerWizard = 8;         // spec L292, L462
            public const int EquippedSpellSlots = 6;      // spec L292, L462: defaults to the first six
            public const int Signatures = 1;              // spec L292
            public const int Passives = 1;                // spec L292
        }

        /// <summary>Match structure for Conclave and Duel (spec §2 L34-L71, L391).</summary>
        public static class Match
        {
            public const int LobbySize = 8;                   // spec L34
            public const int StartingResolve = 8;             // spec L36
            public const int RegularRounds = 12;              // spec L36, L38
            public const int MaxBracketRounds = 3;            // spec L38
            public const int BracketDrawReplays = 1;          // spec L38: a second draw records a shared finish
            public const int PairingRepeatWindow = 2;         // spec L59: previous two opponents
            public const int MaxConsecutiveSameSpawnSide = 2; // spec L59
            public const float ReconnectWindowSec = 90f;      // spec L55
            public const int TargetMatchMinutesMin = 14;      // spec L55
            public const int TargetMatchMinutesMax = 24;      // spec L55
            public const int FullScheduleMinutesApprox = 39;  // spec L55

            public const int DuelBestOf = 7;                  // spec L65
            public const float DuelWinsToClinch = 4f;         // spec L65: best of seven (half wins are possible)
            public const int DuelReplaysBeforeHalfWin = 2;    // spec L65
            public const float DuelHalfWin = 0.5f;            // spec L65
            public const int DuelDeciderAttempts = 2;         // spec L65: two drawn deciders record a match draw

            public const int CustomMinPlayers = 2;            // spec L71
            public const int CustomMaxPlayers = 8;            // spec L71
            public const float SpectatorDelayMinSec = 15f;    // spec L391
            public const int BotCompetenceLevels = 4;         // spec L381
        }

        /// <summary>Procedural arena constraints (spec §7 L306-L323; L660-L679).</summary>
        public static class Arena
        {
            public const int DiameterSmall = 24;              // spec L306
            public const int DiameterMedium = 32;             // spec L306
            public const int DiameterLarge = 40;              // spec L306
            public const int DiameterHuge = 48;               // spec L306
            public const int DiameterSetRounds = 4;           // spec L306: one of each diameter per consecutive four rounds
            public const float UsableAreaTolerance = 0.10f;   // spec L306

            public const float SpawnPathMin = 16f;            // spec L310, shortest spawn-to-spawn path by size
            public const float SpawnPathMax = 34f;            // spec L310
            public const float CentralReachMinSec = 2.0f;     // spec L310, mobile melee build to central cover
            public const float CentralReachMaxSec = 3.5f;     // spec L310
            public const int MinApproachesInterruptingLane = 2; // spec L310
            public const float FiringLaneMin = 12f;           // spec L310, longest uninterrupted lane
            public const float FiringLaneMax = 28f;           // spec L310
            public const float CoverFractionMin = 0.18f;      // spec L310, of usable floor area
            public const float CoverFractionMax = 0.30f;      // spec L310

            public const float ElevationMax = 3f;             // spec L312, ordinary routes 0-3 m
            public const float VantageHeight = 4f;            // spec L312, optional platforms
            public const int VantageAccessRoutes = 2;         // spec L312, plus a safe descent
            public const float RampMaxSlopeDeg = 30f;         // spec L312
            public const float MinRouteWidth = 2.5f;          // spec L312

            public const int MaxRetrySeeds = 8;               // spec L323, then a prevalidated fallback

            public const int CenterModules = 1;               // spec L660
            public const int SpawnAlcoves = 2;                // spec L308, L660
            public const int ApproachChains = 2;              // spec L308, L660 (mirrored side approaches)
            public const int FiringLanes = 1;                 // spec L308, L660
            public const int MinCrossoverRoutes = 2;          // spec L308, L660
            public const int ElevationModulesMin = 2;         // spec L660
            public const int ElevationModulesMax = 4;         // spec L660
            public const int CoverModulesMin = 3;             // spec L660
            public const int CoverModulesMax = 6;             // spec L660

            public const int PropDefinitionsMin = 3;          // spec L662
            public const int PropDefinitionsMax = 8;          // spec L662
            public const int PropInstancesPerDefinition = 2;  // spec L662, ordinarily, for parity
            public const int MaxPropInstances = 48;           // spec L662, L421
            public const int MaxNaturalSourcePatches = 8;     // spec L668, at combat start

            public const float FinalFloorDiameter = Boundary.FinalFloorDiameter; // spec L48, L678
        }

        /// <summary>Networking and simulation budgets (spec §14 L419-L423; L55).</summary>
        public static class Network
        {
            public const int TicksPerSecond = 60;             // spec L421
            public const int SnapshotRateMin = 20;            // spec L421, Hz
            public const int SnapshotRateMax = 30;            // spec L421, Hz
            public const int DefaultSnapshotRate = 30;        // docs/DECISIONS.md D-005
            public const int LagCompensationMaxMs = 150;      // spec L423
            public const float ReconnectWindowSec = Match.ReconnectWindowSec; // spec L55

            public const int TargetFrameRate = 120;           // spec L421, competitive preset target
            public const int MinFrameRate = 60;               // spec L421

            // Worst-case duel budget (L421).
            public const int BudgetEquippedSpellSlots = 12;   // spec L421, both players
            public const int BudgetPlayerFields = 6;          // spec L421, total
            public const int BudgetSummons = 4;               // spec L421, total
            public const int BudgetCellRegions = World.MaxActiveCellRegions; // spec L421
            public const int BudgetProps = Arena.MaxPropInstances;           // spec L421
        }

        /// <summary>Client options with rule-level bounds (spec §9 L371, §10 L383).</summary>
        public static class Interface
        {
            public const int FieldOfViewMin = 80;   // spec L371
            public const int FieldOfViewMax = 110;  // spec L371
            public const int MaxSavedBuildPlans = 100; // spec L383
        }
    }
}
