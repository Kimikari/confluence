using System;
using System.Globalization;

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

    /// <summary>
    /// Player command-line switches for previews and automated smoke tests (Unity's own switches such as
    /// -screen-width are ignored here):
    /// <list type="bullet">
    /// <item>-arenaPreview: skip the menu and open the sample arena preview.</item>
    /// <item>-previewView fly|walk|overview: starting preview camera (default fly).</item>
    /// <item>-previewTime &lt;seconds&gt;: initial combat time for the boundary contour (default 0).</item>
    /// <item>-screenshot &lt;path&gt;: write a PNG of the screen shortly before quitting (or after 3 s).</item>
    /// <item>-quitAfter &lt;seconds&gt;: quit after this much real time; the exit code is 1 if errors were logged.</item>
    /// </list>
    /// </summary>
    public sealed class CommandLineOptions
    {
        public bool ArenaPreview { get; private set; }
        public PreviewView PreviewView { get; private set; } = PreviewView.Fly;
        public float PreviewTimeSec { get; private set; }
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
                else if (Is(arg, "-previewTime") && i + 1 < args.Length)
                {
                    options.PreviewTimeSec = ParseSeconds(args[++i], "-previewTime", options);
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

        private static float ParseSeconds(string text, string name, CommandLineOptions options)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value >= 0f)
            {
                return value;
            }

            options.Warnings += "Ignoring " + name + " '" + text + "'. ";
            return 0f;
        }
    }
}
