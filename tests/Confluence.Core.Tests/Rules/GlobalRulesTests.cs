using System;
using System.Linq;
using System.Reflection;
using Confluence.Core.Rules;

namespace Confluence.Core.Tests.Rules
{
    public class GlobalRulesTests
    {
        [Fact]
        public void Core_Combat_Numbers()
        {
            Assert.Equal(300f, GlobalRules.Resources.BaseHealth);
            Assert.Equal(100f, GlobalRules.Resources.BaseMana);
            Assert.Equal(100f, GlobalRules.Resources.BaseStamina);
            Assert.Equal(450f, GlobalRules.Resources.MaxHealthCap);
            Assert.Equal(14f, GlobalRules.Resources.ManaRegenCapPerSecond);
            Assert.Equal(5.5f, GlobalRules.Movement.WalkSpeed);
            Assert.Equal(7f, GlobalRules.Movement.SprintSpeed);
            Assert.Equal(0.22f, GlobalRules.Movement.DodgeDurationSec);
            Assert.Equal(0.8f, GlobalRules.Guard.MaxHoldSec);
            Assert.Equal(0.12f, GlobalRules.Guard.PerfectWindowSec);
            Assert.Equal(0.35f, GlobalRules.Damage.ChannelResistanceCap);
            Assert.Equal(0.45f, GlobalRules.Damage.PassiveReductionCap);
            Assert.Equal(-0.15f, GlobalRules.Damage.NegativeResistanceFloor);
            Assert.Equal(100f, GlobalRules.Shields.Cap);
            Assert.Equal(8f, GlobalRules.Shields.MaxTimedDurationSec);
            Assert.Equal(35f, GlobalRules.Healing.MaxPerSecond);
            Assert.Equal(120f, GlobalRules.Healing.MaxPerDuel);
            Assert.Equal(0.10f, GlobalRules.Healing.LifestealFractionCap);
            Assert.Equal(20f, GlobalRules.Healing.LifestealMaxPerSecond);
            Assert.Equal(0.75f, GlobalRules.Control.HardControlMaxSec);
            Assert.Equal(0.6f, GlobalRules.Control.SilenceMaxSec);
            Assert.Equal(2f, GlobalRules.Control.HardControlImmunitySec);
            Assert.Equal(0.30f, GlobalRules.Control.SlowCap);
            Assert.Equal(3f, GlobalRules.Control.SlowMaxDurationSec);
            Assert.Equal(2f, GlobalRules.Statuses.RevealMaxSec);
            Assert.Equal(1.5f, GlobalRules.Statuses.InvisibilityMaxSec);
            Assert.Equal(4f, GlobalRules.Statuses.InvisibilitySilhouetteRadius);
        }

        [Fact]
        public void Modifier_Caps_And_Budgets()
        {
            Assert.Equal(0.20f, GlobalRules.Modifiers.MovementBonusCap);
            Assert.Equal(2, GlobalRules.Modifiers.MaxDashCharges);
            Assert.Equal(8f, GlobalRules.Modifiers.MaxDashDistance);
            Assert.Equal(10f, GlobalRules.Modifiers.MaxTeleportDistance);
            Assert.Equal(0.25f, GlobalRules.Modifiers.CooldownReductionCap);
            Assert.Equal(0.5f, GlobalRules.Modifiers.MinRechargeSec);
            Assert.Equal(0.15f, GlobalRules.Modifiers.MasteryAttributeCap);
            Assert.Equal(0.20f, GlobalRules.Modifiers.MasteryEfficiencyCap);
            Assert.Equal(1f, GlobalRules.Modifiers.MinPaidCostFraction + GlobalRules.Modifiers.CostReductionCap, 6);
            Assert.Equal(0.25f, GlobalRules.Modifiers.RefundCapFraction);
            Assert.Equal(GlobalRules.Casting.RefundCapFraction, GlobalRules.Modifiers.RefundCapFraction);
            Assert.Equal(0.5f, GlobalRules.Triggers.ProcInternalCooldownSec);
            Assert.Equal(3, GlobalRules.Triggers.ChainMaxJumps);
            Assert.Equal(6f, GlobalRules.Triggers.ChainMaxJumpDistance);
            Assert.Equal(3, GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer);
            Assert.Equal(2, GlobalRules.FieldsAndSummons.MaxSummonsPerPlayer);
            Assert.Equal(8f, GlobalRules.FieldsAndSummons.MaxLifetimeSec);

            // The worst-case duel budget (spec L421) is the per-player caps for two players.
            Assert.Equal(2 * GlobalRules.Loadout.EquippedSpellSlots, GlobalRules.Network.BudgetEquippedSpellSlots);
            Assert.Equal(2 * GlobalRules.FieldsAndSummons.MaxFieldsPerPlayer, GlobalRules.Network.BudgetPlayerFields);
            Assert.Equal(2 * GlobalRules.FieldsAndSummons.MaxSummonsPerPlayer, GlobalRules.Network.BudgetSummons);
            Assert.Equal(128, GlobalRules.Network.BudgetCellRegions);
            Assert.Equal(48, GlobalRules.Network.BudgetProps);
        }

