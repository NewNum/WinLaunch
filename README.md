# WinLaunch

WinLaunch 是一款 Windows 平台上的启动器（Launchpad）应用，界面与交互参考 macOS 的 Launchpad：全屏网格式图标、多页翻页、文件夹分组、模糊搜索，并内置了一个可调用本机能力的 AI 助手。

本仓库 fork 自 [jensroth-git/WinLaunch](https://github.com/jensroth-git/WinLaunch)。

## 功能

**启动器**

- 全屏图标网格，行列数与图标大小可配置（默认 8 × 5）
- 多页 Springboard 布局，支持拖拽排序、拖入文件夹分组
- 搜索栏支持模糊匹配（基于 Trie 前缀索引）
- 自动同步本机应用列表：监听开始菜单与桌面，新装的应用自动入库，卸载后的残留条目自动清除
- 按快捷方式的真实目标去重，同一程序在多个位置的快捷方式只保留一个，可从右键菜单一次性清理历史重复项
- 自由摆放模式（FreeItemPlacement）与平板模式（长按 2 秒进入抖动编辑状态）
- 桌面模式（Desk Mode）：作为桌面子窗口常驻显示
- 自动监听桌面新增的快捷方式并添加，可选添加后删除原快捷方式
- 主题系统、壁纸联动、多显示器选择、全屏应用运行时自动屏蔽唤起
- 便携模式：程序目录下存在 `Data` 目录时，配置与缓存写入该目录，否则写入 `%AppData%\WinLaunch`
- 配置备份与自动更新检查

**唤起方式**（`ActivationMethods/`）

| 方式 | 说明 |
| --- | --- |
| 快捷键 | 默认 `Shift + Tab`，修饰键与主键可自定义 |
| 屏幕热角 | 默认左上角，可设置触发延迟 |
| 鼠标中键 | 单击 / 双击等行为可选 |
| Windows 键 | 借助 `HookWindowsKey.dll` 接管 Win 键 |
| 双击 Ctrl / Alt | 连按两次修饰键唤起 |
| 手柄 | 基于 XInput |
| 语音 | 基于 System.Speech 语音识别 |

**AI 助手**（`Assistant/`）

通过 Socket.IO 连接助手服务端，支持语音合成（TTS）、Markdown 渲染回复，并提供一组本机功能调用：

- `people` / `messages` / `gmail` / `calendar`：联系人、消息、邮件与日程
- `items`：增删改启动器中的图标项
- `system`：系统操作
- `commands`：执行系统命令（`ExecuteAssistantCommands` 开关）
- `memory`：长期记忆条目
- Python 脚本执行（`ExecuteAssistantPython` 开关）

服务端地址配置在 `WinLaunch/Assistant/AssistantConfig.cs`，默认指向 `http://localhost:3001`。助手账号密码经 DPAPI 加密后保存，密文与当前 Windows 用户绑定。

**本地化**

内置 20+ 语言资源（`WinLaunch/Properties/Resources.*.resx`），包含简体中文 `zh-CN`、英语、德语、日语、俄语、法语等。

## 技术栈

- C# / WPF，.NET 10（`net10.0-windows`，x64）
- SDK 风格项目，NuGet 使用 `PackageReference`
- 主要依赖：Newtonsoft.Json、SocketIOClient、MdXaml、AvalonEdit、Extended.Wpf.Toolkit、Microsoft.Xaml.Behaviors.Wpf、System.Speech
- 原生互操作：`HookWindowsKey.dll`（Win 键钩子）、`XInputInterface.dll` / `XInputDotNetPure.dll`（手柄）；快捷方式读写通过后期绑定调用 `WScript.Shell` 与 `Shell.Application`

上游原版基于 .NET Framework 4.8，本仓库已迁移到 .NET 10，详见 [迁移说明](#从-net-framework-48-迁移)。

## 构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。不需要 Visual Studio，也不需要 .NET Framework 开发包。

```powershell
git clone https://github.com/NewNum/WinLaunch.git
cd WinLaunch
dotnet build -c Release
```

生成产物为 `WinLaunch\bin\Release\net10.0-windows\WinLaunch.exe`，运行需要 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)。

若要以便携方式运行，在可执行文件同级目录创建 `Data` 文件夹即可。

运行单元测试：

```powershell
dotnet test WinLaunch.Tests\WinLaunch.Tests.csproj
```

## 从 .NET Framework 4.8 迁移

相对上游，本仓库做了以下改动：

- 项目文件改为 SDK 风格，`packages.config` 换成 `PackageReference`，目标框架 `net10.0-windows`，平台固定 x64（两个原生 DLL 均为 x64）
- 移除了 `IWshRuntimeLibrary` 与 `Shell32` 的 tlbimp COM 引用，改为 `ComUtils.CreateInstance` 后期绑定。COM 引用需要 .NET Framework 版 MSBuild，去掉后 `dotnet build` 可直接使用
- `HookWindowsKey.dll` 和 `XInputInterface.dll` 此前未在项目文件中声明，从源码构建时不会复制到输出目录，程序一启动就会因 `DllNotFoundException` 崩溃；现已声明为 `Content` 自动复制
- 依赖升级：AvalonEdit、MdXaml、Microsoft.Xaml.Behaviors.Wpf、Newtonsoft.Json、SocketIOClient 升到当前版本；`System.Text.Json` 7.0.3 存在已知高危漏洞（[GHSA-hh2w-p6rv-4g7w](https://github.com/advisories/GHSA-hh2w-p6rv-4g7w)），已随传递依赖一并解决
- `System.Buffers`、`System.Memory`、`System.ValueTuple` 等一批垫片包已内置于 .NET 10，全部移除；签入仓库的 `WPFToolkit.Extended.dll` 与已无用的 `app.config`、`Properties/Settings.settings` 一并删除
- Extended.Wpf.Toolkit 固定在 3.8.2——4.0 起改用仅限非商用的 Xceed Community License，与本项目的 MIT 许可证不兼容
- 代码适配：`RegistryHive.DynData`（Win9x 遗留）在现代 .NET 中已移除；Xceed `ColorPicker` 的 `SelectedColor` 改为可空类型
- 崩溃时除了上报远端，现在还会把完整异常链写入配置目录下的 `crash.log`

## 迁移后的稳定性与安全修复

- **`Thread.Abort` 全部改为协作式取消。** 上游有 10 处用 `Thread.Abort` 停止后台线程，该 API 在 .NET Core 之后会直接抛 `PlatformNotSupportedException`。其中桌面模式的窗口保活线程每次重新定位窗口都会走到，属于必然触发的崩溃。现改为 `CancellationTokenSource` + `Join`；鼠标钩子线程改为向其消息泵投递 `WM_QUIT` 后正常退出
- **配置与图标数据改为原子写入。** 此前直接以 `FileMode.Create` 覆盖原文件，写入过程中崩溃或断电会留下被截断的 `Settings.xml` / `Items.xml`，导致配置丢失。现统一经 `AtomicFile`：先写临时文件并 flush 到磁盘，再用 `File.Replace` 替换
- **助手密码改用 DPAPI 保护。** 上游用硬编码在源码里的共享 AES 密钥加密密码，等同于明文存储。现改为 DPAPI（`DataProtectionScope.CurrentUser`），密文离开当前用户即无法解密；旧格式凭据会在下次登录时自动读出并迁移，无需重新输入密码
- 新增 `WinLaunch.Tests` 单元测试项目与 GitHub Actions 构建流水线

## 应用列表的去重与自动同步

上游按「快捷方式文件名精确匹配」判断条目是否已存在，同一个程序在公共开始菜单和用户开始菜单各有一个快捷方式时会被当成两个应用重复导入；扫描只覆盖开始菜单顶层和一层子目录、只认 `.lnk`，深层目录的程序会整个漏掉；桌面监听只订阅了 `Changed` 事件而没订阅 `Created`，新装程序常常收不到通知；开始菜单则完全没有监听，必须手动点刷新。

本仓库的改动：

- **去重键改为快捷方式解析出的真实目标 + 启动参数**，大小写与路径写法差异均归一化，无法解析的（MSI 通告式快捷方式、Store 应用）回退到显示名。参数参与比较，因此 `cmd.exe` 这类共用启动壳、仅参数不同的条目不会被误合并
- **自动同步**：监听开始菜单（递归）与桌面的新增快捷方式，并在每次启动时后台增量扫描一次，补上未运行期间安装的程序
- **自动清理**：目标已不存在的条目会被移除。判定刻意保守，网络路径、未就绪的可移动盘、Store 应用、无法解析的快捷方式一律跳过，不作为删除依据
- 卸载程序常在开始菜单留下失效快捷方式，扫描会跳过这些孤儿项，避免出现「删掉又被加回来」的循环
- 扫描改用系统 API 获取开始菜单路径（原先硬编码 `C:\ProgramData\...`）、递归全部子目录、同时识别 `.lnk` 与 `.url`
- 右键菜单新增「移除重复的应用程序」，用于清理历史遗留的重复条目
- 扫描与快捷方式解析在后台 STA 线程完成，不阻塞界面
- 新增设置项 `WatchForInstalledApps`、`RemoveUninstalledApps`，均默认开启，可在设置面板关闭

## 目录结构

```
WinLaunch/
├─ ActivationMethods/   各种唤起方式（快捷键、热角、手柄、语音等）
├─ Assistant/           AI 助手核心、状态机、UI 与功能调用
│  ├─ Functions/        联系人、日历、邮件、命令、记忆等能力实现
│  └─ UI/               助手界面
├─ MainWindow/          主窗口拆分：事件、渲染、窗口与设置管理
├─ Springboard/         图标网格、分页、文件夹、item 集合
├─ Windows/             添加链接、编辑项、欢迎页、EULA 等窗口
├─ Utils/               图标获取、自启动、备份、加密、更新检查等工具
├─ Themes/ Theme/       主题资源
├─ Language/            本地化辅助
├─ Properties/          多语言资源文件与程序集信息
└─ Converters/          WPF 值转换器
```

## 配置文件位置

| 内容 | 安装模式 | 便携模式 |
| --- | --- | --- |
| 图标数据 | `%AppData%\WinLaunch\Items.xml` | `Data\Items.xml` |
| 设置 | `%AppData%\WinLaunch\Settings.xml` | `Data\Settings.xml` |
| 主题 | `%AppData%\WinLaunch\CurrentTheme` | `Data\CurrentTheme` |
| 图标 / 快捷方式缓存 | `%AppData%\WinLaunch\IconCache`、`LinkCache` | `Data\IconCache`、`Data\LinkCache` |
| 崩溃日志 | `%AppData%\WinLaunch\crash.log` | `Data\crash.log` |

## 许可证

MIT License，版权归原作者 MrC0rrupted 所有，详见 [LICENSE](LICENSE)。
