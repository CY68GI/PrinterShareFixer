# 打印机共享修复工具（WinUI 3）

一个用来解决 **Windows 10 / Windows 11 局域网打印机共享**问题的桌面工具。
界面按「这台电脑的角色」分成两套：**本机接有打印机**（开放共享，让别人连上来）和
**本机没有打印机**（修复客户端，连上别人共享的打印机），每套再按本机系统分
Windows 10 / Windows 11 两个按钮，点击后自动完成服务启用、网络发现、防火墙放行、
打印 RPC 兼容性等一整套修复动作，并逐项显示每一步的执行结果。

## 界面

- **顶部**：标题、当前系统信息、权限状态，以及右上角的 **设置** 按钮
  （查看版本号、每个版本的更新内容、日志与备份目录位置）。
- **第一步：这台电脑是哪种情况？** 两个可切换的大按钮：
  「本机接有打印机」（开放共享）/「本机没有打印机」（连接共享）。
- **第二步：这台电脑是什么系统？** Windows 10 / Windows 11 两个按钮，
  会根据检测到的系统自动加「当前系统推荐」标记。角色不同，按钮说明与执行的步骤也随之变化。
- **客户端模式**多一个输入框：填写接打印机那台电脑的名称或 IP，修复最后会
  自动测试连通性（ping、TCP 445、TCP 135、共享列表），直接告诉你卡在哪一环。
- **高级选项**：注册表备份、来宾访问、SMB 签名、RPC 动态端口、防火墙远程范围、
  清理打印缓存、SMB1、受保护的打印模式、修复后重启服务。
- **系统状态检测**（可折叠）：操作系统版本、管理员权限、关键服务状态、网络位置、
  防火墙规则、SMB 来宾登录、打印 RPC 隐私认证、NetBIOS，以及
  服务端视角的「已共享的打印机」或客户端视角的「已连接的共享打印机」。
- **修复进度**：每个步骤的图标、结果说明和详细命令输出，底部有进度条与日志入口。

程序通过 `app.manifest` 声明 `requireAdministrator`，启动时会自动弹出 UAC 提权提示；
没有管理员权限时两个修复按钮会被禁用并给出提示。

修复全程在后台线程执行，窗口始终可以拖动、可以点「取消」；关闭窗口会立刻取消正在进行的
修复并退出进程（不会残留后台进程）。日志里记录了每个步骤的耗时，方便定位慢在哪一步。

## 四种修复方案

**本机接有打印机（服务端）**：目标是让别的电脑能连过来。

| 角色 | Windows 10 | Windows 11 |
| --- | --- | --- |
| 本机接有打印机 | `win10-provider`：共享服务 + 网络发现 + 防火墙 + 来宾访问 + 可选 SMB1 | `win11-provider`：同上，另含 24H2 打印 RPC 与受保护的打印模式处理 |
| 本机没有打印机 | `win10-consumer`：客户端服务 + 端口放行 + 来宾访问 + 清理打印缓存 + 连通性测试 | `win11-consumer`：同上，另含 `AllowInsecureGuestAuth` 策略与 24H2 打印 RPC 处理 |

共用部分（四种方案都会执行）：

| 步骤 | 说明 |
| --- | --- |
| 权限与系统检查 | 校验管理员权限，读取系统版本与当前共享状态 |
| 备份注册表 | 用 `reg export` 导出将要修改的注册表分支到 `%ProgramData%\PrinterShareFixer\Backups\<时间戳>` |
| 服务配置 | 服务端启用 `LanmanServer`、`LanmanWorkstation`、`Spooler`、`FDResPub`、`fdPHost`、`SSDPSRV`、`upnphost`、`Browser`(Win10) 等；客户端只启用连接所需的 `LanmanWorkstation`、`Spooler`、`Dnscache`、`FDResPub`、`fdPHost`、网络位置等 |
| 网络位置 | 把当前网络从「公用」改为「专用」（公用网络会阻止共享与发现） |
| NetBIOS | 对所有已启用 IP 的网卡启用 NetBIOS over TCP/IP |
| 防火墙 | 启用内置的「文件和打印机共享」（`@FirewallAPI.dll,-32752`）与「网络发现」（`@FirewallAPI.dll,-32753`）规则组，并创建 `PSF-*` 入站规则：SMB 445、NetBIOS 137/139/138、RPC 135、打印 RPC 动态端口 49152-65535、WSD 5357/5358/3702/5353/5355（默认仅允许本地子网） |
| 密码保护的共享 | `LimitBlankPasswordUse=0`、`everyoneincludesanonymous=1`、`restrictanonymous=0`、`LocalAccountTokenFilterPolicy=1` |
| SMB 兼容性 | `EnableInsecureGuestLogons=true`，取消客户端/服务端 SMB 签名强制（`RequireSecuritySignature=0`） |
| 打印 RPC 隐私认证 | `HKLM\SYSTEM\CurrentControlSet\Control\Print\RpcAuthnLevelPrivacyEnabled=0`，修复 **0x0000011b** |
| 打印机驱动安装 | `PointAndPrint\RestrictDriverInstallationToAdministrators=0`，修复 **0x00000740** |
| 重启服务 | 重启 `Spooler`、`LanmanServer`、`FDResPub` |
| 复核 | 重新读取全部状态，输出仍有问题的检查项 |

