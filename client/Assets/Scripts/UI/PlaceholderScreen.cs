using System.Collections.Generic;
using Confluence.Client.App;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Screen for flow states whose real UI arrives with networking (Stage 3-4): it states what the phase is
    /// (with its spec reference) and offers exactly the legal next states from <see cref="AppStateMachine"/>.
    /// </summary>
    public static class PlaceholderScreen
    {
        public static VisualElement Build(ClientContext context, AppState state)
        {
            VisualElement screen = Ui.Element("cf-screen", "cf-screen--menu");
            VisualElement column = Ui.Column();
            column.style.maxWidth = 1100;
            column.Add(Ui.Label(Title(state), "cf-title"));
            column.Add(Ui.Text(Description(context, state)));
            column.Add(Ui.Muted("Placeholder: this phase has no gameplay yet. The buttons below follow the client state machine."));

            VisualElement buttons = Ui.Column();
            buttons.style.marginTop = 24;
            buttons.style.width = 420;
            IReadOnlyList<AppState> targets = AppStateMachine.LegalTargets(state);
            for (int i = 0; i < targets.Count; i++)
            {
                AppState target = targets[i];
                buttons.Add(Ui.Button(ButtonText(state, target), () => context.States.TryTransitionTo(target), i == 0 ? "cf-button--primary" : null));
            }

            column.Add(buttons);
            screen.Add(column);
            return screen;
        }

        public static string Title(AppState state)
        {
            switch (state)
            {
                case AppState.Connecting: return "Connecting";
                case AppState.Lobby: return "Lobby";
                case AppState.Shop: return "Shop";
                case AppState.Draft: return "Augment draft";
                case AppState.Alcove: return "Alcove";
                case AppState.Duel: return "Duel";
                case AppState.Results: return "Results";
                case AppState.Training: return "Training";
                default: return state.ToString();
            }
        }

        private static string Description(ClientContext context, AppState state)
        {
            switch (state)
            {
                case AppState.Connecting:
                    return "Connecting to " + context.Profile.Host + ":" + context.Profile.Port + " as " + context.Profile.PlayerName
                        + ". Networking (Confluence.Net) is not available in this build yet.";
                case AppState.Lobby:
                    return "Eight players select one wizard for the whole match; the lobby offers practice dummies and neutral props (spec L34, L343).";
                case AppState.Shop:
                    return "35 seconds: income, three equipment offers with one reroll each, selling, choosing six of eight spells, and the rotatable arena preview (spec L45, L206, L329).";
                case AppState.Draft:
                    return "30 seconds on augment rounds: five free augment candidates, one reroll per card, one choice (spec L45, L227-L229).";
                case AppState.Alcove:
                    return "5 seconds in the protected spawn alcove: resources restore, the ward prevents damage and casting, final loadouts become visible (spec L46).";
                case AppState.Duel:
                    return "Up to 90 seconds; the boundary contracts from 55 s to the 8 m final floor at 85 s (spec L47-L48).";
                case AppState.Results:
                    return "8 seconds: health, major interactions, damage channels, winner, and Resolve change (spec L49, L359-L361).";
                case AppState.Training:
                    return "Onboarding, wizard lessons, the reaction garden, and bots arrive later (spec L377-L381). The arena preview below walks the client sample layout.";
                default:
                    return string.Empty;
            }
        }

        private static string ButtonText(AppState from, AppState to)
        {
            switch (to)
            {
                case AppState.MainMenu: return from == AppState.Training ? "Back to main menu" : "Leave to main menu";
                case AppState.ArenaPreview: return "Arena preview (sample layout)";
                case AppState.Lobby: return from == AppState.Results ? "Match over: lobby" : "Lobby";
                case AppState.Shop: return from == AppState.Results ? "Next round: shop" : "Shop";
                case AppState.Draft: return "Augment draft";
                case AppState.Alcove: return from == AppState.Shop ? "Alcove (no draft this round)" : "Alcove";
                case AppState.Duel: return "Duel";
                case AppState.Results: return "Results";
                default: return to.ToString();
            }
        }
    }
}
