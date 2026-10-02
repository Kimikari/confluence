using System;
using Confluence.Client.Content;
using Confluence.Client.Settings;
using Confluence.Client.UI;

namespace Confluence.Client.App
{
    /// <summary>Services shared by every screen; created once by <see cref="Bootstrap"/>.</summary>
    public sealed class ClientContext
    {
        public ClientContext(AppStateMachine states, ClientSettings settings, PlayProfile profile, CatalogueService catalogue,
            UiShell ui, CommandLineOptions options, Action quit)
        {
            States = states;
            Settings = settings;
            Profile = profile;
            Catalogue = catalogue;
            Ui = ui;
            Options = options;
            Quit = quit;
        }

        public AppStateMachine States { get; }
        public ClientSettings Settings { get; }
        public PlayProfile Profile { get; }
        public CatalogueService Catalogue { get; }
        public UiShell Ui { get; }
        public CommandLineOptions Options { get; }

        /// <summary>Quits the application (stops play mode in the editor).</summary>
        public Action Quit { get; }

        /// <summary>Settings must not be persisted during automated smoke runs.</summary>
        public bool PersistSettings => !Options.IsSmokeTest;

        /// <summary>Opens the settings overlay above the current screen.</summary>
        public void OpenSettings()
        {
            Ui.ShowOverlay(SettingsPanel.Build(this, () =>
            {
                if (PersistSettings)
                {
                    Settings.Save();
                }

                Ui.CloseOverlay();
            }));
        }
    }
}
