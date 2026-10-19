param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'dist/BaiduMapsDesktop.exe'),
    [string]$WebView2Package = '',
    [string]$LegacyWebView2Package = '',
    [ValidateSet('x64','x86')][string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
function Resolve-WebView2Package([string]$Version, [string]$SuppliedPackage) {
    if ($SuppliedPackage) { return $SuppliedPackage }
    $cachedPackage = Join-Path $env:USERPROFILE ".nuget/packages/microsoft.web.webview2/$Version"
    if (Test-Path -LiteralPath $cachedPackage) { return $cachedPackage }
    $dependencyPackage = Join-Path $PSScriptRoot "artifacts/dependencies/webview2-$Version"
    if (-not (Test-Path -LiteralPath $dependencyPackage)) {
            New-Item -ItemType Directory -Path $dependencyPackage -Force | Out-Null
            $packageFile = Join-Path $PSScriptRoot "artifacts/dependencies/webview2-$Version.zip"
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$Version/microsoft.web.webview2.$Version.nupkg" -OutFile $packageFile
            Expand-Archive -LiteralPath $packageFile -DestinationPath $dependencyPackage -Force
    }
    return $dependencyPackage
}
$WebView2Package = Resolve-WebView2Package '1.0.4129.50' $WebView2Package
$LegacyWebView2Package = Resolve-WebView2Package '1.0.1518.46' $LegacyWebView2Package
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe' }
$framework = Split-Path $compiler
$wpf = Join-Path $framework 'WPF'
$outputAbsolute = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path $outputAbsolute) -Force | Out-Null
$noticesPath = Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt'
$refs = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Runtime.Serialization.dll'),
    (Join-Path $framework 'System.Security.dll'),
    (Join-Path $framework 'System.IO.Compression.dll'),
    (Join-Path $framework 'System.Xaml.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $WebView2Package 'lib/net462/Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $WebView2Package 'lib/net462/Microsoft.Web.WebView2.Wpf.dll')
)
$arguments = @('/nologo', '/target:winexe', "/platform:$Architecture", '/optimize+', '/debug-', '/codepage:65001', '/utf8output', "/out:$outputAbsolute", "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')", "/win32icon:$(Join-Path $PSScriptRoot 'assets/AppIcon.ico')")
$defines = @()
if ($Architecture -eq 'x86') { $defines += 'X86_BUILD' }
if ($defines.Count -gt 0) { $arguments += ('/define:' + ($defines -join ';')) }
$arguments += $refs | ForEach-Object { "/reference:$_" }
$arguments += @(
    "/resource:$(Join-Path $PSScriptRoot 'src/MainWindow.xaml'),Embedded.MainWindow.xaml",
    "/resource:$(Join-Path $PSScriptRoot 'assets/AppIcon.ico'),Embedded.AppIcon.ico",
    "/resource:$(Join-Path $PSScriptRoot 'assets/AppIcon.svg'),Embedded.AppIcon.svg",
    "/resource:$(Join-Path $PSScriptRoot 'assets/AppIcon.xaml'),Embedded.AppIcon.xaml",
    "/resource:$(Join-Path $PSScriptRoot 'assets/page-adapter.js'),Embedded.PageAdapter.js",
    "/resource:$(Join-Path $PSScriptRoot 'assets/ModernSans.woff2'),Embedded.ModernSans.woff2",
    "/resource:$(Join-Path $PSScriptRoot 'assets/FONT-LICENSE.txt'),Embedded.FontLicense.txt",
    "/resource:$noticesPath,Embedded.ThirdPartyNotices.txt",
    "/resource:$(Join-Path $WebView2Package 'lib/net462/Microsoft.Web.WebView2.Core.dll'),Embedded.Modern.Microsoft.Web.WebView2.Core.dll",
    "/resource:$(Join-Path $WebView2Package 'lib/net462/Microsoft.Web.WebView2.Wpf.dll'),Embedded.Modern.Microsoft.Web.WebView2.Wpf.dll",
    "/resource:$(Join-Path $WebView2Package "runtimes/win-$Architecture/native/WebView2Loader.dll"),Embedded.Modern.WebView2Loader.dll",
    "/resource:$(Join-Path $LegacyWebView2Package 'lib/net45/Microsoft.Web.WebView2.Core.dll'),Embedded.Legacy.Microsoft.Web.WebView2.Core.dll",
    "/resource:$(Join-Path $LegacyWebView2Package 'lib/net45/Microsoft.Web.WebView2.Wpf.dll'),Embedded.Legacy.Microsoft.Web.WebView2.Wpf.dll",
    "/resource:$(Join-Path $LegacyWebView2Package "runtimes/win-$Architecture/native/WebView2Loader.dll"),Embedded.Legacy.WebView2Loader.dll"
)
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
$built = Get-Item -LiteralPath $outputAbsolute
if ($built.Length -ge 10000000) { throw "EXE exceeds the strict 10 MB (10,000,000 byte) limit: $($built.Length) bytes" }
Write-Output "Built: $($built.FullName)"
Write-Output "Size: $([Math]::Round($built.Length / 1MB, 3)) MiB ($($built.Length) bytes)"
Get-FileHash -LiteralPath $outputAbsolute -Algorithm SHA256 | Format-List
