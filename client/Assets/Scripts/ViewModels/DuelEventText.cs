using System.Collections.Generic;
using System.Globalization;
using Confluence.Core.Time;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;

namespace Confluence.Client.ViewModels
{
    /// <summary>
    /// Numeric one-line text for authoritative duel events: the expandable "numeric events" form of the results
    /// screen's decisive sequence (spec L359). The narrative explanation ("Fireball heated Water Pool; ...") needs the
    /// spell and reaction runtimes and is generated from these same events later.
    /// </summary>
    public static class DuelEventText
    {
        /// <summary>
        /// Repeated damage from one source to one target (boundary ticks, status and environment pulses) merges into one
        /// line while consecutive pulses are at most this far apart.
        /// </summary>
        public static readonly int MergeGapTicks = Ticks.FromSeconds(0.6f);

        public static List<string> Lines(IReadOnlyList<DuelEvent> events, int alcoveTicks, string[] names)
        {
            var lines = new List<string>();
            if (events == null)
            {
                return lines;
            }

            // Entries keep the order of each line's first event; open runs collect later pulses of the same source.
            var entries = new List<Entry>();
            var open = new Dictionary<string, Entry>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] is DamageEvent d && IsPeriodic(d.Kind))
                {
                    string key = d.Attacker + "|" + d.Target + "|" + d.Kind + "|" + d.SourceId;
                    if (open.TryGetValue(key, out Entry run) && d.Tick - run.LastTick <= MergeGapTicks)
                    {
                        run.Add(d);
                        continue;
                    }

                    run = new Entry(d);
                    open[key] = run;
                    entries.Add(run);
                    continue;
                }

