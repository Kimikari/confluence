using System;
using System.Runtime.InteropServices;

namespace Confluence.HeadlessClient
{
    /// <summary>
    /// Raises the Windows system timer resolution to 1 ms for the process lifetime (research §2.8.6): without it
    /// Thread.Sleep(1) can oversleep up to about 15.6 ms, bunching the 60 Hz ticks and their sends. Per-process since
    /// Windows 10 2004 (timeBeginPeriod). A no-op elsewhere.
    /// </summary>
    public sealed class WindowsTimer : IDisposable
    {
        private readonly bool _active;

        private WindowsTimer(bool active)
        {
            _active = active;
        }

        public bool Active => _active;

        public static WindowsTimer HighResolution()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return new WindowsTimer(false);
            try
            {
                return new WindowsTimer(timeBeginPeriod(1) == 0);
            }
            catch (DllNotFoundException)
            {
                return new WindowsTimer(false);
            }
            catch (EntryPointNotFoundException)
            {
                return new WindowsTimer(false);
            }
        }

        public void Dispose()
        {
            if (_active) timeEndPeriod(1);
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint milliseconds);
    }
}
