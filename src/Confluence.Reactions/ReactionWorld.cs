using System;
using System.Collections.Generic;
using Confluence.Core.Arena;
using Confluence.Core.Geometry;
using Confluence.Core.Rules;
using Confluence.Core.Time;
using Confluence.Reactions.Electricity;
using Confluence.Reactions.Environment;
using Confluence.Reactions.Events;
using Confluence.Reactions.Grid;
using Confluence.Reactions.Hazards;
using Confluence.Reactions.Operations;
using Confluence.Reactions.Queries;
using Confluence.Reactions.Volumes;

namespace Confluence.Reactions
{
    /// <summary>
    /// The authoritative physical world state of one duel (spec §4 L130-L175, L654-L688): the 0.5 m material cell grid,
    /// server-owned volumes, conductors, and the fixed-order reaction engine.
    /// <para>
    /// Usage per simulation step: Sim queues this tick's world operations (deposits, impacts, discharges, winds,
    /// forces, dispels, volumes), then calls <see cref="Step"/> once with the current bodies. Step processes the
    /// phases of <see cref="GlobalRules.World.ReactionOrder"/> (spec L165): direct impacts, heat-water / heat-ice /
    /// frost-water consumption, electricity connectivity, wind movement, decay; then evaluates body contact. Operations
    /// queued in one tick are applied in a canonical order (server event id, then content), never in call order, so
    /// the result is identical on server, clients, and replays.
    /// </para>
    /// <para>Reactions never apply damage or move props; they return requests in the <see cref="StepResult"/>.</para>
    /// </summary>
    public sealed partial class ReactionWorld
    {
        /// <summary>Conductor id base for arena solids flagged metal or conductive (Sim props use their own ids below this).</summary>
        public const int SolidConductorIdBase = 1000000;

        /// <summary>Conductor id base for conductive material patches (plates such as MOD-039).</summary>
        public const int PatchConductorIdBase = 2000000;

        /// <summary>Prism id base for arena solids flagged reflective.</summary>
        public const int SolidPrismIdBase = 1000000;

        private readonly CellGrid _grid;
        private readonly bool[] _activeMark;
        private List<int> _active = new List<int>();
        private List<int> _activeScratch = new List<int>();
        private readonly List<int> _pendingActive = new List<int>();
        private readonly ActiveRegions _regions = new ActiveRegions();

        private readonly List<ObscurationVolume> _volumes = new List<ObscurationVolume>();
        private readonly Dictionary<int, ObscurationVolume> _volumeById = new Dictionary<int, ObscurationVolume>();
        private readonly List<Hazard> _hazards = new List<Hazard>();
        private readonly Dictionary<int, Hazard> _hazardById = new Dictionary<int, Hazard>();
        private readonly List<Conductor> _conductors = new List<Conductor>();
        private readonly Dictionary<int, Conductor> _conductorById = new Dictionary<int, Conductor>();
        private readonly List<PendingRelease> _releases = new List<PendingRelease>();
        private readonly List<ActiveWind> _winds = new List<ActiveWind>();
        private readonly List<ScheduledSource> _sources = new List<ScheduledSource>();
        private readonly List<ScheduledGust> _gusts = new List<ScheduledGust>();
        private readonly Dictionary<int, FieldLayer> _fieldLayers = new Dictionary<int, FieldLayer>();
        private readonly Dictionary<int, Solid> _solids = new Dictionary<int, Solid>();
        private readonly HashSet<int> _ignitedSolids = new HashSet<int>();
        private readonly List<LightShaft> _lightShafts = new List<LightShaft>();

        // Operations queued for the next step.
        private readonly List<MaterialDeposit> _deposits = new List<MaterialDeposit>();
        private readonly List<DirectImpact> _impacts = new List<DirectImpact>();
        private readonly List<ElectricDischarge> _discharges = new List<ElectricDischarge>();
        private readonly List<WindGust> _newWinds = new List<WindGust>();
        private readonly List<PropForce> _forces = new List<PropForce>();
        private readonly List<DispelRequest> _dispels = new List<DispelRequest>();
        private readonly List<VolumeSpec> _newVolumes = new List<VolumeSpec>();
        private readonly List<CondenseRequest> _condenses = new List<CondenseRequest>();
        private readonly List<UnitRemoval> _removals = new List<UnitRemoval>();
        private readonly List<DustRaise> _dustRaises = new List<DustRaise>();
        private readonly List<HotSurface> _hotSurfaces = new List<HotSurface>();
        private readonly List<VolumeClear> _clears = new List<VolumeClear>();

