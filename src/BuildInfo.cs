using System;
using System.Collections.Generic;
using System.Text;

namespace BaiduMapsDesktop
{
    internal static class BuildInfo
    {
        internal const string Version = "1.6";
        internal const string FileVersion = "1.6.0.0";
        private static bool legacyWebView = Environment.OSVersion.Version.Major < 10 || !HasModernFramework();
        internal static bool UseLegacyWebView { get { return legacyWebView; } }
        private static bool LegacyFonts { get { return Environment.OSVersion.Version.Major < 10; } }
        internal static string WebViewResourcePrefix { get { return "Embedded." + (UseLegacyWebView ? "Legacy." : "Modern."); } }
        internal static string SdkVersion { get { return UseLegacyWebView ? "1.0.1518.46" : "1.0.4129.50"; } }
        internal const string Edition = "通用版 · Windows 7–11";
        internal const string RuntimeRequirement = "需要 .NET Framework 和 Microsoft Edge WebView2 运行时。\nWindows 7 SP1 / 8 / 8.1：.NET 4.5 以上，配对应位数的 WebView2 Runtime 109；Win7/8.1 推荐 .NET 4.8，Win8 可用 4.6.1。\nWindows 10/11：推荐 .NET 4.8，优先复用系统安装的新版运行时，不降级引擎。\n官方下载：https://developer.microsoft.com/microsoft-edge/webview2/\n旧引擎已停更，百度网页后续兼容性取决于网站。";
        private static bool HasModernFramework()
        {
            object release = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 0);
            return release is int && (int)release >= 394802; // .NET 4.6.2
        }
#if X86_BUILD
        internal const string Architecture = "x86";
#else
        internal const string Architecture = "x64";
#endif
        internal static string Icon(string value)
        {
            if (!LegacyFonts) return value;
            string result;
            return Symbols.TryGetValue(value, out result) ? result : value;
        }
        internal static byte[] AdaptView(byte[] view)
        {
            if (!LegacyFonts) return view;
            // Segoe MDL2 Assets and Microsoft YaHei UI were introduced after
            // Windows 7. Common Unicode symbols use its installed system fonts.
            string text = Encoding.UTF8.GetString(view).Replace("Segoe MDL2 Assets", "Segoe UI Symbol, Segoe UI")
                .Replace("Microsoft YaHei UI", "Microsoft YaHei");
            foreach (var symbol in Symbols) text = text.Replace("&#x" + ((int)symbol.Key[0]).ToString("X4") + ";", "&#x" + ((int)symbol.Value[0]).ToString("X4") + ";");
            return Encoding.UTF8.GetBytes(text);
        }
        private static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>
        {
            { "\uE707", "\u25C8" }, { "\uE70A", "\u21C4" }, { "\uE81C", "\u25F7" },
            { "\uE734", "\u2606" }, { "\uE722", "\u25A3" }, { "\uE71B", "\u2197" },
            { "\uE921", "\u2212" }, { "\uE922", "\u25A1" }, { "\uE923", "\u25A3" },
            { "\uE8BB", "\u00D7" }, { "\uE72B", "\u2190" }, { "\uE72A", "\u2192" },
            { "\uE72C", "\u21BB" }, { "\uE80F", "\u2302" }, { "\uE73F", "\u2199" },
            { "\uE740", "\u2197" }, { "\uE774", "\u25CE" }, { "\uE946", "?" },
            { "\uE74D", "\u00D7" }
        };
    }
}
