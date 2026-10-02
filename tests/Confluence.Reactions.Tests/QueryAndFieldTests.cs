using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Queries;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions.Tests
{
    /// <summary>
    /// Force and loose props (spec L155, L173), sound and cover (L156), light and prisms (L157), shadow and bright
    /// light (L158), venom and water (L159), gravity and projectiles (L160), arcane dispel (L161).
    /// </summary>
    public class QueryAndFieldTests
    {
        [Theory]
        [InlineData(PropMass.Light, 4f, 5f)]
        [InlineData(PropMass.Medium, 2f, 15f)]
        [InlineData(PropMass.Heavy, 0.5f, 25f)]
        [InlineData(PropMass.Anchored, 0f, 25f)]
        public void Force_On_A_Loose_Prop_Emits_An_Impulse_Clamped_By_Mass_And_Speed(PropMass mass, float distance, float collisionCap)
        {
            var h = new Harness();
            h.World.ApplyForce(new PropForce { PropId = 3, Mass = mass, Direction = new Vec3(0f, 0.5f, 2f), RequestedSpeed = 20f, Owner = Harness.PlayerA, SourceEventId = 5 });
            StepResult r = h.Step();
            PropImpulse impulse = Assert.Single(r.PropImpulses);
            Assert.Equal(3, impulse.PropId);
            Assert.Equal(distance, impulse.Distance);
            Assert.Equal(mass == PropMass.Anchored, impulse.ShakeOnly);
            Assert.Equal(mass == PropMass.Anchored ? 0f : GlobalRules.Terrain.DamagingPropVelocityCap, impulse.MaxSpeed);
            Assert.True(impulse.CollisionDamageCap <= GlobalRules.World.PropCollisionDamageCap);
            if (mass != PropMass.Anchored)
            {
                Assert.Equal(collisionCap, impulse.CollisionDamageCap);
            }

            Assert.Equal(Vec3.Forward, impulse.Direction); // ground-plane push direction
            Assert.Empty(r.Damage); // Sim moves the prop and applies collision damage
        }

        [Fact]
        public void A_Smaller_Requested_Push_Is_Kept()
        {
            var h = new Harness();
            h.World.ApplyForce(new PropForce { PropId = 1, Mass = PropMass.Light, Direction = Vec3.Right, RequestedDistance = 1.5f, RequestedSpeed = 5f });
            PropImpulse impulse = Assert.Single(h.Step().PropImpulses);
            Assert.Equal(1.5f, impulse.Distance);
            Assert.Equal(5f, impulse.MaxSpeed);
        }

        [Fact]
        public void Sound_Is_Attenuated_By_Solid_Cover_But_Never_By_Steam()
        {
            ArenaLayout layout = TestArenas.Empty();
            layout.Solids.Add(new Solid { Id = 1, Kind = SolidKind.Wall, Box = YawBox.OnFloor(new Vec3(0f, 0f, 0f), new Vec3(4f, 3f, 0.5f), 0f), Anchored = true });
            layout.Solids.Add(new Solid { Id = 2, Kind = SolidKind.Cover, Box = YawBox.OnFloor(new Vec3(6f, 0f, 0f), new Vec3(2f, 3f, 0.5f), 0f), Flags = MaterialFlags.Absorbent, Durability = 40f });
            var world = new ReactionWorld(layout);
            world.CreateVolume(new VolumeSpec { Kind = VolumeKind.Steam, Footprint = Footprint.Circle(new Vec2(-6f, 0f), 2f), LifetimeTicks = 120 });
            world.Step(0, null);

            SoundOcclusion wall = world.Sound.Trace(new Vec3(0f, 1f, -3f), new Vec3(0f, 1f, 3f));
            Assert.Equal(1, wall.SolidsCrossed);
            Assert.Equal(0.5f, wall.Thickness, 3);
            Assert.Equal(ReactionRules.HearingTransmissionPerSolid, wall.HearingTransmission);
            Assert.False(wall.SonicDamagePasses); // a sonic attack cannot pass through for full damage
            Assert.Equal(-1, wall.FirstFragileSolidId);

            SoundOcclusion cover = world.Sound.Trace(new Vec3(6f, 1f, -3f), new Vec3(6f, 1f, 3f));
            Assert.Equal(ReactionRules.HearingTransmissionAbsorbent, cover.HearingTransmission);
            Assert.Equal(2, cover.FirstFragileSolidId); // fragile cover a sonic attack may shake

            SoundOcclusion steam = world.Sound.Trace(new Vec3(-6f, 1f, -3f), new Vec3(-6f, 1f, 3f));
            Assert.True(world.BlocksAcquisition(new Vec3(-6f, 1f, -3f), new Vec3(-6f, 1f, 3f)));
            Assert.Equal(1f, steam.HearingTransmission); // L167: steam does not disable audio
            Assert.True(steam.SonicDamagePasses);
        }

        [Fact]
        public void A_Prism_Reflects_One_Ray_Along_Its_Fixed_Face_With_70_Percent_Damage()
        {
            ArenaLayout layout = TestArenas.Empty(biomeId: "BIO-003");
            layout.Solids.Add(new Solid { Id = 4, Kind = SolidKind.Pillar, Box = new YawBox(new Vec3(0f, 1f, 0f), new Vec3(0.5f, 1f, 0.5f), 90f), Flags = MaterialFlags.Reflective, Anchored = true });
            var world = new ReactionWorld(layout);
            Vec3 face = new YawBox(Vec3.Zero, Vec3.One, 90f).Forward; // +X

            PrismHit hit = world.Prisms.Trace(new Vec3(5f, 1f, 0f), Vec3.Left, 20f, false);
            Assert.Equal(PrismResult.Reflected, hit.Result);
            Assert.Equal(ReactionWorld.SolidPrismIdBase + 4, hit.PrismId);
            Assert.Equal(GlobalRules.World.PrismRetainedDamageFraction, hit.RetainedFraction);
            Assert.Equal(face.X, hit.OutDirection.X, 4);
            Assert.Equal(0.5f, hit.Point.X, 3);

            Assert.Equal(PrismResult.Blocked, world.Prisms.Trace(new Vec3(5f, 1f, 0f), Vec3.Left, 20f, true).Result);   // one reflection
            Assert.Equal(PrismResult.Blocked, world.Prisms.Trace(new Vec3(-5f, 1f, 0f), Vec3.Right, 20f, false).Result); // back face
            Assert.Equal(PrismResult.None, world.Prisms.Trace(new Vec3(5f, 1f, 3f), Vec3.Left, 20f, false).Result);

            world.RemoveSolid(4); // a broken prism no longer reflects
            Assert.Equal(PrismResult.None, world.Prisms.Trace(new Vec3(5f, 1f, 0f), Vec3.Left, 20f, false).Result);
        }

        [Fact]
        public void Gravity_Bends_Physical_Projectiles_At_Most_20_Degrees_And_Never_Beams()
        {
            var fields = new GravityFieldSet();
            fields.Register(new GravityFieldDef { Id = 1, Center = Vec3.Zero, Radius = 2f, MaxDeflectionDeg = 45f });
            fields.Register(new GravityFieldDef { Id = 2, Center = new Vec3(0.5f, 0f, 0f), Radius = 2f });
            var velocity = new Vec3(0f, 0f, 30f);
            Vec3 at = new Vec3(1.5f, 0f, 0f);

            Assert.False(fields.TryDeflect(9, at, velocity, ProjectileClass.Beam, 0, out _, out _));
            Assert.False(fields.TryDeflect(9, at, velocity, ProjectileClass.Magical, 0, out _, out _));

            Assert.True(fields.TryDeflect(1, at, velocity, ProjectileClass.Physical, 0, out Vec3 bent, out int fieldId));
            Assert.Equal(1, fieldId);
            Assert.Equal(GlobalRules.World.GravityMaxDeflectionDeg, fields.TotalDeflectionDeg(1), 3);
            Assert.Equal(velocity.Length(), bent.Length(), 3);
            float angle = CMath.RadToDeg(System.MathF.Acos(CMath.Clamp(Vec3.Dot(bent, velocity) / (bent.Length() * velocity.Length()), -1f, 1f)));
            Assert.Equal(20f, angle, 2);
            Assert.True(bent.X < 0f); // toward the center

            // The second field and re-entry add nothing: the total stays within 20 degrees.
            Assert.False(fields.TryDeflect(1, at, bent, ProjectileClass.Physical, 1, out _, out _));
            Assert.Equal(20f, fields.TotalDeflectionDeg(1), 3);
        }

        [Fact]
        public void A_Coil_Field_Is_Stricter_And_Waits_For_Its_Pulse()
        {
            var fields = new GravityFieldSet();
            fields.Register(new GravityFieldDef { Id = 1, Center = Vec3.Zero, Radius = 1.5f, MaxDeflectionDeg = ReactionRules.MagneticCoilMaxDeflectionDeg, ActiveFromTick = 15 });
            var velocity = new Vec3(0f, 0f, 30f);
            Assert.False(fields.TryDeflect(1, new Vec3(1f, 0f, 0f), velocity, ProjectileClass.Physical, 14, out _, out _));
            Assert.True(fields.TryDeflect(1, new Vec3(1f, 0f, 0f), velocity, ProjectileClass.Physical, 15, out _, out _));
            Assert.Equal(10f, fields.TotalDeflectionDeg(1), 3);
        }

        [Fact]
        public void Bright_Light_Drains_Shadow_Concealment_One_Second_Per_Second()
        {
            var h = new Harness();
            Vec3 lit = h.CellPoint(10, 10);
            h.World.AddLightShaft(new LightShaft { Position = lit, Radius = 1f });
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.ShadowConcealment, Footprint = Footprint.Circle(lit.XZ(), 1f), LifetimeTicks = Ticks.FromSeconds(4f), Owner = Harness.PlayerA });
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.ShadowConcealment, Footprint = Footprint.Circle(h.CellPoint(30, 30).XZ(), 1f), LifetimeTicks = Ticks.FromSeconds(4f), Owner = Harness.PlayerA });
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Steam, Footprint = Footprint.Circle(lit.XZ(), 1f), LifetimeTicks = Ticks.FromSeconds(3f), Owner = Harness.PlayerB });
            h.Step();
            int exposed = h.World.Volumes[0].Id;
            int dark = h.World.Volumes[1].Id;
            int steam = h.World.Volumes[2].Id;

            h.StepFor(Ticks.FromSeconds(2f) - 1);
            Assert.NotNull(h.World.FindVolume(exposed));
            h.Step(); // 2 s of exposure removed 4 s of lifetime
            Assert.Null(h.World.FindVolume(exposed));
            Assert.NotNull(h.World.FindVolume(dark));
            Assert.NotNull(h.World.FindVolume(steam)); // only shadow concealment is drained

            h.StepSeconds(2f);
            Assert.Null(h.World.FindVolume(dark));
        }

        [Fact]
        public void Soluble_Venom_Dilutes_To_Half_Damage_And_Stays_In_Its_Original_Patch()
        {
            var h = new Harness();
            Vec3 a = h.CellPoint(20, 20);
            MaterialDeposit venom = MaterialDeposit.Field(Material.Venom, Footprint.Lane(a.XZ(), h.CellPoint(22, 20).XZ(), 0.5f), 0f, 8f, Harness.PlayerA, 1);
            venom.DeclaredDamagePerSecond = 6f;
            venom.Soluble = true;
            h.World.Deposit(venom);
            h.Bodies.Add(ReactionBody.Player(1, h.CellPoint(21, 20)));
            StepResult first = h.Step();
            ContactDamage full = Assert.Single(first.Damage);
            Assert.Equal(6f, full.Amount);
            Assert.Equal(DamageChannel.Corrosive, full.Channel);
            Assert.Equal(ReactionKind.VenomContact, full.Reaction);

            // A water lane touches one end of the patch and runs on past it.
            h.Field(Material.Water, Footprint.Lane(h.CellPoint(22, 20).XZ(), h.CellPoint(28, 20).XZ(), 0.5f), 1, 8f, Harness.PlayerB, 2);
            h.Bodies.Add(ReactionBody.Player(2, h.CellPoint(26, 20)));
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.VenomDilution));

            StepLog log = h.StepSeconds(1f);
            Assert.Equal(6f * GlobalRules.World.VenomDilutionFactor, log.DamageTo(1));
            Assert.Equal(0f, log.DamageTo(2)); // the water does not carry the venom
            for (int x = 23; x <= 28; x++)
            {
                Assert.Equal(0, h.World.UnitsAt(h.CellPoint(x, 20)).Venom);
            }
        }

        [Fact]
        public void Insoluble_Venom_Keeps_Full_Damage_And_Venom_Clouds_Dilute_Over_Water()
        {
            var h = new Harness();
            Vec3 a = h.CellPoint(20, 20);
            MaterialDeposit venom = MaterialDeposit.Field(Material.Venom, Footprint.Point(a.XZ()), 0f, 8f, Harness.PlayerA, 1);
            venom.DeclaredDamagePerSecond = 6f;
            h.World.Deposit(venom);
            h.Step();
            h.Field(Material.Water, Footprint.Point(a.XZ()), 1, 8f, Harness.PlayerB, 2);
            Assert.Equal(0, h.Step().Count(ReactionKind.VenomDilution));
            h.Bodies.Add(ReactionBody.Player(1, a));
            Assert.Equal(6f, h.Step().DamageTo(1));

            // BIO-016 style cloud: 4 corrosive damage per second; soluble clouds over water dilute.
            var h2 = new Harness();
            Vec3 c = h2.CellPoint(30, 30);
            h2.Field(Material.Water, Footprint.Point(c.XZ()), 1, 8f);
            h2.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.VenomCloud, Footprint = Footprint.Circle(c.XZ(), 1f), Height = 2f, LifetimeTicks = 120, DamagePerSecond = 4f, Channel = DamageChannel.Corrosive, Soluble = true });
            h2.Bodies.Add(ReactionBody.Player(1, c + new Vec3(0.5f, 0f, 0f)));
            StepLog log = h2.StepSeconds(2f);
            Assert.Equal(1, log.Count(ReactionKind.VenomDilution));
            Assert.Equal(2f * 4f * GlobalRules.World.VenomDilutionFactor, log.DamageTo(1));
        }

        [Fact]
        public void A_Dispel_Consumes_One_Removable_Layer_And_Never_Map_Material()
        {
            ArenaLayout layout = TestArenas.Empty();
            layout.Materials.Add(TestArenas.Bed(new Vec2(0f, 0f), 1f, 1f, sand: 1, flags: MaterialFlags.Sand));
            var h = new Harness(new ReactionWorld(layout));
            Footprint area = Footprint.Circle(Vec2.Zero, 1f);
            Vec3 p = new Vec3(0.25f, 0f, 0.25f);
            h.World.Deposit(MaterialDeposit.Field(Material.Dust, area, 0f, 8f, Harness.PlayerA, 1, fieldId: 11));
            h.Step();
            h.World.Deposit(MaterialDeposit.Field(Material.Growth, area, 0f, 8f, Harness.PlayerA, 2, fieldId: 12));
            MaterialDeposit locked = MaterialDeposit.Field(Material.Frost, area, 0f, 8f, Harness.PlayerA, 3, fieldId: 13);
            locked.Removable = false;
            h.World.Deposit(locked);
            h.Step();

            h.World.Dispel(new DispelRequest { Area = Footprint.Circle(Vec2.Zero, 0.5f), Owner = Harness.PlayerB, SourceEventId = 20 });
            StepResult r = h.Step();
            Assert.Contains(r.Events, e => e.Kind == ReactionKind.Dispel && e.TargetId == 11);
            CellUnits u = h.World.UnitsAt(p);
            Assert.Equal(0, u.Dust);   // the oldest layer is gone everywhere
            Assert.Equal(1, u.Growth); // the newer layer stays
            Assert.Equal(1, u.Sand);   // map material is never dispelled

            h.World.Dispel(new DispelRequest { Area = area, Owner = Harness.PlayerB, SourceEventId = 21 });
            h.Step();
            Assert.Equal(0, h.World.UnitsAt(p).Growth);
            h.World.Dispel(new DispelRequest { Area = area, Owner = Harness.PlayerB, SourceEventId = 22 });
            StepResult none = h.Step();
            Assert.Contains(none.Events, e => e.Kind == ReactionKind.Dispel && e.TargetId == 0 && e.Amount == 0);
            Assert.Equal(1, h.World.UnitsAt(p).Frost); // not removable
            Assert.Equal(1, h.World.UnitsAt(p).Sand);
        }

        [Fact]
        public void A_Dispel_Takes_An_Older_Removable_Volume_Before_A_Ground_Layer()
        {
            var h = new Harness();
            Vec3 p = h.CellPoint(24, 24);
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Mist, Footprint = Footprint.Circle(p.XZ(), 1f), LifetimeTicks = 300, Owner = Harness.PlayerA, Removable = false });
            h.World.CreateVolume(new VolumeSpec { Kind = VolumeKind.Ash, Footprint = Footprint.Circle(p.XZ(), 1f), LifetimeTicks = 300, Owner = Harness.PlayerA, SourceEventId = 1 });
            h.Step();
            h.Field(Material.Dust, Footprint.Circle(p.XZ(), 1f), 1, 8f, fieldId: 30);
            h.Step();
            h.World.Dispel(new DispelRequest { Area = Footprint.Circle(p.XZ(), 0.5f), SourceEventId = 9 });
            StepResult r = h.Step();
            Assert.Equal(1, r.Count(ReactionKind.DispelVolume));
            Assert.Contains(r.VolumeEvents, e => e.VolumeKind == VolumeKind.Ash && e.Reason == VolumeRetireReason.Dispelled);
            Assert.Single(h.World.Volumes); // the unremovable mist stays
            Assert.Equal(1, h.World.UnitsAt(p).Dust);
        }
    }
}
