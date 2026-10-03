using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions;
using Confluence.Reactions.Environment;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Duel;
using Confluence.Sim.Content;
using Confluence.Sim.Fields;
using Confluence.Sim.Hashing;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Status;
using ReactionSurface = Confluence.Reactions.SurfaceInfo;

namespace Confluence.Sim.World.Materials
{
    /// <summary>A materials stage that needs its duel world before the first step (attached by the world's constructor).</summary>
    public interface IMaterialsBinding
    {
        void Attach(DuelWorld world);
    }

    /// <summary>
    /// <see cref="IWorldMaterials"/> over the authoritative <see cref="ReactionWorld"/> (spec §4 L130-L175, L668-L672).
    /// <list type="bullet">
    /// <item>The reaction world is built from the duel's layout with combat time 0 at the end of the alcove; the biome
    /// rule is installed through the layout biome's behavior (BIO-001, BIO-011), otherwise the announced gusts run
    /// under the global wind rule.</item>
    /// <item>Spells queue typed requests here (deposit, impact, discharge, wind, prop force, dispel, volumes); they apply
    /// at tick-order step 5 in the reaction world's canonical order.</item>
    /// <item>Each step passes both players (and summons) as bodies. Contact damage returns to the duel's damage pipeline
    /// as environment damage attributed to the reaction's owner: it never triggers on-hit effects, lifesteal, or procs
    /// (spec L126, L672). Prop impulses move the duel's props; reaction feats are recorded.</item>
    /// <item>Player-owned clouds that count as fields become owned entities, so the owner's three-field cap covers spell
    /// fields and clouds together; the oldest retires at the limit (RetireVolume FieldLimit, spec L128, L670).</item>
    /// <item>Standing in liquid water makes a player Wet (spec L118); surfaces map to the motor (L175); sight-blocking
    /// volumes give the obscuring path length for acquisition (L167). Volumes never stop projectiles or sound.</item>
    /// </list>
    /// </summary>
    public sealed class ReactionMaterials : IWorldMaterials, IMaterialsBinding
    {
        private readonly List<ReactionBody> _bodies = new List<ReactionBody>();
        private readonly List<FieldLink> _links = new List<FieldLink>();
        private readonly Dictionary<int, string> _sourceIds = new Dictionary<int, string>();
        private readonly HashSet<long> _clearedClouds = new HashSet<long>();
        private readonly Dictionary<int, int> _volumeOwners = new Dictionary<int, int>();

        /// <summary>A reaction world built from the duel's layout when attached.</summary>
        public ReactionMaterials()
        {
        }

        /// <summary>A pre-built reaction world (Laboratory gardens, tests); its environment is used as built.</summary>
        public ReactionMaterials(ReactionWorld world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
        }

        public ReactionWorld World { get; private set; }

        public DuelWorld Duel { get; private set; }

        /// <summary>The biome whose rule behavior was installed, or null.</summary>
        public string InstalledBiomeRule { get; private set; }

        /// <summary>Volume and field links currently counted against owners' field limits.</summary>
        public int CountedCloudCount => _links.Count;

        /// <summary>The result of the last step (events for presentation and audit).</summary>
        public StepResult LastStep { get; private set; }

        public void Attach(DuelWorld world)
        {
            if (Duel != null)
            {
                throw new InvalidOperationException("Materials are already attached to a duel.");
            }

            Duel = world ?? throw new ArgumentNullException(nameof(world));
            if (World != null)
            {
                return;
            }

            World = new ReactionWorld(world.Layout, world.AlcoveTicks, false);
            InstallBiome();
            World.RefreshActiveCells();
        }

        // The layout biome's rule (spec L656 "one declared environmental rule", L664 gust schedules, L668).
        private void InstallBiome()
        {
            string biomeId = Duel.Layout.BiomeId;
            if (biomeId != null && Duel.Content.TryGetBiomeRule(biomeId, out IBiomeRuleBehavior rule))
            {
                BiomeDef def = null;
                Duel.Catalogue?.BiomesById.TryGetValue(biomeId, out def);
                rule.Install(Duel, def);
                InstalledBiomeRule = biomeId;
                return;
            }

            EnvironmentRules.InstallAnnouncedGusts(World, Duel.Layout, false, float.PositiveInfinity);
        }

