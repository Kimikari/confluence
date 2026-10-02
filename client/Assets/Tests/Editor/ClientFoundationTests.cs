using System;
using System.Collections.Generic;
using Confluence.Client.App;
using Confluence.Client.Settings;
using NUnit.Framework;

namespace Confluence.Client.Tests
{
    /// <summary>App flow, command-line switches, and settings: plain C#, no scene objects.</summary>
    public sealed class AppStateMachineTests
    {
        [Test]
        public void StartsAtBootAndFollowsLegalEdges()
        {
            var states = new AppStateMachine();
            var seen = new List<string>();
            states.StateChanged += (from, to) => seen.Add(from + ">" + to);

            Assert.AreEqual(AppState.Boot, states.Current);
            states.TransitionTo(AppState.MainMenu);
            states.TransitionTo(AppState.Connecting);
            states.TransitionTo(AppState.Lobby);
            states.TransitionTo(AppState.Shop);
            states.TransitionTo(AppState.Draft);
            states.TransitionTo(AppState.Alcove);
            states.TransitionTo(AppState.Duel);
            states.TransitionTo(AppState.Results);
            states.TransitionTo(AppState.Shop);

            Assert.AreEqual(AppState.Shop, states.Current);
            Assert.AreEqual(AppState.Results, states.Previous);
            CollectionAssert.AreEqual(new[]
            {
                "Boot>MainMenu", "MainMenu>Connecting", "Connecting>Lobby", "Lobby>Shop", "Shop>Draft", "Draft>Alcove",
                "Alcove>Duel", "Duel>Results", "Results>Shop",
            }, seen);
        }

        [Test]
        public void IllegalTransitionIsRejectedWithoutSideEffects()
        {
            var states = new AppStateMachine();
            states.TransitionTo(AppState.MainMenu);
            int events = 0;
            states.StateChanged += (from, to) => events++;

            Assert.IsFalse(states.TryTransitionTo(AppState.Duel));
            Assert.Throws<InvalidOperationException>(() => states.TransitionTo(AppState.Results));
            Assert.AreEqual(AppState.MainMenu, states.Current);
            Assert.AreEqual(0, events);
        }

        [Test]
        public void CombatIsOnlyEnteredFromTheAlcove()
        {
            // Spec L46-L47: both players spend the protected alcove phase before combat begins.
            foreach (AppState state in (AppState[])Enum.GetValues(typeof(AppState)))
            {
                bool leadsToDuel = false;
                foreach (AppState target in AppStateMachine.LegalTargets(state))
                {
                    leadsToDuel |= target == AppState.Duel;
                }

                Assert.AreEqual(state == AppState.Alcove, leadsToDuel, state.ToString());
            }
        }

        [Test]
        public void EveryStateCanReturnToTheMainMenuExceptBoot()
        {
            foreach (AppState state in (AppState[])Enum.GetValues(typeof(AppState)))
            {
                if (state == AppState.Boot || state == AppState.MainMenu)
                {
                    continue;
                }

                var states = new AppStateMachine(state);
                Assert.IsTrue(states.CanTransitionTo(AppState.MainMenu), state.ToString());
            }
        }
    }

    public sealed class CommandLineOptionsTests
    {
        [Test]
        public void ParsesSmokeTestSwitchesAndIgnoresUnitySwitches()
        {
            CommandLineOptions o = CommandLineOptions.Parse(new[]
            {
                "Confluence.exe", "-screen-width", "1280", "-arenaPreview", "-screenshot", "out/client-shot.png", "-quitAfter", "8",
                "-screen-fullscreen", "0",
            });

            Assert.IsTrue(o.ArenaPreview);
            Assert.AreEqual("out/client-shot.png", o.ScreenshotPath);
            Assert.AreEqual(8f, o.QuitAfterSec);
            Assert.IsTrue(o.IsSmokeTest);
            Assert.AreEqual(PreviewView.Fly, o.PreviewView);
            Assert.AreEqual(string.Empty, o.Warnings);
        }

        [Test]
        public void ParsesCaseInsensitivelyWithInvariantNumbers()
        {
            CommandLineOptions o = CommandLineOptions.Parse(new[] { "-ARENAPREVIEW", "-previewView", "overview", "-previewTime", "62.5", "-menuPanel", "SETTINGS" });
            Assert.IsTrue(o.ArenaPreview);
            Assert.AreEqual(PreviewView.Overview, o.PreviewView);
            Assert.AreEqual(MenuPanel.Settings, o.MenuPanel);
            Assert.AreEqual(62.5f, o.PreviewTimeSec);
            Assert.IsFalse(o.IsSmokeTest);
        }

