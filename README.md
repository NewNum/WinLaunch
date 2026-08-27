# WinLaunch

WinLaunch 是一款 Windows 平台上的启动器（Launchpad）应用，界面与交互参考 macOS 的 Launchpad：全屏网格式图标、多页翻页、文件夹分组、模糊搜索，并内置了一个可调用本机能力的 AI 助手。

本仓库 fork 自 [jensroth-git/WinLaunch](https://github.com/jensroth-git/WinLaunch)。

## 功能

**启动器**

- 全屏图标网格，行列数与图标大小可配置（默认 8 × 5）
- 多页 Springboard 布局，支持拖拽排序、拖入文件夹分组
- 搜索栏支持模糊匹配（基于 Trie 前缀索引）
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

服务端地址与加密密钥配置在 `WinLaunch/Assistant/AssistantConfig.cs`，默认指向 `http://localhost:3001`。

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