        // ----- requests (queued for tick-order step 5) -----------------------------------------------------

        public void Deposit(MaterialDeposit deposit) => RequireWorld().Deposit(deposit);

        public void Impact(DirectImpact impact) => RequireWorld().Impact(impact);

        public void Discharge(ElectricDischarge discharge) => RequireWorld().Discharge(discharge);

        public void Wind(WindGust gust) => RequireWorld().Wind(gust);

        public void Dispel(DispelRequest dispel) => RequireWorld().Dispel(dispel);

        public void CreateVolume(VolumeSpec spec) => RequireWorld().CreateVolume(spec);

        public void Condense(CondenseRequest request) => RequireWorld().Condense(request);

        public void RemoveUnits(UnitRemoval removal) => RequireWorld().RemoveUnits(removal);

        public void RaiseDust(DustRaise raise) => RequireWorld().RaiseDust(raise);

        public void ClearVolumes(VolumeClear clear) => RequireWorld().ClearVolumes(clear);

        public void AddHotSurface(HotSurface surface) => RequireWorld().AddHotSurface(surface);

        /// <summary>
        /// A force on one prop (spec L155, L173): the reaction world clamps it by the prop's mass class and the 8 m/s cap
        /// and the resulting impulse moves the prop. Returns false for an unknown or destroyed prop.
        /// </summary>
        public bool PushProp(int propId, Vec3 direction, int owner, int sourceEventId, float requestedDistance = float.PositiveInfinity,
            float requestedSpeed = GlobalRules.Terrain.DamagingPropVelocityCap)
        {
            PropBody prop = Duel?.Props.Find(propId);
            if (prop == null || prop.Destroyed)
            {
                return false;
            }

            RequireWorld().ApplyForce(new PropForce
            {
                PropId = propId,
                Mass = prop.Mass,
                Direction = direction,
                RequestedDistance = requestedDistance,
                RequestedSpeed = requestedSpeed,
                Owner = owner,
                SourceEventId = sourceEventId,
            });
            return true;
        }

        /// <summary>
        /// Names the catalogue source of an origin event, so reaction damage from it carries the spell id (attribution,
        /// spec L670, L672). Spells call it once per cast that interacts with the world.
        /// </summary>
        public void RecordSource(int sourceEventId, string sourceId)
        {
            if (sourceEventId != 0 && !string.IsNullOrEmpty(sourceId))
            {
                _sourceIds[sourceEventId] = sourceId;
            }
        }

        /// <summary>
        /// The generic Sim deposit (spec L163 defaults: a quick projectile deposits one unit at its impact cell). Units
        /// without a stated lifetime are instant residue that reacts this step; spells that state lifetimes, damage, or
        /// footprints use the typed requests above.
        /// </summary>
        public void QueueDeposit(in WorldDeposit d)
        {
            ReactionWorld world = RequireWorld();
            int owner = d.SourcePlayer >= 0 && d.SourcePlayer < DuelWorld.PlayerCount ? d.SourcePlayer : ReactionRules.MapOwner;
            RecordSource(d.OriginEventId, d.SourceId);
            Footprint footprint = d.Radius > 0f ? Footprint.Circle(d.Position.XZ(), d.Radius) : Footprint.Point(d.Position.XZ());
            int strength = Math.Max(1, Math.Min(d.Strength, GlobalRules.World.MaxUnitsPerCell));
            Vec3 direction = new Vec3(d.Direction.X, 0f, d.Direction.Y);
            switch (d.Kind)
            {
                case DepositKind.Water:
                case DepositKind.Heat:
                case DepositKind.Frost:
                case DepositKind.Dust:
                case DepositKind.Sand:
                case DepositKind.Growth:
                case DepositKind.Venom:
                    world.Deposit(new MaterialDeposit
                    {
                        Material = MaterialOf(d.Kind),
                        Strength = strength,
                        Footprint = footprint,
                        SurfaceY = d.Position.Y,
                        Owner = owner,
                        SourceEventId = d.OriginEventId,
                    });
                    break;
                case DepositKind.Electricity:
                    world.Discharge(new ElectricDischarge { Point = d.Position, Strength = strength, Owner = owner, SourceEventId = d.OriginEventId });
                    break;
                case DepositKind.Wind:
                    world.Wind(new WindGust
                    {
                        Area = footprint,
                        Direction = d.Direction.Normalized(),
                        Strength = strength,
                        Owner = owner,
                        SourceEventId = d.OriginEventId,
                    });
                    break;
                case DepositKind.Force:
                    PushPropsIn(d.Position, Math.Max(d.Radius, GlobalRules.World.CellSize), direction, owner, d.OriginEventId);
                    break;
                case DepositKind.Arcane:
                    world.Dispel(new DispelRequest { Area = footprint, Owner = owner, SourceEventId = d.OriginEventId });
                    break;
                default:
                    break; // light, shadow, sound, gravity act through prisms, volumes, and queries, not cell units
            }
        }

