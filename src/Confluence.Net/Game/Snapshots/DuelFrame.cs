using System;
using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Casting.Grammars;
using Confluence.Core.Geometry;
using Confluence.Net.Game.Wire;
using Confluence.Net.Serialization;
using Confluence.Sim.Duel;
using Confluence.Sim.Movement;

namespace Confluence.Net.Game.Snapshots
{
    /// <summary>Public per-player state bits (spec L419 "players' motor ... statuses ... cast public state").</summary>
    [Flags]
    public enum PlayerBits : uint
    {
        None = 0,
        Grounded = 1u << 0,
        Crouched = 1u << 1,
        Sprinting = 1u << 2,
        Dodging = 1u << 3,
        Displaced = 1u << 4,
        Alive = 1u << 5,
        Recalling = 1u << 6,
        Surrendered = 1u << 7,
        Swapping = 1u << 8,
        Guarding = 1u << 9,
        GuardPerfect = 1u << 10,
        GuardReleaseWindow = 1u << 11,
        Shielded = 1u << 12,
        OutsideBoundary = 1u << 13,
        Invisible = 1u << 14,
        All = (1u << 15) - 1u,
    }

    /// <summary>A visible status on a player (spec L347 status icons, L433 StatusEvent).</summary>
    public struct StatusWire : IEquatable<StatusWire>
    {
        public byte Kind;
        public string DefinitionId;
        public int ExpireTick;

        /// <summary>Intensity in hundredths (slow fraction, damage per second, charge stacks).</summary>
        public int IntensityCenti;

        public bool Equals(StatusWire o) => Kind == o.Kind && DefinitionId == o.DefinitionId && ExpireTick == o.ExpireTick && IntensityCenti == o.IntensityCenti;
        public override bool Equals(object obj) => obj is StatusWire o && Equals(o);
        public override int GetHashCode() => Kind * 397 ^ ExpireTick;
    }

    /// <summary>
    /// One fighter as other viewers may see it: quantized position, velocity and view angles, public action state
    /// (guard, melee phase, cast phase and the spell once committed), statuses, and vitals. Vitals are -1 when the
    /// viewer may not know them (the opponent's exact health, shield, mana; D-net interest rules in docs/notes/net.md).
    /// </summary>
    public sealed class PlayerEntry
    {
        public QuantizedVec3 Position;

        /// <summary>1/64 m/s.</summary>
        public QuantizedVec3 Velocity;

        public ushort Yaw;
        public ushort Pitch;
        public PlayerBits Bits;
        public byte ActiveWeapon;

        /// <summary>Casting CastPhase as a byte.</summary>
        public byte CastPhase;

        /// <summary>The spell being cast, only once committed (windup, channel, recovery); null otherwise.</summary>
        public string CastSpellId;

        /// <summary>Sim MeleePhase as a byte.</summary>
        public byte MeleePhase;

        public int MeleeSerial;
        public int HealthDeci = -1;
        public int MaxHealthDeci = -1;
        public int ShieldDeci = -1;
        public List<StatusWire> Statuses = new List<StatusWire>();

        public bool Has(PlayerBits bit) => (Bits & bit) == bit;

        public Vec3 PositionMeters => Quantization.DequantizePosition(Position);
        public Vec3 VelocityMetersPerSecond => new Vec3(Velocity.X / 64f, Velocity.Y / 64f, Velocity.Z / 64f);
        public float YawDeg => Quantization.DequantizeAngle(Yaw);
        public float PitchDeg => Quantization.DequantizePitch(Pitch);

        public void Write(NetWriter w)
        {
            w.WriteQuantizedPosition(Position);
            w.WriteVarInt(Velocity.X);
            w.WriteVarInt(Velocity.Y);
            w.WriteVarInt(Velocity.Z);
            w.WriteUShort(Yaw);
            w.WriteUShort(Pitch);
            w.WriteVarUInt((uint)Bits);
            w.WriteByte(ActiveWeapon);
            w.WriteByte(CastPhase);
            w.WriteId(CastSpellId);
            w.WriteByte(MeleePhase);
            w.WriteVarInt(MeleeSerial);
            w.WriteVarInt(HealthDeci);
            w.WriteVarInt(MaxHealthDeci);
            w.WriteVarInt(ShieldDeci);
            w.WriteVarUInt((uint)Statuses.Count);
            foreach (StatusWire s in Statuses)
            {
                w.WriteByte(s.Kind);
                w.WriteId(s.DefinitionId);
                w.WriteVarInt(s.ExpireTick);
                w.WriteVarInt(s.IntensityCenti);
            }
        }

