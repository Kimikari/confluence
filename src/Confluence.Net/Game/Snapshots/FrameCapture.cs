using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Net.Serialization;
using Confluence.Reactions;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Volumes;
using Confluence.Sim.Casting;
using Confluence.Sim.Duel;
using Confluence.Sim.Fields;
using Confluence.Sim.Movement;
using Confluence.Sim.Players;
using Confluence.Sim.Props;
using Confluence.Sim.Spells;
using Confluence.Sim.Stats;
using Confluence.Sim.Status;
using Confluence.Sim.World;
using Confluence.Sim.World.Materials;

namespace Confluence.Net.Game.Snapshots
{
    /// <summary>
    /// Server-side extraction of replicated duel state from a live <see cref="DuelWorld"/> (read-only; it never changes
    /// the world). <see cref="Capture"/> builds the full truth once per duel and send tick; <see cref="ForViewer"/> and
    /// <see cref="ForSpectator"/> project it for one audience (interest management, docs/notes/net.md).
    /// </summary>
    public static class FrameCapture
    {
        public const int VelocityScale = 64;

        public static DuelFrame Capture(DuelWorld world, int duelId, int matchTick) => Capture(world, duelId, matchTick, null);

        /// <summary>
        /// Capture with a per-duel incremental cell tracker (research §2.4.5); null scans the whole grid. Both yield the
        /// same cells (CellCaptureTests).
        /// </summary>
        public static DuelFrame Capture(DuelWorld world, int duelId, int matchTick, CellCapture cells)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var f = new DuelFrame
            {
                DuelId = duelId,
                Tick = world.Tick,
                MatchTick = matchTick,
                Phase = world.Phase,
                CombatTick = world.CombatTick,
            };

            for (int i = 0; i < DuelWorld.PlayerCount; i++) f.Players[i] = CapturePlayer(world, world.Player(i));
            CaptureEntities(world, f.Entities);
            CaptureOwned(world, f.Owned);
            ReactionWorld reactions = (world.Materials as ReactionMaterials)?.World;
            if (reactions != null)
            {
                CaptureVolumes(reactions, f.Volumes);
                if (cells != null) cells.Capture(reactions, f.Cells);
                else CaptureCells(reactions, f.Cells);
            }

            CaptureProps(world, f.Props);
            CaptureSolids(world, f.Solids);
            return f;
        }

        /// <summary>The frame a duelist receives: the opponent's exact vitals are removed (only alive and shielded bits stay).</summary>
        public static DuelFrame ForViewer(DuelFrame truth, int viewerSide) => ForViewer(truth, viewerSide, true, null);

        /// <summary>
        /// The frame a duelist receives. When the opponent is not replicated to the viewer (invisible beyond the
        /// silhouette radius, see <see cref="ReplicatesTo"/>), its entry is <see cref="PlayerEntry.AsHidden"/> of the last
        /// pose the viewer saw, so a modified client learns nothing the rules hide (spec L120, research §2.6.1). The
        /// opponent's owner-attached entities (movement, buff, other: they ride on their owner and carry no position of
        /// their own) are left out as well, like the frozen entry's action state.
        /// </summary>
        public static DuelFrame ForViewer(DuelFrame truth, int viewerSide, bool opponentReplicated, PlayerEntry lastSeen)
        {
            DuelFrame copy = truth.ShallowCopy();
            int other = 1 - viewerSide;
            copy.Players[other] = opponentReplicated ? truth.Players[other].WithoutVitals() : truth.Players[other].AsHidden(lastSeen);
            if (!opponentReplicated)
            {
                List<EntityEntry> kept = null;
                for (int i = 0; i < truth.Entities.Count; i++)
                {
                    EntityEntry e = truth.Entities[i];
                    bool drop = e.Owner == other && EntityEntry.IsOwnerAttached(e.Kind);
                    if (drop && kept == null) kept = truth.Entities.GetRange(0, i);
                    else if (!drop && kept != null) kept.Add(e);
                }

                if (kept != null) copy.Entities = kept;
            }

            return copy;
        }

        /// <summary>
        /// Whether <paramref name="target"/>'s live pose may be sent to <paramref name="viewer"/>: exactly when it is
        /// visible to it (DuelWorld.IsVisibleTo: not invisible, or within the 4 m silhouette radius, spec L120). The
        /// authoritative boundary applies before serialization, so a modified client never receives an invisible
        /// player's coordinates beyond 4 m; the honest client draws the silhouette from the first replicated frame
        /// (ClientDuel never interpolates from a hidden pose).
        /// </summary>
        public static bool ReplicatesTo(DuelWorld world, int viewer, int target) => world.IsVisibleTo(viewer, target);

