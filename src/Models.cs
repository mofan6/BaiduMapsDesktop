using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Input;

namespace BaiduMapsDesktop
{
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action action;
        private readonly Func<bool> allowed;
        public RelayCommand(Action action, Func<bool> allowed = null) { this.action = action; this.allowed = allowed; }
        public bool CanExecute(object parameter) { return allowed == null || allowed(); }
        public void Execute(object parameter) { if (CanExecute(parameter)) action(); }
        public event EventHandler CanExecuteChanged;
        public void Refresh() { var handler = CanExecuteChanged; if (handler != null) handler(this, EventArgs.Empty); }
    }

    internal sealed class ParameterCommand : ICommand
    {
        private readonly Action<object> action;
        public ParameterCommand(Action<object> action) { this.action = action; }
        public bool CanExecute(object parameter) { return parameter != null; }
        public void Execute(object parameter) { if (parameter != null) action(parameter); }
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }

    internal sealed partial class MapViewModel : INotifyPropertyChanged
    {
        private bool busy = true, ready, error, fullScreen, canBack, canForward, permission, library, favorites;
        private double viewportWidth = 1280;
        private string message = "正在打开百度地图", detail = "首次启动可能需要几秒钟。", maximizeGlyph = BuildInfo.Icon("\uE922");
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string name) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name)); }
        public bool Busy { get { return busy; } set { busy = value; Changed("Busy"); } }
        public bool Ready { get { return ready; } set { ready = value; Changed("Ready"); Changed("CoverVisibility"); UpdateBrowserLayers(); } }
        public bool Error { get { return error; } set { error = value; Changed("Error"); Changed("RetryVisibility"); } }
        public bool FullScreen { get { return fullScreen; } set { fullScreen = value; Changed("FullScreen"); Changed("ChromeVisibility"); Changed("FullscreenGlyph"); Changed("FullscreenTip"); } }
        public bool CanBack { get { return canBack; } set { canBack = value; Back.Refresh(); } }
        public bool CanForward { get { return canForward; } set { canForward = value; Forward.Refresh(); } }
        public string Message { get { return message; } set { message = value; Changed("Message"); } }
        public string Detail { get { return detail; } set { detail = value; Changed("Detail"); } }
        public bool Popup { get; set; }
        private bool windowActive;
        public bool WindowActive { get { return windowActive; } set { windowActive = value; UpdateBrowserLayers(); } }
        private void UpdateBrowserLayers() { Changed("BrowserVisibility"); Changed("NavigationOpen"); Changed("LibraryOpen"); Changed("PermissionOpen"); }
        public bool NavigationOpen { get { return !Popup && windowActive && Ready && !CaptureSelecting && NavigationVisible; } }
        public bool LibraryOpen { get { return windowActive && Library && Ready && !CaptureSelecting; } }
        public bool PermissionOpen { get { return windowActive && Permission && !CaptureSelecting; } }
        public Visibility BrowserVisibility { get { return Ready && !CaptureSelecting ? Visibility.Visible : Visibility.Hidden; } }
        public Visibility FeatureVisibility { get { return Popup ? Visibility.Collapsed : Visibility.Visible; } }
        public bool Permission { get { return permission; } set { permission = value; Changed("PermissionVisibility"); UpdateBrowserLayers(); } }
        public string PermissionDetail { get; set; }
        public Visibility PermissionVisibility { get { return permission ? Visibility.Visible : Visibility.Collapsed; } }
        public void AskPermission(string value) { PermissionDetail = value; Changed("PermissionDetail"); Permission = true; }
        public bool Library { get { return library; } set { library = value; Changed("LibraryVisibility"); UpdateBrowserLayers(); } }
        public Visibility LibraryVisibility { get { return library ? Visibility.Visible : Visibility.Collapsed; } }
        public bool ShowingFavorites { get { return favorites; } set { favorites = value; Changed("LibraryTitle"); Changed("FavoriteVisibility"); Changed("LibraryEmptyText"); } }
        public string LibraryTitle { get { return favorites ? "我的收藏" : "最近搜索"; } }
        public string LibraryEmptyText { get { return favorites ? "收藏当前地图，下次一键回到这里。" : "搜索过的地点会出现在这里。"; } }
        public string FavoriteName { get; set; }
        public Visibility FavoriteVisibility { get { return favorites ? Visibility.Visible : Visibility.Collapsed; } }
        public ObservableCollection<PlaceEntry> LibraryEntries { get; private set; }
        public Visibility LibraryEmptyVisibility { get { return LibraryEntries == null || LibraryEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public void SetEntries(System.Collections.Generic.IEnumerable<PlaceEntry> entries) { LibraryEntries = new ObservableCollection<PlaceEntry>(entries); Changed("LibraryEntries"); Changed("LibraryEmptyVisibility"); }
        public void SetFavoriteName(string value) { FavoriteName = value; Changed("FavoriteName"); }
        public double ViewportWidth { get { return viewportWidth; } set { viewportWidth = value; Changed("LabelVisibility"); Changed("HeaderSearchWidth"); } }
        public Visibility LabelVisibility { get { return viewportWidth >= 1180 ? Visibility.Visible : Visibility.Collapsed; } }
        public string MaximizeGlyph { get { return maximizeGlyph; } set { maximizeGlyph = value; Changed("MaximizeGlyph"); } }
        public string FullscreenGlyph { get { return BuildInfo.Icon(fullScreen ? "\uE73F" : "\uE740"); } }
        public string FullscreenTip { get { return fullScreen ? "退出全屏 · F11 / Esc" : "全屏 · F11"; } }
        public Visibility ChromeVisibility { get { return fullScreen ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility CoverVisibility { get { return ready ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility RetryVisibility { get { return error ? Visibility.Visible : Visibility.Collapsed; } }
        public RelayCommand Back { get; set; }
        public RelayCommand Forward { get; set; }
        public RelayCommand Refresh { get; set; }
        public RelayCommand Home { get; set; }
        public RelayCommand OpenBrowser { get; set; }
        public RelayCommand Fullscreen { get; set; }
        public RelayCommand Minimize { get; set; }
        public RelayCommand Maximize { get; set; }
        public RelayCommand Close { get; set; }
        public RelayCommand Retry { get; set; }
        public RelayCommand Help { get; set; }
        public RelayCommand AllowPermission { get; set; }
        public RelayCommand DenyPermission { get; set; }
        public RelayCommand Route { get; set; }
        public RelayCommand Recent { get; set; }
        public RelayCommand Favorites { get; set; }
        public RelayCommand SaveFavorite { get; set; }
        public RelayCommand CloseLibrary { get; set; }
        public ParameterCommand OpenEntry { get; set; }
        public ParameterCommand RemoveEntry { get; set; }
        public RelayCommand Screenshot { get; set; }
        public RelayCommand Traffic { get; set; }
        public RelayCommand Measure { get; set; }
        public RelayCommand Share { get; set; }
        private bool trafficActive;
        public bool TrafficActive { get { return trafficActive; } set { trafficActive = value; Changed("TrafficActive"); } }
        public void Fail(string title, string description) { Message = title; Detail = description; Error = true; Busy = false; Ready = false; }
        public void Loading() { Error = false; Busy = true; Message = "正在打开百度地图"; Detail = "首次启动可能需要几秒钟。"; }
    }

    [DataContract]
    internal sealed class WindowSettings
    {
        [DataMember] public double Width = 1280;
        [DataMember] public double Height = 820;
        [DataMember] public double Left = -99999;
        [DataMember] public double Top = -99999;
        [DataMember] public bool Maximized;
    }

    internal static class SettingsStore
    {
        internal static string ProfilePath { get { return Path.Combine(Bootstrap.DataRoot, "WebView2"); } }
        internal static WindowSettings Load() { return LoadFrom(Path.Combine(Bootstrap.DataRoot, "settings.json")); }
        internal static WindowSettings LoadFrom(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var settings = (WindowSettings)new DataContractJsonSerializer(typeof(WindowSettings)).ReadObject(stream);
                    if (settings == null || double.IsNaN(settings.Width) || double.IsInfinity(settings.Width) || double.IsNaN(settings.Height) || double.IsInfinity(settings.Height)) return new WindowSettings();
                    settings.Width = Math.Max(760, Math.Min(6000, settings.Width));
                    settings.Height = Math.Max(520, Math.Min(4000, settings.Height));
                    if (double.IsNaN(settings.Left) || double.IsInfinity(settings.Left)) settings.Left = -99999;
                    if (double.IsNaN(settings.Top) || double.IsInfinity(settings.Top)) settings.Top = -99999;
                    return settings;
                }
            }
            catch { return new WindowSettings(); }
        }
        internal static void Save(WindowSettings settings) { SaveTo(Path.Combine(Bootstrap.DataRoot, "settings.json"), settings); }
        internal static void SaveTo(string path, WindowSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(WindowSettings)).WriteObject(stream, settings);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }

    internal static class NavigationPolicy
    {
        internal const string Home = "https://map.baidu.com/";
        internal static bool IsTrusted(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri)) return false;
            return uri.Scheme == "https" && (uri.Host.Equals("baidu.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".baidu.com", StringComparison.OrdinalIgnoreCase));
        }
        internal static bool IsWeb(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http");
        }
    }

    internal static class Diagnostics
    {
        internal static string TestPath;
        internal static void Write(string kind, string text)
        {
            // Record operational failures only. No visited URLs, cookies or account names.
            try { File.AppendAllText(TestPath ?? Path.Combine(Bootstrap.DataRoot, "diagnostics.log"), DateTime.UtcNow.ToString("u") + " " + kind + " " + text + Environment.NewLine); } catch { }
        }
    }
}
