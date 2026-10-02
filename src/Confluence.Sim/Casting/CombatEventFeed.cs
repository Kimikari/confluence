using System.Collections.Generic;
using Confluence.Casting;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;

namespace Confluence.Sim.Casting
{
    /// <summary>
    /// Turns authoritative duel events into the <see cref="CombatEvent"/>s grammars read (spec L472, L482: parry and
    /// melee grammars need a genuine combat event; clients never claim them). Events emitted before a player's casting
    /// step reach that step; later ones (deliveries, reactions, statuses) reach the next tick's step, in emission order.
    /// </summary>
    public sealed class CombatEventFeed : IDuelEventObserver
    {
        private readonly List<CombatEvent>[] _pending = new List<CombatEvent>[DuelWorld.PlayerCount];

        public CombatEventFeed()
        {
            for (int i = 0; i < _pending.Length; i++) _pending[i] = new List<CombatEvent>();
        }

        /// <summary>Events waiting for the player's next casting step.</summary>
        public int PendingCount(int player) => _pending[player].Count;

        public void OnEvent(DuelWorld world, DuelEvent e)
        {
            switch (e)
            {
                case GuardEvent g when g.Change == GuardChange.PerfectBlocked:
                    Add(g.Player, CombatEventKind.PerfectGuard, e.Tick);
                    break;
                case GuardEvent g when g.Change == GuardChange.Blocked:
                    Add(g.Player, CombatEventKind.GuardBlock, e.Tick);
                    break;
                case MeleeEvent m when m.Change == MeleeChange.Hit:
                    Add(m.Player, CombatEventKind.MeleeHit, e.Tick);
                    break;
                case DamageEvent d:
                    if (d.Dealt > 0f) Add(d.Target, CombatEventKind.DamageTaken, e.Tick, d.SourceId, d.Dealt);
                    if (d.Kind == DamageKind.Melee && d.Resolution != null && d.Resolution.Guard != GuardOutcome.None)
                    {
                        Add(d.Target, CombatEventKind.MeleeParry, e.Tick, d.SourceId);
                    }

                    break;
                case MovementEvent mv:
                    switch (mv.Change)
                    {
                        case MovementChange.Landed:
                            Add(mv.Player, CombatEventKind.Landed, e.Tick, null, mv.Amount);
                            break;
                        case MovementChange.Teleported:
                            Add(mv.Player, CombatEventKind.Teleported, e.Tick, mv.SourceId, mv.Amount);
                            break;
                        case MovementChange.KnockbackStarted:
                        case MovementChange.EdgeImpact:
                            Add(mv.Player, CombatEventKind.ForcedDisplacement, e.Tick, mv.SourceId, mv.Amount);
                            break;
                    }

                    break;
            }
        }

        /// <summary>Moves the player's pending events into <paramref name="into"/> (in emission order).</summary>
        internal void Drain(int player, List<CombatEvent> into)
        {
            into.AddRange(_pending[player]);
            _pending[player].Clear();
        }

        public void Hash(ref Hashing.StateHasher h)
        {
            for (int p = 0; p < _pending.Length; p++)
            {
                h.Add(_pending[p].Count);
                for (int i = 0; i < _pending[p].Count; i++)
                {
                    h.Add((int)_pending[p][i].Kind);
                    h.Add(_pending[p][i].Tick);
                    h.Add(_pending[p][i].Value);
                }
            }
        }

        private void Add(int player, CombatEventKind kind, int tick, string spellId = null, float value = 0f)
        {
            if (player >= 0 && player < _pending.Length) _pending[player].Add(new CombatEvent(kind, tick, spellId, value));
        }
    }
}
