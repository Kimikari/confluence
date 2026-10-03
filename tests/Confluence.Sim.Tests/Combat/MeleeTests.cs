using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using Confluence.Sim.Input;
using InputButtons = Confluence.Casting.InputButtons;
using Confluence.Sim.Players;
using Confluence.Sim.World;

namespace Confluence.Sim.Tests.Combat
{
    /// <summary>Common melee, equip, and swap rules (spec L100-L106).</summary>
    public class MeleeTests
    {
        private static readonly PlayerInput Strike = TestWorlds.A.With(InputButtons.Melee);

        private static DuelPlayerSetup Wielding(EquipmentCategory category, string tag, EquipmentSlot slot = EquipmentSlot.MainFocus)
        {
            DuelPlayerSetup setup = TestWorlds.Setup("A");
            setup.Equipment.Add(new EquippedItem("EQP-T-" + tag, category, slot, new[] { tag }));
            return setup;
        }

        // Attacker 0 at the origin facing +Z; target 1 at distance d on the +Z axis facing back.
        private static DuelWorld Facing(float distance, DuelPlayerSetup attacker = null)
        {
            DuelWorld w = TestWorlds.Duel(a: attacker);
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            TestWorlds.Place(w, 1, new Vec3(0f, 0f, distance), 180f);
            return w;
        }

        private static List<DamageEvent> MeleeDamage(DuelWorld w)
        {
            var hits = new List<DamageEvent>();
            foreach (DuelEvent e in w.EventLog)
            {
                if (e is DamageEvent d && d.Kind == DamageKind.Melee)
                {
                    hits.Add(d);
                }
            }

            return hits;
        }

        private static void SwingOnce(DuelWorld w, PlayerInput strike, PlayerInput idle, int cycleTicks)
        {
            w.Step(strike, TestWorlds.B);
            TestWorlds.Run(w, cycleTicks + 2, idle, TestWorlds.B);
        }

        public static IEnumerable<object[]> Profiles()
        {
            yield return new object[] { EquipmentCategory.Wand, "wand", MeleeProfile.Focus };
            yield return new object[] { EquipmentCategory.Weapon, "light_blade", MeleeProfile.LightBlade };
            yield return new object[] { EquipmentCategory.Weapon, "heavy", MeleeProfile.Heavy };
            yield return new object[] { EquipmentCategory.Weapon, "spear", MeleeProfile.Spear };
        }

        [Theory]
        [MemberData(nameof(Profiles))]
        public void Each_Profile_Hits_Within_Its_Reach_After_Its_Windup_For_Its_Damage_And_Stamina(EquipmentCategory category, string tag, MeleeProfile expected)
        {
            DuelPlayerSetup setup = Wielding(category, tag);
            Assert.Equal(expected, setup.MeleeProfileFor(EquipmentSlot.MainFocus));

            float radius = GlobalRules.Movement.CapsuleRadius;
            DuelWorld near = Facing(expected.Reach + radius - 0.1f, setup);
            near.Step(Strike, TestWorlds.B);
            int start = near.Tick;
            Assert.Equal(100f - expected.Stamina, near.Player(0).Resources.Stamina, 3);
            TestWorlds.Run(near, expected.CycleTicks + 2, TestWorlds.A, TestWorlds.B);
            List<DamageEvent> hits = MeleeDamage(near);
            Assert.Single(hits); // one damage per opponent per attack
            // Never before the windup ends; a slash reaches a centered target part-way through its sweep.
            Assert.InRange(hits[0].Tick, start + expected.WindupTicks, start + expected.WindupTicks + SimRules.Melee.StrikeActiveTicks - 1);
            if (MeleeProfiles.ArcHalfDeg(expected) == 0f)
            {
                Assert.Equal(start + expected.WindupTicks, hits[0].Tick); // thrusts land the moment the windup ends
            }

            Assert.Equal(expected.Damage, hits[0].Resolution.HealthLost, 3);
            Assert.Equal("EQP-T-" + tag, hits[0].SourceId);

            DuelWorld far = Facing(expected.Reach + radius + 0.15f, Wielding(category, tag));
            SwingOnce(far, Strike, TestWorlds.A, expected.CycleTicks);
            Assert.Empty(MeleeDamage(far));
        }

