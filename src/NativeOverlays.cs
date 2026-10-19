using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BaiduMapsDesktop
{
    internal sealed partial class MapWindow
    {
        private void InitializeNativeOverlays()
        {
            string[] names = { "NavigationPopup", "LibraryPopup", "PermissionPopup" };
            foreach (string name in names)
            {
                string placement = name;
                var overlay = (Popup)Window.FindName(name);
                overlay.PlacementTarget = browser;
                overlay.Placement = PlacementMode.Custom;
                overlay.CustomPopupPlacementCallback = delegate(Size size, Size target, Point offset)
                {
                    // WPF passes screen-pixel bounds to this callback and expects
                    // screen-pixel offsets. ActualWidth/Height are logical DIPs.
                    // Use the measured target bounds, scaling only our DIP gaps.
                    double scaleX = browser.ActualWidth > 0 ? target.Width / browser.ActualWidth : 1;
                    double scaleY = browser.ActualHeight > 0 ? target.Height / browser.ActualHeight : 1;
                    Point point = OverlayPlacement.Calculate(placement, size, target, new Vector(scaleX, scaleY));
                    return new[] { new CustomPopupPlacement(point, PopupPrimaryAxis.None) };
                };
                overlay.Child.PreviewKeyDown += OnKeyDown;
                Action reposition = delegate { if (overlay.IsOpen) overlay.HorizontalOffset = overlay.HorizontalOffset == 0 ? 0.01 : 0; };
                Window.LocationChanged += delegate { reposition(); };
                browser.SizeChanged += delegate { reposition(); };
                Window.StateChanged += delegate { if (Window.WindowState == WindowState.Minimized) model.WindowActive = false; reposition(); };
                Window.SourceInitialized += delegate
                {
                    var source = HwndSource.FromHwnd(new WindowInteropHelper(Window).Handle);
                    if (source != null) source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
                    {
                        // Re-anchor after WPF applies a monitor DPI change.
                        if (message == 0x02E0) Window.Dispatcher.BeginInvoke(reposition, DispatcherPriority.Loaded);
                        return IntPtr.Zero;
                    });
                };
            }
        }
    }

    internal static class OverlayPlacement
    {
        internal static Point Calculate(string name, Size popupPixels, Size viewportPixels, Vector pixelScale)
        {
            double width = viewportPixels.Width, height = viewportPixels.Height;
            if (name == "NavigationPopup") return new Point(Math.Max(0, (width - popupPixels.Width) / 2), Math.Max(0, height - popupPixels.Height));
            if (name == "LibraryPopup") return new Point(Math.Max(0, width - popupPixels.Width - 14 * pixelScale.X), 14 * pixelScale.Y);
            return new Point(Math.Max(0, width - popupPixels.Width - 20 * pixelScale.X), Math.Max(0, height - popupPixels.Height - 115 * pixelScale.Y));
        }
    }
}