        /// <summary>The frame a delayed spectator receives (spec L391): public combat including health, no private state.</summary>
        public static DuelFrame ForSpectator(DuelFrame truth) => truth.ShallowCopy();

        // ---- Players ----------------------------------------------------------------------------------------------

        private static PlayerEntry CapturePlayer(DuelWorld world, DuelPlayer p)
        {
            int tick = world.Tick;
            var e = new PlayerEntry
            {
                Position = Quantization.QuantizePosition(p.Motor.Position),
                Velocity = QuantizeVelocity(p.Motor.Velocity),
                Yaw = Quantization.QuantizeAngle(p.Motor.YawDeg),
                Pitch = Quantization.QuantizePitch(p.Motor.PitchDeg),
                ActiveWeapon = (byte)(p.ActiveWeapon == EquipmentSlot.MainFocus ? 0 : 1),
                MeleePhase = (byte)p.Melee.Phase,
                MeleeSerial = p.Melee.AttackSerial,
                HealthDeci = Deci(p.Resources.Health),
                MaxHealthDeci = Deci(p.Resources.MaxHealth),
                ShieldDeci = Deci(p.Shields.Total),
            };

            PlayerBits bits = PlayerBits.None;
            if (p.Motor.Grounded) bits |= PlayerBits.Grounded;
            if (p.Motor.Crouched) bits |= PlayerBits.Crouched;
            if (p.Motor.Sprinting) bits |= PlayerBits.Sprinting;
            if (p.Motor.IsDodging) bits |= PlayerBits.Dodging;
            if (p.Motor.IsDisplaced) bits |= PlayerBits.Displaced;
            if (p.Alive) bits |= PlayerBits.Alive;
            if (p.IsRecalling) bits |= PlayerBits.Recalling;
            if (p.Surrendered) bits |= PlayerBits.Surrendered;
            if (p.IsSwapping) bits |= PlayerBits.Swapping;
            if (p.Guard.Active) bits |= PlayerBits.Guarding;
            if (p.Guard.IsPerfect(tick)) bits |= PlayerBits.GuardPerfect;
            if (p.Guard.InReleaseWindow(tick)) bits |= PlayerBits.GuardReleaseWindow;
            if (p.Shields.Total > 0f) bits |= PlayerBits.Shielded;
            if (p.Boundary.Outside) bits |= PlayerBits.OutsideBoundary;
            if (p.Statuses.IsInvisible) bits |= PlayerBits.Invisible;
            e.Bits = bits;

            CastMachine machine = p.Caster?.Machine;
            if (machine != null)
            {
                e.CastPhase = (byte)machine.Phase;
                // The spell becomes public at commitment (cue and windup are visible tells, spec L96); the open
                // preparation's spell and grammar state stay private (spec L472).
                if (machine.Phase == CastPhase.Windup || machine.Phase == CastPhase.Channel || machine.Phase == CastPhase.Recovery)
                {
                    e.CastSpellId = machine.CurrentSpell?.Id;
                }
            }

            foreach (StatusInstance s in p.Statuses.Active)
            {
                if (e.Statuses.Count >= Wire.WireLimits.MaxStatuses) break;
                e.Statuses.Add(new StatusWire
                {
                    Kind = (byte)s.Kind,
                    DefinitionId = s.DefinitionId,
                    ExpireTick = s.ExpireTick,
                    IntensityCenti = (int)Math.Round(s.Intensity * 100f),
                });
            }

            return e;
        }