        [Fact]
        public void Reaction_Table_Numbers()
        {
            Assert.Equal(0.5f, GlobalRules.World.CellSize);
            Assert.Equal(4, GlobalRules.World.MaxUnitsPerCell);
            Assert.Equal(3f, GlobalRules.World.SteamLifetimeSec);
            Assert.Equal(2f, GlobalRules.World.SteamRise);
            Assert.Equal(2, GlobalRules.World.IceFrostConsumed);
            Assert.Equal(45f, GlobalRules.World.IceTileDurability);
            Assert.Equal(5f, GlobalRules.World.IceLifetimeSec);
            Assert.Equal(20f, GlobalRules.World.ShatterMinDirectDamage);
            Assert.Equal(10f, GlobalRules.World.ShardDamage);
            Assert.Equal(0.6f, GlobalRules.World.WetChargeDurationSec);
            Assert.Equal(12, GlobalRules.World.WetChargeMaxCells);
            Assert.Equal(3, GlobalRules.World.MetalMaxBranches);
            Assert.Equal(3f, GlobalRules.World.WindTranslateMaxSpeed);
            Assert.Equal(0.70f, GlobalRules.World.PrismRetainedDamageFraction);
            Assert.Equal(0.5f, GlobalRules.World.VenomDilutionFactor);
            Assert.Equal(20f, GlobalRules.World.GravityMaxDeflectionDeg);
            Assert.Equal(2f, GlobalRules.World.SteamSightBlockPathLength);
        }

        [Theory]
        [InlineData(0, false, false)]
        [InlineData(1, false, false)] // spec L151 "above strength 1": strength-1 sources move clouds only by their own text (D-023)
        [InlineData(2, true, true)]
        [InlineData(4, true, true)]
        public void Wind_Moves_Clouds_Only_Above_Strength_One(int strength, bool translates, bool disperses)
        {
            Assert.Equal(translates, GlobalRules.World.WindTranslatesClouds(strength));
            Assert.Equal(disperses, GlobalRules.World.WindDispersesClouds(strength));
        }

        [Fact]
        public void Reactions_Process_In_The_Fixed_Order()
        {
            // Spec L165: direct impacts, heat-water/heat-ice/frost-water consumption, electricity connectivity,
            // wind movement, then decay.
            Assert.Equal(new[]
            {
                ReactionPhase.DirectImpacts,
                ReactionPhase.MaterialConsumption,
                ReactionPhase.ElectricityConnectivity,
                ReactionPhase.WindMovement,
                ReactionPhase.Decay,
            }, GlobalRules.World.ReactionOrder);
            Assert.Equal(Enum.GetValues(typeof(ReactionPhase)).Length, GlobalRules.World.ReactionOrder.Count);
        }

