using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace BaiduMapsDesktop
{
    internal sealed partial class MapViewModel
    {
        private bool navigationVisible = true;
        public bool NavigationVisible { get { return navigationVisible; } set { if (navigationVisible == value) return; navigationVisible = value; Changed("NavigationOpen"); } }
    }

    internal sealed partial class MapWindow
    {
        private DispatcherTimer navigationTimer;
        private DateTime navigationLastNear;
        private Border navigationMotion;
        private TranslateTransform navigationSlide;
        private bool navigationHiding;
        private int navigationAnimation;

        private void InitializeNavigationBar()
        {
            var overlay = (Popup)Window.FindName("NavigationPopup");
            navigationMotion = (Border)Window.FindName("NavigationMotion");
            navigationSlide = (TranslateTransform)navigationMotion.RenderTransform;
            navigationLastNear = DateTime.UtcNow;
            overlay.Opened += delegate { navigationLastNear = DateTime.UtcNow; AnimateNavigation(true); };
            navigationMotion.MouseEnter += delegate { navigationLastNear = DateTime.UtcNow; if (navigationHiding) AnimateNavigation(true); };
            Window.Activated += delegate { navigationLastNear = DateTime.UtcNow; model.NavigationVisible = true; };
            navigationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            navigationTimer.Tick += delegate
            {
                if (closing || popup || !model.WindowActive || !model.Ready || model.CaptureSelecting || Window.WindowState == WindowState.Minimized)
                {
                    navigationLastNear = DateTime.UtcNow;
                    return;
                }
                Point cursor;
                if (!Native.TryGetCursorPosition(out cursor) || !browser.IsVisible || browser.ActualWidth <= 0) return;
                Point start = browser.PointToScreen(new Point(0, 0));
                Point end = browser.PointToScreen(new Point(browser.ActualWidth, browser.ActualHeight));
                double scale = (end.X - start.X) / browser.ActualWidth;
                double halfWidth = (Math.Max(320, navigationMotion.ActualWidth) / 2 + 50) * scale;
                double center = (start.X + end.X) / 2;
                double distance = model.NavigationVisible ? 170 : 58;
                bool near = Math.Abs(cursor.X - center) <= halfWidth && cursor.Y >= end.Y - distance * scale && cursor.Y <= end.Y;
                if (near)
                {
                    navigationLastNear = DateTime.UtcNow;
                    if (!model.NavigationVisible) model.NavigationVisible = true;
                    else if (navigationHiding) AnimateNavigation(true);
                }
                else if (model.NavigationVisible && !navigationHiding && DateTime.UtcNow - navigationLastNear >= TimeSpan.FromSeconds(3))
                    AnimateNavigation(false);
            };
            Window.Loaded += delegate { navigationTimer.Start(); };
            Window.Closed += delegate { navigationTimer.Stop(); };
        }

        private void AnimateNavigation(bool reveal)
        {
            int animation = ++navigationAnimation;
            navigationHiding = !reveal;
            navigationMotion.IsHitTestVisible = reveal;
            double from = navigationSlide.Y;
            if (reveal && navigationMotion.Opacity < 0.01) from = 120;
            var movement = new DoubleAnimation(from, reveal ? 0 : 150, TimeSpan.FromMilliseconds(reveal ? 240 : 220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            if (!reveal) movement.Completed += delegate
            {
                if (animation != navigationAnimation) return;
                model.NavigationVisible = false;
                navigationHiding = false;
            };
            navigationSlide.BeginAnimation(TranslateTransform.YProperty, movement, HandoffBehavior.SnapshotAndReplace);
            navigationMotion.BeginAnimation(UIElement.OpacityProperty,
                // WPF Opacity is the visible fraction: 35% transparent = 65% opaque.
                new DoubleAnimation(navigationMotion.Opacity, reveal ? 0.65 : 0, TimeSpan.FromMilliseconds(180)),
                HandoffBehavior.SnapshotAndReplace);
        }
    }
}
