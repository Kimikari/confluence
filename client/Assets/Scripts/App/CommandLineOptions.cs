using System;
using System.Globalization;
using Confluence.Core.Rules;

namespace Confluence.Client.App
{
    /// <summary>Which camera the arena preview starts with.</summary>
    public enum PreviewView
    {
        /// <summary>Free-fly camera at an elevated establishing position.</summary>
        Fly,

        /// <summary>First-person walk from spawn side 0.</summary>
        Walk,

        /// <summary>Rotatable top-down overview (spec L329).</summary>
        Overview,
    }

    /// <summary>Main-menu entry opened at start, so smoke screenshots can cover every menu screen.</summary>
    public enum MenuPanel
    {
        None,

        /// <summary>The Play card (host, port, display name).</summary>
        Play,

        /// <summary>The settings overlay.</summary>
        Settings,

        /// <summary>The Codex screen.</summary>
        Codex,

        /// <summary>The Training screen.</summary>
        Training,
    }

    /// <summary>
    /// Player command-line switches for previews and automated smoke tests (Unity's own switches such as
    /// -screen-width are ignored here):
    /// <list type="bullet">
    /// <item>-arenaPreview: skip the menu and open the sample arena preview.</item>
    /// <item>-previewView fly|walk|overview: starting preview camera (default fly).</item>
    /// <item>-previewTime &lt;seconds&gt;: initial preview clock, -5 (alcove phase, the default) to 90 (combat end).</item>
    /// <item>-menuPanel play|settings|codex|training: open that main-menu entry at start.</item>
    /// <item>-screenshot &lt;path&gt;: write a PNG of the screen shortly before quitting (or after 3 s).</item>
    /// <item>-quitAfter &lt;seconds&gt;: quit after this much real time; the exit code is 1 if errors were logged.</item>
    /// </list>
    /// </summary>
    public sealed class CommandLineOptions
    {
        public const float DefaultPreviewTimeSec = -GlobalRules.Round.AlcoveSec;

        public bool ArenaPreview { get; private set; }
        public PreviewView PreviewView { get; private set; } = PreviewView.Fly;
        public MenuPanel MenuPanel { get; private set; } = MenuPanel.None;

        /// <summary>Initial preview clock; defaults to the start of the 5 s alcove phase (spec L46).</summary>
        public float PreviewTimeSec { get; private set; } = DefaultPreviewTimeSec;
        public string ScreenshotPath { get; private set; }

        /// <summary>Seconds of real time before quitting; 0 or less disables the timer.</summary>
        public float QuitAfterSec { get; private set; }

        /// <summary>Unrecognized or malformed switches, for logging.</summary>
        public string Warnings { get; private set; } = string.Empty;

        public bool IsSmokeTest => ScreenshotPath != null || QuitAfterSec > 0f;

        public static CommandLineOptions FromEnvironment() => Parse(Environment.GetCommandLineArgs());

        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();
            if (args == null)
            {
                return options;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (Is(arg, "-arenaPreview"))
                {
                    options.ArenaPreview = true;
                }
                else if (Is(arg, "-previewView") && i + 1 < args.Length)
                {
                    string value = args[++i];
                    if (Enum.TryParse(value, true, out PreviewView view))
                    {
                        options.PreviewView = view;
                    }
                    else
                    {
                        options.Warnings += "Unknown -previewView '" + value + "'. ";
                    }
                }
                else if (Is(arg, "-menuPanel") && i + 1 < args.Length)
                {
                    string value = args[++i];
                    if (Enum.TryParse(value, true, out MenuPanel panel))
                    {
                        options.MenuPanel = panel;
                    }
                    else
                    {
                        options.Warnings += "Unknown -menuPanel '" + value + "'. ";
                    }
                }
                else if (Is(arg, "-previewTime") && i + 1 < args.Length)
                {
                    // Negative values down to -5 are the alcove phase (spec L46); the preview clamps the range.
                    options.PreviewTimeSec = ParseSeconds(args[++i], "-previewTime", options, allowNegative: true, fallback: DefaultPreviewTimeSec);
                }
                else if (Is(arg, "-screenshot") && i + 1 < args.Length)
                {
                    options.ScreenshotPath = args[++i];
                }
                else if (Is(arg, "-quitAfter") && i + 1 < args.Length)
                {
                    options.QuitAfterSec = ParseSeconds(args[++i], "-quitAfter", options);
                }
            }

            return options;
        }

        private static bool Is(string arg, string name) => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);

        private static float ParseSeconds(string text, string name, CommandLineOptions options, bool allowNegative = false, float fallback = 0f)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                && !float.IsNaN(value) && !float.IsInfinity(value) && (allowNegative || value >= 0f))
            {
                return value;
            }

            options.Warnings += "Ignoring " + name + " '" + text + "'. ";
            return fallback;
        }
    }
}