        [Fact]
        public void Movement_Decisions_Support_The_Arena_And_Reaction_Rules()
        {
            // D-024 derivations: the common jump outlasts the 0.6 s wet charge it counters ("jump the short pulse",
            // spec L148), reaches the 1 m ledges and gaps modules use, never causes fall damage on flat ground, and air
            // steering cannot reverse a full-speed run within one jump (spec L80).
            float launch = GlobalRules.Movement.JumpLaunchSpeed;
            Assert.Equal(MathF.Sqrt(2f * 18f * 1.1f), launch, 5);
            Assert.True(GlobalRules.Movement.JumpAirtimeSec > GlobalRules.World.WetChargeDurationSec);
            Assert.True(GlobalRules.Movement.JumpApexHeight > GlobalRules.Movement.MaxCommonJumpLedge);
            Assert.True(GlobalRules.Movement.JumpApexHeight < GlobalRules.Terrain.FallDamageThreshold);
            Assert.True(GlobalRules.Movement.WalkSpeed * GlobalRules.Movement.JumpAirtimeSec > 2f * GlobalRules.Movement.MaxCommonJumpGap);
            Assert.True(GlobalRules.Movement.AirAcceleration * GlobalRules.Movement.JumpAirtimeSec < 2f * GlobalRules.Movement.WalkSpeed);
            Assert.True(GlobalRules.Movement.CrouchHeight < GlobalRules.Movement.StandingHeight);
            Assert.True(GlobalRules.Movement.StandingEyeHeight < GlobalRules.Movement.StandingHeight);
            Assert.True(GlobalRules.Movement.CrouchSpeed < GlobalRules.Movement.WalkSpeed);
            // Two players pass each other on the narrowest route (spec L312).
            Assert.True(4f * GlobalRules.Movement.CapsuleRadius < GlobalRules.Arena.MinRouteWidth);
            Assert.InRange(GlobalRules.Terrain.PropDamageImpactSpeedMin, 0.01f, GlobalRules.Terrain.DamagingPropVelocityCap);
        }

        [Fact]
        public void Roster_Difficulty_Range()
        {
            Assert.Equal(36, GlobalRules.Roster.WizardCount);
            Assert.Equal(1, GlobalRules.Roster.DifficultyMin); // spec L464
            Assert.Equal(5, GlobalRules.Roster.DifficultyMax);
            Assert.Equal(8, GlobalRules.Loadout.EquipmentSlots); // spec L210
            Assert.Equal(8, Enum.GetValues(typeof(EquipmentSlot)).Length);
        }

        [Fact]
        public void Terrain_Helpers()
        {
            Assert.Equal(10f, GlobalRules.Terrain.PropMassValue(PropMass.Light));
            Assert.Equal(30f, GlobalRules.Terrain.PropMassValue(PropMass.Medium));
            Assert.Equal(80f, GlobalRules.Terrain.PropMassValue(PropMass.Heavy));
            Assert.True(float.IsPositiveInfinity(GlobalRules.Terrain.PropMassValue(PropMass.Anchored)));

            Assert.Equal(4f, GlobalRules.Terrain.PushDistance(PropMass.Light));
            Assert.Equal(2f, GlobalRules.Terrain.PushDistance(PropMass.Medium));
            Assert.Equal(0.5f, GlobalRules.Terrain.PushDistance(PropMass.Heavy));
            Assert.Equal(0f, GlobalRules.Terrain.PushDistance(PropMass.Anchored));

            Assert.Equal(5f, GlobalRules.Terrain.CollisionDamage(PropMass.Light));
            Assert.Equal(15f, GlobalRules.Terrain.CollisionDamage(PropMass.Medium));
            Assert.Equal(25f, GlobalRules.Terrain.CollisionDamage(PropMass.Heavy));
            Assert.Equal(0f, GlobalRules.Terrain.CollisionDamage(PropMass.Anchored));
            Assert.Equal(GlobalRules.World.PropCollisionDamageCap, GlobalRules.Terrain.HeavyPropCollisionDamage);
        }

        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(3f, 0f)]
        [InlineData(3.5f, 5f)]
        [InlineData(4f, 10f)]
        [InlineData(7f, 40f)]
        [InlineData(8f, 50f)]
        [InlineData(30f, 50f)]
        [InlineData(float.NaN, 0f)]
        public void Fall_Damage(float drop, float expected)
        {
            // Spec L175: begins above 3 m, 10 per excess meter, capped at 50.
            Assert.Equal(expected, GlobalRules.Terrain.FallDamage(drop), 4);
        }

        [Theory]
        [InlineData(0f, 15f)]
        [InlineData(1.99f, 15f)]
        [InlineData(2f, 30f)]
        [InlineData(3.99f, 30f)]
        [InlineData(4f, 60f)]
        [InlineData(20f, 60f)]
        public void Boundary_Damage_Escalation(float secondsOutside, float expected)
        {
            // Spec L51: 15/s for the first 2 s, then 30/s, then 60/s after 4 s.
            Assert.Equal(expected, GlobalRules.Boundary.DamagePerSecond(secondsOutside));
        }

