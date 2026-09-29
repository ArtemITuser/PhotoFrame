using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace PhotoFrame.Services
{
    public sealed record MonitorDescriptor(int Index, string DeviceName, string FriendlyName, bool Primary, int Width, int Height);

    public static class MultiMonitorService
    {
        public static IReadOnlyList<MonitorDescriptor> GetMonitors()
        {
            var screens = Screen.AllScreens;
            return screens.Select((s, i) => new MonitorDescriptor(i, s.DeviceName, s.DeviceName, s.Primary,
                s.Bounds.Width, s.Bounds.Height)).ToList();
        }

        public static void PlaceWindow(Window window, int monitorIndex)
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 0) return;
            monitorIndex = Math.Clamp(monitorIndex, 0, screens.Length - 1);
            var b = screens[monitorIndex].Bounds;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = b.Left;
            window.Top = b.Top;
            window.Width = b.Width;
            window.Height = b.Height;
            window.WindowState = WindowState.Normal;
        }
    }
}
