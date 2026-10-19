using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;

namespace BaiduMapsDesktop
{
    internal sealed partial class MapWindow
    {
        private bool pageVerified;
        private System.Windows.Threading.DispatcherTimer verificationTimeout;
        private void BeginVerificationTimeout()
        {
            string argument = args.FirstOrDefault(a => a.StartsWith("--page-probe=", StringComparison.Ordinal));
            if (argument == null) return;
            verificationTimeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(65) };
            verificationTimeout.Tick += delegate
            {
                verificationTimeout.Stop();
                try { File.WriteAllText(argument.Substring("--page-probe=".Length), "FAIL: official page probe timed out; network/page was not ready."); } catch { }
                Window.Close();
            };
            verificationTimeout.Start();
        }
        private async Task VerifyPageAsync()
        {
            string argument = args.FirstOrDefault(a => a.StartsWith("--page-probe=", StringComparison.Ordinal));
            if (argument == null || pageVerified || !browser.CoreWebView2.Source.StartsWith("https://map.baidu.com/", StringComparison.OrdinalIgnoreCase)) return;
            pageVerified = true;
            string output = argument.Substring("--page-probe=".Length);
            try
            {
                await Task.Delay(3000);
                await browser.ExecuteScriptAsync("document.fonts.load('14px \"BMD Sans SC\"', '淮北站路线');void 0");
                await Task.Delay(1500);
                string result = await browser.ExecuteScriptAsync(@"(()=>{
                    const input=document.querySelector('#sole-input');
                    const map=document.querySelector('#MapHolder');
                    const originalMap=map?getComputedStyle(map).fontFamily:null;
                    const holder=document.createElement('div');
                    holder.innerHTML='<div id=leadDownloadCard style=height:80px></div><div class=poiLeadDownloadCard style=height:80px></div><p id=bmd-place-test>地点资料</p>';
                    document.body.append(holder);
                    const hidden=Array.from(holder.querySelectorAll('#leadDownloadCard,.poiLeadDownloadCard')).every(n=>getComputedStyle(n).display==='none');
                    window.__baiduDesktop.clean();
                    const removed=!holder.querySelector('#leadDownloadCard,.poiLeadDownloadCard')&&!!holder.querySelector('#bmd-place-test');
                    holder.remove();
                    return JSON.stringify({adapter:!!window.__baiduDesktop,search:!!input,fontLoaded:document.fonts.check('14px ""BMD Sans SC""','淮北站路线'),searchFont:input?getComputedStyle(input).fontFamily:null,mapFont:originalMap,mapFontUnchanged:!map||getComputedStyle(map).fontFamily===originalMap,adBlankHidden:hidden,adContainersRemoved:removed,canvas:document.querySelectorAll('canvas').length});
                })()");
                File.WriteAllText(output, "browser=" + browser.GetType().Name + Environment.NewLine + "page=" + result + Environment.NewLine
                    + "navigation-binding=" + ((Popup)Window.FindName("NavigationPopup")).GetBindingExpression(Popup.IsOpenProperty).Status);
            }
            catch (Exception ex) { File.WriteAllText(output, ex.ToString()); }
            finally { Window.Close(); }
        }
    }
}