        [Fact]
        public void Round_Timeline_Is_Consistent()
        {
            Assert.Equal(35f, GlobalRules.Round.ShopSec);
            Assert.Equal(30f, GlobalRules.Round.DraftSec);
            Assert.Equal(5f, GlobalRules.Round.AlcoveSec);
            Assert.Equal(90f, GlobalRules.Round.CombatMaxSec);
            Assert.Equal(55f, GlobalRules.Round.ContractionStartSec);
            Assert.Equal(85f, GlobalRules.Round.ContractionEndSec);
            Assert.Equal(8f, GlobalRules.Round.ResultsSec);
            Assert.Equal(0.005f, GlobalRules.Round.DrawThresholdFraction);
            Assert.Equal(75f, GlobalRules.Round.JudgementIndicatorStartSec);
            Assert.Equal(83f, GlobalRules.Round.FinalCoverWarningSec);
            Assert.Equal(85f, GlobalRules.Round.FinalCoverRetireSec);
            Assert.Equal(4f, GlobalRules.Boundary.FinalFloorRadius);
            Assert.True(GlobalRules.Round.ContractionEndSec < GlobalRules.Round.CombatMaxSec);
            Assert.True(GlobalRules.Round.FinalCoverWarningSec < GlobalRules.Round.FinalCoverRetireSec);
            Assert.Equal(GlobalRules.Round.ContractionEndSec, GlobalRules.Round.FinalCoverRetireSec);
        }

        [Fact]
        public void Economy_Draft_Match_Arena_Network()
        {
            Assert.Equal(600, GlobalRules.Economy.FirstShopIncome);
            Assert.Equal(500, GlobalRules.Economy.LaterShopIncome);
            Assert.Equal(100, GlobalRules.Economy.WinnerBonus);
            Assert.Equal(0.5f, GlobalRules.Economy.SellFraction);
            Assert.Equal(3, GlobalRules.Economy.ShopOffers);
            Assert.Equal(1, GlobalRules.Economy.RerollsPerOffer);
            Assert.Equal(3, GlobalRules.Economy.ReserveSlots);

            Assert.Equal(5, GlobalRules.Draft.Candidates);
            Assert.Equal(1, GlobalRules.Draft.RerollsPerCandidate);
            Assert.Equal(6, GlobalRules.Draft.AugmentSlots);
            Assert.Equal(2, GlobalRules.Draft.MaxRank);
            Assert.Equal(10, GlobalRules.Draft.MinLegalAlternativesPerRarity);
            // The floor covers five opening cards plus five distinct rerolls (spec L233).
            Assert.Equal(GlobalRules.Draft.Candidates * (1 + GlobalRules.Draft.RerollsPerCandidate), GlobalRules.Draft.MinLegalAlternativesPerRarity);

            Assert.Equal(8, GlobalRules.Match.LobbySize);
            Assert.Equal(8, GlobalRules.Match.StartingResolve);
            Assert.Equal(12, GlobalRules.Match.RegularRounds);
            Assert.Equal(3, GlobalRules.Match.MaxBracketRounds);
            Assert.Equal(90f, GlobalRules.Match.ReconnectWindowSec);
            // Three bracket rounds resolve at most eight survivors (2^3).
            Assert.True(1 << GlobalRules.Match.MaxBracketRounds >= GlobalRules.Match.LobbySize);

            Assert.Equal(16f, GlobalRules.Arena.SpawnPathMin);
            Assert.Equal(34f, GlobalRules.Arena.SpawnPathMax);
            Assert.Equal(12f, GlobalRules.Arena.FiringLaneMin);
            Assert.Equal(28f, GlobalRules.Arena.FiringLaneMax);
            Assert.Equal(0.18f, GlobalRules.Arena.CoverFractionMin);
            Assert.Equal(0.30f, GlobalRules.Arena.CoverFractionMax);
            Assert.Equal(30f, GlobalRules.Arena.RampMaxSlopeDeg);
            Assert.Equal(2.5f, GlobalRules.Arena.MinRouteWidth);
            Assert.Equal(48, GlobalRules.Arena.MaxPropInstances);
            Assert.Equal(8, GlobalRules.Arena.MaxNaturalSourcePatches);
            Assert.Equal(8, GlobalRules.Arena.MaxRetrySeeds);
            Assert.Equal(2.0f, GlobalRules.Arena.CentralReachMinSec);
            Assert.Equal(3.5f, GlobalRules.Arena.CentralReachMaxSec);
            // Eight prop definitions in pairs never exceed the instance cap.
            Assert.True(GlobalRules.Arena.PropDefinitionsMax * GlobalRules.Arena.PropInstancesPerDefinition <= GlobalRules.Arena.MaxPropInstances);

            Assert.Equal(60, GlobalRules.Network.TicksPerSecond);
            Assert.Equal(Confluence.Core.Time.Ticks.PerSecond, GlobalRules.Network.TicksPerSecond);
            Assert.InRange(GlobalRules.Network.DefaultSnapshotRate, GlobalRules.Network.SnapshotRateMin, GlobalRules.Network.SnapshotRateMax);
            Assert.Equal(150, GlobalRules.Network.LagCompensationMaxMs);
        }