        public static PlayerEntry Read(NetReader r)
        {
            var p = new PlayerEntry();
            p.Position = r.ReadQuantizedPosition();
            p.Velocity = new QuantizedVec3(r.ReadVarIntInRange(-1 << 20, 1 << 20), r.ReadVarIntInRange(-1 << 20, 1 << 20), r.ReadVarIntInRange(-1 << 20, 1 << 20));
            p.Yaw = r.ReadUShort();
            p.Pitch = r.ReadUShort();
            uint bits = r.ReadVarUInt();
            if ((bits & ~(uint)PlayerBits.All) != 0) throw new NetReadException(NetReadError.InvalidValue, "unknown player bits");
            p.Bits = (PlayerBits)bits;
            p.ActiveWeapon = r.ReadByteInRange(0, 1);
            p.CastPhase = r.ReadByteInRange(0, (byte)Casting.CastPhase.Recovery);
            p.CastSpellId = r.ReadId();
            p.MeleePhase = r.ReadByteInRange(0, 3);
            p.MeleeSerial = r.ReadVarInt();
            p.HealthDeci = r.ReadVarIntInRange(-1, 1000000);
            p.MaxHealthDeci = r.ReadVarIntInRange(-1, 1000000);
            p.ShieldDeci = r.ReadVarIntInRange(-1, 1000000);
            int n = r.ReadCount(WireLimits.MaxStatuses, 3);
            for (int i = 0; i < n; i++)
            {
                p.Statuses.Add(new StatusWire
                {
                    Kind = r.ReadByteInRange(0, 31),
                    DefinitionId = r.ReadId(),
                    ExpireTick = r.ReadVarInt(),
                    IntensityCenti = r.ReadVarInt(),
                });
            }

            return p;
        }

        public bool SameAs(PlayerEntry o)
        {
            if (o == null || o.Position != Position || o.Velocity != Velocity || o.Yaw != Yaw || o.Pitch != Pitch || o.Bits != Bits ||
                o.ActiveWeapon != ActiveWeapon || o.CastPhase != CastPhase || o.CastSpellId != CastSpellId || o.MeleePhase != MeleePhase ||
                o.MeleeSerial != MeleeSerial || o.HealthDeci != HealthDeci || o.MaxHealthDeci != MaxHealthDeci || o.ShieldDeci != ShieldDeci ||
                o.Statuses.Count != Statuses.Count)
            {
                return false;
            }

            for (int i = 0; i < Statuses.Count; i++)
                if (!Statuses[i].Equals(o.Statuses[i])) return false;
            return true;
        }

        /// <summary>A copy with the private vitals hidden (what the opponent may see).</summary>
        public PlayerEntry WithoutVitals()
        {
            var copy = (PlayerEntry)MemberwiseClone();
            copy.HealthDeci = -1;
            copy.MaxHealthDeci = -1;
            copy.ShieldDeci = -1;
            copy.Statuses = new List<StatusWire>(Statuses);
            return copy;
        }
    }

    /// <summary>
    /// The receiving player's own private state, sent whole in every snapshot and never to anyone else: the exact
    /// motor state for prediction and reconciliation, resources, cooldowns, the open grammar's HUD state (spec L472),
    /// and the inputs the motor reads from the rest of the world (casting movement factor, slows, surface, locks,
    /// tuning). Hidden information: the opponent never receives it (spec L72, L425).
    /// </summary>
    public sealed class PrivateEntry
    {
        public MotorState Motor;
        public float Health;
        public float MaxHealth;
        public float Mana;
        public float MaxMana;
        public float Stamina;
        public float MaxStamina;
        public float Shield;
        public float ReserveShield;
        public float HealingRemaining;
        public int SelectedSlot;

        /// <summary>Remaining cooldown ticks per equipped slot.</summary>
        public int[] CooldownTicks = new int[0];

        public int SignatureCooldownTicks;

        // Cast machine and grammar (spec L472 "selected spell, preparation progress, and exact input state").
        public byte MachinePhase;
        public string MachineSpellId;
        public int MachinePhaseEndTick;
        public GrammarUiState Grammar;

        // What the motor reads besides the input (prediction, docs/notes/net.md).
        public float CastMovementFactor = 1f;
        public bool CastSuppressesSprint;
        public bool CastChanneling;
        public float SpeedMultiplier = 1f;
        public float Traction;
        public byte SurfaceKind;
        public float SurfaceSlow;
        public MotorLocks Locks;
        public MotorTuning Tuning;
        public int ControlImmuneUntil;

