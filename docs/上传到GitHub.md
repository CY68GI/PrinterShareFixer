# 把这个项目上传到 GitHub（手把手）

> 全程只需要在项目根目录执行几条 git 命令。仓库已经初始化好了（分支 `main`，
> 已有 5 个提交和 `v1.0.0` → `v1.2.0` 的版本标签），你要做的是"关联远程仓库 + 推送 + 建 Release"。

## 0. 先改两处占位信息

1. **提交身份**：仓库里的提交现在用的是占位身份，推送前改成你自己的（只需在本仓库执行一次）：

   ```powershell
   cd C:\Data\ChatGPT_Files\ChatGPT_Codex_Files\Printer_Sharing_Troubleshooter
   git config user.name "你的名字或GitHub用户名"
   git config user.email "你的邮箱（建议用 GitHub 账号邮箱）"
   ```

2. **README 里的徽章地址**：把 `README.md` 顶部的 `你的用户名/你的仓库名` 替换成真实地址
   （用记事本或 VS Code 全局替换一次即可）。

## 1. 在 GitHub 上创建空仓库

1. 打开 <https://github.com/new>。
2. **Repository name** 填 `PrinterShareFixer`（或你喜欢的名字）。
3. **Public / Private** 按需选择（公开仓库所有人都能看到源码）。
4. **不要**勾选 "Add a README file"、"Add .gitignore"、"Choose a license"——
   我们本地已经有这些文件了，勾了会产生冲突。
5. 点 **Create repository**，记下页面上显示的地址，形如：
   `https://github.com/你的用户名/PrinterShareFixer.git`

## 2. 关联远程仓库并推送

```powershell
cd C:\Data\ChatGPT_Files\ChatGPT_Codex_Files\Printer_Sharing_Troubleshooter

# 关联远程（origin 是远程仓库的默认别名）
git remote add origin https://github.com/你的用户名/PrinterShareFixer.git

# 确认当前分支是 main
git branch --show-current

# 推送代码与所有版本标签
git push -u origin main
git push origin --tags
```

第一次推送会弹出认证窗口（Git Credential Manager），推荐选
**"Sign in with your browser"** 用浏览器登录 GitHub 授权即可，凭据会被记住。

如果没弹出窗口、或提示 `Authentication failed`，可以用 **Personal Access Token**：

1. GitHub 右上角头像 → Settings → Developer settings → Personal access tokens → Tokens (classic)
   → Generate new token (classic)，勾选 `repo` 权限，生成后**复制保存**（只显示一次）。
2. 推送时用户名填你的 GitHub 用户名，密码处粘贴这个 token。

想用 SSH 的话：`ssh-keygen -t ed25519 -C "你的邮箱"` 生成密钥，
把 `~/.ssh/id_ed25519.pub` 内容加到 GitHub → Settings → SSH and GPG keys，
然后把远程地址换成 `git@github.com:你的用户名/PrinterShareFixer.git`。

## 3. 检查上传结果

```powershell
git remote -v          # 看看 origin 地址对不对
git log --oneline      # 本地提交历史
git status             # 应为 "nothing to commit, working tree clean"
```

刷新 GitHub 页面，应该能看到 README、`src/`、`docs/`、`LICENSE`、`CHANGELOG.md`、
`.github/workflows/build.yml` 等文件。注意 `build/`、`release/` 不会出现在仓库里
（它们在 `.gitignore` 中被排除），这是刻意的：**编译产物不进代码仓库，发布包放 Releases**。

## 4. 发布版本（把两个压缩包给大家下载）

先把包打出来（如果还没打）：

```powershell
powershell -ExecutionPolicy Bypass -File .\package.ps1
```

`release\` 目录里会得到：

- `PrinterShareFixer-1.2.0-win-x64.zip`（自带 .NET 运行时，约 127 MB）
- `PrinterShareFixer-1.2.0-win-x64-requires-dotnet.zip`（不自带运行时，约 59 MB）

然后在 GitHub 上创建 Release：

1. 仓库页面右侧 **Releases** → **Create a new release**（或 `Draft a new release`）。
2. **Choose a tag** 选已有的 `v1.2.0`（也可以在这里新建 tag，但我们已经推送过了）。
3. **Release title** 填 `v1.2.0`，描述可以把 `CHANGELOG.md` 里对应版本的内容复制进去。
4. 把上面两个 zip 拖到 **Attach binaries** 区域。
5. 点 **Publish release**。之后 README 里的 `Releases` 链接就能直接下载。

## 5. 自动化构建（可选，已经配好）

`.github/workflows/build.yml` 已经写好：推送到 `main` 或提交 Pull Request 时会自动
在 GitHub 的 Windows 机器上编译项目、自检内嵌脚本、并产出两种发布包。

在仓库 **Actions** 页面能看到运行结果；每次运行页面的 **Artifacts** 里可以下载
`PrinterShareFixer-build`（保留 7 天）。有了它，你就不必每次都本地打包。

> 首次推送后如果 Actions 报错，多半是 .NET SDK 版本问题，
> 把 `build.yml` 里的 `dotnet-version: '10.0.x'` 改成当时的最新版即可。

## 6. 以后每次改代码的固定动作

```powershell
git add -A
git commit -m "说明这次改了什么"
git push

# 发新版本时：改 src/*/**.csproj 里的 <Version>，补一条 AppInfo.cs 的更新说明，然后
powershell -ExecutionPolicy Bypass -File .\tools\update-changelog.ps1   # 同步 CHANGELOG.md
powershell -ExecutionPolicy Bypass -File .\package.ps1                  # 重新打包
git add -A; git commit -m "v1.2.1：..." ; git tag -a v1.2.1 -m "v1.2.1"
git push --follow-tags
# 最后到 GitHub Releases 上传新包
```

## 7. 常见问题

**Q：`git push` 提示 `Support for password authentication was removed`**
不能用账号密码，请用第 2 步里的浏览器登录或 Personal Access Token。

**Q：提示 `remote origin already exists`**
说明已经加过了，改成 `git remote set-url origin https://github.com/你的用户名/PrinterShareFixer.git`。

**Q：仓库太大了 / 推送很慢**
检查有没有误把 `release/`、`build/` 提交进去：`git ls-files | Select-String '^release/'`。
如果有，先 `git rm -r --cached release build`，确认 `.gitignore` 里包含它们，再提交。

**Q：本仓库的 `.git` 是个文件而不是文件夹？**
这是 git 官方的 `--separate-git-dir` 布局（`.git` 里写着 `gitdir: .../.git-store`），
用于绕开本机开发环境对 `.git` 目录的只读限制。`status/commit/push` 用法完全一样，
推送 GitHub 不受影响。如果你想换回最常见的标准布局（例如把整个目录拷到别的机器继续用 git）：

```powershell
Remove-Item .git            # 删除指针文件
Move-Item .git-store .git   # 数据目录改名
git status                  # 正常可用
```

**Q：私有仓库别人能下载发布包吗？**
不能，Private 仓库的 Releases 只有你自己可见。想公开分享就选 Public。

**Q：我不想用 MIT 许可证怎么办？**
删掉 `LICENSE` 文件，或把内容换成你选择的许可证（例如
<https://choosealicense.com/> 上挑一个），同时更新 README 末尾的许可证说明。
