using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("百度地图桌面版")]
[assembly: AssemblyDescription("百度地图官方网页的轻量桌面封装（非百度官方客户端）")]
[assembly: AssemblyProduct("Baidu Maps Desktop")]
[assembly: AssemblyVersion(BaiduMapsDesktop.BuildInfo.FileVersion)]
[assembly: AssemblyFileVersion(BaiduMapsDesktop.BuildInfo.FileVersion)]

namespace BaiduMapsDesktop
{
    internal static class Bootstrap
    {
        private static string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiduMapsDesktop");
        internal static string DataRoot { get { return dataRoot; } }
        internal static string NativeRoot { get { return Path.Combine(Storage == null ? DataRoot : Storage.WorkingRoot, "Runtime", BuildInfo.SdkVersion + "-" + BuildInfo.Architecture); } }
        private static Mutex instance;
        internal static SessionStorage Storage;
        private static bool testing;
        private static readonly System.Collections.Generic.Dictionary<string, Assembly> webViewAssemblies = new System.Collections.Generic.Dictionary<string, Assembly>();

        [STAThread]
        private static int Main(string[] args)
        {
            if (SessionStorage.RunCleanup(args)) return 0;
            testing = Array.Exists(args, a => a.StartsWith("--test-", StringComparison.Ordinal) || a == "--self-test");
            // Isolated compatibility smoke checks can exercise the old managed
            // bridge on the current machine without changing the user's profile.
            if (testing && Array.Exists(args, a => a.StartsWith("--test-data=", StringComparison.Ordinal)) && Array.IndexOf(args, "--test-legacy-sdk") >= 0)
                typeof(BuildInfo).GetField("legacyWebView", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, true);
            string testLog = Array.Find(args, a => a.StartsWith("--test-log=", StringComparison.Ordinal));
            if (testing && testLog != null) Diagnostics.TestPath = testLog.Substring("--test-log=".Length);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
            bool ownsInstance = false;
            try
            {
                bool isolatedTest = false;
                foreach (string argument in args)
                {
                    if (!argument.StartsWith("--test-data=", StringComparison.Ordinal)) continue;
                    string path = argument.Substring("--test-data=".Length);
                    if (!Path.IsPathRooted(path)) throw new ArgumentException("测试数据目录必须是绝对路径。");
                    dataRoot = Path.GetFullPath(path);
                    isolatedTest = true;
                }
                if (Array.IndexOf(args, "--self-test") >= 0)
                {
                    Directory.CreateDirectory(DataRoot); Directory.CreateDirectory(NativeRoot); ExtractNative();
                    return SelfTests.Run();
                }
                string testVault = Array.Find(args, a => a.StartsWith("--test-vault=", StringComparison.Ordinal));
                if (testVault != null) isolatedTest = true;
                bool created;
                string mutexName = isolatedTest ? @"Local\BaiduMapsDesktop.test." + System.Diagnostics.Process.GetCurrentProcess().Id : @"Local\BaiduMapsDesktop.v1";
                instance = new Mutex(false, mutexName, out created);
                try { ownsInstance = instance.WaitOne(Array.IndexOf(args, "--restart") >= 0 ? 10000 : 0); }
                catch (AbandonedMutexException) { ownsInstance = true; }
                if (!ownsInstance) { Native.ActivateExisting(); return 0; }
                if (!isolatedTest || testVault != null)
                {
                    string executable = Assembly.GetExecutingAssembly().Location;
                    string applicationRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiduMapsDesktop");
                    string vault = testVault == null ? Path.Combine(applicationRoot, "state.data") : testVault.Substring("--test-vault=".Length);
                    string workArgument = Array.Find(args, a => a.StartsWith("--work-dir=", StringComparison.Ordinal));
                    string work = SessionStorage.ChooseWorkParent(executable, workArgument == null ? null : workArgument.Substring("--work-dir=".Length), isolatedTest);
                    string importArgument = Array.Find(args, a => a.StartsWith("--import-vault=", StringComparison.Ordinal));
                    string portable = importArgument == null ? Path.ChangeExtension(executable, ".data") : importArgument.Substring("--import-vault=".Length);
                    if (testVault == null && !File.Exists(portable)) portable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "百度地图桌面版.data");
                    Storage = new SessionStorage(vault, work, testVault == null ? applicationRoot : null, testVault == null || importArgument != null ? portable : null);
                    dataRoot = Storage.SessionRoot;
                    Storage.Open();
                }
                Directory.CreateDirectory(DataRoot); Directory.CreateDirectory(NativeRoot); ExtractNative();
                if (Array.IndexOf(args, "--migrate-only") >= 0) return 0;
                return StartApplication(args);
            }
            catch (Exception ex)
            {
                Diagnostics.Write("startup-failed", ex.ToString());
                if (!testing) MessageBox.Show("软件未能启动。\n\n" + ex.Message + "\n\n诊断日志：" + Path.Combine(DataRoot, "diagnostics.log"), "百度地图桌面版", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            finally
            {
                if (Storage != null) Storage.Finish();
                if (instance != null) { if (ownsInstance) { try { instance.ReleaseMutex(); } catch (ApplicationException) { } } instance.Dispose(); }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int StartApplication(string[] args)
        {
            // AppContext was added in .NET 4.6; Win8's original .NET 4.5 can
            // still run the legacy bridge. Modern WPF retains its DPI switches.
            var contextType = typeof(object).Assembly.GetType("System.AppContext");
            var setSwitch = contextType == null ? null : contextType.GetMethod("SetSwitch", new[] { typeof(string), typeof(bool) });
            if (setSwitch != null)
            {
                setSwitch.Invoke(null, new object[] { "Switch.System.Windows.DoNotScaleForDpiChanges", false });
                setSwitch.Invoke(null, new object[] { "Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false });
            }
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                Diagnostics.Write("ui-error", e.Exception.ToString());
                if (testing) app.Shutdown(1);
                else MessageBox.Show("发生了一个错误，请重新打开软件。\n" + e.Exception.Message, "百度地图桌面版", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            var window = new MapWindow(false, args);
            app.MainWindow = window.Window;
            return app.Run(window.Window);
        }

        private static Assembly ResolveEmbedded(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.Wpf") return null;
            lock (webViewAssemblies)
            {
                Assembly assembly;
                if (!webViewAssemblies.TryGetValue(name, out assembly))
                {
                    // AssemblyResolve deliberately supplies the matching Core
                    // and WPF pair, including its version, before MapWindow JIT.
                    assembly = Assembly.Load(ReadResource(BuildInfo.WebViewResourcePrefix + name + ".dll"));
                    webViewAssemblies.Add(name, assembly);
                }
                return assembly;
            }
        }

        internal static byte[] ReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("缺少嵌入资源：" + name);
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }

        private static void ExtractNative()
        {
            string path = Path.Combine(NativeRoot, "WebView2Loader.dll");
            byte[] expected = ReadResource(BuildInfo.WebViewResourcePrefix + "WebView2Loader.dll");
            // A versioned per-user folder avoids DLL search in the executable directory.
            // Hash comparison also repairs an interrupted extraction on the next launch.
            if (File.Exists(path) && SelfTests.SameHash(File.ReadAllBytes(path), expected)) return;
            string temp = path + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
            File.WriteAllBytes(temp, expected);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (IOException)
            {
                if (!File.Exists(path) || !SelfTests.SameHash(File.ReadAllBytes(path), expected)) throw;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