                string text = Describe(events[i], names);
                if (text != null)
                {
                    entries.Add(new Entry(events[i].Tick, text));
                }
            }

            foreach (Entry entry in entries)
            {
                string time = Seconds(entry.FirstTick, alcoveTicks);
                if (entry.LastTick != entry.FirstTick)
                {
                    time += "-" + Seconds(entry.LastTick, alcoveTicks);
                }

                lines.Add(time + " s  " + (entry.Text ?? entry.Describe(names)));
            }

            return lines;
        }

        /// <summary>One event in words, or null for events the sequence does not list.</summary>
        public static string Describe(DuelEvent e, string[] names)
        {
            switch (e)
            {
                case DamageEvent d:
                    return DescribeDamage(d, names);
                case GuardEvent g:
                    return Name(names, g.Player) + " guard " + g.Change.ToString().ToLowerInvariant()
                        + (g.ManaGained > 0f ? " (+" + ContentFormat.Number(g.ManaGained) + " mana)" : string.Empty);
                case MeleeEvent m when m.Change == MeleeChange.Started:
                    return Name(names, m.Player) + " started a " + m.ProfileName + " melee strike";
                case StatusEvent s:
                    return Name(names, s.Player) + " " + s.Kind + " " + s.Change.ToString().ToLowerInvariant();
                case ShieldEvent sh:
                    return Name(names, sh.Player) + " shield " + sh.Change.ToString().ToLowerInvariant() + " " + ContentFormat.Number(sh.Amount);
                case MovementEvent mv when mv.Change == MovementChange.Dodged || mv.Change == MovementChange.KnockbackStarted
                    || mv.Change == MovementChange.FellOutOfMap || mv.Change == MovementChange.EdgeImpact:
                    return Name(names, mv.Player) + " " + Words(mv.Change.ToString());
                case BoundaryEvent b:
                    return Name(names, b.Player) + " boundary " + b.Change.ToString().ToLowerInvariant();
                case DuelEndEvent end:
                    return end.Outcome == DuelOutcome.Draw ? "Draw (" + end.Reason + ")" : Name(names, end.Winner) + " won (" + end.Reason + ")";
                default:
                    return null;
            }
        }

        /// <summary>Damage that arrives as a stream of small pulses rather than as one hit.</summary>
        public static bool IsPeriodic(DamageKind kind)
        {
            return kind == DamageKind.Boundary || kind == DamageKind.Status || kind == DamageKind.Environment;
        }

        private static string DescribeDamage(DamageEvent d, string[] names)
        {
            DamageResolution r = d.Resolution;
            string text = Head(d, names, 1);
            if (r == null)
            {
                return text;
            }

            text += ": " + ContentFormat.Number(r.Raw) + " raw";
            if (r.Guard != GuardOutcome.None)
            {
                text += ", " + (r.Guard == GuardOutcome.PerfectGuard ? "perfect guard" : "guard") + " -" + ContentFormat.Percent(r.GuardReduction);
            }

            // Mitigated is the amount after every passive reduction; listed only when the reductions changed anything.
            float beforeReductions = r.Guard != GuardOutcome.None ? r.AfterGuard : r.Raw;
            if (r.Mitigated < beforeReductions - 0.005f)
            {
                text += ", " + ContentFormat.Number(r.Mitigated) + " after reductions";
            }

            if (r.ShieldAbsorbed > 0f)
            {
                text += ", shield " + ContentFormat.Number(r.ShieldAbsorbed);
            }

            text += ", health -" + ContentFormat.Number(r.HealthLost);
            return r.Lethal ? text + " (lethal)" : text;
        }

        // "You hit Dummy with SPL-009 (direct)" for a player's attack; "You took boundary damage (RULE:BOUNDARY)" for rules.
        private static string Head(DamageEvent d, string[] names, int count)
        {
            string kind = d.Kind.ToString().ToLowerInvariant();
            string times = count > 1 ? " x" + count : string.Empty;
            if (d.Attacker >= 0)
            {
                string source = string.IsNullOrEmpty(d.SourceId) ? kind : d.SourceId + " (" + kind + ")";
                return Name(names, d.Attacker) + " hit " + Name(names, d.Target) + " with " + source + times;
            }

            return Name(names, d.Target) + " took " + kind + " damage" + times + (string.IsNullOrEmpty(d.SourceId) ? string.Empty : " (" + d.SourceId + ")");
        }

        private static string Seconds(int tick, int alcoveTicks)
        {
            return ((tick - alcoveTicks) / (float)Ticks.PerSecond).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Name(string[] names, int player)
        {
            return names != null && player >= 0 && player < names.Length ? names[player] : "player " + player;
        }

        private static string Words(string pascal)
        {
            var text = new System.Text.StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && char.IsUpper(c))
                {
                    text.Append(' ');
                }

                text.Append(char.ToLowerInvariant(c));
            }

            return text.ToString();
        }

        // One listed line: a single event's text, or a run of periodic damage pulses summed.
        private sealed class Entry
        {
            private readonly DamageEvent _first;
            private int _count;
            private float _raw;
            private float _shield;
            private float _health;
            private bool _lethal;

            public Entry(int tick, string text)
            {
                FirstTick = tick;
                LastTick = tick;
                Text = text;
            }

            public Entry(DamageEvent first)
            {
                _first = first;
                FirstTick = first.Tick;
                LastTick = first.Tick;
                Add(first);
            }

            public int FirstTick { get; }

            public int LastTick { get; private set; }

            public string Text { get; }

            public void Add(DamageEvent d)
            {
                LastTick = d.Tick;
                _count++;
                if (d.Resolution != null)
                {
                    _raw += d.Resolution.Raw;
                    _shield += d.Resolution.ShieldAbsorbed;
                    _health += d.Resolution.HealthLost;
                    _lethal |= d.Resolution.Lethal;
                }
            }

            public string Describe(string[] names)
            {
                if (_count == 1)
                {
                    return DescribeDamage(_first, names);
                }

                string text = Head(_first, names, _count) + ": " + ContentFormat.Number(_raw) + " raw in total";
                if (_shield > 0f)
                {
                    text += ", shield " + ContentFormat.Number(_shield);
                }

                text += ", health -" + ContentFormat.Number(_health);
                return _lethal ? text + " (lethal)" : text;
            }
        }
    }
}
