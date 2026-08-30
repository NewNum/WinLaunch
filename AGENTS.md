# AGENTS.md

面向在本仓库中工作的 AI 编码代理的说明。用户文档见 [README.md](README.md)。

## 项目概览

WinLaunch 是 Windows 上的 WPF 启动器（类 macOS Launchpad）：全屏图标网格、文件夹、搜索、多种唤起方式，以及可选的 Socket.IO AI 助手。

- **语言 / 框架**：C#、WPF、.NET 10（`net10.0-windows`，x64）
- **主分支**：`main`
- **许可证**：MIT
- **上游**：fork 自 [jensroth-git/WinLaunch](https://github.com/jensroth-git/WinLaunch)

## 构建与验证

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。使用系统安装的 `dotnet`（`C:\Program Files\dotnet`），不要依赖用户目录下的独立安装。

```powershell
dotnet build WinLaunch.sln -c Release
dotnet test WinLaunch.Tests\WinLaunch.Tests.csproj -c Release
```

- 可执行文件：`WinLaunch\bin\Release\net10.0-windows\WinLaunch.exe`
- CI：`.github/workflows/build.yml`（`windows-latest`，`dotnet-version: 10.0.x`）
- Release：推送 `v*` tag 触发 `.github/workflows/release.yml`，产出 exe / msi / zip 及 release-notes（含 MD5）
- 运行前若已有实例，需先结束 `WinLaunch` 进程，否则 exe 会被锁定导致构建失败
- 默认单实例：再次启动会激活已有实例并退出；调试时可用 `WM_TOGGLELAUNCHPAD`（`0x8808`）或快捷键唤起面板

## 目录与职责

| 路径 | 职责 |
| --- | --- |
| `WinLaunch/MainWindow/` | 主窗口 partial 拆分：事件、渲染、窗口管理、设置、项目列表 |
| `WinLaunch/Springboard/` | 图标网格、分页、文件夹、`SBItem`、`ItemCollection` |
| `WinLaunch/ActivationMethods/` | 快捷键、热角、中键、Win 键、手柄、语音等唤起 |
| `WinLaunch/Assistant/` | AI 助手、Socket.IO、功能调用（`Functions/`）、UI（`UI/`） |
| `WinLaunch/Utils/` | 工具类：图标、备份、加密、更新、应用身份与扫描等 |
| `WinLaunch/Language/` | `TranslationSource`、`LocExtension` 本地化绑定 |
| `WinLaunch/Properties/Resources*.resx` | 多语言字符串资源 |
| `WinLaunch.Tests/` | xUnit 测试；通过 `Link` 引用可独立测试的源文件 |

`MainWindow` 是 `partial class`，逻辑分散在 `MainWindow.xaml.cs` 与 `MainWindow/*.cs` 多个文件中，修改前先确认目标文件。

## 编码约定

1. **最小改动**：只改与任务直接相关的代码，不顺手重构无关模块。
2. **沿用现有风格**：命名、缩进、region、`#region` 注释风格与周边文件一致；新代码优先扩展现有类而非重复实现。
3. **命名空间**：应用代码统一 `namespace WinLaunch`（助手子模块有嵌套命名空间）。
4. **持久化**：设置、主题、`Items.xml` 写入应使用 `AtomicFile.Write`，避免写入中断导致配置损坏。
5. **线程**：禁止使用 `Thread.Abort()`；后台工作用 `CancellationToken` 协作取消。涉及 COM（快捷方式解析）的后台扫描使用 STA 线程。
6. **UI 线程**：WPF `DependencyProperty` 与 `SBItem` 只能在 UI 线程访问；后台扫描前在 UI 线程做快照（参见 `AppIdentitySnapshot`）。
7. **COM 互操作**：不要添加 `<COMReference>` 或 tlbimp 互操作程序集。通过 `ComUtils.CreateInstance(progId)` 后期绑定调用 `WScript.Shell` 等。
8. **凭据**：新密码存储用 `CredentialProtector`（DPAPI）；`EncryptionUtils` 仅用于迁移旧 AES 密文。
9. **注释**：代码应自解释；仅对非显而易见的业务规则或 Windows/COM 行为加简短注释。

## 本地化

- 字符串放在 `WinLaunch/Properties/Resources.resx`（默认）及各语言 `Resources.<culture>.resx`
- XAML 使用 `{l:Loc KeyName}`（`LocExtension`）
- 新增键时：先加 `Resources.resx`，再同步所有已维护语言的 resx；至少补全 `zh-CN`、`zh-TW`、`en-US`
- 在 `WinLaunch.csproj` 的 `SatelliteResourceLanguages` 中登记新语言
- **不要**用 `CultureInfo.GetCultures` 枚举来发现已翻译语言；`GetAvailableCultures()` 通过扫描已部署的卫星程序集目录实现（ICU 不会返回 `zh-CN`/`zh-TW` 这类旧名称）

## 测试

- 框架：xUnit
- 适合单测的纯逻辑放在 `WinLaunch/Utils/`，测试项目用 `<Compile Include=".." Link="...">` 链接，避免引用整个 WPF 项目
- 当前已链接：`AtomicFile`、`CredentialProtector`、`AppIdentityKey`
- 修改上述工具类或应用身份/扫描逻辑时，应补充或更新对应测试
- 完成前运行：`dotnet test WinLaunch.Tests\WinLaunch.Tests.csproj`

## 依赖与限制

| 项 | 说明 |
| --- | --- |
| `Extended.Wpf.Toolkit` | 固定在 **3.8.2**（最后 MS-PL 版本）；不要升级到 4.x（许可证变更） |
| `HookWindowsKey.dll` / `XInputInterface.dll` | 原生 DLL，需在 csproj 中 `CopyToOutputDirectory` |
| `GenerateAssemblyInfo` | 为 `false`，程序集信息在 `Properties/AssemblyInfo.cs` |
| 平台 | 仅 **x64**；solution 配置为 `Debug\|x64` / `Release\|x64` |
| COM / SDK 构建 | `dotnet build` 不支持 `ResolveComReference`，勿恢复旧式 COM 引用 |

## 应用列表同步（近期重要模块）

涉及开始菜单/桌面快捷方式扫描、去重、自动增删时，优先阅读：

- `AppIdentityKey` — 纯字符串/路径归一化，可单测
- `AppIdentity` — 快捷方式解析、卸载判定（含 COM）
- `InstalledAppScanner` — 开始菜单枚举
- `ShortcutFolderWatcher` — 目录监听（Created/Changed/Renamed + 防抖）
- `MainWindow/ItemManagement.cs` — 扫描调度、去重、应用结果

去重键基于解析后的目标路径 + 参数，无法解析时回退到显示名称。卸载判定偏保守：网络路径、未就绪驱动器、shell 虚拟路径不据此删除。

## 配置与数据路径

由 `PortabilityManager` 决定：

- 默认：`%AppData%\WinLaunch\`
- 便携：exe 同级的 `Data\` 目录存在时使用

主要文件：`Settings.xml`、`Items.xml`、`CurrentTheme`、`crash.log`。

## Git 与 PR

- **不要**擅自 `git commit` 或 `git push`，除非用户明确要求
- 提交信息用完整英文句子，说明「为什么」而不只是「改了什么」
- 创建 PR 时用 `gh pr create`，包含 Summary 与 Test plan

## 常见陷阱

1. 构建输出路径已统一为 `bin\$(Configuration)\`，不要改回带 RID 的路径，否则 solution 与 project 构建结果不一致。
2. 资源文件（`Images\`、字体等）需在 `WinLaunch.csproj` 的 `<Resource>` / `<Content>` 中显式包含，否则运行时报找不到资源。
3. 修改 `Resources.resx` 后 `Resources.Designer.cs` 可能需要重新生成；手写 Designer 时保持与 resx 键一致。
4. WinLaunch 失焦会隐藏主窗口；自动化测试时注意窗口可见性与 DPI 缩放。
5. README 面向最终用户，不写仓库迁移/重构过程；实现细节放在代码注释或本文件。
