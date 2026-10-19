using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace BaiduMapsDesktop
{
    internal sealed partial class MapWindow
    {
        // Test only: measure the real popup HWND geometry, not just the formula.
        private async Task VerifyOverlaysAsync()
        {
            string output = args.First(a => a.StartsWith("--overlay-probe=", StringComparison.Ordinal)).Substring("--overlay-probe=".Length);
            var report = new StringBuilder();
            try
            {
                foreach (string name in new[] { "NavigationPopup", "LibraryPopup", "PermissionPopup" }) ((Popup)Window.FindName(name)).Child.Opacity = 0;
                model.Ready = true; model.WindowActive = true; model.Library = true; model.Permission = true;
                await MeasureOverlaysAsync("normal", report);
                Window.Width -= 120; Window.Height -= 80;
                await MeasureOverlaysAsync("resized", report);
                Window.WindowState = WindowState.Maximized;
                await MeasureOverlaysAsync("maximized", report);
                ToggleFullScreen();
                await MeasureOverlaysAsync("fullscreen", report);
                ToggleFullScreen(); Window.WindowState = WindowState.Normal;
                await MeasureOverlaysAsync("restored", report);
                report.AppendLine("PASS: physical popup bounds are centered/anchored in all window states.");
            }
            catch (Exception ex) { report.AppendLine("FAIL: " + ex); }
            finally { File.WriteAllText(output, report.ToString()); Window.Close(); }
        }
        private async Task MeasureOverlaysAsync(string state, StringBuilder report)
        {
            Window.UpdateLayout(); await Task.Delay(200);
            Point top = browser.PointToScreen(new Point()), bottom = browser.PointToScreen(new Point(browser.ActualWidth, browser.ActualHeight));
            Size viewport = new Size(bottom.X - top.X, bottom.Y - top.Y);
            var scale = new Vector(viewport.Width / browser.ActualWidth, viewport.Height / browser.ActualHeight);
            foreach (string name in new[] { "NavigationPopup", "LibraryPopup", "PermissionPopup" })
            {
                var panel = (Popup)Window.FindName(name);
                if (!panel.IsOpen) throw new InvalidOperationException(name + " did not open");
                var content = (FrameworkElement)panel.Child;
                Point actualTop = content.PointToScreen(new Point()), actualBottom = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
                Size panelSize = new Size(actualBottom.X - actualTop.X, actualBottom.Y - actualTop.Y);
                Point expected = OverlayPlacement.Calculate(name, panelSize, viewport, scale);
                double dx = actualTop.X - top.X - expected.X, dy = actualTop.Y - top.Y - expected.Y;
                report.AppendLine(state + " " + name + " dpi-scale=" + scale.X.ToString("0.00") + " x-error=" + dx.ToString("0.00") + " y-error=" + dy.ToString("0.00")
                    + " bottom-gap-dip=" + ((bottom.Y - actualBottom.Y) / scale.Y).ToString("0.00"));
                if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) throw new InvalidOperationException("Popup physical bounds differ from the map anchor.");
            }
        }
    }
}