服务端额外包含：

- **共享状态检查**：确认本机确实有已共享的打印机，没有就提示去「打印机属性 → 共享」勾选。

客户端额外包含：

- **打印缓存清理**：删除卡住的打印任务（`spool\PRINTERS`）和客户端缓存的旧服务端信息
  （`Client Side Rendering Print Provider\Servers`），解决「能连上但打不开、提示找不到打印机」；
- **`AllowInsecureGuestAuth` 策略**：`HKLM\SOFTWARE\Policies\Microsoft\Windows\LanmanWorkstation`
  与 `...\Services\LanmanWorkstation\Parameters`，解决 Windows 11 24H2 访问无密码共享被拒；
- **连通性测试**：ping、TCP 445、TCP 135、`net view \\目标电脑`，结果里会直接说明是哪一环不通。

Windows 10 方案额外包含：

- 启动遗留的 **Computer Browser**（`Browser`）服务；
- 可选的 **SMB1** 协议启用（`Enable-WindowsOptionalFeature -FeatureName SMB1Protocol` +
  `Set-SmbServerConfiguration -EnableSMB1Protocol $true`），默认关闭，仅在对接很老的
  打印机 / NAS 时勾选。

Windows 11 方案额外包含：

- **24H2 打印后台处理程序变更**：默认只监听命名管道，
  设置 `Printers\RPC\RpcUseNamedPipeProtocol=0`、`RpcAuthentication=0`、`ForceKerberosForRpc=0`，
  恢复 RPC over TCP，老客户端才能连上共享打印机；
- **受保护的打印模式（WPP）** 检测，若已启用可在高级选项中关闭
  （`Printers\WPP\WindowsProtectedPrintMode=0`）——该模式只允许 IPP 类驱动，
  会直接禁掉传统共享打印机；
- 不包含 SMB1（Windows 11 已移除该功能，步骤会自动跳过）。

## 环境要求

- 开发：Windows 10 2004（内部版本 19041）及以上 + .NET 10 SDK + Windows App SDK 2.4（NuGet 自动还原）。
- 运行：Windows 10 1809+ / Windows 11（x64）。`release` 目录下是 .NET 自包含版本，
  目标电脑无需安装 .NET 运行时；Windows App SDK 也已以自包含方式随程序发布。

## 目录结构

```
Printer_Sharing_Troubleshooter/
├─ src/                          源代码（只有源码，不含任何可执行文件）
│  ├─ PrinterShareFixer.App/     WinUI 3 界面程序（两个修复按钮）
│  │  └─ Assets/                 应用图标 app.ico / app-icon.png
│  ├─ PrinterShareFixer.Core/    修复逻辑与检测逻辑（界面与命令行共用）
│  └─ PrinterShareFixer.Cli/     命令行工具 psfix
├─ assets/icon/                  图标母版与生成脚本（make_icon.py）
├─ release/                      编译打包好的可直接运行程序（分发用）
│  ├─ PrinterShareFixer-win-x64/        双击 PrinterShareFixer.exe 即可运行
│  ├─ psfix-cli-win-x64/                 可选命令行工具
│  ├─ PrinterShareFixer-1.1.1-win-x64.zip  一个压缩包包含全部内容，拷到其他电脑解压即用
│  └─ 使用说明.txt                       给最终用户的使用说明
├─ build/                        编译中间产物（可随时删除，不属于源码）
├─ build.ps1                     编译到 build/
├─ package.ps1                   编译 + 打包到 release/
└─ preview-ui.ps1                开发用：构建免提权的界面预览版
```