        [Test]
        public void MalformedValuesWarnAndFallBack()
        {
            CommandLineOptions o = CommandLineOptions.Parse(new[] { "-quitAfter", "-2", "-previewView", "sideways", "-previewTime", "later" });
            Assert.AreEqual(0f, o.QuitAfterSec);
            Assert.AreEqual(CommandLineOptions.DefaultPreviewTimeSec, o.PreviewTimeSec);
            Assert.AreEqual(PreviewView.Fly, o.PreviewView);
            StringAssert.Contains("-quitAfter", o.Warnings);
            StringAssert.Contains("-previewView", o.Warnings);
            StringAssert.Contains("-previewTime", o.Warnings);
        }

        [Test]
        public void EmptyArgumentsGiveDefaults()
        {
            CommandLineOptions o = CommandLineOptions.Parse(null);
            Assert.IsFalse(o.ArenaPreview);
            Assert.IsNull(o.ScreenshotPath);
            Assert.IsFalse(o.IsSmokeTest);
            Assert.AreEqual(MenuPanel.None, o.MenuPanel);

            // The preview clock starts with the 5 s alcove phase (spec L46).
            Assert.AreEqual(-5f, o.PreviewTimeSec);
            Assert.AreEqual(-3f, CommandLineOptions.Parse(new[] { "-previewTime", "-3" }).PreviewTimeSec);
        }
    }

    public sealed class ClientSettingsTests
    {
        [Test]
        public void FieldOfViewIsClampedToTheSpecRange()
        {
            // Spec L371: field of view 80 through 110.
            var settings = new ClientSettings(new MemorySettingsStore());
            settings.FieldOfView = 60f;
            Assert.AreEqual(80f, settings.FieldOfView);
            settings.FieldOfView = 130f;
            Assert.AreEqual(110f, settings.FieldOfView);
            settings.FieldOfView = 97f;
            Assert.AreEqual(97f, settings.FieldOfView);
            settings.FieldOfView = float.NaN;
            Assert.AreEqual(ClientSettings.DefaultFieldOfView, settings.FieldOfView);
        }

        [Test]
        public void VolumesAreSeparateAndClamped()
        {
            // Spec L371: separate voice/music/effects controls.
            var settings = new ClientSettings(new MemorySettingsStore());
            settings.MasterVolume = 2f;
            settings.MusicVolume = 0.25f;
            settings.EffectsVolume = -1f;
            settings.VoiceVolume = 0.5f;
            Assert.AreEqual(1f, settings.MasterVolume);
            Assert.AreEqual(0.25f, settings.MusicVolume);
            Assert.AreEqual(0f, settings.EffectsVolume);
            Assert.AreEqual(0.5f, settings.VoiceVolume);
        }

        [Test]
        public void SaveAndLoadRoundTripThroughTheStore()
        {
            var store = new MemorySettingsStore();
            var a = new ClientSettings(store)
            {
                FieldOfView = 104f,
                MouseSensitivity = 3.5f,
                InvertY = true,
                MasterVolume = 0.3f,
                MusicVolume = 0.2f,
                EffectsVolume = 0.9f,
                VoiceVolume = 0.1f,
            };
            a.Save();
            Assert.AreEqual(1, store.FlushCount);

            var b = new ClientSettings(store);
            b.Load();
            Assert.AreEqual(104f, b.FieldOfView);
            Assert.AreEqual(3.5f, b.MouseSensitivity);
            Assert.IsTrue(b.InvertY);
            Assert.AreEqual(0.3f, b.MasterVolume);
            Assert.AreEqual(0.2f, b.MusicVolume);
            Assert.AreEqual(0.9f, b.EffectsVolume);
            Assert.AreEqual(0.1f, b.VoiceVolume);
        }

        [Test]
        public void ChangedFiresOnlyForRealChanges()
        {
            var settings = new ClientSettings(new MemorySettingsStore());
            int changes = 0;
            settings.Changed += () => changes++;
            settings.InvertY = false;
            settings.InvertY = true;
            settings.FieldOfView = settings.FieldOfView;
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void PlayProfileValidatesHostPortAndName()
        {
            Assert.IsNull(PlayProfile.Validate("127.0.0.1", 7777, "Ashwin"));
            Assert.IsNotNull(PlayProfile.Validate(string.Empty, 7777, "Ashwin"));
            Assert.IsNotNull(PlayProfile.Validate("localhost", 0, "Ashwin"));
            Assert.IsNotNull(PlayProfile.Validate("localhost", 70000, "Ashwin"));
            Assert.IsNotNull(PlayProfile.Validate("localhost", 7777, "   "));
        }
    }
}