        [Fact]
        public void Unspecified_Weapons_Use_The_Light_Blade_Profile_And_Empty_Slots_The_Focus_Profile()
        {
            Assert.Equal(MeleeProfile.LightBlade, MeleeProfiles.For(EquipmentCategory.Weapon, new[] { "melee" }));
            Assert.Equal(MeleeProfile.Focus, MeleeProfiles.For(EquipmentCategory.Staff, new[] { "heavy" }));
            Assert.Equal(MeleeProfile.Focus, TestWorlds.Setup().MeleeProfileFor(EquipmentSlot.MainFocus));
            Assert.Equal(DamageSources.CommonMelee, TestWorlds.Setup().MeleeSourceFor(EquipmentSlot.MainFocus));
        }

        [Fact]
        public void Catalogue_Weapons_Pick_Their_Profile_From_Their_Category()
        {
            Catalogue cat = Catalogue.Load(Catalogue.FindDefaultPath());
            var equipment = new Dictionary<string, EquipmentSlot>
            {
                { "EQP-035", EquipmentSlot.MainFocus }, // Basalt Maul: heavy
                { "EQP-034", EquipmentSlot.Sidearm },   // Gutter Pike: spear
            };
            DuelPlayerSetup setup = DuelPlayerSetup.FromCatalogue(cat, "A", "WIZ-002", equipment: equipment);
            Assert.Equal(6, setup.EquippedSpellIds.Count);
            Assert.Equal(MeleeProfile.Heavy, setup.MeleeProfileFor(EquipmentSlot.MainFocus));
            Assert.Equal(MeleeProfile.Spear, setup.MeleeProfileFor(EquipmentSlot.Sidearm));
            Assert.Equal(MeleeProfile.LightBlade, EquippedItem.FromDefinition(cat.Equipment("EQP-031"), EquipmentSlot.MainFocus).MeleeProfile);
            Assert.Equal(MeleeProfile.Focus, EquippedItem.FromDefinition(cat.Equipment("EQP-001"), EquipmentSlot.MainFocus).MeleeProfile);
        }

        [Fact]
        public void Strike_Keeps_The_Committed_Aim_Without_Tracking()
        {
            // A spear thrust committed toward +Z; the target stands at +X. Turning toward it during the windup misses.
            DuelWorld w = TestWorlds.Duel(a: Wielding(EquipmentCategory.Weapon, "spear"));
            TestWorlds.Place(w, 0, Vec3.Zero, 0f);
            TestWorlds.Place(w, 1, new Vec3(2f, 0f, 0f), 270f);
            SwingOnce(w, Strike, TestWorlds.Face(90f), MeleeProfile.Spear.CycleTicks);
            Assert.Empty(MeleeDamage(w));

            // Committed toward the target; looking away during the windup still hits along the committed line.
            DuelWorld v = TestWorlds.Duel(a: Wielding(EquipmentCategory.Weapon, "spear"));
            TestWorlds.Place(v, 0, Vec3.Zero, 0f);
            TestWorlds.Place(v, 1, new Vec3(0f, 0f, 2f), 180f);
            SwingOnce(v, Strike, TestWorlds.Face(90f), MeleeProfile.Spear.CycleTicks);
            Assert.Single(MeleeDamage(v));
        }