`src` 目录下不会产生 `bin` / `obj`：所有中间产物和输出都通过 `Directory.Build.props`
统一重定向到根目录的 `build/`。

## 编译、打包与运行

```powershell
# 1. 编译（输出到 build/，src 保持纯净）
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 2. 打包出可直接分发的 release 目录（含 .zip）
powershell -ExecutionPolicy Bypass -File .\package.ps1

# 3. 运行（会弹出 UAC 提权提示）
.\release\PrinterShareFixer-win-x64\PrinterShareFixer.exe

# 界面预览版（不申请管理员权限，仅用于调界面，不能执行修复）
powershell -ExecutionPolicy Bypass -File .\preview-ui.ps1
```

也可以在 Visual Studio 中打开 `PrinterShareFixer.sln`（解决方案平台选择 **x64**）。
`package.ps1` 支持 `-FrameworkDependent`（目标机需装 .NET 10 桌面运行时）与
`-SkipArchive`（只生成目录不压缩）。

## 应用图标

图标母版和生成脚本在 `assets/icon/`，产物是 `src/PrinterShareFixer.App/Assets/app.ico`
（内含 16 / 24 / 32 / 48 / 64 / 128 / 256 七种尺寸，256 用 PNG 条目、小尺寸用 32 位 BMP 条目）
和界面标题栏用的 `app-icon.png`。

设计：蓝色圆角方块底板上是一台白色打印机，出纸口吐出一张浅蓝色纸张，纸上有深蓝色对勾，
表示"已修复"。没有文字，保证 16×16 的任务栏尺寸也清晰。

修改图标后重新生成：

```powershell
python .\assets\icon\make_icon.py            # 生成图标
python .\assets\icon\make_icon.py preview    # 额外在终端用字符画预览 16/24/32 尺寸
```

图标通过三个地方生效：exe 内嵌图标（`ApplicationIcon`）、任务栏/窗口图标
（`AppWindow.SetIcon`）、界面标题栏小图。

## 版本控制

仓库已经初始化（分支 `main`），并打好了版本标签（当前 `v1.1.0`）：

```powershell
git log --oneline          # 查看提交
git tag                    # 查看标签
git remote add origin <你的仓库地址>
git push -u origin main --tags
```

本仓库的 git 数据目录是 `.git-store/`，根目录下的 `.git` 是一个指针文件（内容为
`gitdir: .../.git-store`）。这是 git 官方的 `--separate-git-dir` 布局，`git status`、
`commit`、`push` 等命令用法完全不变。之所以不用标准的 `.git` 目录：本机开发环境中
`.git` 目录被标记为只读，会让沙箱里的命令集体报错。

如果想把仓库改回最常见的标准布局（例如要拷到别的机器继续用 git）：

```powershell
Remove-Item .git           # 删除指针文件
Move-Item .git-store .git  # 把数据目录改名为 .git
git status                 # 正常可用
```

改成标准布局后，如果继续用 Codex 在这个目录里工作，沙箱命令可能会报错，届时用非沙箱
方式执行即可，或者把 `.git` 再换回指针文件 + `.git-store` 布局。

- 提交身份是本仓库级别的本地配置，推送前请改成你自己的：`git config user.name "你的名字"`、`git config user.email "你的邮箱"`。
- `build/`、`release/`、`bin/`、`obj/`、`.preview/` 已在 `.gitignore` 中排除，仓库只保存源码与资源；
  发布包用 `package.ps1` 随时重新生成。
- `assets/icon/*.png`、`src/PrinterShareFixer.App/Assets/app.ico` 属于要入库的二进制资源，
  已在 `.gitattributes` 中标记为 binary。

## 命令行工具（可选）

`psfix` 与界面共用同一套修复逻辑，适合批量部署或排查脚本问题：