        /// <summary>The duelist's own private state (exact motor, resources, cooldowns, grammar HUD, prediction inputs).</summary>
        public static PrivateEntry CapturePrivate(DuelWorld world, int side)
        {
            DuelPlayer p = world.Player(side);
            var e = new PrivateEntry
            {
                Motor = p.Motor,
                Health = p.Resources.Health,
                MaxHealth = p.Resources.MaxHealth,
                Mana = p.Resources.Mana,
                MaxMana = p.Resources.MaxMana,
                Stamina = p.Resources.Stamina,
                MaxStamina = p.Resources.MaxStamina,
                Shield = p.Shields.Total,
                ReserveShield = p.Shields.ReserveTotal,
                HealingRemaining = p.Healing.RemainingDuel,
                SelectedSlot = Math.Max(0, Math.Min(5, p.SelectedSlot)),
                ControlImmuneUntil = Math.Max(-1, p.Statuses.ControlImmuneUntil),
                Judgement = world.JudgementIndicatorVisible ? world.JudgementIndicator(side) : -2,
                BoundaryOutsideTicks = p.Boundary.OutsideTicks,
                RecallEndTick = p.RecallEndTick,
            };

            PlayerCaster caster = p.Caster;
            if (caster != null)
            {
                CastMachine m = caster.Machine;
                int slots = Math.Min(6, m.SlotCount);
                e.CooldownTicks = new int[slots];
                for (int i = 0; i < slots; i++)
                {
                    CastableSpell spell = m.SpellInSlot(i);
                    e.CooldownTicks[i] = spell != null ? caster.CooldownRemainingTicks(spell.Id) : 0;
                }

                e.MachinePhase = (byte)m.Phase;
                e.MachineSpellId = m.CurrentSpell?.Id;
                e.MachinePhaseEndTick = m.PhaseEndTick;
                e.Grammar = m.GrammarUi;
            }

            CastingStatus casting = world.Spells.GetStatus(side);
            e.CastMovementFactor = casting.MovementFactor;
            e.CastSuppressesSprint = casting.SuppressesSprint;
            e.CastChanneling = casting.Channeling;
            Confluence.Sim.World.SurfaceInfo surface = world.SurfaceUnder(p);
            e.SurfaceKind = (byte)surface.Kind;
            e.SurfaceSlow = surface.SlowFraction;
            e.SpeedMultiplier = 1f - StatBlock.EffectiveSlow(p.Statuses.StrongestSlow, surface.SlowFraction);
            e.Traction = p.Stats.Traction;
            e.Tuning = p.Stats.Motor;
            e.Locks = MotorLocksOf(world, p, casting);
            return e;
        }

        /// <summary>The motor locks tick-order step 2 applies (same rules as DuelWorld's motor command).</summary>
        public static MotorLocks MotorLocksOf(DuelWorld world, DuelPlayer p, CastingStatus casting)
        {
            MotorLocks locks = MotorLocks.None;
            if (!p.Alive || world.Phase == DuelPhase.Ended) locks |= MotorLocks.All;
            if (p.Statuses.IsStunned || p.Statuses.IsRooted) locks |= MotorLocks.Move | MotorLocks.Jump | MotorLocks.Dodge | MotorLocks.Sprint;
            if (p.Melee.Busy || casting.Channeling || casting.SuppressesSprint) locks |= MotorLocks.Sprint;
            if (p.Guard.InReleaseWindow(world.Tick)) locks |= MotorLocks.Dodge;
            return locks;
        }

        // ---- Spell entities and owned fields ----------------------------------------------------------------------

        private static void CaptureEntities(DuelWorld world, List<EntityEntry> into)
        {
            if (!(world.Spells is SpellRuntime runtime)) return;
            foreach (SpellEntity entity in runtime.Entities)
            {
                if (!entity.Alive || into.Count >= Wire.WireLimits.MaxEntities) continue;
                // Owner-attached entities (movement, buff, other) have no position of their own: the wire carries the
                // origin and the client draws them on the owner's replicated pose, so they never locate a hidden owner.
                EntityKind kind = EntityKind.Other;
                Vec3 position = Vec3.Zero;
                Vec3 velocity = Vec3.Zero;
                float radius = 0f;
                int extra = 0;
                switch (entity)
                {
                    case ProjectileEntity projectile:
                        kind = EntityKind.Projectile;
                        position = projectile.Position;
                        velocity = projectile.Velocity;
                        radius = projectile.Spec.Radius;
                        break;
                    case FieldEntity field:
                        kind = EntityKind.Field;
                        position = field.Center;
                        radius = field.BoundRadius;
                        break;
                    case DeployableEntity deployable:
                        kind = EntityKind.Deployable;
                        position = deployable.Position;
                        velocity = deployable.Velocity;
                        radius = deployable.BodyRadius;
                        break;
                    case SummonEntity summon:
                        kind = EntityKind.Summon;
                        position = summon.BodyCenter;
                        radius = summon.BodyRadius;
                        break;
                    case WaveEntity wave:
                        kind = EntityKind.Wave;
                        position = entity.Cast.CommitOrigin;
                        velocity = entity.Cast.CommitDirection;
                        extra = (int)Math.Round(wave.Front * 100f);
                        break;
                    case TetherEntity tether:
                        kind = EntityKind.Tether;
                        position = tether.Position;
                        break;
                    case MovementEntity _:
                        kind = EntityKind.Movement;
                        break;
                    case BuffEntity buff:
                        kind = EntityKind.Buff;
                        extra = buff.EndTick;
                        break;
                }

                into.Add(new EntityEntry(entity.Id, kind, entity.Owner, entity.SpellId, Quantization.QuantizePosition(position),
                    QuantizeVelocity(velocity), (int)Math.Round(radius * 100f), extra));
            }

            KeyedSection.Sort(into);
        }

