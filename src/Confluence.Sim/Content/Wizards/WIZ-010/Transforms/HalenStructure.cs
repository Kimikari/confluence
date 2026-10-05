using System;
using System.Collections.Generic;
using Confluence.Core.Content;
using Confluence.Sim.Spells;

namespace Confluence.Sim.Content.Wizards.HalenTide
{
    /// <summary>
    /// The structural tag changes of WIZ-010's transformations, declared once in Core's <see cref="StructuralDeltas"/>
    /// (D-012) so Economy eligibility and the Sim's resolved forms read the same profile (spec L243, L537, L543). Each
    /// transformation's behavior registers the table when it is created (the content registry creates every behavior), so
    /// any process that loads the registry sees the deltas. The authored timed puddles of AUG-435 / AUG-441 / AUG-446 are
    /// gameplay fields (owned, allocation-counted) and add "field". Transformations not listed keep their base spell's tags:
    /// reaction byproducts (puffs, mist and steam clouds) and parameter changes are not structure, as in the base records
    /// (SPL-009's steam patch adds no field tag).
    /// </summary>
    public static class HalenStructure
    {
        private static readonly object Gate = new object();
        private static bool _registered;

        /// <summary>(augment, target spell, added tags, removed tags) in augment order.</summary>
        public static readonly IReadOnlyList<Tuple<string, string, string[], string[]>> Table = new[]
        {
            // AUG-433 Jet Bank: one bank from a wet wall is a ricochet trajectory (SPL-049 "may bounce once" is ricochet).
            Tuple.Create("AUG-433", "SPL-073", new[] { "ricochet" }, new string[0]),

            // AUG-435 Hammer Wake, AUG-441 Landing Basin and AUG-446 Prison Pool: each leaves an authored timed puddle that
            // is a gameplay field (it takes the normal three-field allocation and retires the oldest, spec L128, L510), so
            // the branch adds a field (L543), as SPL-076's equivalent water pool carries "field".
            Tuple.Create("AUG-435", "SPL-074", new[] { "field" }, new string[0]),

            // AUG-436 Hammer Fan: "Thrust becomes a 75-degree 40-damage cone with no push" (an offensive close cone stays
            // melee, like SPL-010 Cinder Sweep).
            Tuple.Create("AUG-436", "SPL-074", new[] { "cone" }, new[] { "push" }),

            // AUG-437 Bubble Release: the recast throws a 20 m/s 24-damage physical bubble with a 20 m range (spec L535:
            // offensive range of at least 12 m is ranged).
            Tuple.Create("AUG-437", "SPL-075", new[] { "projectile", "damaging", "channel:physical", "ranged", "recast" }, new string[0]),

            // AUG-442 Cascade Brake and AUG-444 Riptide Valve add a recast (spec L549 "adding a recast shows a pending state").
            Tuple.Create("AUG-441", "SPL-077", new[] { "field" }, new string[0]),
            Tuple.Create("AUG-442", "SPL-077", new[] { "recast" }, new string[0]),
            Tuple.Create("AUG-444", "SPL-078", new[] { "recast" }, new string[0]),

            // AUG-445 Prison Roll: "Bubble rolls along ground ... instead of flying" (SPL-210's rolling boulder is grounded).
            Tuple.Create("AUG-445", "SPL-079", new[] { "grounded" }, new string[0]),
            Tuple.Create("AUG-446", "SPL-079", new[] { "field" }, new string[0]),

            // AUG-447 Spring Fan: "Utility becomes a 6 m cone".
            Tuple.Create("AUG-447", "SPL-080", new[] { "cone" }, new string[0]),
        };

        /// <summary>Registers every WIZ-010 structural delta (idempotent; a conflicting registration throws).</summary>
        public static void EnsureRegistered()
        {
            lock (Gate)
            {
                if (_registered && AllPresent()) return;
                for (int i = 0; i < Table.Count; i++)
                {
                    StructuralDeltas.Register(Table[i].Item1, Table[i].Item2, Table[i].Item3, Table[i].Item4);
                }

                _registered = true;
            }
        }

        private static bool AllPresent()
        {
            for (int i = 0; i < Table.Count; i++)
            {
                if (!StructuralDeltas.TryGet(Table[i].Item1, out _)) return false;
            }

            return true;
        }

        /// <summary>The kit transformation's form-stage base for the shared checks.</summary>
        internal static T Delivery<T>(SpellFormBuilder form, string augmentId) where T : DeliverySpec
        {
            if (form.Delivery is T typed) return typed;
            throw new InvalidOperationException(augmentId + " transforms " + form.SpellId + " but its delivery is " +
                (form.Delivery == null ? "missing" : form.Delivery.GetType().Name) + ", not " + typeof(T).Name + ".");
        }

        internal static void CopyDeposits(PayloadSpec from, PayloadSpec to)
        {
            for (int i = 0; i < from.Deposits.Count; i++)
            {
                DepositSpec d = from.Deposits[i];
                to.AddDeposit(d.Kind, d.Strength, d.Placement, d.Radius, d.LifetimeSec).HeavyImpact = d.HeavyImpact;
            }
        }
    }
}
