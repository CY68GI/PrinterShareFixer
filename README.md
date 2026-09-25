# 打印机共享修复工具

[![build](https://github.com/CY68GI/PrinterShareFixer/actions/workflows/build.yml/badge.svg)](https://github.com/CY68GI/PrinterShareFixer/actions/workflows/build.yml)
![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)
![license](https://img.shields.io/badge/license-MIT-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

**Windows 10 / Windows 11 局域网打印机共享一键修复工具。**
按「这台电脑是提供打印机还是使用打印机」分两套界面，点一下按钮就自动完成服务启用、
网络发现、防火墙放行、来宾访问、打印 RPC 兼容性等一系列设置，并逐项显示结果。

A WinUI 3 desktop tool that fixes LAN printer sharing problems on Windows 10 / 11
with one click, from either side (the PC that shares the printer, or the PC that
connects to it).

<img src="assets/icon/app-icon-256.png" width="120" alt="应用图标">

## 功能

- **两套界面按角色划分**：`本机接有打印机`（开放共享，让别的电脑连上来）与
  `本机没有打印机`（修复客户端，解决连不上、连上打不开）。
- **左侧导航**：修复打印机共享 / 打印机信息 / 设置（设置固定在左下角）；「打印机信息」
  页可以查看本机装了哪些打印机、哪些已共享，以及计算机名、用户类型与 IP 地址。
- **覆盖常见故障**：共享服务、网络发现、防火墙放行、密码保护的共享、SMB 来宾访问与签名、
  打印 RPC 隐私认证（`0x0000011b`）、驱动安装限制（`0x00000740`）、打印缓存与卡住的任务。
- **Windows 11 24H2+ 专项**：打印后台处理程序 RPC 传输、`AllowInsecureGuestAuth` 策略、
  受保护的打印模式（WPP）。
- **可观测**：修复前检测、逐项进度、每步耗时、完整日志；修改前自动备份注册表，可回滚。
- **客户端连通性诊断**：填入对方电脑名或 IP，自动测试名称解析、ping、TCP 445/135、共享列表。
- **应用内自动更新**：设置里「检查更新」→「立即更新并重启」，自动从 GitHub Releases 下载、校验、替换并重启，不用重新下载整个包。
- **命令行工具 `psfix`**：`detect` / `plan` / `run` / `version`，适合批量部署与远程排查。
- 界面使用 WinUI 3（Windows App SDK 2.4 + .NET 10），自带应用图标，支持深浅色主题。

## 下载

到 [Releases](../../releases) 页面下载（`Releases → 最新版本 → Assets`）：

| 压缩包 | 体积 | 说明 |
| --- | --- | --- |
| `PrinterShareFixer-<版本>-win-x64-requires-dotnet.zip` | 约 59 MB | 解压即用；**首次使用需要安装 [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)（x64）**，包内附「运行前必读」与 `启动程序.bat`：没装运行时会用中文提示并自动打开下载页 |

发行版刻意**只提供不带运行时和其他依赖的精简包**（体积约为完整包的一半以下），
Windows App SDK 已随程序打包，所以唯一的额外依赖就是 .NET 运行时。
系统要求：Windows 10 1809 及以上 / Windows 11，64 位（x64）。

## 使用方法

1. 解压压缩包（**整个文件夹一起**，不要只拷 exe）。
2. 推荐双击包内的 **`启动程序.bat`**：它会先检查 .NET 10 运行时，装了就直接启动程序，没装就用中文提示并打开下载页。
   直接双击 `PrinterShareFixer.exe` 也可以，运行时缺失时 Windows 会弹出「必须安装 .NET」的提示框。
3. 在"用户账户控制"里选"是"（修改服务和防火墙需要管理员权限）。
4. 界面第一步选这台电脑的角色，第二步选这台电脑的系统（会自动标出推荐项），点按钮开始修复。

> 用打印机的那台电脑连不上时，一般两台电脑各跑一次最稳妥：
> 接打印机的那台选"本机接有打印机"，要用打印机的那台选"本机没有打印机"。

更详细的图文步骤、排查清单和常见问题见 [docs/使用说明.txt](docs/使用说明.txt)。
右上角 **设置** 里可以看到当前版本号与每个版本的更新内容。

## 界面

程序采用左侧导航，共三个入口，**设置固定在最左下方**：

**① 修复打印机共享**

- 第一步选角色：`本机接有打印机`（开放共享）/ `本机没有打印机`（连接共享）；
- 第二步选本机系统（Windows 10 / 11，会自动标出推荐项）；
- 客户端模式可填对方电脑名或 IP，修复最后一步自动测试连通性；
- 高级选项：注册表备份、来宾访问、SMB 签名、RPC 动态端口、防火墙范围、清理打印缓存、SMB1、受保护的打印模式；
- 修复进度逐项显示，可随时取消；「系统状态检测」展开可见服务、防火墙、SMB、打印 RPC、NetBIOS 等检查结果。

**② 打印机信息**

- 这台电脑：计算机名、当前用户与账号类型（管理员 / 标准用户、是否已提权）、IPv4 地址，
  以及每块网卡的名称、描述、IP、MAC 与速率；
- 本机打印机：列出全部打印机（含 OneNote、Print to PDF 这类虚拟打印机），
  标明**哪些已共享**、哪些是网络连接，并显示驱动、端口与状态；
- 顶部汇总「共 N 台 / 已共享 M 台 / 网络连接 K 台」，可「刷新」或一键「打开打印机设置」。

**③ 设置**（最左下方）

- 版本号与运行环境；
- 检查更新 / 立即更新并重启 / 打开下载页（见下方「自动更新」）；
- 各版本更新内容；
- 日志与备份目录，以及「打开日志目录 / 打开备份目录」按钮。

## 自动更新

免安装的应用没有安装程序帮忙升级，所以更新是自己做的：

1. 打开 **设置 → 更新 → 检查更新**（程序启动时也会每天静默检查一次，有新版本会在「设置」按钮上显示提示点）。
2. 有新版本时对话框会列出该版本的更新内容，点 **「立即更新并重启」**。
3. 程序自动完成：下载更新包 → 用 Release 里的 **SHA-256 校验** → 解压到临时目录 → 启动更新程序 → 等待主程序退出 → 复制新版本、把旧版本改名备份 → 切换到新版本 → 重新启动。

细节与安全设计：

- 更新包来自本仓库的 GitHub Releases，按当前安装形态自动选择对应的 zip；
- 校验失败会删除下载的文件并报错，**不会替换**正在使用的程序；
- 切换失败会自动还原成旧版本，整个过程记录在 `%ProgramData%\PrinterShareFixer\Logs\update-*.log`；
- 只替换程序目录，日志与注册表备份都在 `%ProgramData%` 下，**用户数据不受影响**；
- 老版本目录会保留为 `<目录名>.old-<时间戳>` 作为回滚点，下次更新时自动清理更早的备份；
- 网络受限的环境可以设置镜像：环境变量 `PSF_UPDATE_MIRROR`（例如 `https://ghproxy.net/`），或直接点「打开下载页」手动下载；
- 程序目录不可写（只读介质、网络共享）时会失败并提示手动更新，不会破坏现有安装。

## 修复方案一览

| 角色 | 本机 Windows 10 | 本机 Windows 11 |
| --- | --- | --- |
| 本机接有打印机（服务端） | `win10-provider` | `win11-provider` |
| 本机没有打印机（客户端） | `win10-consumer` | `win11-consumer` |

两者共用的主要步骤：

| 步骤 | 说明 |
| --- | --- |
| 权限与系统检查 | 校验管理员权限并读取系统信息 |
| 备份注册表 | 导出将要修改的注册表分支，便于回滚 |
| 服务配置 | 服务端启用 `LanmanServer`/`FDResPub`/`SSDPSRV` 等；客户端只启用连接所需服务 |
| 网络位置 / NetBIOS | 网络改为"专用"，启用 NetBIOS over TCP/IP |
| 防火墙 | 启用内置"文件和打印机共享"规则组，并创建 `PSF-*` 入站规则（SMB 445、NetBIOS、RPC 135、打印 RPC 动态端口、WSD） |
| 来宾与 SMB | 关闭"密码保护的共享"、允许 SMB 来宾登录、取消签名强制 |
| 打印兼容性 | 关闭打印 RPC 隐私认证（`0x0000011b`）、放宽驱动安装限制（`0x00000740`） |
| 复核 | 重新读取状态，给出仍需处理的项 |

服务端额外检查本机是否真的勾了共享；客户端额外清理失效打印缓存、做目标电脑连通性测试。

## 仓库结构

按 .NET / GitHub 常见约定组织（和 `dotnet/runtime`、`aspnetcore` 等仓库一致，源码放在 `src/`）：

```
.
├─ src/                                  源代码（不含任何可执行文件）
│  ├─ PrinterShareFixer.App/             WinUI 3 界面程序
│  ├─ PrinterShareFixer.Core/            修复与检测逻辑（界面 / 命令行共用）
│  └─ PrinterShareFixer.Cli/             命令行工具 psfix
├─ docs/                                 文档
│  └─ 使用说明.txt                       给最终用户的使用说明
├─ packaging/                            打包时附带的文件（运行前必读、启动脚本）
├─ tools/                                开发用脚本（内嵌 PowerShell 脚本语法自检等）
├─ assets/icon/                          图标母版与生成脚本
├─ .github/workflows/build.yml           GitHub Actions：编译 + 产出两种发布包
├─ build.ps1                             编译（输出到 build/）
├─ package.ps1                           打包（输出到 release/，两种版本）
├─ preview-ui.ps1                        免提权的界面预览版（开发调试用）
├─ CHANGELOG.md                          更新日志（由 tools/update-changelog.ps1 生成）
├─ LICENSE                               MIT 许可证
└─ PrinterShareFixer.sln
```

`build/`、`release/` 等产物不入库（见 `.gitignore`），发布包通过
GitHub Releases 或 `package.ps1` 提供。

## 构建与打包

需要 .NET 10 SDK（Windows 10 2004 及以上）。

```powershell
# 编译（输出到 build/）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 打包出两种发布包（输出到 release/）
powershell -ExecutionPolicy Bypass -File .\package.ps1
powershell -ExecutionPolicy Bypass -File .\package.ps1 -Variant FrameworkDependent   # 只打不自带运行时的版本

# 提交前自检：把源码里所有内嵌 PowerShell 脚本过一遍语法
powershell -ExecutionPolicy Bypass -File .\tools\check-scripts.ps1

# 修改更新内容后重新生成 CHANGELOG.md
powershell -ExecutionPolicy Bypass -File .\tools\update-changelog.ps1
```

也可以在 Visual Studio 打开 `PrinterShareFixer.sln`（解决方案平台选 **x64**）。

## 命令行工具

```powershell
psfix version [--markdown]        # 版本号与各版本更新内容（--markdown 输出 GitHub 用格式）
psfix detect                      # 只读检测（--role consumer 按客户端视角，--verbose 打印日志）
psfix plan win11-consumer         # 查看某个方案包含哪些动作
psfix run win11-consumer --yes --target 192.168.1.10
                                  # 真正执行（需管理员命令行；--opt key=value 传高级选项）
```

方案键：`win10-provider` / `win11-provider` / `win10-consumer` / `win11-consumer`。

## 日志与回滚

- 日志：`%ProgramData%\PrinterShareFixer\Logs\printer-share-fix-<时间戳>.log`
  （无写入权限时退回 `%LOCALAPPDATA%\PrinterShareFixer\Logs`），日志里记录每个步骤的耗时。
- 备份：`%ProgramData%\PrinterShareFixer\Backups\<时间戳>\*.reg`，
  双击对应 `.reg` 文件即可恢复到修复前的状态。

## 常见报错对照

| 报错 / 现象 | 主要原因 | 对应步骤 |
| --- | --- | --- |
| `0x0000011b` 无法连接到打印机 | 打印 RPC 隐私认证 | 关闭 `RpcAuthnLevelPrivacyEnabled` |
| `0x00000740` 需要管理员权限安装驱动 | PointAndPrint 驱动安装限制 | 放宽 `RestrictDriverInstallationToAdministrators` |
| `0x00000709` 无法设置默认打印机 | 打印后台处理程序 / RPC 通道 | 服务配置 + 打印 RPC 兼容性 |
| 提示"不允许不安全的来宾登录" | 客户端默认拒绝来宾访问 | SMB 兼容性 + `AllowInsecureGuestAuth` |
| 在网络中看不到对方电脑或打印机 | 网络发现、`FDResPub`、防火墙 | 服务配置 + 防火墙 + 专用网络 |
| 能 ping 通但访问 `\\电脑名` 提示无权限 | 密码保护的共享 / 本地账号远程令牌 | 来宾访问策略 |
| 能连上但打不开 / `0x0000007c` | 失效的打印缓存、卡住的打印任务 | 客户端：清理打印缓存与队列 |
| 修复后仍连不上，想知道卡在哪 | — | 客户端模式填对方电脑名，自动测试连通性 |

## 更新日志

见 [CHANGELOG.md](CHANGELOG.md)（内容与程序内"设置 → 更新内容"完全一致）。

## 许可证与免责声明

- 本项目使用 [MIT 许可证](LICENSE)。
- 程序会修改本机的服务、防火墙与注册表设置，请先确认已勾选"修复前备份相关注册表项"。
- 关闭"密码保护的共享""允许不安全的来宾登录"会降低局域网安全性，建议只在可信内网使用。
- 程序未做数字签名，SmartScreen 可能提示"未知发布者"，选择"仍要运行"即可。