        private static void CaptureOwned(DuelWorld world, List<OwnedEntry> into)
        {
            foreach (OwnedEntity o in world.Owned.Entities)
            {
                if (!o.Active || into.Count >= Wire.WireLimits.MaxEntities) continue;
                into.Add(new OwnedEntry(o.Id, (byte)o.Kind, o.Owner, o.DefinitionId, Quantization.QuantizePosition(o.Position),
                    (int)Math.Round(o.Radius * 100f), o.ExpireTick, Deci(o.Health)));
            }

            KeyedSection.Sort(into);
        }

        // ---- Reactions --------------------------------------------------------------------------------------------

        private static void CaptureVolumes(ReactionWorld reactions, List<VolumeEntry> into)
        {
            foreach (ObscurationVolume v in reactions.Volumes)
            {
                if (into.Count >= Wire.WireLimits.MaxVolumes) break;
                ColumnSet columns = v.Columns;
                int sizeX = columns.SizeX;
                int sizeZ = columns.SizeZ;
                var bits = new byte[(sizeX * sizeZ + 7) / 8];
                if (bits.Length > Wire.WireLimits.MaxVolumeBitmapBytes) continue;
                for (int z = 0; z < sizeZ; z++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        if (!columns.Contains(columns.MinX + x, columns.MinZ + z)) continue;
                        int bit = z * sizeX + x;
                        bits[bit >> 3] |= (byte)(1 << (bit & 7));
                    }
                }

                int owner = v.Owner == 0 || v.Owner == 1 ? v.Owner : -1;
                into.Add(new VolumeEntry(v.Id, (byte)v.Kind, owner, (int)Math.Round(v.BaseY * 100f), (int)Math.Round(v.Height * 100f),
                    (int)Math.Round(v.Offset.X * 1000f), (int)Math.Round(v.Offset.Y * 1000f), v.BlocksSight, v.ExpiresTick,
                    columns.MinX, columns.MinZ, sizeX, sizeZ, bits));
            }

            KeyedSection.Sort(into);
        }

        /// <summary>Every non-empty cell of the grid in key order (the reference full scan).</summary>
        public static void CaptureCells(ReactionWorld reactions, List<CellEntry> into)
        {
            int count = reactions.Grid.CellCount;
            for (int i = 0; i < count && into.Count < Wire.WireLimits.MaxCells; i++)
            {
                CellEntry e = CellCapture.Query(reactions, i);
                if (e != null) into.Add(e);
            }
        }

        // ---- Props and solids -------------------------------------------------------------------------------------

        private static void CaptureProps(DuelWorld world, List<PropEntry> into)
        {
            foreach (PropBody prop in world.Props.Props)
            {
                if (into.Count >= Wire.WireLimits.MaxProps) break;
                into.Add(new PropEntry(prop.Id, prop.DefinitionId, Quantization.QuantizePosition(prop.Position), Quantization.QuantizeAngle(prop.Box.YawDeg),
                    float.IsInfinity(prop.Durability) ? -1 : Deci(prop.Durability), prop.Destroyed, prop.Moving));
            }

            KeyedSection.Sort(into);
        }

        private static void CaptureSolids(DuelWorld world, List<SolidEntry> into)
        {
            foreach (CollisionSolid s in world.Collision.Solids)
            {
                if (into.Count >= Wire.WireLimits.MaxSolids) break;
                if (s.Dynamic && s.Active)
                {
                    into.Add(new SolidEntry(s.Id, true, true, (byte)s.Kind, (int)s.Flags, s.Walkable, s.BlocksSight, s.Box,
                        float.IsInfinity(s.Durability) ? -1 : Deci(s.Durability)));
                }
                else if (!s.Dynamic && !s.Active)
                {
                    into.Add(new SolidEntry(s.Id, false, false, (byte)s.Kind, (int)s.Flags, s.Walkable, s.BlocksSight, s.Box, 0));
                }
            }

            KeyedSection.Sort(into);
        }

        // ---- Helpers ----------------------------------------------------------------------------------------------

        public static QuantizedVec3 QuantizeVelocity(Vec3 v)
        {
            return new QuantizedVec3(QuantizeComponent(v.X), QuantizeComponent(v.Y), QuantizeComponent(v.Z));
        }

        private static int QuantizeComponent(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0;
            float scaled = Math.Max(-(1 << 20) + 1, Math.Min((1 << 20) - 1, v * VelocityScale));
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private static int Deci(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0;
            return (int)Math.Max(0, Math.Min(1000000, Math.Round(v * 10f, MidpointRounding.AwayFromZero)));
        }

        private static byte B(int units) => (byte)Math.Max(0, Math.Min(255, units));
    }
}
