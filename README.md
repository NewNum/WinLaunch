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
- 桌面新增的快捷方式自动添加，可选添加后删除原快捷方式
- 自由摆放模式（FreeItemPlacement）与平板模式（长按 2 秒进入抖动编辑状态）
- 桌面模式（Desk Mode）：作为桌面子窗口常驻显示
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

内置 20+ 语言资源（`WinLaunch/Properties/Resources.*.resx`），包含简体中文 `zh-CN`、繁体中文 `zh-TW`、英语、德语、日语、俄语、法语等。在偏好设置的「通用 → 语言」中切换，即时生效。

## 技术栈

- C# / WPF，.NET 10（`net10.0-windows`，x64）
- SDK 风格项目，NuGet 使用 `PackageReference`
- 主要依赖：Newtonsoft.Json、SocketIOClient、MdXaml、AvalonEdit、Extended.Wpf.Toolkit、Microsoft.Xaml.Behaviors.Wpf、System.Speech
- 原生互操作：`HookWindowsKey.dll`（Win 键钩子）、`XInputInterface.dll` / `XInputDotNetPure.dll`（手柄）；快捷方式读写通过后期绑定调用 `WScript.Shell` 与 `Shell.Application`

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
