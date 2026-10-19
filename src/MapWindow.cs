using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace BaiduMapsDesktop
{
    internal sealed partial class MapWindow
    {
        internal readonly Window Window;
        private readonly MapViewModel model = new MapViewModel();
        private readonly WebView2 browser;
        private readonly bool popup;
        private readonly string[] args;
        private bool initializing, closing, wired, needsRecreate;
        private static bool loaderConfigured;
        private CoreWebView2Environment environment;
        private WindowState beforeFullScreen;
        private Rect boundsBeforeFullScreen;
        private string probePath;
        private DispatcherTimer watchdog;
        private DispatcherTimer probeTimer;
        private readonly LibraryStore library = new LibraryStore();
        private readonly Queue<PendingPermission> permissions = new Queue<PendingPermission>();
        private bool documentReady, errorDocument;
        private ulong navigationId;
        private sealed class PendingPermission { internal CoreWebView2PermissionRequestedEventArgs Args; internal CoreWebView2Deferral Deferral; }

        internal MapWindow(bool popup, string[] args)
        {
            this.popup = popup;
            model.Popup = popup;
            this.args = args ?? new string[0];
            using (var stream = new MemoryStream(BuildInfo.AdaptView(Bootstrap.ReadResource("Embedded.MainWindow.xaml")))) Window = (Window)XamlReader.Load(stream);
            if (!popup && this.args.Any(a => a.StartsWith("--test-data=", StringComparison.Ordinal))) Window.Title = "百度地图 · 测试";
            browser = (WebView2)Window.FindName("Browser");
            using (var stream = new MemoryStream(Bootstrap.ReadResource("Embedded.AppIcon.ico")))
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Window.Icon = decoder.Frames[decoder.Frames.Count - 1];
            }
            using (var stream = new MemoryStream(Bootstrap.ReadResource("Embedded.AppIcon.xaml")))
            {
                var vectorIcon = (System.Windows.Media.DrawingImage)XamlReader.Load(stream);
                vectorIcon.Freeze();
                ((Image)Window.FindName("AppBrandIcon")).Source = vectorIcon;
            }
            BindCommands();
            InitializeScreenshotSelection();
            Window.DataContext = model;
            Window.SourceInitialized += delegate { Native.RoundCorners(Window, false); Native.ConfigureMaximize(Window, delegate { return model.FullScreen; }); };
            Window.StateChanged += delegate { model.MaximizeGlyph = BuildInfo.Icon(Window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922"); Probe("window-state " + Window.WindowState); };
            Window.LocationChanged += delegate { Probe("window-position " + Window.Left + "," + Window.Top); };
            Window.SizeChanged += delegate { model.ViewportWidth = Window.ActualWidth; };
            Window.Activated += delegate { model.WindowActive = true; };
            Window.Deactivated += delegate { model.WindowActive = false; };
            InitializeNativeOverlays();
            InitializeNavigationBar();
            InitializeNativeSearch();
            Window.PreviewKeyDown += OnKeyDown;
            Window.Closing += OnClosing;
            Window.Closed += delegate { closing = true; if (watchdog != null) watchdog.Stop(); if (probeTimer != null) probeTimer.Stop(); if (verificationTimeout != null) verificationTimeout.Stop(); browser.Dispose(); };
            // Native caption hit testing covers every unused part of the common bar.
            // XAML buttons opt into client hit testing so dragging never steals a click.
            if (popup) { Window.Title = "百度账号 · 登录"; Window.Width = 520; Window.Height = 720; Window.MinWidth = 420; Window.MinHeight = 480; Window.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else
            {
                RestoreWindow();
                if (this.args.Any(a => a.StartsWith("--profile-probe=", StringComparison.Ordinal) || a.StartsWith("--page-probe=", StringComparison.Ordinal))) { Window.WindowState = WindowState.Minimized; Window.ShowInTaskbar = false; }
                if (this.args.Any(a => a.StartsWith("--page-probe=", StringComparison.Ordinal))) { Window.WindowState = WindowState.Normal; Window.ShowActivated = false; Window.Left = -32000; Window.Top = -32000; }
                bool overlayProbe = this.args.Any(a => a.StartsWith("--overlay-probe=", StringComparison.Ordinal));
                if (overlayProbe)
                {
                    Window.ShowActivated = false; Window.ShowInTaskbar = false; Window.WindowState = WindowState.Normal;
                    Window.WindowStartupLocation = WindowStartupLocation.Manual;
                    Window.Left = SystemParameters.WorkArea.Left + 24; Window.Top = SystemParameters.WorkArea.Top + 24;
                    Window.Width = Math.Min(1000, SystemParameters.WorkArea.Width - 48); Window.Height = Math.Min(680, SystemParameters.WorkArea.Height - 48);
                }
                Window.Loaded += async delegate { BeginVerificationTimeout(); if (overlayProbe) await VerifyOverlaysAsync(); else await InitializeAsync(null, true); };
            }
        }

        private void BindCommands()
        {
            model.Back = new RelayCommand(delegate { if (browser.CoreWebView2 != null) browser.CoreWebView2.GoBack(); }, delegate { return model.CanBack; });
            model.Forward = new RelayCommand(delegate { if (browser.CoreWebView2 != null) browser.CoreWebView2.GoForward(); }, delegate { return model.CanForward; });
            model.Refresh = new RelayCommand(delegate { if (browser.CoreWebView2 != null && !needsRecreate) browser.Reload(); else Retry(); });
            model.Home = new RelayCommand(delegate { if (browser.CoreWebView2 != null && !needsRecreate) browser.CoreWebView2.Navigate(NavigationPolicy.Home); else Retry(); });
            model.OpenBrowser = new RelayCommand(delegate { OpenExternal(CurrentAddress()); });
            model.Fullscreen = new RelayCommand(ToggleFullScreen);
            model.Minimize = new RelayCommand(delegate { Window.WindowState = WindowState.Minimized; });
            model.Maximize = new RelayCommand(ToggleMaximize);
            model.Close = new RelayCommand(delegate { Window.Close(); });
            model.Retry = new RelayCommand(Retry);
            model.AllowPermission = new RelayCommand(delegate { AnswerPermission(true); });
            model.DenyPermission = new RelayCommand(delegate { AnswerPermission(false); });
            model.Route = new RelayCommand(OpenRoute);
            model.Recent = new RelayCommand(delegate { ShowLibrary(false); });
            model.Favorites = new RelayCommand(delegate { ShowLibrary(true); });
            model.SaveFavorite = new RelayCommand(delegate { library.AddFavorite(model.FavoriteName, CurrentAddress()); RefreshLibrary(); });
            model.CloseLibrary = new RelayCommand(delegate { model.Library = false; });
            model.OpenEntry = new ParameterCommand(OpenEntry);
            model.RemoveEntry = new ParameterCommand(delegate(object value) { library.Remove(value as PlaceEntry); RefreshLibrary(); });
            model.Screenshot = new RelayCommand(SaveScreenshot);
            model.Traffic = new RelayCommand(delegate { OpenMapTool("traffic"); });
            model.Measure = new RelayCommand(delegate { OpenMapTool("measure"); });
            model.Share = new RelayCommand(delegate { OpenMapTool("share"); });
            model.Help = new RelayCommand(delegate
            {
                MessageBox.Show(Window, "百度地图桌面版 " + BuildInfo.Version + "\n" + BuildInfo.Edition + " · " + BuildInfo.Architecture + "\n\n地图和搜索直接来自百度地图官方网页。\n本软件为非官方桌面封装。\n\n顶部：路线、最近搜索、收藏、路况、测距、框选截图、百度分享。\n底部：返回、前进、刷新、首页、全屏、浏览器、帮助。\n鼠标远离底栏 3 秒后滑出隐藏，接近底部中间时滑入。\n地点详情右上角 ×：关闭详情并清空搜索。\n按住顶部横栏空白处可拖动，双击可最大化。\n最大化自动避开任务栏。\n\n截图：框选矩形，松开后直接选择保存位置。\nF11：全屏    Esc：退出全屏 / 取消截图 / 收起面板\nF5 / Ctrl + R：刷新    Ctrl + L：地图搜索\nCtrl + D：收藏    Ctrl + Shift + S：框选截图\nAlt + ← / →：返回 / 前进    Alt + Home：地图首页\n\n清理已识别的推广和广告空白；底部链接全部隐藏，保留版权备案文字。\n版权栏整体 97% 透明。\n左侧内置现代黑体，地图标注字体保持原样。\n\n资料压缩加密保存在 AppData 的 state.data。\n临时 Work 正常退出后清理；异常时保留以恢复。\n支持只有 C 盘的电脑，EXE 旁不再生成资料文件。\n仅原 Windows 账号可读取资料，分享软件只需发送 EXE。\n" + BuildInfo.RuntimeRequirement + "\n百度会话过期或主动退出后需要重新登录。\n\n资料文件：\n" + (Bootstrap.Storage == null ? Bootstrap.DataRoot : Bootstrap.Storage.VaultPath), "帮助", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private async void Retry()
        {
            if (needsRecreate) { Restart(); return; }
            if (browser.CoreWebView2 != null)
            {
                model.Loading();
                browser.CoreWebView2.Navigate(NavigationPolicy.Home);
                return;
            }
            await InitializeAsync(null, true);
        }

        private async Task InitializeAsync(CoreWebView2Environment sharedEnvironment, bool navigate)
        {
            if (initializing || closing) return;
            initializing = true;
            model.Loading();
            try
            {
                if (sharedEnvironment == null)
                {
                    if (!loaderConfigured) { CoreWebView2Environment.SetLoaderDllFolderPath(Bootstrap.NativeRoot); loaderConfigured = true; }
                    string runtimeFolder = GetRuntimeFolder();
                    CoreWebView2Environment.GetAvailableBrowserVersionString(runtimeFolder);
                    // The two SDKs expose different optional constructor
                    // signatures. Invoke the selected assembly's own defaults.
                    var optionsConstructor = typeof(CoreWebView2EnvironmentOptions).GetConstructors().OrderBy(c => c.GetParameters().Length).First();
                    var options = (CoreWebView2EnvironmentOptions)optionsConstructor.Invoke(optionsConstructor.GetParameters().Select(p => p.DefaultValue).ToArray());
                    options.Language = "zh-CN";
                    environment = await CoreWebView2Environment.CreateAsync(runtimeFolder, SettingsStore.ProfilePath, options);
                }
                else environment = sharedEnvironment;
                if (closing) return;
                var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
                controllerOptions.ProfileName = "Default";
                controllerOptions.IsInPrivateModeEnabled = false;
                await browser.EnsureCoreWebView2Async(environment, controllerOptions);
                if (closing) return;
                if (Bootstrap.Storage != null) Bootstrap.Storage.RegisterBrowser(browser.CoreWebView2.BrowserProcessId);
                browser.CoreWebView2.AddWebResourceRequestedFilter("https://map.baidu.com/__desktop-assets/modern-sans.woff2", CoreWebView2WebResourceContext.Font);
                browser.CoreWebView2.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    if (e.Request.Uri != "https://map.baidu.com/__desktop-assets/modern-sans.woff2") return;
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(Bootstrap.ReadResource("Embedded.ModernSans.woff2")), 200, "OK", "Content-Type: font/woff2\r\nCache-Control: public, max-age=31536000\r\nAccess-Control-Allow-Origin: https://map.baidu.com");
                };
                WireBrowser();
                if (!popup)
                {
                    await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Encoding.UTF8.GetString(Bootstrap.ReadResource("Embedded.PageAdapter.js")));
                }
                Diagnostics.Write("runtime", environment.BrowserVersionString);
                if (!popup && args.Any(a => a.StartsWith("--profile-probe=", StringComparison.Ordinal)))
                {
                    await ProfileProbeAsync();
                    return;
                }
                if (navigate)
                {
                    browser.CoreWebView2.Navigate(NavigationPolicy.Home);
                    if (args.Any(a => a.StartsWith("--page-probe=", StringComparison.Ordinal)))
                    {
                        await Task.Delay(12000);
                        if (!closing) await VerifyPageAsync();
                    }
                }
            }
            catch (WebView2RuntimeNotFoundException)
            {
                model.Fail("需要 WebView2 运行时", BuildInfo.RuntimeRequirement);
            }
            catch (Exception ex)
            {
                Diagnostics.Write("webview-init-failed", ex.ToString());
                model.Fail("地图未能启动", "请重新尝试；如果仍无法打开，可以先在浏览器中使用百度地图。\n" + ex.Message);
            }
            finally { initializing = false; }
        }

        private void WireBrowser()
        {
            if (wired) return;
            wired = true;
            var core = browser.CoreWebView2;
            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
            {
                if (!NavigationPolicy.IsTrusted(e.Uri) && e.Uri != "about:blank")
                {
                    e.Cancel = true;
                    if (e.IsUserInitiated && NavigationPolicy.IsWeb(e.Uri)) OpenExternal(e.Uri);
                    model.Busy = false;
                    return;
                }
                model.Loading();
                model.TrafficActive = false;
                documentReady = false;
                errorDocument = false;
                navigationId = e.NavigationId;
                StartWatchdog();
                Probe("navigation-started");
            };
            core.NavigationCompleted += async delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (closing) return;
                if (e.NavigationId != navigationId) return;
                if (watchdog != null) watchdog.Stop();
                model.Busy = false;
                if (e.IsSuccess)
                {
                    model.Ready = true;
                    model.Error = false;
                    Probe("navigation-completed");
                    await CaptureProbeAsync();
                    await VerifyPageAsync();
                }
                else if (!documentReady && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    Diagnostics.Write("navigation-error", e.WebErrorStatus.ToString());
                    model.Fail("暂时无法连接百度地图", "请检查网络连接后重试。也可以在浏览器中打开。\n错误：" + e.WebErrorStatus);
                    Probe("navigation-error: " + e.WebErrorStatus);
                }
                if (closing) return;
                model.CanBack = core.CanGoBack;
                model.CanForward = core.CanGoForward;
            };
            core.ContentLoading += delegate(object sender, CoreWebView2ContentLoadingEventArgs e) { if (e.NavigationId == navigationId) errorDocument = e.IsErrorPage; };
            core.DOMContentLoaded += async delegate(object sender, CoreWebView2DOMContentLoadedEventArgs e)
            {
                if (closing || e.NavigationId != navigationId || errorDocument) return;
                documentReady = true;
                model.Ready = true;
                model.Error = false;
                model.Busy = false;
                if (watchdog != null) watchdog.Stop();
                Probe("dom-ready");
                await CaptureProbeAsync();
                await VerifyPageAsync();
            };
            core.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e)
            {
                Uri origin;
                if (!Uri.TryCreate(e.Source, UriKind.Absolute, out origin) || origin.Scheme != "https" || origin.Host != "map.baidu.com") return;
                try
                {
                    string json = e.TryGetWebMessageAsString();
                    if (json.Length > 4096) return;
                    PageMessage message;
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json))) message = (PageMessage)new DataContractJsonSerializer(typeof(PageMessage)).ReadObject(stream);
                    if (message != null && message.Type == "search") { SyncNativeSearch(message.Query); library.AddSearch(message.Query); if (model.Library && !model.ShowingFavorites) RefreshLibrary(); }
                    else if (message != null && message.Type == "traffic-state") model.TrafficActive = message.Query == "on";
                    else if (message != null && message.Type == "native-header") UpdateHeaderFullScreen();
                    else if (message != null && message.Type == "search-display") SyncNativeSearch(message.Query, true);
                    else if (message != null && message.Type == "map-tool-error") ShowMapToolUnavailable(message.Query);
                }
                catch { }
            };
            core.HistoryChanged += delegate { model.CanBack = core.CanGoBack; model.CanForward = core.CanGoForward; };
            core.NewWindowRequested += OnNewWindowRequested;
            core.PermissionRequested += OnPermissionRequested;
            core.WindowCloseRequested += delegate { if (popup) Window.Close(); };
            core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e)
            {
                Diagnostics.Write("webview-process-failed", e.ProcessFailedKind.ToString());
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                {
                    needsRecreate = true;
                    model.Fail("浏览器运行时已停止", "点击重新尝试，将重新启动地图。登录配置仍保存在本机。 ");
                }
                else if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                    model.Fail("地图页面意外停止", "点击重新尝试以加载地图。 ");
            };
            // The page adapter only removes identified promotional DOM elements and
            // connects user searches to the local recent list. No native host objects,
            // API keys, altered user-agent or login scripts are injected.
            // WebView2 owns normal cookies/localStorage/IndexedDB in the persistent profile.
            ConfigureProbe();
        }

        private string GetRuntimeFolder()
        {
            string argument = args.FirstOrDefault(a => a.StartsWith("--runtime-dir=", StringComparison.Ordinal));
            string path = argument == null ? null : argument.Substring("--runtime-dir=".Length);
            if (path == null && BuildInfo.UseLegacyWebView)
            {
                // Even the legacy bridge prefers an installed shared runtime.
                // A fixed 109 folder is a fallback when none is installed.
                try
                {
                    if (!string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString(null))) return null;
                }
                catch (WebView2RuntimeNotFoundException) { }
                string shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiduMapsDesktop", "WebView2Runtime109", BuildInfo.Architecture);
                if (File.Exists(Path.Combine(shared, "msedgewebview2.exe"))) path = shared;
            }
            if (path == null) return null;
            if (!Path.IsPathRooted(path) || !File.Exists(Path.Combine(path, "msedgewebview2.exe"))) throw new ArgumentException("运行时目录必须是包含 msedgewebview2.exe 的绝对路径。");
            return Path.GetFullPath(path);
        }

        private async void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (!NavigationPolicy.IsTrusted(e.Uri) && e.Uri != "about:blank")
            {
                e.Handled = true;
                if (e.IsUserInitiated && NavigationPolicy.IsWeb(e.Uri)) OpenExternal(e.Uri);
                return;
            }
            var deferral = e.GetDeferral();
            MapWindow child = null;
            try
            {
                child = new MapWindow(true, null);
                child.Window.Owner = Window;
                child.Window.Show();
                await child.InitializeAsync(environment, false);
                if (child.browser.CoreWebView2 != null)
                {
                    // A real child webview preserves window.opener and shares the login profile.
                    e.NewWindow = child.browser.CoreWebView2;
                    e.Handled = true;
                }
                else { e.Handled = true; child.Window.Close(); }
            }
            catch (Exception ex)
            {
                e.Handled = true;
                Diagnostics.Write("popup-error", ex.Message);
                if (child != null) child.Window.Close();
            }
            finally { deferral.Complete(); }
        }

        private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            permissions.Enqueue(new PendingPermission { Args = e, Deferral = e.GetDeferral() });
            if (permissions.Count == 1) ShowPermission();
        }
        private void ShowPermission()
        {
            if (permissions.Count == 0) { model.Permission = false; return; }
            var e = permissions.Peek().Args;
            string resource = e.PermissionKind == CoreWebView2PermissionKind.Geolocation ? "获取你的当前位置，以便查找附近地点。" : "使用以下权限：" + e.PermissionKind;
            Uri uri;
            string origin = Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) ? uri.Host : "当前网站";
            model.AskPermission(origin + " 请求" + resource + "\n由你选择是否允许。");
        }
        private void AnswerPermission(bool allowed)
        {
            if (permissions.Count == 0) return;
            var pending = permissions.Dequeue();
            try
            {
                pending.Args.State = allowed ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                // This property is absent from the Win7 managed bridge. Avoid
                // a static member reference that would fail when that method JITs.
                var persistence = pending.Args.GetType().GetProperty("SavesInProfile");
                if (persistence != null)
                {
                    try { persistence.SetValue(pending.Args, true, null); }
                    catch (TargetInvocationException) { /* Older runtimes keep their default permission lifetime. */ }
                }
            }
            finally { pending.Deferral.Complete(); }
            ShowPermission();
        }

        private void StartWatchdog()
        {
            if (watchdog == null)
            {
                watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(35) };
                watchdog.Tick += delegate
                {
                    watchdog.Stop();
                    if (closing || !model.Busy || documentReady) return;
                    browser.CoreWebView2.Stop();
                    model.Fail("连接百度地图的时间较长", "请检查网络连接后重新尝试，或者在浏览器中打开地图。");
                    Probe("navigation-timeout");
                };
            }
            watchdog.Stop(); watchdog.Start();
        }

        private async void OpenRoute()
        {
            if (browser.CoreWebView2 == null || !model.Ready) return;
            model.Library = false;
            try
            {
                string result = await browser.ExecuteScriptAsync("window.__baiduDesktop ? window.__baiduDesktop.route() : false");
                if (result != "true") MessageBox.Show(Window, "地图界面还在加载，请稍后再试。也可以使用地图搜索框旁的路线按钮。", "路线规划", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { Diagnostics.Write("route-open-failed", ex.GetType().Name); }
        }
        private async void OpenMapTool(string action)
        {
            if (browser.CoreWebView2 == null || !model.Ready) return;
            model.Library = false;
            try
            {
                string result = await browser.ExecuteScriptAsync("window.__baiduDesktop ? window.__baiduDesktop.mapTool('" + action + "') : false");
                if (result == "false") ShowMapToolUnavailable(action);
                else browser.Focus();
            }
            catch (Exception ex) { Diagnostics.Write("map-tool-open-failed", action + " " + ex.GetType().Name); }
        }
        private void ShowMapToolUnavailable(string action)
        {
            if (closing) return;
            string name = action == "traffic" ? "路况" : action == "measure" ? "测距" : "分享";
            MessageBox.Show(Window, "当前地图暂时无法使用“" + name + "”，请等待网页加载完成，或切回普通地图后重试。", name, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private async void FocusSearch()
        {
            if (!popup && !model.FullScreen) { nativeSearch.Focus(); nativeSearch.SelectAll(); return; }
            if (browser.CoreWebView2 == null) return;
            try { await browser.ExecuteScriptAsync("window.__baiduDesktop && window.__baiduDesktop.focusSearch()"); } catch { }
        }
        private async void ShowLibrary(bool favorites)
        {
            bool close = model.Library && model.ShowingFavorites == favorites;
            model.ShowingFavorites = favorites;
            model.Library = !close;
            RefreshLibrary();
            if (favorites && model.Library)
            {
                string label = "当前地图";
                if (browser.CoreWebView2 != null)
                {
                    try { label = FromJsonString(await browser.ExecuteScriptAsync("window.__baiduDesktop ? window.__baiduDesktop.label() : '当前地图'")); } catch { }
                }
                model.SetFavoriteName(label);
            }
        }
        private void RefreshLibrary() { model.SetEntries(model.ShowingFavorites ? library.Data.Favorites : library.Data.Recent); }
        private async void OpenEntry(object value)
        {
            var entry = value as PlaceEntry;
            if (entry == null || browser.CoreWebView2 == null || needsRecreate) return;
            model.Library = false;
            if (entry.Kind == "search")
            {
                try
                {
                    string result = await browser.ExecuteScriptAsync("window.__baiduDesktop ? window.__baiduDesktop.search(" + ToJsonString(entry.Name) + ") : false");
                    if (result != "true") browser.CoreWebView2.Navigate(NavigationPolicy.Home + "?newmap=1&ie=utf-8&s=s%26wd%3D" + Uri.EscapeDataString(entry.Name));
                    library.AddSearch(entry.Name);
                }
                catch (Exception ex) { Diagnostics.Write("recent-search-failed", ex.GetType().Name); }
            }
            else if (NavigationPolicy.IsTrusted(entry.Url)) browser.CoreWebView2.Navigate(entry.Url);
        }
        private static string ToJsonString(string value)
        {
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(string)).WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray()); }
        }
        private static string FromJsonString(string value)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(value))) return (string)new DataContractJsonSerializer(typeof(string)).ReadObject(stream);
        }

        private string CurrentAddress()
        {
            try { return browser.CoreWebView2 != null && NavigationPolicy.IsWeb(browser.CoreWebView2.Source) ? browser.CoreWebView2.Source : NavigationPolicy.Home; }
            catch { return NavigationPolicy.Home; }
        }
        private static void OpenExternal(string address)
        {
            if (!NavigationPolicy.IsWeb(address)) return;
            try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
            catch (Exception ex) { Diagnostics.Write("external-open-failed", ex.Message); }
        }
        private void Restart()
        {
            string path = Assembly.GetExecutingAssembly().Location;
            Window.Close();
            // Delay allows the existing instance mutex and webview processes to finish shutting down.
            string restartArgs = "--restart";
            foreach (string argument in args.Where(a => a.StartsWith("--work-dir=", StringComparison.Ordinal) || a.StartsWith("--test-data=", StringComparison.Ordinal) || a.StartsWith("--test-vault=", StringComparison.Ordinal)))
                restartArgs += " " + SessionStorage.QuoteArgument(argument);
            Process.Start(new ProcessStartInfo(path, restartArgs) { UseShellExecute = true });
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;
            if (model.CaptureSelecting)
            {
                if (key == Key.Escape) { CancelScreenshot(); e.Handled = true; }
                else if (key == Key.Enter) { if (model.HasCaptureSelection) SaveScreenshotSelection(); e.Handled = true; }
                else if (key == Key.F11) { CancelScreenshot(); ToggleFullScreen(); e.Handled = true; }
                return;
            }
            if (key == Key.F11) { if (!e.IsRepeat) ToggleFullScreen(); e.Handled = true; }
            else if (key == Key.Escape && model.FullScreen) { ToggleFullScreen(); e.Handled = true; }
            else if (key == Key.Escape && model.Library) { model.Library = false; e.Handled = true; }
            else if (key == Key.L && modifiers == ModifierKeys.Control) { FocusSearch(); e.Handled = true; }
            else if (key == Key.D && modifiers == ModifierKeys.Control) { ShowLibrary(true); e.Handled = true; }
            else if (key == Key.S && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { SaveScreenshot(); e.Handled = true; }
            else if (key == Key.F5 || (key == Key.R && modifiers == ModifierKeys.Control)) { model.Refresh.Execute(null); e.Handled = true; }
            else if (modifiers == ModifierKeys.Alt && key == Key.Left) { model.Back.Execute(null); e.Handled = true; }
            else if (modifiers == ModifierKeys.Alt && key == Key.Right) { model.Forward.Execute(null); e.Handled = true; }
            else if (modifiers == ModifierKeys.Alt && key == Key.Home) { model.Home.Execute(null); e.Handled = true; }
        }

        private void ToggleMaximize()
        {
            if (model.FullScreen) ToggleFullScreen();
            Window.WindowState = Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        private void ToggleFullScreen()
        {
            if (!model.FullScreen)
            {
                beforeFullScreen = Window.WindowState;
                boundsBeforeFullScreen = Window.WindowState == WindowState.Normal ? new Rect(Window.Left, Window.Top, Window.Width, Window.Height) : Window.RestoreBounds;
                Rect screen = Native.ScreenBounds(Window);
                model.FullScreen = true;
                Window.WindowState = WindowState.Normal;
                WindowChrome.SetWindowChrome(Window, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
                Window.ResizeMode = ResizeMode.NoResize;
                Window.Left = screen.Left; Window.Top = screen.Top; Window.Width = screen.Width; Window.Height = screen.Height;
                Window.Topmost = true;
            }
            else
            {
                model.FullScreen = false;
                Window.Topmost = false;
                Window.ResizeMode = ResizeMode.CanResize;
                WindowChrome.SetWindowChrome(Window, new WindowChrome { CaptionHeight = 54, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
                Window.Left = boundsBeforeFullScreen.Left; Window.Top = boundsBeforeFullScreen.Top; Window.Width = boundsBeforeFullScreen.Width; Window.Height = boundsBeforeFullScreen.Height;
                Window.WindowState = beforeFullScreen;
            }
            Native.RoundCorners(Window, model.FullScreen);
            UpdateHeaderFullScreen();
        }

        private void RestoreWindow()
        {
            var settings = SettingsStore.Load();
            Window.Width = Math.Min(settings.Width, SystemParameters.WorkArea.Width);
            Window.Height = Math.Min(settings.Height, SystemParameters.WorkArea.Height);
            var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var saved = new Rect(settings.Left, settings.Top, Window.Width, Window.Height);
            if (saved.IntersectsWith(desktop) && Rect.Intersect(saved, desktop).Width >= 240 && Rect.Intersect(saved, desktop).Height >= 100)
            {
                Window.Left = settings.Left; Window.Top = settings.Top;
            }
            else Window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (settings.Maximized) Window.WindowState = WindowState.Maximized;
        }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            while (permissions.Count > 0) { var pending = permissions.Dequeue(); pending.Deferral.Complete(); }
            if (popup) return;
            try
            {
                Rect bounds = model.FullScreen ? boundsBeforeFullScreen : Window.WindowState == WindowState.Normal ? new Rect(Window.Left, Window.Top, Window.Width, Window.Height) : Window.RestoreBounds;
                SettingsStore.Save(new WindowSettings { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Maximized = model.FullScreen ? beforeFullScreen == WindowState.Maximized : Window.WindowState == WindowState.Maximized });
            }
            catch (Exception ex) { Diagnostics.Write("settings-save-failed", ex.Message); }
        }

        private void ConfigureProbe()
        {
            string argument = args.FirstOrDefault(a => a.StartsWith("--smoke-log=", StringComparison.Ordinal));
            if (argument == null) return;
            probePath = argument.Substring("--smoke-log=".Length);
            Probe("webview-ready");
            probeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            probeTimer.Tick += async delegate { await CaptureProbeAsync(); };
            probeTimer.Start();
        }
        private void Probe(string message)
        {
            if (probePath == null) return;
            try { File.AppendAllText(probePath, DateTime.UtcNow.ToString("u") + " " + message + Environment.NewLine, Encoding.UTF8); } catch { }
        }
        private async Task CaptureProbeAsync()
        {
            if (probePath == null || closing || browser.CoreWebView2 == null) return;
            try
            {
                // Test-only read-only DOM inspection; disabled in normal launches.
                string snapshot = await browser.ExecuteScriptAsync("JSON.stringify({title:document.title,ready:document.readyState,inputs:document.querySelectorAll('input').length,canvas:document.querySelectorAll('canvas').length,images:document.images.length,ads:document.querySelectorAll('#message-panel,.ad-banner,.advertisement').length,adapter:!!window.__baiduDesktop,route:!!document.querySelector('#sole-searchbox-content .route-button'),searchHTML:document.querySelector('#sole-searchbox-content')?.outerHTML.slice(0,3000),accountHTML:document.querySelector('#user-center')?.outerHTML.slice(0,1600),text:(document.body?document.body.innerText:'').slice(0,900)})");
                Probe("dom " + snapshot);
                string details = await browser.ExecuteScriptAsync("JSON.stringify({footerOpacity:Array.from(document.querySelectorAll('#MapHolder .BMap_cpyCtrl')).map(n=>getComputedStyle(n).opacity),promos:document.querySelectorAll('.leadDownloadCard,.trafficLeadDownloadCard,.bannerNew-suggest,.download-banner,.banner-new,.banner-new-traffic').length,suggest:!!document.querySelector('#ui3-suggest-wrap'),login:!!document.querySelector('.clear-login-link'),searchFocused:document.activeElement?.id,routeInputs:Array.from(document.querySelectorAll('#route-searchbox-content input')).map(n=>n.placeholder)})");
                Probe("details " + details);
            }
            catch (Exception ex) { Probe("probe-error " + ex.GetType().Name); }
        }
        private async Task ProfileProbeAsync()
        {
            string argument = args.First(a => a.StartsWith("--profile-probe=", StringComparison.Ordinal));
            string output = argument.Substring("--profile-probe=".Length);
            bool write = Array.IndexOf(args, "--probe-write") >= 0;
            var manager = browser.CoreWebView2.CookieManager;
            if (write)
            {
                var cookie = manager.CreateCookie("profile_persistence_probe", "remembered-v1", "desktop-test.invalid", "/");
                cookie.Expires = DateTime.UtcNow.AddDays(2);
                cookie.IsSecure = true;
                manager.AddOrUpdateCookie(cookie);
                await Task.Delay(1500);
            }
            var cookies = await manager.GetCookiesAsync("https://desktop-test.invalid/");
            bool found = cookies.Any(c => c.Name == "profile_persistence_probe" && c.Value == "remembered-v1");
            File.WriteAllText(output, "persistent-profile=" + SettingsStore.ProfilePath + Environment.NewLine + "cookie-found=" + found + Environment.NewLine + "mode=" + (write ? "write" : "read")
                + Environment.NewLine + "bridge=" + typeof(CoreWebView2Environment).Assembly.GetName().Version + Environment.NewLine + "architecture=" + BuildInfo.Architecture);
            if (!write) manager.DeleteCookiesWithDomainAndPath("profile_persistence_probe", "desktop-test.invalid", "/");
            Window.Close();
        }
    }
}
