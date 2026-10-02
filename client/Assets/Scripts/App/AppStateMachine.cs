using System;
using System.Collections.Generic;

namespace Confluence.Client.App
{
    /// <summary>
    /// The client's flow state with an explicit transition table, so screens can only move along legal edges
    /// (for example combat always passes through the alcove, spec L46-L47). Plain C#: no Unity dependency, so
    /// EditMode tests exercise it directly. Listeners run synchronously after the state changes.
    /// </summary>
    public sealed class AppStateMachine
    {
        // Ordered target lists: placeholder screens render their buttons in this order.
        private static readonly Dictionary<AppState, AppState[]> Transitions = new Dictionary<AppState, AppState[]>
        {
            { AppState.Boot, new[] { AppState.MainMenu, AppState.ArenaPreview, AppState.Laboratory } },
            { AppState.MainMenu, new[] { AppState.Connecting, AppState.Laboratory, AppState.Training, AppState.Codex, AppState.ArenaPreview } },
            { AppState.Connecting, new[] { AppState.Lobby, AppState.MainMenu } },
            { AppState.Lobby, new[] { AppState.Shop, AppState.MainMenu } },

            // Spec L45: the draft follows the shop only on augment rounds; otherwise the shop leads to the alcove.
            { AppState.Shop, new[] { AppState.Draft, AppState.Alcove, AppState.MainMenu } },
            { AppState.Draft, new[] { AppState.Alcove, AppState.MainMenu } },
            { AppState.Alcove, new[] { AppState.Duel, AppState.MainMenu } },
            { AppState.Duel, new[] { AppState.Results, AppState.MainMenu } },

            // Spec L49, L36-L38: results lead to the next round's shop, or back to the lobby when the match ends.
            { AppState.Results, new[] { AppState.Shop, AppState.Lobby, AppState.MainMenu } },
            { AppState.Training, new[] { AppState.Laboratory, AppState.ArenaPreview, AppState.MainMenu } },
            { AppState.Codex, new[] { AppState.MainMenu } },
            { AppState.ArenaPreview, new[] { AppState.MainMenu, AppState.Training } },
            { AppState.Laboratory, new[] { AppState.MainMenu, AppState.Training } },
        };

        public AppStateMachine(AppState initial = AppState.Boot)
        {
            Current = initial;
        }

        public AppState Current { get; private set; }

        public AppState Previous { get; private set; }

        /// <summary>Raised after every successful transition with (from, to).</summary>
        public event Action<AppState, AppState> StateChanged;

        public static IReadOnlyList<AppState> LegalTargets(AppState from)
        {
            return Transitions.TryGetValue(from, out AppState[] targets) ? targets : Array.Empty<AppState>();
        }

        public bool CanTransitionTo(AppState target)
        {
            IReadOnlyList<AppState> targets = LegalTargets(Current);
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryTransitionTo(AppState target)
        {
            if (!CanTransitionTo(target))
            {
                return false;
            }

            AppState from = Current;
            Previous = from;
            Current = target;
            StateChanged?.Invoke(from, target);
            return true;
        }

        /// <summary>Transitions or throws <see cref="InvalidOperationException"/> for an illegal edge.</summary>
        public void TransitionTo(AppState target)
        {
            if (!TryTransitionTo(target))
            {
                throw new InvalidOperationException("Illegal client state transition " + Current + " -> " + target + ".");
            }
        }
    }
}
