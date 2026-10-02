using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Confluence.Client.App
{
    /// <summary>
    /// Automated smoke-test support for -screenshot and -quitAfter (see <see cref="CommandLineOptions"/>). The
    /// screenshot is written as PNG shortly before quitting (or after 3 s without -quitAfter). On quit the exit
    /// code is 1 if any error or exception was logged, else 0, so scripts can gate on it. Uses real time,
    /// which is fine for presentation tooling (rules never read it).
    /// </summary>
    public sealed class SmokeTestDriver : MonoBehaviour
    {
        public const float DefaultScreenshotDelaySec = 3f;
        public const float ScreenshotLeadSec = 1.5f;

        private CommandLineOptions _options;
        private int _errorCount;
        private bool _screenshotDone;
        private bool _quitting;

        public static SmokeTestDriver Attach(GameObject host, CommandLineOptions options)
        {
            SmokeTestDriver driver = host.AddComponent<SmokeTestDriver>();
            driver._options = options;
            Application.logMessageReceived += driver.OnLog;
            Debug.Log("[Smoke] Smoke test: screenshot=" + (options.ScreenshotPath ?? "none") + ", quitAfter=" + options.QuitAfterSec + " s.");
            return driver;
        }

        private float ScreenshotTime
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
            if (!_screenshotDone && _options.ScreenshotPath != null && t >= ScreenshotTime)
            {
                _screenshotDone = true;
                StartCoroutine(Capture(_options.ScreenshotPath));
            }

            if (!_quitting && _options.QuitAfterSec > 0f && t >= _options.QuitAfterSec)
            {
                _quitting = true;
                int code = _errorCount > 0 ? 1 : 0;
                Debug.Log("[Smoke] Quitting after " + t.ToString("0.0") + " s with exit code " + code + " (" + _errorCount + " errors logged).");
                Application.Quit(code);
            }
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
        }
    }
}
