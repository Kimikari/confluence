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

        /// <summary>
        /// The viewer may not see this player (invisible beyond the 4 m silhouette, spec L120): the entry carries no
        /// live pose, only the last pose the viewer saw, frozen (research §2.6.1 server-side visibility culling).
        /// </summary>
        Hidden = 1u << 15,
        All = (1u << 16) - 1u,
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

        private const byte PlPos = 1, PlVel = 2, PlView = 4, PlBits = 8, PlCast = 16, PlMelee = 32, PlVitals = 64, PlStatuses = 128;

        /// <summary>Writes this entry as a change of the receiver's baseline entry (field groups, research §2.4.4).</summary>
        public void WriteAgainst(NetWriter w, PlayerEntry p)
        {
            byte mask = 0;
            if (Position != p.Position) mask |= PlPos;
            if (Velocity != p.Velocity) mask |= PlVel;
            if (Yaw != p.Yaw || Pitch != p.Pitch) mask |= PlView;
            if (Bits != p.Bits) mask |= PlBits;
            if (ActiveWeapon != p.ActiveWeapon || CastPhase != p.CastPhase || CastSpellId != p.CastSpellId) mask |= PlCast;
            if (MeleePhase != p.MeleePhase || MeleeSerial != p.MeleeSerial) mask |= PlMelee;
            if (HealthDeci != p.HealthDeci || MaxHealthDeci != p.MaxHealthDeci || ShieldDeci != p.ShieldDeci) mask |= PlVitals;
            if (!SameStatuses(p)) mask |= PlStatuses;
            w.WriteByte(mask);
            if ((mask & PlPos) != 0) w.WritePositionDelta(Position, p.Position);
            if ((mask & PlVel) != 0)
            {
                w.WriteVarInt(Velocity.X - p.Velocity.X);
                w.WriteVarInt(Velocity.Y - p.Velocity.Y);
                w.WriteVarInt(Velocity.Z - p.Velocity.Z);
            }

            if ((mask & PlView) != 0)
            {
                w.WriteUShort(Yaw);
                w.WriteUShort(Pitch);
            }

            if ((mask & PlBits) != 0) w.WriteVarUInt((uint)Bits);
            if ((mask & PlCast) != 0)
            {
                w.WriteByte(ActiveWeapon);
                w.WriteByte(CastPhase);
                w.WriteId(CastSpellId);
            }

            if ((mask & PlMelee) != 0)
            {
                w.WriteByte(MeleePhase);
                w.WriteVarInt(MeleeSerial);
            }

            if ((mask & PlVitals) != 0)
            {
                w.WriteVarInt(HealthDeci);
                w.WriteVarInt(MaxHealthDeci);
                w.WriteVarInt(ShieldDeci);
            }

            if ((mask & PlStatuses) != 0) WriteStatuses(w);
        }

        public static PlayerEntry ReadAgainst(NetReader r, PlayerEntry b)
        {
            byte mask = r.ReadByte();
            var p = (PlayerEntry)b.MemberwiseClone();
            p.Statuses = new List<StatusWire>(b.Statuses);
            if ((mask & PlPos) != 0) p.Position = r.ReadPositionDelta(b.Position);
            if ((mask & PlVel) != 0)
            {
                p.Velocity = new QuantizedVec3(VelocityAxis((long)b.Velocity.X + r.ReadVarInt()), VelocityAxis((long)b.Velocity.Y + r.ReadVarInt()),
                    VelocityAxis((long)b.Velocity.Z + r.ReadVarInt()));
            }

            if ((mask & PlView) != 0)
            {
                p.Yaw = r.ReadUShort();
                p.Pitch = r.ReadUShort();
            }

            if ((mask & PlBits) != 0) p.Bits = ReadBits(r);
            if ((mask & PlCast) != 0)
            {
                p.ActiveWeapon = r.ReadByteInRange(0, 1);
                p.CastPhase = r.ReadByteInRange(0, (byte)Casting.CastPhase.Recovery);
                p.CastSpellId = r.ReadId();
            }

            if ((mask & PlMelee) != 0)
            {
                p.MeleePhase = r.ReadByteInRange(0, 3);
                p.MeleeSerial = r.ReadVarInt();
            }

            if ((mask & PlVitals) != 0)
            {
                p.HealthDeci = r.ReadVarIntInRange(-1, 1000000);
                p.MaxHealthDeci = r.ReadVarIntInRange(-1, 1000000);
                p.ShieldDeci = r.ReadVarIntInRange(-1, 1000000);
            }

            if ((mask & PlStatuses) != 0)
            {
                p.Statuses = new List<StatusWire>();
                ReadStatuses(r, p.Statuses);
            }

            return p;
        }

        private bool SameStatuses(PlayerEntry o)
        {
            if (o.Statuses.Count != Statuses.Count) return false;
            for (int i = 0; i < Statuses.Count; i++)
                if (!Statuses[i].Equals(o.Statuses[i])) return false;
            return true;
        }

        private void WriteStatuses(NetWriter w)
        {
            w.WriteVarUInt((uint)Statuses.Count);
            foreach (StatusWire s in Statuses)
            {
                w.WriteByte(s.Kind);
                w.WriteId(s.DefinitionId);
                w.WriteVarInt(s.ExpireTick);
                w.WriteVarInt(s.IntensityCenti);
            }
        }

        private static void ReadStatuses(NetReader r, List<StatusWire> into)
        {
            int n = r.ReadCount(WireLimits.MaxStatuses, 3);
            for (int i = 0; i < n; i++)
            {
                into.Add(new StatusWire
                {
                    Kind = r.ReadByteInRange(0, 31),
                    DefinitionId = r.ReadId(),
                    ExpireTick = r.ReadVarInt(),
                    IntensityCenti = r.ReadVarInt(),
                });
            }
        }

        private static PlayerBits ReadBits(NetReader r)
        {
            uint bits = r.ReadVarUInt();
            if ((bits & ~(uint)PlayerBits.All) != 0) throw new NetReadException(NetReadError.InvalidValue, "unknown player bits");
            return (PlayerBits)bits;
        }

        private static int VelocityAxis(long v)
        {
            if (v < -(1 << 20) || v > 1 << 20) throw new NetReadException(NetReadError.InvalidValue, "velocity out of range");
            return (int)v;
        }

        /// <summary>
        /// What a viewer that cannot see this player receives (spec L120, research §2.6.1): the last pose it saw (or the
        /// origin when it never saw one) with no motion, no action state, no statuses, and no vitals; only alive,
        /// invisible, and hidden.
        /// </summary>
        public PlayerEntry AsHidden(PlayerEntry lastSeen)
        {
            PlayerEntry pose = lastSeen ?? new PlayerEntry();
            var hidden = new PlayerEntry
            {
                Position = pose.Position,
                Velocity = default,
                Yaw = pose.Yaw,
                Pitch = pose.Pitch,
                Bits = (Bits & (PlayerBits.Alive | PlayerBits.Invisible)) | PlayerBits.Hidden,
                ActiveWeapon = pose.ActiveWeapon,
                CastPhase = 0,
                CastSpellId = null,
                MeleePhase = 0,
                MeleeSerial = pose.MeleeSerial,
                HealthDeci = -1,
                MaxHealthDeci = -1,
                ShieldDeci = -1,
            };
            return hidden;
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

        private const byte PrMotor = 1, PrVitals = 2, PrSlots = 4, PrMachine = 8, PrContext = 16, PrTuning = 32, PrMisc = 64, PrAll = 127;

        /// <summary>
        /// Writes this private state as a change of the receiver's previous one (field groups, research §2.4.4): an
        /// unchanged motor tuning, context, or grammar costs nothing; the motor is exact whenever it changed.
        /// </summary>
        public void WriteAgainst(NetWriter w, PrivateEntry p)
        {
            byte mask = 0;
            if (!SameMotor(Motor, p.Motor)) mask |= PrMotor;
            if (Health != p.Health || MaxHealth != p.MaxHealth || Mana != p.Mana || MaxMana != p.MaxMana || Stamina != p.Stamina ||
                MaxStamina != p.MaxStamina || Shield != p.Shield || ReserveShield != p.ReserveShield || HealingRemaining != p.HealingRemaining)
            {
                mask |= PrVitals;
            }

            if (SelectedSlot != p.SelectedSlot || SignatureCooldownTicks != p.SignatureCooldownTicks || !SameInts(CooldownTicks, p.CooldownTicks)) mask |= PrSlots;
            if (MachinePhase != p.MachinePhase || MachineSpellId != p.MachineSpellId || MachinePhaseEndTick != p.MachinePhaseEndTick || !SameGrammar(Grammar, p.Grammar)) mask |= PrMachine;
            if (CastMovementFactor != p.CastMovementFactor || CastSuppressesSprint != p.CastSuppressesSprint || CastChanneling != p.CastChanneling ||
                SpeedMultiplier != p.SpeedMultiplier || Traction != p.Traction || SurfaceKind != p.SurfaceKind || SurfaceSlow != p.SurfaceSlow || Locks != p.Locks)
            {
                mask |= PrContext;
            }

            if (!SameTuning(Tuning, p.Tuning)) mask |= PrTuning;
            if (ControlImmuneUntil != p.ControlImmuneUntil || Judgement != p.Judgement || BoundaryOutsideTicks != p.BoundaryOutsideTicks) mask |= PrMisc;
            w.WriteByte(mask);
            if ((mask & PrMotor) != 0) WriteMotor(w, Motor);
            if ((mask & PrVitals) != 0) WriteVitals(w);
            if ((mask & PrSlots) != 0) WriteSlots(w);
            if ((mask & PrMachine) != 0) WriteMachine(w);
            if ((mask & PrContext) != 0) WriteContext(w);
            if ((mask & PrTuning) != 0) WriteTuning(w, Tuning);
            if ((mask & PrMisc) != 0) WriteMisc(w);
        }

        public static PrivateEntry ReadAgainst(NetReader r, PrivateEntry b)
        {
            byte mask = r.ReadByte();
            if ((mask & ~PrAll) != 0) throw new NetReadException(NetReadError.InvalidValue, "unknown private field group");
            var p = (PrivateEntry)b.MemberwiseClone();
            p.CooldownTicks = (int[])b.CooldownTicks.Clone();
            if ((mask & PrMotor) != 0) p.Motor = ReadMotor(r);
            if ((mask & PrVitals) != 0) p.ReadVitals(r);
            if ((mask & PrSlots) != 0) p.ReadSlots(r);
            if ((mask & PrMachine) != 0) p.ReadMachine(r);
            if ((mask & PrContext) != 0) p.ReadContext(r);
            if ((mask & PrTuning) != 0) p.Tuning = ReadTuning(r);
            if ((mask & PrMisc) != 0) p.ReadMisc(r);
            return p;
        }

        private void WriteVitals(NetWriter w)
        {
            w.WriteFloat(Health);
            w.WriteFloat(MaxHealth);
            w.WriteFloat(Mana);
            w.WriteFloat(MaxMana);
            w.WriteFloat(Stamina);
            w.WriteFloat(MaxStamina);
            w.WriteFloat(Shield);
            w.WriteFloat(ReserveShield);
            w.WriteFloat(HealingRemaining);
        }

        private void ReadVitals(NetReader r)
        {
            Health = r.ReadFloat();
            MaxHealth = r.ReadFloat();
            Mana = r.ReadFloat();
            MaxMana = r.ReadFloat();
            Stamina = r.ReadFloat();
            MaxStamina = r.ReadFloat();
            Shield = r.ReadFloat();
            ReserveShield = r.ReadFloat();
            HealingRemaining = r.ReadFloat();
        }

        private void WriteSlots(NetWriter w)
        {
            w.WriteByte((byte)SelectedSlot);
            w.WriteVarUInt((uint)CooldownTicks.Length);
            foreach (int c in CooldownTicks) w.WriteVarUInt((uint)Math.Max(0, c));
            w.WriteVarUInt((uint)Math.Max(0, SignatureCooldownTicks));
        }

        private void ReadSlots(NetReader r)
        {
            SelectedSlot = r.ReadByteInRange(0, 5);
            int n = r.ReadCount(6, 1);
            CooldownTicks = new int[n];
            for (int i = 0; i < n; i++) CooldownTicks[i] = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
            SignatureCooldownTicks = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
        }

        private void WriteMachine(NetWriter w)
        {
            w.WriteByte(MachinePhase);
            w.WriteId(MachineSpellId);
            w.WriteVarInt(MachinePhaseEndTick);
            WriteGrammar(w, Grammar);
        }

        private void ReadMachine(NetReader r)
        {
            MachinePhase = r.ReadByteInRange(0, (byte)Casting.CastPhase.Recovery);
            MachineSpellId = r.ReadId();
            MachinePhaseEndTick = r.ReadVarInt();
            Grammar = ReadGrammar(r);
        }

        private void WriteContext(NetWriter w)
        {
            w.WriteFloat(CastMovementFactor);
            w.WriteBool(CastSuppressesSprint);
            w.WriteBool(CastChanneling);
            w.WriteFloat(SpeedMultiplier);
            w.WriteFloat(Traction);
            w.WriteByte(SurfaceKind);
            w.WriteFloat(SurfaceSlow);
            w.WriteByte((byte)Locks);
        }

        private void ReadContext(NetReader r)
        {
            CastMovementFactor = r.ReadFloat();
            CastSuppressesSprint = r.ReadBool();
            CastChanneling = r.ReadBool();
            SpeedMultiplier = r.ReadFloat();
            Traction = r.ReadFloat();
            SurfaceKind = r.ReadByteInRange(0, 15);
            SurfaceSlow = r.ReadFloat();
            Locks = (MotorLocks)r.ReadByteInRange(0, (byte)MotorLocks.All);
        }

        private void WriteMisc(NetWriter w)
        {
            w.WriteVarInt(ControlImmuneUntil);
            w.WriteVarInt(Judgement);
            w.WriteVarUInt((uint)Math.Max(0, BoundaryOutsideTicks));
        }

        private void ReadMisc(NetReader r)
        {
            ControlImmuneUntil = r.ReadVarInt();
            Judgement = r.ReadVarIntInRange(-2, 1);
            BoundaryOutsideTicks = (int)Math.Min(int.MaxValue, r.ReadVarUInt());
        }

        private static bool SameInts(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SameMotor(MotorState a, MotorState b)
        {
            return a.Position == b.Position && a.Velocity == b.Velocity && a.YawDeg == b.YawDeg && a.PitchDeg == b.PitchDeg && a.Grounded == b.Grounded &&
                a.Crouched == b.Crouched && a.Sprinting == b.Sprinting && a.GroundNormal == b.GroundNormal && a.GroundSolidId == b.GroundSolidId &&
                a.AirPeakY == b.AirPeakY && a.SupportY == b.SupportY && a.DodgeTicks == b.DodgeTicks && a.DodgeVelocity == b.DodgeVelocity &&
                a.DodgeCooldownTicks == b.DodgeCooldownTicks && a.DisplaceTicks == b.DisplaceTicks && a.DisplaceVelocity == b.DisplaceVelocity &&
                a.DisplaceKind == b.DisplaceKind && a.LastGroundedPosition == b.LastGroundedPosition;
        }

        private static bool SameTuning(MotorTuning a, MotorTuning b)
        {
            if (a.Specified != b.Specified) return false;
            if (!a.Specified) return true;
            return a.WalkSpeed == b.WalkSpeed && a.SprintSpeed == b.SprintSpeed && a.CrouchSpeed == b.CrouchSpeed && a.WalkMultiplier == b.WalkMultiplier &&
                a.SprintMultiplier == b.SprintMultiplier && a.SprintStaminaPerSecond == b.SprintStaminaPerSecond && a.JumpStaminaCost == b.JumpStaminaCost &&
                a.DodgeStaminaCost == b.DodgeStaminaCost && a.DodgeDistance == b.DodgeDistance && a.DodgeCooldownTicks == b.DodgeCooldownTicks;
        }

        [ThreadStatic] private static NetWriter _grammarA;
        [ThreadStatic] private static NetWriter _grammarB;

        private static bool SameGrammar(GrammarUiState a, GrammarUiState b)
        {
            NetWriter wa = _grammarA ?? (_grammarA = new NetWriter(128));
            NetWriter wb = _grammarB ?? (_grammarB = new NetWriter(128));
            wa.Reset();
            wb.Reset();
            WriteGrammar(wa, a);
            WriteGrammar(wb, b);
            if (wa.Length != wb.Length) return false;
            for (int i = 0; i < wa.Length; i++)
                if (wa.Buffer[i] != wb.Buffer[i]) return false;
            return true;
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

        /// <summary>
        /// The receiver's private state as of this frame (baseline bookkeeping only: the next snapshot's private state is
        /// delta-coded against it). Null for spectator frames.
        /// </summary>
        public PrivateEntry Own;

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
