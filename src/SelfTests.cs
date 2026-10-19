using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;

namespace BaiduMapsDesktop
{
    internal static class SelfTests
    {
        internal static bool SameHash(byte[] first, byte[] second)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(first).SequenceEqual(sha.ComputeHash(second));
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception("Test failed: " + message); }
        internal static int Run()
        {
            string temp = Path.Combine(Path.GetTempPath(), "BaiduMapsDesktop-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                Assert(NavigationPolicy.IsTrusted("https://map.baidu.com/"), "map origin allowed");
                Assert(NavigationPolicy.IsTrusted("https://passport.baidu.com/v2/?login"), "login origin allowed");
                Assert(!NavigationPolicy.IsTrusted("https://baidu.com.attacker.invalid/"), "lookalike hostname denied");
                Assert(!NavigationPolicy.IsTrusted("https://attacker.invalid/?map.baidu.com"), "hostname in query denied");
                Assert(!NavigationPolicy.IsTrusted("http://map.baidu.com/"), "insecure top-level map denied");
                Assert(!NavigationPolicy.IsWeb("javascript:alert(1)"), "script URI never opens externally");
                Assert(!NavigationPolicy.IsWeb("file:///C:/Windows/system.ini"), "file URI denied");
                string file = Path.Combine(temp, "settings.json");
                SettingsStore.SaveTo(file, new WindowSettings { Width = 1111, Height = 777, Left = 80, Top = 90, Maximized = true });
                var restored = SettingsStore.LoadFrom(file);
                Assert(restored.Width == 1111 && restored.Height == 777 && restored.Left == 80 && restored.Top == 90 && restored.Maximized, "window restore roundtrip");
                SettingsStore.SaveTo(file, new WindowSettings { Width = 1300, Height = 850 });
                Assert(SettingsStore.LoadFrom(file).Width == 1300, "atomic settings replacement");
                File.WriteAllText(file, "broken json");
                Assert(SettingsStore.LoadFrom(file).Width == 1280, "corrupt settings fall back");
                SettingsStore.SaveTo(file, new WindowSettings { Width = 1, Height = 99999 });
                Assert(SettingsStore.LoadFrom(file).Width == 760 && SettingsStore.LoadFrom(file).Height == 4000, "invalid dimensions bounded");
                Assert(Path.IsPathRooted(SettingsStore.ProfilePath) && SettingsStore.ProfilePath.StartsWith(Bootstrap.DataRoot, StringComparison.OrdinalIgnoreCase), "stable per-user browser profile");
                Assert(SameHash(File.ReadAllBytes(Path.Combine(Bootstrap.NativeRoot, "WebView2Loader.dll")), Bootstrap.ReadResource(BuildInfo.WebViewResourcePrefix + "WebView2Loader.dll")), "embedded native payload integrity");
                Assert(Bootstrap.ReadResource("Embedded.MainWindow.xaml").Length > 1000, "declarative view embedded");
                string libraryPath = Path.Combine(temp, "library.json");
                var library = new LibraryStore(libraryPath);
                library.AddSearch("故宫"); library.AddSearch("天安门"); library.AddSearch("故宫");
                Assert(library.Data.Recent.Count == 2 && library.Data.Recent[0].Name == "故宫", "search history deduplicated and reordered");
                for (int i = 0; i < 40; i++) library.AddSearch("测试地点 " + i);
                Assert(library.Data.Recent.Count == 30, "search history bounded");
                library.AddFavorite("地图位置", "https://map.baidu.com/@12958160,4825907,16z");
                library.AddFavorite("改名", "https://map.baidu.com/@12958160,4825907,16z");
                library.AddFavorite("恶意地址", "file:///C:/Windows/system.ini");
                var restoredLibrary = new LibraryStore(libraryPath);
                Assert(restoredLibrary.Data.Favorites.Count == 1 && restoredLibrary.Data.Favorites[0].Name == "改名" && restoredLibrary.Data.Recent.Count == 30, "library persists and rejects unsafe addresses");
                File.WriteAllText(libraryPath, "broken");
                Assert(new LibraryStore(libraryPath).Data.Recent.Count == 0, "corrupt library recovers");
                Assert(Bootstrap.ReadResource("Embedded.PageAdapter.js").Length > 1000, "page cleanup adapter embedded");
                Assert(Bootstrap.ReadResource("Embedded.ModernSans.woff2").Length > 100000, "modern Chinese font embedded");
                VerifySessionStorage(temp);
                Assert(SessionStorage.ChooseWorkParent(Path.Combine(temp, "Map.exe"), null, false) == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiduMapsDesktop", "Work"), "single-C-drive default supported");
                Assert(SessionStorage.ChooseWorkParent(Path.Combine(temp, "Map.exe"), temp, false) == temp, "system-drive custom work path allowed");
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    Size viewport = new Size(1280 * scale, 766 * scale), panel = new Size(352 * scale, 150 * scale);
                    Point position = OverlayPlacement.Calculate("NavigationPopup", panel, viewport, new Vector(scale, scale));
                    Assert(Math.Abs(position.X + panel.Width / 2 - viewport.Width / 2) < 0.01, "navigation centered at scale " + scale);
                    Assert(Math.Abs(viewport.Height - position.Y - panel.Height) < 0.01, "navigation animation stage ends at the viewport bottom at scale " + scale);
                }
                var scaledCrop = ScreenshotCrop.ToPixels(new Rect(10, 20, 100, 80), 1280, 720, 1920, 1080);
                Assert(scaledCrop.X == 15 && scaledCrop.Y == 30 && scaledCrop.Width == 150 && scaledCrop.Height == 120, "selection respects 150 percent image scaling");
                var reverseCrop = ScreenshotCrop.ToPixels(new Rect(new Point(120, 100), new Point(20, 10)), 1000, 700, 1000, 700);
                Assert(reverseCrop.X == 20 && reverseCrop.Y == 10 && reverseCrop.Width == 100 && reverseCrop.Height == 90, "reverse-direction selection");
                var edgeCrop = ScreenshotCrop.ToPixels(new Rect(-10, -10, 1020, 720), 1000, 700, 2000, 1400);
                Assert(edgeCrop.X == 0 && edgeCrop.Y == 0 && edgeCrop.Width == 2000 && edgeCrop.Height == 1400, "selection clamped to image edges");
                File.WriteAllText(Path.Combine(Bootstrap.DataRoot, "self-test-result.txt"), "PASS: origin policy, settings/library roundtrip, resources/font, rectangle/DPI crop, DPAPI archive/migration/recovery, cache exclusion, cleanup, path traversal rejection, native overlay state.");
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Bootstrap.DataRoot, "self-test-result.txt"), ex.ToString());
                return 1;
            }
            finally
            {
                string resolved = Path.GetFullPath(temp);
                string allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "BaiduMapsDesktop-tests-";
                if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
            }
        }
        private static void VerifySessionStorage(string temp)
        {
            string legacy = Path.Combine(temp, "legacy"), vault = Path.Combine(temp, "state.data"), work = Path.Combine(temp, "work");
            string[] paths = { "settings.json", "library.json", "WebView2/EBWebView/Local State", "WebView2/EBWebView/WV2Profile_default/Network/Cookies", "WebView2/EBWebView/WV2Profile_default/Local Storage/leveldb/000003.log", "WebView2/EBWebView/WV2Profile_default/IndexedDB/site/000003.ldb", "WebView2/EBWebView/WV2Profile_default/History", "WebView2/EBWebView/Default/Preferences" };
            foreach (string path in paths) { string target = Path.Combine(legacy, path); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllText(target, "persistent:" + path); }
            string cache = Path.Combine(legacy, "WebView2/EBWebView/WV2Profile_default/Cache/Cache_Data/junk");
            Directory.CreateDirectory(Path.GetDirectoryName(cache)); File.WriteAllText(cache, "cache should disappear");
            var first = new SessionStorage(vault, work, legacy);
            first.Open();
            Assert(File.Exists(vault) && !File.Exists(Path.Combine(first.SessionRoot, "WebView2/EBWebView/WV2Profile_default/Cache/Cache_Data/junk")), "migration excludes cache");
            Assert(first.Finish() && !Directory.Exists(first.SessionRoot), "archive saved and workspace removed");
            byte[] zip = SessionStorage.ReadVault(vault);
            using (var buffer = new MemoryStream(zip)) using (var archive = new ZipArchive(buffer, ZipArchiveMode.Read)) Assert(archive.Entries.Count == paths.Length, "all persistent files retained");
            var second = new SessionStorage(vault, work, null);
            second.Open();
            foreach (string path in paths) Assert(File.ReadAllText(Path.Combine(second.SessionRoot, path)) == "persistent:" + path, "restored " + path);
            string root = second.SessionRoot;
            // Simulate crash recovery from fresher state while retaining a valid vault.
            File.WriteAllText(Path.Combine(root, "settings.json"), "newer");
            Assert(second.Finish(), "second atomic save");
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "session.info"), vault); File.WriteAllText(Path.Combine(root, "settings.json"), "recovered");
            var third = new SessionStorage(vault, work, null); third.Open();
            Assert(File.ReadAllText(Path.Combine(root, "settings.json")) == "recovered" && third.Finish(), "fresh crash state recovered");
            string portable = Path.Combine(temp, "Map.data"), oldParent = Path.Combine(temp, "old-work");
            File.Copy(vault, portable);
            var oldWork = new SessionStorage(portable, oldParent, null);
            Directory.CreateDirectory(oldWork.SessionRoot); File.WriteAllText(Path.Combine(oldWork.SessionRoot, "session.info"), portable);
            File.WriteAllText(Path.Combine(oldWork.SessionRoot, "settings.json"), "fresh-working-state");
            File.WriteAllText(Path.Combine(oldWork.SessionRoot, "weather.json"), "{\"Radar\":true,\"Opacity\":20}");
            string centralized = Path.Combine(temp, "application-data", "state.data");
            var moved = new SessionStorage(centralized, Path.Combine(temp, "application-data", "Work"), null, portable, oldParent);
            moved.Open();
            Assert(File.Exists(centralized) && !File.Exists(portable) && !Directory.Exists(oldWork.WorkingRoot), "portable state moved and old workspace retired");
            Assert(File.ReadAllText(Path.Combine(moved.SessionRoot, "settings.json")) == "fresh-working-state", "fresh old working state wins over older archive");
            Assert(!File.Exists(Path.Combine(moved.SessionRoot, "weather.json")) && moved.Finish(), "retired settings excluded during migration");
            using (var previous = new MemoryStream())
            {
                using (var archive = new ZipArchive(previous, ZipArchiveMode.Create, true))
                {
                    using (var output = new StreamWriter(archive.CreateEntry("weather.json").Open())) output.Write("{}");
                    using (var output = new StreamWriter(archive.CreateEntry("library.json").Open())) output.Write("kept-history");
                }
                string restoredRoot = Path.Combine(temp, "previous-version");
                SessionStorage.RestoreArchive(previous.ToArray(), restoredRoot);
                Assert(!File.Exists(Path.Combine(restoredRoot, "weather.json")) && File.ReadAllText(Path.Combine(restoredRoot, "library.json")) == "kept-history", "previous archive remains compatible while retired settings are omitted");
            }
            using (var bad = new MemoryStream())
            {
                using (var archive = new ZipArchive(bad, ZipArchiveMode.Create, true)) using (var stream = archive.CreateEntry("WebView2/../../escape").Open()) stream.WriteByte(1);
                bool rejected = false;
                try { SessionStorage.RestoreArchive(bad.ToArray(), Path.Combine(temp, "unpack")); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && !File.Exists(Path.Combine(temp, "escape")), "archive traversal rejected");
            }
            var vm = new MapViewModel { Ready = true, WindowActive = true };
            Assert(vm.NavigationOpen && vm.BrowserVisibility == Visibility.Visible, "native navigation visible");
            vm.Library = true; Assert(vm.LibraryOpen, "library overlay enabled");
            vm.CaptureSelecting = true;
            Assert(!vm.NavigationOpen && !vm.LibraryOpen && vm.BrowserVisibility == Visibility.Hidden, "capture hides HWND and floating panels");
            vm.CaptureSelecting = false; vm.WindowActive = false;
            Assert(!vm.NavigationOpen && !vm.LibraryOpen, "overlays close when app loses focus");
        }
    }
}
