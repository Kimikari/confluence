using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Confluence.Client.EditorTools
{
    /// <summary>
    /// Batch-mode player builds, invoked by tools/build-client.ps1:
    /// Unity.exe -batchmode -quit -projectPath client -executeMethod Confluence.Client.EditorTools.BuildScript.BuildWindowsPlayer
    /// Optional: -buildOutput &lt;path to .exe&gt; (default: &lt;repo&gt;/out/client/Confluence.exe).
    /// Builds a development, Mono, 64-bit Windows player. Throws (non-zero editor exit) on any failure, and
    /// prints a "[Build] RESULT" line the script parses.
    /// </summary>
    public static class BuildScript
    {
        public const string ResultMarker = "[Build] RESULT";

        [MenuItem("Confluence/Build Windows Player (development)")]
        public static void BuildWindowsPlayer()
        {
            if (!ProjectConfigurator.Verify(out string problem))
            {
                Fail("Project is not configured: " + problem);
            }

            string output = OutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectConfigurator.ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development | BuildOptions.StrictMode,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            string line = ResultMarker + " " + summary.result + " errors=" + summary.totalErrors + " warnings=" + summary.totalWarnings
                + " size=" + summary.totalSize + " time=" + summary.totalTime.TotalSeconds.ToString("0.0") + "s output=" + output;
            Debug.Log(line);
            if (summary.result != BuildResult.Succeeded)
            {
                Fail("Player build " + summary.result + ".");
            }
        }

        private static string OutputPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-buildOutput", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(args[i + 1]);
                }
            }

            // The project lives in <repo>/client; builds go to <repo>/out/client (gitignored).
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            return Path.Combine(repo, "out", "client", "Confluence.exe");
        }

        private static void Fail(string message)
        {
            Debug.LogError(ResultMarker + " Failed: " + message);
            throw new BuildFailedException(message);
        }
    }
}