```powershell
dotnet run --project .\src\PrinterShareFixer.Cli -- version             # 版本号 + 各版本更新内容
dotnet run --project .\src\PrinterShareFixer.Cli -- detect              # 只读检测（默认服务端视角）
dotnet run --project .\src\PrinterShareFixer.Cli -- detect --role consumer --verbose
dotnet run --project .\src\PrinterShareFixer.Cli -- plan win11-consumer # 查看修复计划与等价命令
dotnet run --project .\src\PrinterShareFixer.Cli -- run win10-provider  # 预演，不做修改
dotnet run --project .\src\PrinterShareFixer.Cli -- run win11-consumer --yes --target 192.168.1.10
```

打包后也可以直接用 `release\psfix-cli-win-x64\psfix.exe`。

方案键由「系统 + 角色」组成：`win10-provider`、`win11-provider`、`win10-consumer`、
`win11-consumer`（只写 `win10` / `win11` 时按服务端处理，兼容旧用法）。
高级选项用 `--opt key=value` 传入，例如
`--opt smb1=true --opt anyremote=true --opt clean-cache=false`。

## 版本与更新内容

版本号来自程序集（`<Version>` 属性），界面右上角 **设置** 里能看到当前版本与历史更新内容，
命令行用 `psfix version` 可以打印同样的内容。更新内容集中维护在
`src/PrinterShareFixer.Core/AppInfo.cs` 的 `ReleaseNotes` 里，发新版本时在这里加一条即可
（同时更新三个 csproj 里的 `<Version>`）。

## 日志与回滚

- 日志：`%ProgramData%\PrinterShareFixer\Logs\printer-share-fix-<时间戳>.log`
  （无权限写入时自动退回到 `%LOCALAPPDATA%\PrinterShareFixer\Logs`）。
- 备份：`%ProgramData%\PrinterShareFixer\Backups\<时间戳>\*.reg`，
  双击对应 `.reg` 文件即可把该项注册表恢复到修复前的状态。

## 常见报错对照

| 报错 / 现象 | 主要原因 | 本工具对应步骤 |
| --- | --- | --- |
| `0x0000011b` 无法连接到打印机 | 打印 RPC 隐私认证（2021 年后补丁默认开启） | 关闭 `RpcAuthnLevelPrivacyEnabled` |
| `0x00000740` 需要管理员权限安装驱动 | PointAndPrint 驱动安装限制 | 放宽 `RestrictDriverInstallationToAdministrators` |
| `0x00000709` 无法设置默认打印机 | 打印后台处理程序 / RPC 通道问题 | 服务配置 + 打印 RPC 兼容性 |
| 提示「不允许不安全的来宾登录」 | 客户端默认拒绝来宾访问 | SMB 兼容性设置 |
| 在网络中看不到对方电脑或打印机 | 网络发现、FDResPub 服务、防火墙规则 | 服务配置 + 防火墙 + 专用网络 |
| 能 ping 通但访问 `\\电脑名` 提示无权限 | 密码保护的共享 / 本地账号远程令牌 | 来宾访问策略 |
| 连接时提示签名不兼容 | SMB 签名强制 | SMB 兼容性设置 |
| 能 ping 通、也能打开共享，但打印机打不开 / 报 `0x0000007c` | 客户端缓存的旧服务端信息、卡住的打印任务 | 客户端：清理打印缓存与队列 |
| 提示「你无法访问此共享文件夹，因为你的帐户已被禁用」/ 无密码共享被拒（Win11 24H2） | 组策略禁止来宾访问 | 客户端：`AllowInsecureGuestAuth` 策略 |
| 输入 `\\电脑名` 打不开，但 `\\IP` 可以 | 名称解析 / NetBIOS 问题 | 启用 NetBIOS over TCP/IP |
| 修复后仍连不上，想知道卡在哪一步 | — | 客户端模式填对方电脑名，修复最后会做连通性测试 |

## 注意

- 本工具只修改本机的共享**服务端**与**客户端**配置，不会替你创建打印机共享，
  需要在「打印机属性 → 共享」中勾选共享后，其他电脑才能通过
  `\\本机名\共享名` 访问。
- 关闭「密码保护的共享」「允许不安全的来宾登录」会降低局域网安全性，
  建议只在可信内网使用，或改为在客户端使用本机账号登录。
- 防火墙采用的是「仅本地子网放行」，跨网段/跨 VLAN 场景需在高级选项中勾选
  「允许所有远程地址」，并确保网关允许相关端口。
- 修改系统配置有风险，请先确认已勾选「修复前备份相关注册表项」。