        [Fact]
        public void Sprinting_Disallows_Normal_Melee()
        {
            DuelWorld w = Facing(1.5f);
            PlayerInput sprintStrike = TestWorlds.A.Moving(0f, 1f).With(InputButtons.Sprint | InputButtons.Melee);
            w.Step(sprintStrike, TestWorlds.B);
            Assert.True(w.Player(0).Motor.Sprinting);
            Assert.False(w.Player(0).Melee.Busy);
            Assert.Equal(100f - 0.2f, w.Player(0).Resources.Stamina, 2); // only the sprint tick was paid
        }

        [Fact]
        public void A_Strike_Contacts_Props_And_Cover_Blocks_It()
        {
            DuelWorld w = Facing(1.8f, Wielding(EquipmentCategory.Weapon, "heavy"));
            int wall = w.AddTemporarySolid(new TemporarySolidSpec
            {
                Box = YawBox.OnFloor(new Vec3(0f, 0f, 1.0f), new Vec3(3f, 2.5f, 0.3f), 0f),
                Kind = Confluence.Core.Arena.SolidKind.Wall,
                BlocksSight = true,
                Durability = 100f,
                OwnerPlayer = -1,
                SourceId = "TEST",
            });
            SwingOnce(w, Strike, TestWorlds.A, MeleeProfile.Heavy.CycleTicks);
            Assert.Empty(MeleeDamage(w));
            int contacts = 0;
            foreach (DuelEvent e in w.EventLog)
            {
                if (e is MeleeEvent m && m.Change == MeleeChange.ContactedSolid)
                {
                    contacts++;
                    Assert.Equal(wall, m.SolidId);
                }
            }

            Assert.Equal(1, contacts);
            Assert.Equal(100f - MeleeProfile.Heavy.Damage, w.Collision.Find(wall).Durability, 3);
        }

        [Fact]
        public void Equip_Swap_Takes_0_35_Seconds_And_Cannot_Cancel_Recovery()
        {
            DuelPlayerSetup setup = Wielding(EquipmentCategory.Weapon, "spear", EquipmentSlot.Sidearm);
            DuelWorld w = Facing(5f, setup);
            w.Step(Strike, TestWorlds.B);
            w.Step(TestWorlds.A.With(InputButtons.SwapFocus), TestWorlds.B);
            Assert.False(w.Player(0).IsSwapping); // recovery cannot be cancelled by a swap
            TestWorlds.Run(w, MeleeProfile.Focus.CycleTicks, TestWorlds.A, TestWorlds.B);
            Assert.False(w.Player(0).Melee.Busy);

            w.Step(TestWorlds.A.With(InputButtons.SwapFocus), TestWorlds.B);
            int started = w.Tick;
            Assert.True(w.Player(0).IsSwapping);
            while (w.Player(0).IsSwapping)
            {
                w.Step(TestWorlds.A, TestWorlds.B);
            }

            Assert.Equal(started + 21, w.Tick);
            Assert.Equal(EquipmentSlot.Sidearm, w.Player(0).ActiveWeapon);
            Assert.Equal(MeleeProfile.Spear, w.Player(0).CurrentMeleeProfile);
        }

        [Fact]
        public void Changing_The_Selected_Spell_Has_No_Windup()
        {
            DuelWorld w = TestWorlds.Duel();
            PlayerInput select = TestWorlds.A;
            select.SelectedSlot = 4;
            w.Step(select, TestWorlds.B);
            Assert.Equal(4, w.Player(0).SelectedSlot);
            Assert.False(w.Player(0).IsSwapping);
            select.SelectedSlot = 9; // out of range: ignored
            w.Step(select, TestWorlds.B);
            Assert.Equal(4, w.Player(0).SelectedSlot);
        }

        [Fact]
        public void Melee_Counts_As_A_Direct_Hit_But_Not_A_Spell_Hit()
        {
            DamageRequest melee = DamageRequest.Single(0, 1, DamageKind.Melee, "EQP-031", DamageChannel.Physical, 28f);
            Assert.True(melee.IsDirectHit);
            Assert.True(melee.CanTriggerEffects);
            Assert.False(melee.IsSpellHit);
        }
    }
}
