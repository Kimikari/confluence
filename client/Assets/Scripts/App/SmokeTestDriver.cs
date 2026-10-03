using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Confluence.Client.App
{
    /// <summary>
    /// Automated smoke-test support for -screenshot and -quitAfter (see <see cref="CommandLineOptions"/>). The
    /// screenshot is written as PNG shortly before quitting (or after 3 s without -quitAfter). Screens with
    /// background work (arena generation, a fast-forwarded duel) hold the screenshot with <see cref="Hold"/> until
    /// they are ready; the screenshot then waits <see cref="SettleSec"/> more and the quit moves back accordingly, up
    /// to <see cref="MaxHoldSec"/>. On quit the exit code is 1 if any error or exception was logged (or a hold never
    /// released), else 0, so scripts can gate on it. Uses real time, which is fine for presentation tooling (rules
    /// never read it).
    /// </summary>
    public sealed class SmokeTestDriver : MonoBehaviour
    {
        public const float DefaultScreenshotDelaySec = 3f;
        public const float ScreenshotLeadSec = 1.5f;
        public const float SettleSec = 1.5f;
        public const float MaxHoldSec = 180f;

        private static readonly List<string> Holds = new List<string>();
        private static float _lastReleaseRealtime;

        private CommandLineOptions _options;
        private int _errorCount;
        private bool _screenshotDone;
        private float _screenshotRealtime = -1f;
        private bool _quitting;

        /// <summary>True while a smoke run is active (screens may then skip interactive-only steps).</summary>
        public static bool Active { get; private set; }

        public static SmokeTestDriver Attach(GameObject host, CommandLineOptions options)
        {
            SmokeTestDriver driver = host.AddComponent<SmokeTestDriver>();
            driver._options = options;
            Active = true;
            Application.logMessageReceived += driver.OnLog;
            Debug.Log("[Smoke] Smoke test: screenshot=" + (options.ScreenshotPath ?? "none") + ", quitAfter=" + options.QuitAfterSec + " s.");
            return driver;
        }

        /// <summary>Delays the smoke screenshot until the returned token is disposed. A no-op outside smoke runs.</summary>
        public static IDisposable Hold(string reason)
        {
            if (!Active)
            {
                return NoHold.Instance;
            }

            Holds.Add(reason);
            return new HoldToken(reason);
        }

        private float PlannedScreenshotTime
        {
            get
            {
                if (_options.QuitAfterSec > 0f)
                {
                    return Mathf.Max(0.5f, _options.QuitAfterSec - ScreenshotLeadSec);
                }

                return DefaultScreenshotDelaySec;
            }
        }

        private void Update()
        {
            float t = Time.realtimeSinceStartup;
            bool held = Holds.Count > 0 || t < _lastReleaseRealtime + SettleSec;
            if (!_screenshotDone && _options.ScreenshotPath != null && t >= PlannedScreenshotTime && !held)
            {
                _screenshotDone = true;
                _screenshotRealtime = t;
                StartCoroutine(Capture(_options.ScreenshotPath));
            }

            if (_quitting || !(_options.QuitAfterSec > 0f))
            {
                return;
            }

            bool waitingForShot = _options.ScreenshotPath != null && !_screenshotDone;
            float quitAt = _screenshotRealtime >= 0f ? Mathf.Max(_options.QuitAfterSec, _screenshotRealtime + ScreenshotLeadSec) : _options.QuitAfterSec;
            if (waitingForShot && t < _options.QuitAfterSec + MaxHoldSec)
            {
                return;
            }

            if (waitingForShot)
            {
                Debug.LogError("[Smoke] Timed out waiting for: " + string.Join(", ", Holds));
            }
            else if (t < quitAt)
            {
                return;
            }

            _quitting = true;
            int code = _errorCount > 0 ? 1 : 0;
            Debug.Log("[Smoke] Quitting after " + t.ToString("0.0") + " s with exit code " + code + " (" + _errorCount + " errors logged).");
            Application.Quit(code);
        }

        private IEnumerator Capture(string path)
        {
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(fullPath, shot.EncodeToPNG());
                Debug.Log("[Smoke] Screenshot written: " + fullPath + " (" + shot.width + "x" + shot.height + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[Smoke] Screenshot failed: " + e.Message);
            }
            finally
            {
                if (shot != null)
                {
                    Destroy(shot);
                }
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errorCount++;
            }
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            Active = false;
            Holds.Clear();
        }

        private sealed class HoldToken : IDisposable
        {
            private string _reason;

            public HoldToken(string reason)
            {
                _reason = reason;
            }

            public void Dispose()
            {
                if (_reason == null)
                {
                    return;
                }

                Holds.Remove(_reason);
                _reason = null;
                _lastReleaseRealtime = Time.realtimeSinceStartup;
            }
        }

        private sealed class NoHold : IDisposable
        {
            public static readonly NoHold Instance = new NoHold();

            public void Dispose()
            {
            }
        }
    }
}
