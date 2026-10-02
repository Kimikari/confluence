using System.Collections.Generic;
using Confluence.Client.Mapping;
using Confluence.Client.ViewModels;
using Confluence.Core.Geometry;
using Confluence.Sim.Combat;
using Confluence.Sim.Duel;
using UnityEngine;
using UnityEngine.UIElements;

namespace Confluence.Client.Presentation
{
    /// <summary>
    /// Floating numbers for hits the local player's attacks actually landed. Only authoritative
    /// <see cref="DamageEvent"/>s are shown — the universal feedback rule (spec L349): no hitmarker for shots into
    /// concealment unless the authoritative world confirms visible damage. In the offline Laboratory the local world is
    /// the authority; online, the same filter runs on server events. Guarded hits show the guard outcome.
    /// </summary>
    public sealed class DamageNumbers
    {
        public const float LifetimeSec = 1.1f;
        public const float RiseMetersPerSec = 0.9f;

        private readonly VisualElement _layer;
        private readonly List<Number> _active = new List<Number>();

        public DamageNumbers(VisualElement layer)
        {
            _layer = layer;
        }

        public int ActiveCount => _active.Count;

        /// <summary>
        /// True for an event this player may see a number for: damage the player dealt to the opponent that resolved
        /// with a mitigation breakdown (not rule damage such as the boundary, which has no attacker).
        /// </summary>
        public static bool IsConfirmedHit(DuelEvent e, int localPlayer, out DamageEvent hit)
        {
            hit = e as DamageEvent;
            return hit != null && hit.Attacker == localPlayer && hit.Target != localPlayer && hit.Resolution != null
                && (hit.Dealt > 0f || hit.Resolution.Guard != GuardOutcome.None);
        }

        public static string Text(DamageEvent hit)
        {
            string amount = ContentFormat.Number(Mathf.Round(hit.Dealt * 10f) / 10f);
            switch (hit.Resolution.Guard)
            {
                case GuardOutcome.PerfectGuard: return amount + " PERFECT GUARD";
                case GuardOutcome.Guarded: return amount + " guarded";
                default: return hit.Lethal ? amount + " LETHAL" : amount;
            }
        }

        public void Spawn(DamageEvent hit)
        {
            Vec3 at = hit.TargetPosition;
            var label = new Label(Text(hit)) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("cf-damage-number");
            label.EnableInClassList("cf-damage-number--guarded", hit.Resolution.Guard != GuardOutcome.None);
            label.EnableInClassList("cf-damage-number--lethal", hit.Lethal);
            label.style.position = Position.Absolute;
            _layer.Add(label);
            float spread = (_active.Count % 3 - 1) * 0.25f;
            _active.Add(new Number { Label = label, World = CoreSpace.ToUnity(at) + new Vector3(spread, 2.05f, 0f), Age = 0f });
        }

        /// <summary>Moves numbers up, fades them, and projects them through the camera; behind-camera numbers hide.</summary>
        public void Update(Camera camera, float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Number n = _active[i];
                n.Age += dt;
                if (n.Age >= LifetimeSec || camera == null || _layer.panel == null)
                {
                    n.Label.RemoveFromHierarchy();
                    _active.RemoveAt(i);
                    continue;
                }

                Vector3 world = n.World + Vector3.up * (RiseMetersPerSec * n.Age);
                Vector3 view = camera.WorldToViewportPoint(world);
                bool visible = view.z > 0f;
                n.Label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible)
                {
                    Vector2 panel = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, world, camera);
                    n.Label.style.left = panel.x - 40f;
                    n.Label.style.top = panel.y - 16f;
                    n.Label.style.opacity = 1f - Mathf.Clamp01((n.Age - LifetimeSec * 0.6f) / (LifetimeSec * 0.4f));
                }
            }
        }

        public void Clear()
        {
            foreach (Number n in _active)
            {
                n.Label.RemoveFromHierarchy();
            }

            _active.Clear();
        }

        private sealed class Number
        {
            public Label Label;
            public Vector3 World;
            public float Age;
        }
    }
}
