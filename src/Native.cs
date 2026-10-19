using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BaiduMapsDesktop
{
    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

        internal static bool TryGetCursorPosition(out Point point)
        {
            NativePoint cursor;
            bool available = GetCursorPos(out cursor);
            point = new Point(cursor.X, cursor.Y);
            return available;
        }

        internal static void ActivateExisting()
        {
            IntPtr hwnd = FindWindow(null, "百度地图 · 桌面");
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, 9);
            SetForegroundWindow(hwnd);
        }
        internal static void RoundCorners(Window window, bool fullScreen)
        {
            try { int value = fullScreen ? 1 : 2; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref value, 4); } catch (DllNotFoundException) { }
        }
        internal static void ConfigureMaximize(Window window, Func<bool> isFullScreen)
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (source == null) return;
            source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                // Borderless windows otherwise maximize beneath the taskbar.
                // MONITORINFO uses physical pixels and works for each monitor/DPI.
                if (message != 0x0024 || isFullScreen()) return IntPtr.Zero;
                var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return IntPtr.Zero;
                var info = (MinMaxInfo)Marshal.PtrToStructure(lParam, typeof(MinMaxInfo));
                info.MaxPosition.X = monitor.Work.Left - monitor.Monitor.Left;
                info.MaxPosition.Y = monitor.Work.Top - monitor.Monitor.Top;
                info.MaxSize.X = monitor.Work.Right - monitor.Work.Left;
                info.MaxSize.Y = monitor.Work.Bottom - monitor.Work.Top;
                info.MaxTrackSize = info.MaxSize;
                var transform = source.CompositionTarget == null ? System.Windows.Media.Matrix.Identity : source.CompositionTarget.TransformToDevice;
                var minimum = transform.Transform(new Point(window.MinWidth, window.MinHeight));
                info.MinTrackSize.X = Math.Min(info.MaxTrackSize.X, (int)Math.Ceiling(minimum.X));
                info.MinTrackSize.Y = Math.Min(info.MaxTrackSize.Y, (int)Math.Ceiling(minimum.Y));
                Marshal.StructureToPtr(info, lParam, false);
                handled = true;
                return IntPtr.Zero;
            });
        }
        internal static System.Windows.Rect ScreenBounds(Window window)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            IntPtr monitor = MonitorFromWindow(new WindowInteropHelper(window).Handle, 2);
            if (!GetMonitorInfo(monitor, ref info)) return new System.Windows.Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            var source = PresentationSource.FromVisual(window);
            var transform = source == null || source.CompositionTarget == null ? System.Windows.Media.Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            var topLeft = transform.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
            var bottomRight = transform.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
            return new System.Windows.Rect(topLeft, bottomRight);
        }
    }
}