        /// <summary>Final-15 s judgement indicator: 1 ahead, 0 equal, -1 behind; -2 when hidden (spec L53).</summary>
        public int Judgement = -2;

        public int BoundaryOutsideTicks;

        public void Write(NetWriter w)
        {
            WriteMotor(w, Motor);
            w.WriteFloat(Health);
            w.WriteFloat(MaxHealth);
            w.WriteFloat(Mana);
            w.WriteFloat(MaxMana);
            w.WriteFloat(Stamina);
            w.WriteFloat(MaxStamina);
            w.WriteFloat(Shield);
            w.WriteFloat(ReserveShield);
            w.WriteFloat(HealingRemaining);
            w.WriteByte((byte)SelectedSlot);
            w.WriteVarUInt((uint)CooldownTicks.Length);
            foreach (int c in CooldownTicks) w.WriteVarUInt((uint)Math.Max(0, c));
            w.WriteVarUInt((uint)Math.Max(0, SignatureCooldownTicks));
            w.WriteByte(MachinePhase);
            w.WriteId(MachineSpellId);
            w.WriteVarInt(MachinePhaseEndTick);
            WriteGrammar(w, Grammar);
            w.WriteFloat(CastMovementFactor);
            w.WriteBool(CastSuppressesSprint);
            w.WriteBool(CastChanneling);
            w.WriteFloat(SpeedMultiplier);
            w.WriteFloat(Traction);
            w.WriteByte(SurfaceKind);
            w.WriteFloat(SurfaceSlow);
            w.WriteByte((byte)Locks);
            WriteTuning(w, Tuning);
            w.WriteVarInt(ControlImmuneUntil);
            w.WriteVarInt(Judgement);
            w.WriteVarUInt((uint)Math.Max(0, BoundaryOutsideTicks));
        }

        public static PrivateEntry Read(NetReader r)
        {
            var p = new PrivateEntry();
            p.Motor = ReadMotor(r);
            p.Health = r.ReadFloat();
            p.MaxHealth = r.ReadFloat();
            p.Mana = r.ReadFloat();
            p.MaxMana = r.ReadFloat();
            p.Stamina = r.ReadFloat();
            p.MaxStamina = r.ReadFloat();
            p.Shield = r.ReadFloat();
            p.ReserveShield = r.ReadFloat();
            p.HealingRemaining = r.ReadFloat();
            p.SelectedSlot = r.ReadByteInRange(0, 5);
            int n = r.ReadCount(6, 1);
            p.CooldownTicks = new int[n];
            for (int i = 0; i < n; i++) p.CooldownTicks[i] = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            p.SignatureCooldownTicks = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            p.MachinePhase = r.ReadByteInRange(0, (byte)Casting.CastPhase.Recovery);
            p.MachineSpellId = r.ReadId();
            p.MachinePhaseEndTick = r.ReadVarInt();
            p.Grammar = ReadGrammar(r);
            p.CastMovementFactor = r.ReadFloat();
            p.CastSuppressesSprint = r.ReadBool();
            p.CastChanneling = r.ReadBool();
            p.SpeedMultiplier = r.ReadFloat();
            p.Traction = r.ReadFloat();
            p.SurfaceKind = r.ReadByteInRange(0, 15);
            p.SurfaceSlow = r.ReadFloat();
            p.Locks = (MotorLocks)r.ReadByteInRange(0, (byte)MotorLocks.All);
            p.Tuning = ReadTuning(r);
            p.ControlImmuneUntil = r.ReadVarInt();
            p.Judgement = r.ReadVarIntInRange(-2, 1);
            p.BoundaryOutsideTicks = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            return p;
        }

        public static void WriteMotor(NetWriter w, MotorState m)
        {
            w.WriteVec3(m.Position);
            w.WriteVec3(m.Velocity);
            w.WriteFloat(m.YawDeg);
            w.WriteFloat(m.PitchDeg);
            w.WriteByte((byte)((m.Grounded ? 1 : 0) | (m.Crouched ? 2 : 0) | (m.Sprinting ? 4 : 0)));
            w.WriteVec3(m.GroundNormal);
            w.WriteVarInt(m.GroundSolidId);
            w.WriteFloat(m.AirPeakY);
            w.WriteFloat(m.SupportY);
            w.WriteVarInt(m.DodgeTicks);
            w.WriteVec3(m.DodgeVelocity);
            w.WriteVarInt(m.DodgeCooldownTicks);
            w.WriteVarInt(m.DisplaceTicks);
            w.WriteVec3(m.DisplaceVelocity);
            w.WriteByte((byte)m.DisplaceKind);
            w.WriteVec3(m.LastGroundedPosition);
        }

