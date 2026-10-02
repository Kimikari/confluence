using Confluence.Client.App;
using Confluence.Client.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Confluence.Client.UI
{
    /// <summary>
    /// Home (spec L341): Play with server host, port, and display name; Training; Codex; Settings; Quit.
    /// Collection and Replays are listed by the spec and shown disabled until their stages land.
    /// </summary>
    public static class MainMenuScreen
    {
        public static VisualElement Build(ClientContext context)
        {
            VisualElement screen = Ui.Element("cf-screen", "cf-screen--menu");

            VisualElement nav = Ui.Column();
            nav.AddToClassList("cf-nav");
            nav.Add(Ui.Label("CONFLUENCE", "cf-title"));
            nav.Add(Ui.Label("First-person wizard duels", "cf-subtitle"));

            VisualElement playCard = BuildPlayCard(context);
            playCard.style.display = context.Options.MenuPanel == MenuPanel.Play ? DisplayStyle.Flex : DisplayStyle.None;

            nav.Add(Ui.Button("Play", () =>
            {
                bool show = playCard.style.display == DisplayStyle.None;
                playCard.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }, "cf-button--primary"));
            nav.Add(Ui.Button("Training", () => context.States.TryTransitionTo(AppState.Training)));
            nav.Add(Ui.Button("Codex", () => context.States.TryTransitionTo(AppState.Codex)));
            Button collection = Ui.Button("Collection", null);
            collection.SetEnabled(false);
            nav.Add(collection);
            Button replays = Ui.Button("Replays", null);
            replays.SetEnabled(false);
            nav.Add(replays);
            nav.Add(Ui.Button("Settings", context.OpenSettings));
            nav.Add(Ui.Button("Quit", context.Quit));
            screen.Add(nav);

            VisualElement main = Ui.Column();
            main.AddToClassList("cf-grow");
            main.style.marginLeft = 48;
            main.style.marginTop = 120;
            main.Add(playCard);
            screen.Add(main);

            VisualElement footer = Ui.Element("cf-footer");
            footer.Add(Ui.Muted(context.Catalogue.Describe()));
            footer.Add(Ui.Muted("Client " + Application.version + " (Unity " + Application.unityVersion + "). Networking, shop, draft, and duels arrive in later stages."));
            screen.Add(footer);
            return screen;
        }

        private static VisualElement BuildPlayCard(ClientContext context)
        {
            PlayProfile profile = context.Profile;
            VisualElement card = Ui.Panel();
            card.style.maxWidth = 760;
            card.Add(Ui.Heading("Play: Conclave"));
            card.Add(Ui.Muted("Eight players, one wizard each, twelve rounds plus a bracket; typically 14-24 minutes (spec L34-L38, L55)."));

            TextField host = Ui.TextField("Server host", profile.Host, PlayProfile.MaxHostLength, null);
            IntegerField port = Ui.IntegerField("Port", profile.Port, null);
            TextField name = Ui.TextField("Display name", profile.PlayerName, PlayProfile.MaxNameLength, null);
            Label error = Ui.Text(string.Empty);
            error.style.color = new Color(1f, 0.55f, 0.45f);
            card.Add(host);
            card.Add(port);
            card.Add(name);
            card.Add(error);

            card.Add(Ui.Button("Connect", () =>
            {
                string problem = PlayProfile.Validate(host.value, port.value, name.value);
                error.text = problem ?? string.Empty;
                if (problem != null)
                {
                    return;
                }

                profile.Host = host.value;
                profile.Port = port.value;
                profile.PlayerName = name.value;
                if (context.PersistSettings)
                {
                    profile.Save();
                }

                context.States.TryTransitionTo(AppState.Connecting);
            }, "cf-button--primary"));
            return card;
        }
    }
}
