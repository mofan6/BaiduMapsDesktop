# 百度地图轻量桌面版 1.6

<p align="center"><img src="assets/AppIcon.svg" width="96" alt="百度地图轻量桌面版图标" /></p>

把百度地图官方在线网页装进一个简洁的 Windows 桌面窗口。保留实时地图、地点搜索和路线规划，加入原生顶栏、最近搜索、本机收藏、矩形截图和自动收起的悬浮栏。

## 下载

在 [Releases](https://github.com/mofan6/BaiduMapsDesktop/releases/latest) 下载对应位数的单 EXE，双击运行。

| 版本 | 大小 | 下载 |
| --- | --- | --- |
| 64 位（x64） | 5.79 MB | [百度地图桌面版-1.6-x64.exe](https://github.com/mofan6/BaiduMapsDesktop/releases/download/v1.6/百度地图桌面版-1.6-x64.exe) |
| 32 位（x86） | 5.71 MB | [百度地图桌面版-1.6-x86.exe](https://github.com/mofan6/BaiduMapsDesktop/releases/download/v1.6/百度地图桌面版-1.6-x86.exe) |

支持 Windows 7 SP1 至 Windows 11，需具备下文列出的 .NET Framework 与 WebView2 运行组件。32 位 Windows 请选择 x86；64 位 Windows 优先选择 x64。

## 系统与运行组件

非百度官方客户端。地图、地点名称、搜索和路线使用百度地图官方在线网页，需要联网。只发布 x64（64 位）和 x86（32 位）两个单 EXE，同一架构文件自动适配 Windows 7 SP1、8、8.1、10、11。

每份 EXE 内嵌两套匹配的 WebView2 Core/WPF 接入组件及对应位数的加载器。Win7/8/8.1 自动选择 SDK 1.0.1518.46；Win10/11 且具备 .NET 4.6.2 以上时选择 SDK 1.0.4129.50，继续使用系统新版浏览器引擎。不会为了兼容旧系统而把现代系统统一锁定到 109 引擎。

Win7 SP1 / 8 / 8.1 需要 .NET 4.5 以上和对应位数的 WebView2 Runtime 109（至少 109.0.1518.46）。Win7/8.1 推荐 .NET 4.8，Win8 可使用 4.6.1。Win10/11 推荐 .NET 4.8 和当前 WebView2 Runtime。旧系统自动回退图标字体；现代系统保留现有字体、圆角、DPI 和接入组件。两种架构的新旧桥接已在当前 Windows 环境完成启动检查，尚未在 Win7/8 实机验证；旧引擎停止更新后，网页后续兼容性取决于百度网站。

## 单 EXE 与资料位置

EXE 分别约 5.79 MB（x64）和 5.71 MB（x86），包含界面、中文字体和两套 WebView2 接入组件，不携带整个浏览器引擎。构建脚本强制 EXE 小于 10,000,000 字节。

只分享 EXE 即可，接收方需要上述系统组件；首次使用会建立自己的资料。电脑只有 C 盘也能使用。字体及接入组件内嵌，WebView2 引擎使用电脑上的共享安装，通常位于 C:\Program Files (x86)\Microsoft\EdgeWebView\Application。缺少时从 https://developer.microsoft.com/microsoft-edge/webview2/ 安装 Evergreen Runtime。

EXE 同目录不再生成 .data。长期资料保存为：

    %LOCALAPPDATA%\BaiduMapsDesktop\state.data

包括登录 Cookie、localStorage、IndexedDB、网站历史、窗口设置、最近搜索、收藏。先 ZIP 压缩，再使用 Windows DPAPI 当前用户加密；个人资料不要随软件分享。原 Windows 账号才能解密，不适用于跨电脑迁移或系统重装后恢复。百度服务端会话过期或主动退出后仍需重新登录。

临时工作资料默认位于：

    %LOCALAPPDATA%\BaiduMapsDesktop\Work\<资料标识>\Session

WebView2 运行时仍需要工作目录，地图瓦片等临时占用可能达到数十或数百 MB。正常退出后，等浏览器释放数据库，提交并校验加密资料，删除临时资料；原生加载器由短暂的无界面辅助进程在宿主退出后清理。地图、脚本和 GPU 缓存不写入长期存档。

异常退出或保存失败时保留工作资料，下次优先恢复。安全软件锁定文件时可能暂留小加载器。持久资料大小取决于网站数据，不能承诺永久保持某个固定大小。本版没有实现运行时完全不产生浏览器目录，也没有去掉 WebView2。

## 升级与迁移

关闭旧版，再运行 1.6。新 state.data 不存在时，优先导入 EXE 旁或桌面“百度地图桌面版.data”。发现旧 D/E 等固定盘的对应工作会话时，优先恢复其中更新的持久资料，避免使用尚未更新的旧存档。

只有新存档提交并验证成功，才移除被导入的同目录 .data 和对应旧工作资料。如另有原 1.3 的 AppData 资料，会先将它去缓存后的存档保存在加密资料内部的 previous-profile.zip，再清理明确的旧 WebView2、Runtime 等项目。不会删除包含新 state.data 和 Work 的整个 AppData 根目录。迁移时请勿交替启动不同版本。

## 原有功能

- 原生顶栏：地图搜索、路线、最近搜索、收藏、路况、测距、矩形截图、百度分享、窗口按钮。空白处拖动，双击最大化，最大化避开任务栏。
- 软件图标源为 assets/AppIcon.svg；顶栏和加载界面用对应 WPF 矢量图，Windows 使用同源 16–256 像素多尺寸 ICO。可通过 tools/generate-icon.cjs 从 SVG 重新生成 ICO。
- 底部悬浮栏：返回、前进、刷新、首页、全屏、浏览器、帮助。显示时约 35% 透明；四角阴影有独立透明余量，鼠标远离 3 秒后向下滑出隐藏，接近窗口底部中间区域时滑入，显示时仍保持高 DPI 居中定位。
- 地点详情右上角新增“×”，调用网页原搜索框的取消/清空逻辑，关闭详情并恢复搜索状态。
- 右下角地图操作、全景入口、地图标识和比例尺默认 80% 透明，鼠标贴近约 4 个 CSS 像素或悬停、键盘聚焦时淡入恢复；离开后淡出，保留原点击和拖动行为。
- 最近搜索去重保留 30 条，本机收藏可命名并保存地图链接。
- 矩形截图松开鼠标后直接选择保存位置，支持反向框选、显示缩放和 Esc 取消。
- 清理已识别广告与推广空白，直接删除 leadDownloadCard 等已确认容器；版权背景和文字整体 3% 不透明度，悬停恢复可读。
- 内置基于 Noto Sans SC Regular 的中文黑体子集，用于左侧搜索、提示、地点详情和路线面板；地图标注、图标字体保持原样。

快捷键：F11 全屏；Esc 退出全屏、截图或收起面板；F5 / Ctrl+R 刷新；Ctrl+L 地图搜索；Ctrl+D 收藏；Ctrl+Shift+S 截图；Alt+左/右 返回/前进；Alt+Home 首页。

## 架构与验证

声明式 WPF XAML + MVVM，标准 HWND WebView2 直接渲染，减少原 CompositionControl 捕获浏览器画面再转交 WPF 的开销；悬浮控件使用 WPF Popup。实际帧率仍取决于网页、GPU、系统引擎和显示器，没有绕过百度内部帧率限制，也没有量化测得 120/144 FPS。

构建：运行 .\build.ps1，可用 -OutputPath 指定输出。使用系统 .NET Framework C# 编译器与 WebView2 SDK 1.0.4129.50，无需 Visual Studio 或 .NET SDK。

64 位构建：.\build.ps1 -Architecture x64 -OutputPath C:\build\Map-x64.exe；32 位将 Architecture 改为 x86。两份文件都内嵌新旧接入组件，启动时按系统与框架选择匹配版本，不再区分标准版与 Win7 兼容版。

旧系统已安装 WebView2 109 时可直接双击对应架构的 EXE。找不到可安装的旧运行时时，可从 https://github.com/westinyang/WebView2RuntimeArchive/releases/tag/109.0.1518.78 获取对应 x64/x86 的 FixedVersionRuntime .cab。该地址为第三方历史归档，不是微软官方下载站；SDK .nupkg 不能代替浏览器运行时。解压后找到包含 msedgewebview2.exe 的文件夹，启动时加 --runtime-dir=C:\指定运行时文件夹，也可将运行时完整放到 %LOCALAPPDATA%\BaiduMapsDesktop\WebView2Runtime109\x64 或 x86，旧系统分支会自动识别；现代系统不会自动选择这个旧引擎目录。旧引擎是额外系统依赖，展开后可能数百 MB，不包含于单 EXE，不会由本软件自动下载或覆盖系统运行时。已有新版浏览器创建的个人配置不保证能降级到 109，Win10/11 日常使用建议复用已有新版运行时。

.NET Framework 4.8：https://dotnet.microsoft.com/download/dotnet-framework/net48 。对应 SDK 和最低浏览器版本说明：https://github.com/MicrosoftDocs/edge-developer/blob/main/microsoft-edge/webview2/release-notes/sdk/1-0-1518-46.md 。

开发验证参数：--self-test --test-data=绝对目录；--test-vault=绝对文件 --work-dir=绝对工作目录 隔离用户资料；--profile-probe=输出文件 --probe-write / --profile-probe=输出文件 检查 Cookie 跨进程保存；--page-probe=输出文件 检查真实页面。日常双击无需参数。

## 许可

WebView2 SDK 许可见 THIRD-PARTY-NOTICES.txt。字体 SIL Open Font License 1.1 见 assets/FONT-LICENSE.txt，来源见 assets/FONT-SOURCE.txt；许可也内嵌于 EXE。百度地图网页、数据及商标归其权利人，本软件不提供离线地图。
