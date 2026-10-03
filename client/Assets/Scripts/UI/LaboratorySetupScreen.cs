using System;
using System.Collections.Generic;
using Confluence.Client.Laboratory;
using Confluence.Core.Content;
using Confluence.Core.Rules;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Laboratory entry (spec L69): choose the wizard, the dummy's wizard and behavior, the arena seed and starting
    /// round, then either spar with the dummy at once (sandbox duel near the center) or play full Laboratory rounds
    /// (reveal, shop, draft, duel, results). The screen states plainly that the session is offline, unranked, and
    /// modified.
    /// </summary>
    public static class LaboratorySetupScreen
    {
        public static VisualElement Build(Catalogue catalogue, LaboratoryConfig config, Action onSandbox, Action onRounds, Action onBack)
        {
            VisualElement screen = Ui.Element("cf-screen", "cf-screen--menu", "cf-column");
            screen.Add(Ui.Label("LABORATORY", "cf-title"));
            screen.Add(Ui.Label("Offline sandbox - unranked - modified session (spec L69)", "cf-lab-label"));
            screen.Add(Ui.Muted("Modified rules: every catalogue record is offered whether or not its behavior is implemented yet, the opponent is a "
                + "training dummy that never shops or drafts, and phase timers may be paused. Nothing here earns ranked rewards."));

            VisualElement panel = Ui.Panel();
            panel.style.maxWidth = 980;
            panel.style.marginTop = 24;
            List<string> wizards = WizardChoices(catalogue);
            panel.Add(Dropdown("Your wizard", wizards, config.PlayerWizardId, id => config.PlayerWizardId = id));
            panel.Add(Dropdown("Dummy's wizard", wizards, config.OpponentWizardId, id => config.OpponentWizardId = id));

            var modes = new List<string>();
            foreach (DummyMode mode in Enum.GetValues(typeof(DummyMode)))
            {
                modes.Add(mode.ToString());
            }

            var dummy = new DropdownField("Dummy behavior", modes, config.Dummy.ToString());
            dummy.AddToClassList("cf-field");
            dummy.RegisterValueChangedCallback(e =>
            {
                if (Enum.TryParse(e.newValue, out DummyMode mode))
                {
                    config.Dummy = mode;
                }
            });
            panel.Add(dummy);
            panel.Add(Ui.Muted("Idle stands still; Guard raises its guard against your melee windup when ready; Strafe side-steps; Spar closes in and strikes."));
            panel.Add(Ui.TextField("Arena seed", config.MatchSeed.ToString(System.Globalization.CultureInfo.InvariantCulture), 20, text =>
            {
                if (ulong.TryParse(text, out ulong seed))
                {
                    config.MatchSeed = seed;
                }
            }));
            panel.Add(Ui.SliderInt("Starting round", 1, GlobalRules.Match.RegularRounds, config.StartRound, v => config.StartRound = v));
            screen.Add(panel);

            VisualElement buttons = Ui.Column();
            buttons.style.maxWidth = 620;
            Button sandbox = Ui.Button("Sandbox duel: spar with the dummy now", onSandbox, "cf-button--primary");
            buttons.Add(sandbox);
            buttons.Add(Ui.Button("Laboratory rounds: reveal, shop, draft, duel, results", onRounds));
            buttons.Add(Ui.Button("Back", onBack));
            screen.Add(buttons);
            screen.Add(Ui.Muted("Duel keys (spec L84): WASD move, mouse aim, LMB cast, RMB guard, Shift sprint, Space jump, Ctrl crouch, Alt dodge, "
                + "1-6 or wheel spell slot, F melee, Q signature, E active, X consumable, T recast, R cast modifier, Z/C modifiers A/B; "
                + "Tab inspect build, Esc Laboratory menu."));
            sandbox.schedule.Execute(() => sandbox.Focus());
            return screen;
        }

        private static List<string> WizardChoices(Catalogue catalogue)
        {
            var list = new List<string>();
            foreach (WizardDef wizard in catalogue.Wizards)
            {
                list.Add(Choice(wizard));
            }

            return list;
        }

        private static string Choice(WizardDef wizard) => wizard.Id + "  " + wizard.Name + ", " + wizard.Title;

        private static DropdownField Dropdown(string label, List<string> choices, string selectedId, Action<string> onChanged)
        {
            string selected = choices.Find(c => c.StartsWith(selectedId + " ", StringComparison.Ordinal)) ?? (choices.Count > 0 ? choices[0] : null);
            var field = new DropdownField(label, choices, selected);
            field.AddToClassList("cf-field");
            field.RegisterValueChangedCallback(e =>
            {
                string value = e.newValue ?? string.Empty;
                int space = value.IndexOf(' ');
                onChanged(space > 0 ? value.Substring(0, space) : value);
            });
            return field;
        }
    }
}