        [Fact]
        public void Every_Numeric_Constant_Is_Finite()
        {
            var fields = typeof(GlobalRules).GetNestedTypes(BindingFlags.Public)
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
                .Where(f => f.IsLiteral)
                .ToList();
            Assert.True(fields.Count > 200, $"only {fields.Count} constants");
            foreach (FieldInfo field in fields)
            {
                object value = field.GetValue(null);
                if (value is float f)
                {
                    Assert.False(float.IsNaN(f) || float.IsInfinity(f), field.Name);
                    if (field.Name != nameof(GlobalRules.Damage.NegativeResistanceFloor))
                    {
                        Assert.True(f >= 0f, $"{field.DeclaringType.Name}.{field.Name} is negative");
                    }
                }
                else if (value is int i)
                {
                    Assert.True(i >= 0, $"{field.DeclaringType.Name}.{field.Name} is negative");
                }
            }
        }

        [Fact]
        public void Melee_Profiles_Match_Spec()
        {
            // Spec L100-L102.
            AssertProfile(MeleeProfile.Focus, 18f, 1.6f, 0.8f, 8f, 0.18f);
            AssertProfile(MeleeProfile.LightBlade, 28f, 1.8f, 0.8f, 12f, 0.20f);
            AssertProfile(MeleeProfile.Heavy, 42f, 2.2f, 1.25f, 20f, 0.35f);
            AssertProfile(MeleeProfile.Spear, 32f, 2.8f, 1.05f, 16f, 0.25f);

            Assert.Equal(0.62f, MeleeProfile.Focus.Recovery, 5);
            Assert.Equal(0.60f, MeleeProfile.LightBlade.Recovery, 5);
            Assert.Equal(0.90f, MeleeProfile.Heavy.Recovery, 5);
            Assert.Equal(0.80f, MeleeProfile.Spear.Recovery, 5);

            Assert.Equal(11, MeleeProfile.Focus.WindupTicks);
            Assert.Equal(48, MeleeProfile.Focus.CycleTicks);
            Assert.Equal(37, MeleeProfile.Focus.RecoveryTicks);
            Assert.Equal(21, MeleeProfile.Heavy.WindupTicks);
            Assert.Equal(75, MeleeProfile.Heavy.CycleTicks);
        }

        [Fact]
        public void Melee_Default_Profile_By_Category()
        {
            // Spec L100: weapons default to the light blade; wands and staffs use the focus strike.
            Assert.Equal(MeleeProfile.LightBlade, MeleeProfile.DefaultFor(EquipmentCategory.Weapon));
            Assert.Equal(MeleeProfile.Focus, MeleeProfile.DefaultFor(EquipmentCategory.Wand));
            Assert.Equal(MeleeProfile.Focus, MeleeProfile.DefaultFor(EquipmentCategory.Staff));
            Assert.NotEqual(MeleeProfile.Heavy, MeleeProfile.Spear);
            Assert.Equal("Spear", MeleeProfile.Spear.ToString());
        }

        private static void AssertProfile(MeleeProfile p, float damage, float reach, float cycle, float stamina, float windup)
        {
            Assert.Equal(damage, p.Damage);
            Assert.Equal(reach, p.Reach);
            Assert.Equal(cycle, p.Cycle);
            Assert.Equal(stamina, p.Stamina);
            Assert.Equal(windup, p.Windup);
            Assert.True(p.Recovery > 0f);
        }
    }
}