        private readonly StepResult _result = new StepResult();
        private readonly List<VolumeEvent> _pendingVolumeEvents = new List<VolumeEvent>();
        private readonly List<ReactionBody> _bodies = new List<ReactionBody>();
        private readonly List<int> _scratchCells = new List<int>();
        private readonly List<int> _scratchCells2 = new List<int>();

        private int _tick;
        private bool _hasStepped;
        private int _nextVolumeId = 1;
        private int _nextHazardId = 1;
        private int _nextSourceId = 1;

        /// <summary>Empty world over a grid (tests, Laboratory reaction garden).</summary>
        public ReactionWorld(CellGrid grid, int combatStartTick = 0)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _activeMark = new bool[grid.CellCount];
            CombatStartTick = combatStartTick;
            _tick = combatStartTick - 1;
            Prisms = new PrismSet();
            GravityFields = new GravityFieldSet();
            Sound = new SoundOcclusionQuery(null);
            Ledger = new UnitLedger();
        }

        /// <summary>
        /// World for an assembled arena: initial material cells (natural sources flagged), solid conductors and
        /// prisms, light shafts, and the biome environment rule (spec L664, L668).
        /// </summary>
        public ReactionWorld(ArenaLayout layout, int combatStartTick = 0)
            : this(CellGrid.ForArena(layout), combatStartTick)
        {
            Layout = layout;
            Sound = new SoundOcclusionQuery(layout.Solids);
            InitializeFromLayout(layout);
            EnvironmentRules.Install(this, layout);
            RebuildActive();
        }

        public CellGrid Grid => _grid;

        /// <summary>The arena this world was built from (null for bare grids).</summary>
        public ArenaLayout Layout { get; }

        /// <summary>Absolute tick of combat time 0 (biome schedules count from here).</summary>
        public int CombatStartTick { get; }

        /// <summary>Last stepped tick.</summary>
        public int Tick => _tick;

        public PrismSet Prisms { get; }
        public GravityFieldSet GravityFields { get; }
        public SoundOcclusionQuery Sound { get; }
        public UnitLedger Ledger { get; }

        /// <summary>Volumes in ascending id order.</summary>
        public IReadOnlyList<ObscurationVolume> Volumes => _volumes;

        /// <summary>Active regions after the last step (at most 128, spec L136).</summary>
        public IReadOnlyList<ActiveRegion> Regions => _regions.Regions;

        /// <summary>Connected components of active cells before cap merging.</summary>
        public int RegionComponentCount => _regions.ComponentCount;

        public int ActiveCellCount => _active.Count;

        /// <summary>Active cell indices in ascending order.</summary>
        public IReadOnlyList<int> ActiveCells => _active;

        public IReadOnlyList<ScheduledSource> ScheduledSources => _sources;

        public IReadOnlyList<LightShaft> LightShafts => _lightShafts;

        // ------------------------------------------------------------------ queueing (applied at the next Step)

        public void Deposit(MaterialDeposit deposit) => _deposits.Add(Require(deposit));
        public void Impact(DirectImpact impact) => _impacts.Add(Require(impact));
        public void Discharge(ElectricDischarge discharge) => _discharges.Add(Require(discharge));
        public void Wind(WindGust gust) => _newWinds.Add(Require(gust));
        public void ApplyForce(PropForce force) => _forces.Add(Require(force));
        public void Dispel(DispelRequest dispel) => _dispels.Add(Require(dispel));

        /// <summary>Queues a volume; its id arrives in the step's <see cref="VolumeEventKind.Created"/> event.</summary>
        public void CreateVolume(VolumeSpec spec) => _newVolumes.Add(Require(spec));

        public void Condense(CondenseRequest request) => _condenses.Add(Require(request));
        public void RemoveUnits(UnitRemoval removal) => _removals.Add(Require(removal));
        public void RaiseDust(DustRaise raise) => _dustRaises.Add(Require(raise));
        public void AddHotSurface(HotSurface surface) => _hotSurfaces.Add(Require(surface));
        public void ClearVolumes(VolumeClear clear) => _clears.Add(Require(clear));

        private static T Require<T>(T op) where T : class => op ?? throw new ArgumentNullException(nameof(op));

        // ------------------------------------------------------------------ immediate registration

