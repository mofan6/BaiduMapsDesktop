using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BaiduMapsDesktop
{
    internal sealed partial class MapViewModel
    {
        public double HeaderSearchWidth { get { return Math.Max(164, Math.Min(425, viewportWidth - 620)); } }
    }

    internal sealed partial class MapWindow
    {
        private TextBox nativeSearch;
        private DispatcherTimer nativeSuggestTimer;
        private bool syncingNativeSearch;

        private void InitializeNativeSearch()
        {
            nativeSearch = (TextBox)Window.FindName("NativeSearchInput");
            nativeSuggestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            nativeSuggestTimer.Tick += delegate { nativeSuggestTimer.Stop(); UpdateNativeSuggestions(); };
            nativeSearch.TextChanged += delegate
            {
                if (nativeSearch.Text.Length == 0) { nativeSearch.CaretIndex = 0; nativeSearch.ScrollToHome(); }
                if (syncingNativeSearch) return;
                nativeSuggestTimer.Stop();
                if (nativeSearch.IsKeyboardFocusWithin) nativeSuggestTimer.Start();
            };
            nativeSearch.GotKeyboardFocus += delegate { nativeSuggestTimer.Stop(); nativeSuggestTimer.Start(); };
            nativeSearch.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { SubmitNativeSearch(); e.Handled = true; }
                else if (e.Key == Key.Down || e.Key == Key.Up) { ForwardNativeSearchKey(e.Key == Key.Down ? 40 : 38); e.Handled = true; }
                else if (e.Key == Key.Escape) { HideNativeSuggestions(); e.Handled = true; }
            };
            ((Button)Window.FindName("NativeSearchButton")).Click += delegate { SubmitNativeSearch(); };
            Window.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                for (DependencyObject node = e.OriginalSource as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                    if (node == nativeSearch) return;
                HideNativeSuggestions();
            };
            Window.Deactivated += delegate { HideNativeSuggestions(); };
            Window.Closed += delegate { nativeSuggestTimer.Stop(); };
        }

        private void SyncNativeSearch(string query, bool force = false)
        {
            if (nativeSearch == null || query == null || (!force && nativeSearch.IsKeyboardFocusWithin)) return;
            syncingNativeSearch = true;
            nativeSearch.Text = query;
            syncingNativeSearch = false;
        }

        private async void UpdateNativeSuggestions()
        {
            if (browser.CoreWebView2 == null || !model.Ready || !nativeSearch.IsKeyboardFocusWithin || model.FullScreen) return;
            try { await browser.ExecuteScriptAsync("window.__baiduDesktop && window.__baiduDesktop.nativeSuggest(" + ToJsonString(nativeSearch.Text) + ")"); }
            catch { }
        }

        private async void ForwardNativeSearchKey(int key)
        {
            if (browser.CoreWebView2 == null || !model.Ready) return;
            nativeSuggestTimer.Stop();
            try
            {
                string result = await browser.ExecuteScriptAsync("window.__baiduDesktop ? window.__baiduDesktop.nativeSearchKey(" + key + ") : null");
                if (result == "null") return;
                syncingNativeSearch = true;
                nativeSearch.Text = FromJsonString(result);
                nativeSearch.CaretIndex = nativeSearch.Text.Length;
            }
            catch { }
            finally { syncingNativeSearch = false; }
        }

        private async void SubmitNativeSearch()
        {
            if (browser.CoreWebView2 == null || !model.Ready || string.IsNullOrWhiteSpace(nativeSearch.Text)) return;
            nativeSuggestTimer.Stop();
            model.Library = false;
            string query = nativeSearch.Text.Trim();
            try
            {
                string result = await browser.ExecuteScriptAsync("window.__baiduDesktop ? window.__baiduDesktop.nativeSubmit(" + ToJsonString(query) + ") : false");
                if (result != "true") browser.CoreWebView2.Navigate(NavigationPolicy.Home + "?newmap=1&ie=utf-8&s=s%26wd%3D" + Uri.EscapeDataString(query));
                browser.Focus();
            }
            catch (Exception ex) { Diagnostics.Write("native-search-failed", ex.GetType().Name); }
        }

        private async void HideNativeSuggestions()
        {
            if (nativeSuggestTimer != null) nativeSuggestTimer.Stop();
            if (closing || browser.CoreWebView2 == null) return;
            try { await browser.ExecuteScriptAsync("window.__baiduDesktop && window.__baiduDesktop.hideNativeSuggestions()"); } catch { }
        }

        private async void UpdateHeaderFullScreen()
        {
            if (browser.CoreWebView2 == null) return;
            try { await browser.ExecuteScriptAsync("window.__baiduDesktop && window.__baiduDesktop.fullScreen(" + (model.FullScreen ? "true" : "false") + ")"); } catch { }
        }
    }
}