        // A force footprint pushes every loose prop whose body center lies inside it, along the force direction or
        // away from the force center (spec L155).
        private void PushPropsIn(Vec3 center, float radius, Vec3 direction, int owner, int sourceEventId)
        {
            if (Duel == null)
            {
                return;
            }

            IReadOnlyList<PropBody> props = Duel.Props.Props;
            for (int i = 0; i < props.Count; i++)
            {
                PropBody prop = props[i];
                if (prop.Destroyed || Vec3.DistanceXZ(prop.Box.Center, center) > radius)
                {
                    continue;
                }

                Vec3 push = direction.LengthSquared() > 0f ? direction : prop.Box.Center - center;
                PushProp(prop.Id, push, owner, sourceEventId);
            }
        }

        private static Material MaterialOf(DepositKind kind)
        {
            switch (kind)
            {
                case DepositKind.Water: return Material.Water;
                case DepositKind.Heat: return Material.Heat;
                case DepositKind.Frost: return Material.Frost;
                case DepositKind.Dust: return Material.Dust;
                case DepositKind.Sand: return Material.Sand;
                case DepositKind.Growth: return Material.Growth;
                default: return Material.Venom;
            }
        }

        // ----- step 5 ------------------------------------------------------------------------------------

        public void Step(IMaterialsContext context)
        {
            ReactionWorld world = RequireWorld();
            RetireUncountedClouds();
            BuildBodies(context);
            StepResult result = world.Step(context.Tick, _bodies);
            LastStep = result;

            IReadOnlyList<VolumeEvent> volumes = result.VolumeEvents;
            for (int i = 0; i < volumes.Count; i++)
            {
                OnVolumeEvent(volumes[i]);
            }

            IReadOnlyList<ContactDamage> damage = result.Damage;
            for (int i = 0; i < damage.Count; i++)
            {
                RouteContactDamage(context, damage[i]);
            }

            IReadOnlyList<ReactionEvent> events = result.Events;
            for (int i = 0; i < events.Count; i++)
            {
                RecordFeats(context, events[i]);
            }

            if (Duel != null)
            {
                IReadOnlyList<PropImpulse> impulses = result.PropImpulses;
                for (int i = 0; i < impulses.Count; i++)
                {
                    Duel.Props.ApplyImpulse(impulses[i]);
                }

                ApplyWetFromCells();
            }
        }

        private void BuildBodies(IMaterialsContext context)
        {
            _bodies.Clear();
            for (int p = 0; p < context.PlayerCount; p++)
            {
                _bodies.Add(ReactionBody.Player(p, context.PlayerFeet(p), context.PlayerGrounded(p)));
            }

            if (Duel == null)
            {
                return;
            }

            IReadOnlyList<OwnedEntity> owned = Duel.Owned.Entities;
            for (int i = 0; i < owned.Count; i++)
            {
                OwnedEntity e = owned[i];
                if (e.Active && e.Kind == OwnedEntityKind.Summon)
                {
                    float radius = e.Radius > 0f ? e.Radius : GlobalRules.Movement.CapsuleRadius;
                    _bodies.Add(new ReactionBody(MaterialRules.SummonBodyBase + e.Id, e.Position, radius, MaterialRules.SummonBodyHeight, true));
                }
            }
        }