        public static MotorState ReadMotor(NetReader r)
        {
            var m = new MotorState();
            m.Position = r.ReadVec3();
            m.Velocity = r.ReadVec3();
            m.YawDeg = r.ReadFloat();
            m.PitchDeg = r.ReadFloat();
            byte bits = r.ReadByteInRange(0, 7);
            m.Grounded = (bits & 1) != 0;
            m.Crouched = (bits & 2) != 0;
            m.Sprinting = (bits & 4) != 0;
            m.GroundNormal = r.ReadVec3();
            m.GroundSolidId = r.ReadVarInt();
            m.AirPeakY = r.ReadFloat();
            m.SupportY = r.ReadFloat();
            m.DodgeTicks = r.ReadVarIntInRange(0, 10000);
            m.DodgeVelocity = r.ReadVec3();
            m.DodgeCooldownTicks = r.ReadVarIntInRange(0, 100000);
            m.DisplaceTicks = r.ReadVarIntInRange(0, 100000);
            m.DisplaceVelocity = r.ReadVec3();
            m.DisplaceKind = (DisplacementKind)r.ReadByteInRange(0, (byte)DisplacementKind.Dash);
            m.LastGroundedPosition = r.ReadVec3();
            return m;
        }

        private static void WriteGrammar(NetWriter w, GrammarUiState g)
        {
            w.WriteBool(g.Active);
            if (!g.Active) return;
            w.WriteVarInt(g.ElapsedTicks);
            w.WriteFloat(g.Gauge);
            w.WriteFloat(g.GaugeValue);
            byte flags = (byte)((g.HasSafeBand ? 1 : 0) | (g.HasMasteryBand ? 2 : 0) | (g.HasReleaseWindow ? 4 : 0) | (g.HasMasteryWindow ? 8 : 0) |
                (g.Queued ? 16 : 0) | (g.Venting ? 32 : 0) | (g.ModifierEngaged ? 64 : 0));
            w.WriteByte(flags);
            if (g.HasSafeBand)
            {
                w.WriteFloat(g.SafeBandLow);
                w.WriteFloat(g.SafeBandHigh);
            }

            if (g.HasMasteryBand)
            {
                w.WriteFloat(g.MasteryBandLow);
                w.WriteFloat(g.MasteryBandHigh);
            }

            if (g.HasReleaseWindow)
            {
                w.WriteVarInt(g.ReleaseWindow.Open);
                w.WriteVarInt(g.ReleaseWindow.Close);
            }

            if (g.HasMasteryWindow)
            {
                w.WriteVarInt(g.MasteryWindow.Open);
                w.WriteVarInt(g.MasteryWindow.Close);
            }

            w.WriteVarInt(g.ProgressTicks);
            w.WriteVarInt(g.RequiredTicks);
            if (g.Queued) w.WriteVarInt(g.QueuedCommitTick);
            if (g.Venting) w.WriteVarInt(g.VentRemainingTicks);
            w.WriteByte((byte)g.ReleaseNow);
            w.WriteVarInt(g.ThrottledInputs);
        }

        private static GrammarUiState ReadGrammar(NetReader r)
        {
            var g = new GrammarUiState();
            g.Active = r.ReadBool();
            if (!g.Active) return g;
            g.ElapsedTicks = r.ReadVarInt();
            g.Gauge = r.ReadFloat();
            g.GaugeValue = r.ReadFloat();
            byte flags = r.ReadByteInRange(0, 127);
            g.HasSafeBand = (flags & 1) != 0;
            g.HasMasteryBand = (flags & 2) != 0;
            g.HasReleaseWindow = (flags & 4) != 0;
            g.HasMasteryWindow = (flags & 8) != 0;
            g.Queued = (flags & 16) != 0;
            g.Venting = (flags & 32) != 0;
            g.ModifierEngaged = (flags & 64) != 0;
            if (g.HasSafeBand)
            {
                g.SafeBandLow = r.ReadFloat();
                g.SafeBandHigh = r.ReadFloat();
            }

            if (g.HasMasteryBand)
            {
                g.MasteryBandLow = r.ReadFloat();
                g.MasteryBandHigh = r.ReadFloat();
            }

            if (g.HasReleaseWindow) g.ReleaseWindow = ReadWindow(r);
            if (g.HasMasteryWindow) g.MasteryWindow = ReadWindow(r);
            g.ProgressTicks = r.ReadVarInt();
            g.RequiredTicks = r.ReadVarInt();
            if (g.Queued) g.QueuedCommitTick = r.ReadVarInt();
            if (g.Venting) g.VentRemainingTicks = r.ReadVarInt();
            g.ReleaseNow = (MissReason)r.ReadByte();
            g.ThrottledInputs = r.ReadVarInt();
            return g;
        }

