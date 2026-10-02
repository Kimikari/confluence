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
        public static List<string> Lines(IReadOnlyList<DuelEvent> events, int alcoveTicks, string[] names)
        {
            var lines = new List<string>();
            if (events == null)
            {
                return lines;
            }

            for (int i = 0; i < events.Count; i++)
            {
                string text = Describe(events[i], names);
                if (text != null)
                {
                    float seconds = (events[i].Tick - alcoveTicks) / (float)Ticks.PerSecond;
                    lines.Add(seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s  " + text);
                }
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

        private static string DescribeDamage(DamageEvent d, string[] names)
        {
            DamageResolution r = d.Resolution;
            string source = string.IsNullOrEmpty(d.SourceId) ? d.Kind.ToString() : d.SourceId;
            string head = (d.Attacker >= 0 ? Name(names, d.Attacker) + " hit " + Name(names, d.Target) : Name(names, d.Target) + " took")
                + " with " + source + " (" + d.Kind.ToString().ToLowerInvariant() + ")";
            if (r == null)
            {
                return head;
            }

            string text = head + ": " + ContentFormat.Number(r.Raw) + " raw";
            if (r.Guard != GuardOutcome.None)
            {
                text += ", " + (r.Guard == GuardOutcome.PerfectGuard ? "perfect guard" : "guard") + " -" + ContentFormat.Percent(r.GuardReduction);
            }

            if (r.Mitigated > 0f)
            {
                text += ", mitigated " + ContentFormat.Number(r.Mitigated);
            }

            if (r.ShieldAbsorbed > 0f)
            {
                text += ", shield " + ContentFormat.Number(r.ShieldAbsorbed);
            }

            text += ", health -" + ContentFormat.Number(r.HealthLost);
            return r.Lethal ? text + " (lethal)" : text;
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
    }
}