        /// <summary>Registers or replaces a conductive object (spec L149-L150).</summary>
        public void RegisterConductor(ConductorDef def)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            RemoveConductor(def.Id);
            var conductor = new Conductor(def.Clone());
            _conductorById[def.Id] = conductor;
            _conductors.Add(conductor);
            _conductors.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        /// <summary>Removes a destroyed conductor; pending releases on it are dropped.</summary>
        public bool RemoveConductor(int id)
        {
            if (!_conductorById.Remove(id))
            {
                return false;
            }

            _conductors.RemoveAll(c => c.Id == id);
            _releases.RemoveAll(r => r.ConductorId == id);
            return true;
        }

        /// <summary>Enables or disables a conductor's grounding (for example its ground was destroyed, L150).</summary>
        public void SetConductorGrounding(int id, bool enabled)
        {
            if (_conductorById.TryGetValue(id, out Conductor c))
            {
                c.GroundingEnabled = enabled;
            }
        }

        public bool HasConductor(int id) => _conductorById.ContainsKey(id);

        /// <summary>True when the conductor would currently absorb a branch.</summary>
        public bool IsConductorGrounded(int id) => _conductorById.TryGetValue(id, out Conductor c) && c.CanAbsorb;

        public bool IsConductorOverloaded(int id) => _conductorById.TryGetValue(id, out Conductor c) && c.OverloadedUntil != 0;

        public int ConductorRemainingCapacity(int id) => _conductorById.TryGetValue(id, out Conductor c) ? c.RemainingCapacity : 0;

        public IReadOnlyList<int> ConductorIds
        {
            get
            {
                var ids = new List<int>(_conductors.Count);
                for (int i = 0; i < _conductors.Count; i++) ids.Add(_conductors[i].Id);
                return ids;
            }
        }

        /// <summary>Adds a scheduled refill source (spec L138, L668). Assigns an id when 0.</summary>
        public void AddScheduledSource(ScheduledSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.Id == 0)
            {
                source.Id = _nextSourceId++;
            }
            else
            {
                _nextSourceId = Math.Max(_nextSourceId, source.Id + 1);
            }

            source.Cells.Sort();
            if (source.NextPulseTick == 0)
            {
                source.NextPulseTick = source.FirstPulseTick;
            }

            _sources.Add(source);
            _sources.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        /// <summary>Adds an announced gust from the arena schedule (spec L664).</summary>
        public void AddScheduledGust(int startTick, WindGust gust)
        {
            _gusts.Add(new ScheduledGust { StartTick = startTick, Gust = Require(gust) });
            _gusts.Sort((a, b) => a.StartTick.CompareTo(b.StartTick));
        }

        /// <summary>Removes a destroyed solid from impact, ignition, and sound queries.</summary>
        public void RemoveSolid(int solidId)
        {
            _solids.Remove(solidId);
            Sound.RemoveSolid(solidId);
            RemoveConductor(SolidConductorIdBase + solidId);
            Prisms.Remove(SolidPrismIdBase + solidId);
        }

        /// <summary>Adds a light shaft (spec L320) that drains shadow concealment (L158).</summary>
        public void AddLightShaft(LightShaft shaft) => _lightShafts.Add(Require(shaft));

        /// <summary>
        /// Retires a volume immediately (Sim's oldest-field retirement at the three-field limit, spec L128, or a spell
        /// that consumes a cloud). The retirement event arrives with the next step's results.
        /// </summary>
        public bool RetireVolume(int volumeId, VolumeRetireReason reason = VolumeRetireReason.Requested)
        {
            if (!_volumeById.TryGetValue(volumeId, out ObscurationVolume volume))
            {
                return false;
            }

            RetireVolumeInternal(volume, reason, _pendingVolumeEvents, _tick);
            return true;
        }

        // ------------------------------------------------------------------ step

        /// <summary>
        /// Processes every queued operation and the world's own timers for one simulation tick, in the fixed phase
        /// order of spec L165, then evaluates contact of the given bodies with reaction hazards. Ticks must increase.
        /// The returned result is reused by the next call.
        /// </summary>
        public StepResult Step(int tick, IReadOnlyList<ReactionBody> bodies)
        {
            if (_hasStepped && tick <= _tick)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Reaction steps must advance: last " + _tick + ", got " + tick + ".");
            }

            _hasStepped = true;
            _tick = tick;
            _result.Clear();
            _result.Tick = tick;
            _result.VolumeEventList.AddRange(_pendingVolumeEvents);
            _pendingVolumeEvents.Clear();
            PrepareBodies(bodies);