        private static TickWindow ReadWindow(NetReader r)
        {
            int start = r.ReadVarInt();
            int end = r.ReadVarInt();
            if (end < start) throw new NetReadException(NetReadError.InvalidValue, "inverted tick window");
            return new TickWindow(start, end);
        }

        private static void WriteTuning(NetWriter w, MotorTuning t)
        {
            w.WriteBool(t.Specified);
            if (!t.Specified) return;
            w.WriteFloat(t.WalkSpeed);
            w.WriteFloat(t.SprintSpeed);
            w.WriteFloat(t.CrouchSpeed);
            w.WriteFloat(t.WalkMultiplier);
            w.WriteFloat(t.SprintMultiplier);
            w.WriteFloat(t.SprintStaminaPerSecond);
            w.WriteFloat(t.JumpStaminaCost);
            w.WriteFloat(t.DodgeStaminaCost);
            w.WriteFloat(t.DodgeDistance);
            w.WriteVarInt(t.DodgeCooldownTicks);
        }

        private static MotorTuning ReadTuning(NetReader r)
        {
            var t = new MotorTuning();
            t.Specified = r.ReadBool();
            if (!t.Specified) return t;
            t.WalkSpeed = r.ReadFloat();
            t.SprintSpeed = r.ReadFloat();
            t.CrouchSpeed = r.ReadFloat();
            t.WalkMultiplier = r.ReadFloat();
            t.SprintMultiplier = r.ReadFloat();
            t.SprintStaminaPerSecond = r.ReadFloat();
            t.JumpStaminaCost = r.ReadFloat();
            t.DodgeStaminaCost = r.ReadFloat();
            t.DodgeDistance = r.ReadFloat();
            t.DodgeCooldownTicks = r.ReadVarIntInRange(0, 100000);
            return t;
        }
    }

    /// <summary>
    /// The replicated public state of one duel at one tick (spec L419-L421): phase and timer, both players, spell
    /// entities, owned fields and summons, obscuration volumes, non-empty reaction cells, props, and the collision
    /// solids that differ from the layout. Built quantized on the server (one capture per duel and send tick), projected
    /// per viewer, and delta-coded against the viewer's acknowledged baseline.
    /// </summary>
    public sealed class DuelFrame
    {
        public int DuelId;

        /// <summary>Duel world tick (0 at alcove start).</summary>
        public int Tick;

        /// <summary>The match tick of the same step.</summary>
        public int MatchTick;

        public DuelPhase Phase;

        /// <summary>Combat ticks elapsed; the boundary contour is a function of it (spec L48).</summary>
        public int CombatTick;

        public PlayerEntry[] Players = new PlayerEntry[DuelWorld.PlayerCount];
        public List<EntityEntry> Entities = new List<EntityEntry>();
        public List<OwnedEntry> Owned = new List<OwnedEntry>();
        public List<VolumeEntry> Volumes = new List<VolumeEntry>();
        public List<CellEntry> Cells = new List<CellEntry>();
        public List<PropEntry> Props = new List<PropEntry>();
        public List<SolidEntry> Solids = new List<SolidEntry>();

        /// <summary>The match tick minus the duel tick: converts the client's match-clock estimate to duel ticks.</summary>
        public int MatchTickOffset => MatchTick - Tick;

        /// <summary>A shallow copy whose player entries can be replaced (per-viewer projection).</summary>
        public DuelFrame ShallowCopy()
        {
            var copy = (DuelFrame)MemberwiseClone();
            copy.Players = (PlayerEntry[])Players.Clone();
            return copy;
        }

        public CellEntry FindCell(int index)
        {
            int lo = 0;
            int hi = Cells.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int key = Cells[mid].Key;
                if (key == index) return Cells[mid];
                if (key < index) lo = mid + 1;
                else hi = mid - 1;
            }

            return null;
        }
    }
}