        // Reaction damage is secondary environment damage (spec L126, L672): routed through the duel's pipeline
        // (resistance, passives, shields apply) with the reaction owner as attacker for attribution only.
        private void RouteContactDamage(IMaterialsContext context, in ContactDamage d)
        {
            int owner = d.Owner >= 0 && d.Owner < DuelWorld.PlayerCount ? d.Owner : -1;
            string sourceId = SourceIdFor(d.SourceEventId, d.Reaction);
            if (d.BodyId >= 0 && d.BodyId < context.PlayerCount)
            {
                context.RequestContactDamage(new ContactDamageRequest
                {
                    Target = d.BodyId,
                    SourcePlayer = owner,
                    SourceId = sourceId,
                    Channel = d.Channel,
                    Amount = d.Amount,
                    OriginEventId = d.SourceEventId,
                    Reaction = MaterialRules.TagOf(d.Reaction),
                });
            }
            else if (d.BodyId >= MaterialRules.SummonBodyBase && Duel != null)
            {
                Duel.DamageSummon(d.BodyId - MaterialRules.SummonBodyBase, d.Amount, owner);
            }
        }

        private string SourceIdFor(int sourceEventId, ReactionKind reaction)
        {
            return sourceEventId != 0 && _sourceIds.TryGetValue(sourceEventId, out string id) ? id : MaterialRules.ReactionSourcePrefix + reaction;
        }

        private void RecordFeats(IMaterialsContext context, in ReactionEvent e)
        {
            if (e.Owner < 0 || e.Owner >= context.PlayerCount)
            {
                return;
            }

            string family = MaterialRules.FeatReactionOf(e.Kind);
            if (family != null)
            {
                context.RecordReaction(e.Owner, family); // FEAT-006
            }

            if (MaterialRules.ClearsCloud(e.Kind) && e.TargetId != 0)
            {
                int cloudOwner = _volumeOwners.TryGetValue(e.TargetId, out int o) ? o : ReactionRules.MapOwner;
                long key = ((long)e.Owner << 32) | (uint)e.TargetId;
                if (cloudOwner != e.Owner && _clearedClouds.Add(key))
                {
                    context.RecordCloudCleared(e.Owner); // FEAT-011: a hostile or neutral cloud, each once
                }
            }
        }

        // ----- field accounting (spec L128, L670) ------------------------------------------------------------

        private void OnVolumeEvent(in VolumeEvent v)
        {
            if (v.Kind == VolumeEventKind.Created)
            {
                _volumeOwners[v.VolumeId] = v.Owner;
                if (v.CountsAsField && v.Owner >= 0 && v.Owner < DuelWorld.PlayerCount)
                {
                    CountCloud(v);
                }

                return;
            }

            _volumeOwners.Remove(v.VolumeId);
            int link = FindLinkByVolume(v.VolumeId);
            if (link >= 0)
            {
                FieldLink l = _links[link];
                _links.RemoveAt(link);
                if (v.Reason != VolumeRetireReason.FieldLimit && Duel != null)
                {
                    FieldChange change = v.Reason == VolumeRetireReason.Dispelled ? FieldChange.Dispelled : FieldChange.Expired;
                    Duel.RemoveOwnedEntity(l.EntityId, change, -1);
                }
            }
        }

        private void CountCloud(in VolumeEvent v)
        {
            if (Duel == null)
            {
                return;
            }

            ObscurationVolume volume = World.FindVolume(v.VolumeId);
            Vec3 center = Vec3.Zero;
            float radius = 0f;
            if (volume != null)
            {
                volume.GetBounds(out Vec2 min, out Vec2 max);
                Vec2 c = (min + max) * 0.5f;
                center = new Vec3(c.X, volume.BaseY, c.Y);
                radius = Vec2.Distance(min, max) * 0.5f;
            }

            OwnedEntity entity = Duel.CreateOwnedEntity(new OwnedEntitySpec
            {
                Kind = OwnedEntityKind.Field,
                Owner = v.Owner,
                DefinitionId = MaterialRules.CloudFieldPrefix + v.VolumeKind,
                LifetimeTicks = OwnedEntityRegistry.MaxLifetimeTicks, // the volume's own (<= 8 s) lifetime ends it first
                Position = center,
                Radius = radius,
                Neutral = true,
                OriginEventId = v.SourceEventId,
            });
            if (entity == null)
            {
                World.RetireVolume(v.VolumeId, VolumeRetireReason.FieldLimit); // nothing retirable: the cloud cannot exist
                return;
            }

            _links.Add(new FieldLink { VolumeId = v.VolumeId, EntityId = entity.Id });
            RetireUncountedClouds();
        }