            IReadOnlyList<ReactionPhase> order = GlobalRules.World.ReactionOrder;
            for (int i = 0; i < order.Count; i++)
            {
                switch (order[i])
                {
                    case ReactionPhase.DirectImpacts:
                        RunDirectImpacts();
                        break;
                    case ReactionPhase.MaterialConsumption:
                        RunConsumption();
                        break;
                    case ReactionPhase.ElectricityConnectivity:
                        RunElectricity();
                        break;
                    case ReactionPhase.WindMovement:
                        RunWind();
                        break;
                    case ReactionPhase.Decay:
                        RunDecay();
                        break;
                }
            }

            RunContacts();
            return _result;
        }

        private void PrepareBodies(IReadOnlyList<ReactionBody> bodies)
        {
            _bodies.Clear();
            if (bodies != null)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    _bodies.Add(bodies[i]);
                }
            }

            _bodies.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        // ------------------------------------------------------------------ queries

        public CellUnits UnitsAt(Vec3 point) => UnitsAt(point.XZ());

        public CellUnits UnitsAt(Vec2 point) => _grid.TryGetIndex(point, out int i) ? _grid[i].Units() : default;

        public CellUnits UnitsAtCell(int index) => _grid[index].Units();

        /// <summary>Natural (map-owned, permanent) units of one material in a cell.</summary>
        public int NaturalUnitsAtCell(int index, Material material) => Cell.Slot(ref _grid[index], material).Natural;

        /// <summary>Liquid water present and not frozen (spec L118: Wet increases reaction eligibility).</summary>
        public bool IsWetAt(Vec3 point) => _grid.TryGetIndex(point, out int i) && _grid[i].IsWet;

        public bool IsWetCell(int index) => _grid[index].IsWet;

        public bool HasIceAt(Vec3 point) => _grid.TryGetIndex(point, out int i) && _grid[i].HasIce;

        /// <summary>Remaining durability of the ice tile at a point (0 when none, L145).</summary>
        public float IceDurabilityAt(Vec3 point) => _grid.TryGetIndex(point, out int i) ? _grid[i].IceDurability : 0f;

        public bool IsBurningAt(Vec3 point) => _grid.TryGetIndex(point, out int i) && _grid[i].IsBurning;

        public bool IsChargedAt(Vec3 point) => _grid.TryGetIndex(point, out int i) && _grid[i].ChargeHazard != 0;

        public bool IsDampAt(Vec3 point) => _grid.TryGetIndex(point, out int i) && _grid[i].DampUnits > 0;

        /// <summary>Dust or sand units that can still raise dust here; damp sand suppresses its paired units (L154).</summary>
        public int DustProductionAt(Vec3 point) => _grid.TryGetIndex(point, out int i) ? DustProduction(ref _grid[i]) : 0;

        /// <summary>Surface conditions for movement, footsteps, and status eligibility (spec L175).</summary>
        public SurfaceInfo SurfaceAt(Vec3 point)
        {
            if (!_grid.TryGetIndex(point, out int index))
            {
                return new SurfaceInfo(SurfaceKind.Normal, SurfaceFlags.None, default, -1);
            }

            ref Cell c = ref _grid[index];
            SurfaceFlags flags = SurfaceFlags.None;
            if (c.IsWet) flags |= SurfaceFlags.Wet;
            if (c.HasIce) flags |= SurfaceFlags.Ice;
            if (c.Sand.Units > 0 || c.Dust.Units > 0) flags |= SurfaceFlags.Sand;
            if (c.DampUnits > 0) flags |= SurfaceFlags.DampSand;
            if (c.Growth.Units > 0)
            {
                flags |= SurfaceFlags.Growth;
                if ((c.State & CellState.GrowthSticky) != 0) flags |= SurfaceFlags.StickyGrowth;
            }

            if (c.IsBurning) flags |= SurfaceFlags.Burning;
            if (c.ChargeHazard != 0) flags |= SurfaceFlags.Charged;
            if (c.Venom.Units > 0) flags |= SurfaceFlags.Venom;
            if (c.Heat.Units > 0) flags |= SurfaceFlags.Hot;
            if (c.BurnSuppressedUntil > _tick) flags |= SurfaceFlags.Extinguished;
            if ((c.Flags & (MaterialFlags.Conductive | MaterialFlags.Metal)) != 0) flags |= SurfaceFlags.Conductive;
            return new SurfaceInfo(SurfaceInfo.Classify(flags), flags, c.Units(), index);
        }

        public ObscurationVolume FindVolume(int id) => _volumeById.TryGetValue(id, out ObscurationVolume v) ? v : null;

        /// <summary>Volumes of one owner that count against the field limit, oldest first (spec L128).</summary>
        public void GetOwnedFieldVolumes(int owner, List<ObscurationVolume> results)
        {
            for (int i = 0; i < _volumes.Count; i++)
            {
                if (_volumes[i].Owner == owner && _volumes[i].CountsAsField)
                {
                    results.Add(_volumes[i]);
                }
            }

            results.Sort((a, b) => a.CreatedTick != b.CreatedTick ? a.CreatedTick.CompareTo(b.CreatedTick) : a.Id.CompareTo(b.Id));
        }

        /// <summary>Volumes containing a point, in id order.</summary>
        public void GetVolumesAt(Vec3 point, List<ObscurationVolume> results)
        {
            for (int i = 0; i < _volumes.Count; i++)
            {
                if (_volumes[i].ContainsPoint(point))
                {
                    results.Add(_volumes[i]);
                }
            }
        }

        public int HazardCount => _hazards.Count;

        // ------------------------------------------------------------------ cell helpers

        private void Touch(int index)
        {
            if (!_activeMark[index])
            {
                _activeMark[index] = true;
                _pendingActive.Add(index);
            }
        }

        /// <summary>Active cells in ascending order, including cells touched since the last rebuild.</summary>
        private List<int> CurrentActive()
        {
            if (_pendingActive.Count == 0)
            {
                return _active;
            }

            _pendingActive.Sort();
            _activeScratch.Clear();
            int a = 0;
            int p = 0;
            while (a < _active.Count || p < _pendingActive.Count)
            {
                if (p >= _pendingActive.Count || (a < _active.Count && _active[a] < _pendingActive[p]))
                {
                    _activeScratch.Add(_active[a++]);
                }
                else
                {
                    _activeScratch.Add(_pendingActive[p++]);
                }
            }

            _pendingActive.Clear();
            List<int> swap = _active;
            _active = _activeScratch;
            _activeScratch = swap;
            return _active;
        }

        /// <summary>Drops inactive cells from the active list and rebuilds regions (end of decay).</summary>
        private void RebuildActive()
        {
            List<int> current = CurrentActive();
            _activeScratch.Clear();
            for (int i = 0; i < current.Count; i++)
            {
                int index = current[i];
                if (_grid[index].IsActive(_tick))
                {
                    _activeScratch.Add(index);
                }
                else
                {
                    _activeMark[index] = false;
                }
            }

            List<int> swap = _active;
            _active = _activeScratch;
            _activeScratch = swap;
            _regions.Rebuild(_grid, _active, _activeMark);
        }

        private int PlayerLifetimeCap(int owner, int lifetimeTicks)
        {
            if (owner == ReactionRules.MapOwner || lifetimeTicks <= 0)
            {
                return Math.Max(0, lifetimeTicks);
            }

            return Math.Min(lifetimeTicks, Ticks.FromSeconds(ReactionRules.PlayerFieldMaxLifetimeSec));
        }

        /// <summary>Adds temporary units under the four-unit cap; returns the units actually added (L163).</summary>
        private int AddTemporary(int index, Material material, int units, int expiry, int owner, int source, int fieldId, float declared)
        {
            ref Cell c = ref _grid[index];
            ref MaterialSlot s = ref Cell.Slot(ref c, material);
            int add = Math.Max(0, Math.Min(units, Materials.MaxUnits - s.Units));
            if (add <= 0)
            {
                return 0;
            }

            if (s.Temporary == 0 || expiry >= s.Expiry)
            {
                s.Expiry = expiry;
                s.Field = fieldId;
            }

            s.Units = (byte)(s.Units + add);
            s.Owner = owner;
            s.Source = source;
            if (declared > 0f)
            {
                s.Declared = declared;
            }

            Touch(index);
            return add;
        }

        /// <summary>Adds natural (map-owned, permanent) units up to a per-cell maximum; returns the units added.</summary>
        private int AddNatural(int index, Material material, int units, int maxUnits)
        {
            ref Cell c = ref _grid[index];
            ref MaterialSlot s = ref Cell.Slot(ref c, material);
            int cap = Math.Min(Materials.MaxUnits, maxUnits);
            int add = Math.Max(0, Math.Min(units, cap - s.Units));
            if (add <= 0)
            {
                return 0;
            }

            s.Units = (byte)(s.Units + add);
            s.Natural = (byte)(s.Natural + add);
            if (s.Units == add)
            {
                s.Owner = ReactionRules.MapOwner;
                s.Source = ReactionRules.NoSource;
            }

            Touch(index);
            return add;
        }

        /// <summary>Removes units, temporary first; returns the units removed. Clears the slot when it empties.</summary>
        private int TakeUnits(int index, Material material, int units)
        {
            ref Cell c = ref _grid[index];
            ref MaterialSlot s = ref Cell.Slot(ref c, material);
            int take = Math.Max(0, Math.Min(units, s.Units));
            if (take == 0)
            {
                return 0;
            }

            int fromTemporary = Math.Min(take, s.Temporary);
            s.Units = (byte)(s.Units - take);
            s.Natural = (byte)(s.Natural - (take - fromTemporary));
            if (s.Units == 0)
            {
                ClearSlot(ref c, material);
            }
            else if (s.Temporary == 0)
            {
                s.Field = 0;
            }

            return take;
        }

        private void ClearSlot(ref Cell c, Material material)
        {
            Cell.Slot(ref c, material).Clear();
            if (material == Material.Growth)
            {
                c.State &= ~(CellState.GrowthCombustible | CellState.GrowthSticky);
            }
            else if (material == Material.Venom && c.VenomHazard != 0)
            {
                ReleaseCellHazard(c.VenomHazard);
                c.VenomHazard = 0;
            }
        }

        private static int DustProduction(ref Cell c) => Math.Max(0, c.Dust.Units + c.Sand.Units - c.DampUnits);

        /// <summary>The later of two slots' committing sources (spec L670: the last direct source owns the reaction).</summary>
        private static void Commit(ref MaterialSlot first, ref MaterialSlot second, out int owner, out int source)
        {
            if (second.Source > first.Source)
            {
                owner = second.Owner;
                source = second.Source;
            }
            else
            {
                owner = first.Owner;
                source = first.Source;
            }
        }

        private void Emit(ReactionKind kind, int owner, int source, Vec3 position, int cellIndex, int amount, int targetId)
        {
            _result.EventList.Add(new ReactionEvent
            {
                Tick = _tick,
                Kind = kind,
                Owner = owner,
                SourceEventId = source,
                Position = position,
                CellIndex = cellIndex,
                Amount = amount,
                TargetId = targetId,
            });
        }

        private Vec3 CellPoint(int index)
        {
            Vec2 c = _grid.CellCenter(index);
            return new Vec3(c.X, _grid[index].SurfaceY, c.Y);
        }

        // ------------------------------------------------------------------ hazards

        private Hazard NewHazard(ReactionKind kind, HazardShape shape, int owner, int source, DamageChannel channel, float amount, bool perSecond, int expiresTick)
        {
            var hazard = new Hazard
            {
                Id = _nextHazardId++,
                Kind = kind,
                Shape = shape,
                Owner = owner,
                Source = source,
                Channel = channel,
                Amount = amount,
                PerSecond = perSecond,
                CreatedTick = _tick,
                ExpiresTick = expiresTick,
            };
            _hazards.Add(hazard);
            _hazardById[hazard.Id] = hazard;
            return hazard;
        }

        private void AttachCellHazard(ref int slot, Hazard hazard)
        {
            if (slot == hazard.Id)
            {
                return;
            }

            if (slot != 0)
            {
                ReleaseCellHazard(slot);
            }

            slot = hazard.Id;
            hazard.CellCount++;
        }

        private void ReleaseCellHazard(int hazardId)
        {
            if (_hazardById.TryGetValue(hazardId, out Hazard hazard) && hazard.CellCount > 0)
            {
                hazard.CellCount--;
            }
        }

        private void RemoveHazard(Hazard hazard)
        {
            _hazardById.Remove(hazard.Id);
            _hazards.Remove(hazard);
        }

        // ------------------------------------------------------------------ field layers (dispel bookkeeping)

        private sealed class FieldLayer
        {
            public int Id;
            public int Owner;
            public int CreatedTick;
            public bool Removable;
        }

        private void NoteFieldLayer(int fieldId, int owner, bool removable)
        {
            if (fieldId == 0 || _fieldLayers.ContainsKey(fieldId))
            {
                return;
            }

            _fieldLayers[fieldId] = new FieldLayer { Id = fieldId, Owner = owner, CreatedTick = _tick, Removable = removable };
        }
    }
}