        // A cloud whose field entity left the registry (oldest retirement at the limit, a dispel of the field, expiry)
        // retires from the reaction world immediately.
        private void RetireUncountedClouds()
        {
            if (Duel == null)
            {
                return;
            }

            for (int i = _links.Count - 1; i >= 0; i--)
            {
                FieldLink l = _links[i];
                if (Duel.Owned.Find(l.EntityId) != null)
                {
                    continue;
                }

                _links.RemoveAt(i);
                World.RetireVolume(l.VolumeId, VolumeRetireReason.FieldLimit);
            }
        }

        private int FindLinkByVolume(int volumeId)
        {
            for (int i = 0; i < _links.Count; i++)
            {
                if (_links[i].VolumeId == volumeId)
                {
                    return i;
                }
            }

            return -1;
        }

        // ----- wet status (spec L118, L175) --------------------------------------------------------------------

        // A grounded player standing in liquid water is Wet; it lingers briefly after leaving the water.
        private void ApplyWetFromCells()
        {
            if (Duel.Phase != DuelPhase.Combat)
            {
                return;
            }

            int linger = Ticks.FromSeconds(MaterialRules.WetLingerSec);
            int margin = Ticks.FromSeconds(MaterialRules.WetRefreshMarginSec);
            for (int p = 0; p < DuelWorld.PlayerCount; p++)
            {
                DuelPlayer player = Duel.Player(p);
                if (!player.Alive || !player.Motor.Grounded || !StandsInWater(player.Position))
                {
                    continue;
                }

                StatusInstance wet = player.Statuses.Find(StatusKind.Wet, MaterialRules.WetCellSource);
                if (wet != null && wet.RemainingTicks(Duel.Tick) > linger)
                {
                    continue;
                }

                Duel.ApplyStatus(p, StatusApplication.Of(StatusKind.Wet, MaterialRules.WetCellSource, -1, linger + margin));
            }
        }

        /// <summary>Whether feet stand on a liquid-water cell (ice is not wet).</summary>
        public bool StandsInWater(Vec3 feet)
        {
            ReactionSurface s = RequireWorld().SurfaceAt(feet);
            return s.Has(SurfaceFlags.Wet) && SurfaceMapping.Touches(s, feet);
        }

        // ----- queries -----------------------------------------------------------------------------------------

        /// <summary>
        /// Path length through sight-blocking volumes (spec L167). A path the reaction world judges blocking reports at
        /// least the 2 m threshold, so the duel's acquisition test and the reaction world always agree.
        /// </summary>
        public float ObscuringPathLength(Vec3 from, Vec3 to)
        {
            ReactionWorld world = RequireWorld();
            float length = world.SightPathLength(from, to);
            return world.BlocksAcquisition(from, to) ? Math.Max(length, GlobalRules.World.SteamSightBlockPathLength) : length;
        }

        /// <summary>Whether volumes alone block target acquisition between two points (spec L167).</summary>
        public bool BlocksAcquisition(Vec3 from, Vec3 to) => RequireWorld().BlocksAcquisition(from, to);

        public SurfaceInfo QuerySurface(Vec3 feet) => SurfaceMapping.ToMotor(RequireWorld().SurfaceAt(feet), feet);

        /// <summary>The footstep surface at the feet (wet stone, sand, growth...; audio and HUD only).</summary>
        public Confluence.Reactions.SurfaceKind FootstepSurface(Vec3 feet) => SurfaceMapping.Footstep(RequireWorld().SurfaceAt(feet), feet);

        public void Hash(ref StateHasher hasher)
        {
            ReactionWorld world = RequireWorld();
            hasher.Add(world.StateHash());
            hasher.Add(_links.Count);
            for (int i = 0; i < _links.Count; i++)
            {
                hasher.Add(_links[i].VolumeId);
                hasher.Add(_links[i].EntityId);
            }

            hasher.Add(_clearedClouds.Count);
        }

        private ReactionWorld RequireWorld()
        {
            return World ?? throw new InvalidOperationException("ReactionMaterials needs its duel world (attach) or a pre-built reaction world.");
        }

        private struct FieldLink
        {
            public int VolumeId;
            public int EntityId;
        }
    }
}
