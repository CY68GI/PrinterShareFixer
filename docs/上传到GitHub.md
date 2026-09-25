# 如何把项目上传到 GitHub

## 0. 先搞清楚两个目录的分工

本项目采用「开发目录 + 发布目录」的方式，根目录不会被推送到 GitHub：

| 目录 | 作用 | 是否上传 |
| --- | --- | --- |
| 项目根目录 | 日常开发。里面有 `.git`（指针文件）、`.git-store/`、`build/`、`release/`、`.preview/` 等本机开发环境产生的东西 | ❌ 不直接上传 |
| `github-repo/` | **要上传的仓库**。里面只有提交过的源码、文档、图标与脚本，并且自带 git 仓库和版本标签 | ✅ 推这个 |

`github-repo/` 由脚本自动生成，所以不会混进本机开发环境的文件，仓库很干净；
它也是被根目录的 `.gitignore` 排除的，不会污染开发历史。

## 1. 生成 / 更新发布目录

```powershell
cd C:\Data\ChatGPT_Files\ChatGPT_Codex_Files\Printer_Sharing_Troubleshooter

# 首次生成：按版本标签回放，做出 5 个提交 + 5 个标签
powershell -ExecutionPolicy Bypass -File .\tools\export-github-repo.ps1 `
    -CommitName "你的名字或GitHub用户名" -CommitEmail "你的邮箱"

# 以后每次改完代码、提交之后：增量同步（在已有历史上追加提交，不会重写历史）
powershell -ExecutionPolicy Bypass -File .\tools\export-github-repo.ps1 -Update `
    -CommitName "你的名字或GitHub用户名" -CommitEmail "你的邮箱"
```

生成结果可以自己核对：

```powershell
cd github-repo
git log --oneline --decorate     # 6 个提交，5 个标签
git ls-files                     # 60 个文件，都是源码/文档/资源
git status                       # 应为 clean
```

> `-CommitName/-CommitEmail` 决定 GitHub 上显示的提交作者，务必换成你自己的，
> 否则历史上会显示成占位的 "PrinterShareFixer"。

## 2. 在 GitHub 上创建空仓库

1. 打开 <https://github.com/new>。
2. **Repository name** 填 `PrinterShareFixer`（或你喜欢的名字）。
3. **Public / Private** 按需选（Public 的话所有人都能看到源码）。
4. **不要**勾选 "Add a README file"、"Add .gitignore"、"Choose a license"——
   发布目录里已经有这些文件，勾了会产生冲突。
5. 点 **Create repository**，记下形如 `https://github.com/你的用户名/PrinterShareFixer.git` 的地址。

## 3. 替换 README 里的占位徽章

`github-repo/README.md` 顶部有 `你的用户名/你的仓库名`，替换成真实地址（全局替换一次即可），
然后提交：

```powershell
cd github-repo
git add README.md
git commit -m "docs: 更新仓库徽章地址"
```

## 4. 关联远程并推送

```powershell
cd github-repo

git remote add origin https://github.com/你的用户名/PrinterShareFixer.git
git push -u origin main
git push origin --tags          # 把 v1.0.0 ~ v1.2.0 一起推上去
```

第一次推送会弹出认证窗口（Git Credential Manager），选 **Sign in with your browser**
用浏览器登录即可，凭据会被记住。

如果没弹窗或提示 `Authentication failed`，用 **Personal Access Token**：

1. GitHub 头像 → Settings → Developer settings → Personal access tokens → Tokens (classic)
   → Generate new token (classic)，勾选 `repo` 权限，生成后复制（只显示一次）。
2. 推送时用户名填 GitHub 用户名，密码处粘贴这个 token。

想用 SSH：`ssh-keygen -t ed25519 -C "你的邮箱"`，把 `~/.ssh/id_ed25519.pub` 加到
GitHub → Settings → SSH and GPG keys，远程地址改成
`git@github.com:你的用户名/PrinterShareFixer.git`。

## 5. 发布版本（上传两种压缩包）

发布包不在仓库里（`release/` 被忽略），要作为 Release 附件上传：

```powershell
# 在项目根目录打包（会生成自带运行时和不带运行时两种）
powershell -ExecutionPolicy Bypass -File .\package.ps1
```

得到：

- `release\PrinterShareFixer-1.2.0-win-x64.zip`（自带 .NET 运行时，约 127 MB）
- `release\PrinterShareFixer-1.2.0-win-x64-requires-dotnet.zip`（需装 .NET 10 运行时，约 59 MB）

在 GitHub 上：

1. 仓库右侧 **Releases → Draft a new release**。
2. **Choose a tag** 选 `v1.2.0`。
3. 标题填 `v1.2.0`，描述可从 `CHANGELOG.md` 里对应版本的内容复制。
4. 把两个 zip 拖到 **Attach binaries** 区域。
5. **Publish release**。

## 6. 自动化构建（可选，已配好）

`.github/workflows/build.yml` 会在推送到 `main` 或提交 Pull Request 时自动：
编译 → 自检内嵌 PowerShell 脚本 → 产出两种发布包。
在仓库 **Actions** 页面能看到结果，每次运行的 **Artifacts** 里可下载
`PrinterShareFixer-build`（保留 7 天）。

> 若首次运行报 .NET SDK 版本问题，把 `build.yml` 里的 `dotnet-version: '10.0.x'`
> 改成当时最新版即可。

## 7. 以后的固定动作

```powershell
cd <项目根目录>

# 1) 改代码、改 src/*/*.csproj 里的 <Version>，并在 AppInfo.cs 补一条更新说明
# 2) 同步更新日志
powershell -ExecutionPolicy Bypass -File .\tools\update-changelog.ps1

# 3) 开发仓库提交 + 打标签
git add -A
git commit -m "v1.2.1：..."
git tag -a v1.2.1 -m "v1.2.1"

# 4) 同步到发布目录（会带上新标签），然后推送
powershell -ExecutionPolicy Bypass -File .\tools\export-github-repo.ps1 -Update
cd github-repo
git push --follow-tags

# 5) 回根目录打包并上传新的 Release 附件
cd ..
powershell -ExecutionPolicy Bypass -File .\package.ps1
```

## 8. 常见问题

**Q：为什么不直接推根目录？**
根目录里有 `.git` 指针文件、`.git-store/`（git 数据目录）、`build/`、`release/`、`.preview/`
等本机开发环境产生的内容。虽然它们在 `.gitignore` 里，但把发布目录单独拆出来更直观，
也避免误操作把这些文件带上去。

**Q：`github-repo/` 里的 `.git` 是正常的目录吗？**
是。它是标准的 git 仓库目录，可以直接 `git push`。根目录里的 `.git` 才是指针文件
（`gitdir: .../.git-store`），那是为了让本机开发环境的沙箱能正常工作。

**Q：`git push` 提示 `Support for password authentication was removed`**
不能用账号密码，请用第 4 步里的浏览器登录或 Personal Access Token。

**Q：提示 `remote origin already exists`**
改成 `git remote set-url origin https://github.com/你的用户名/PrinterShareFixer.git`。

**Q：发布目录里想彻底重来一次？**
直接删掉 `github-repo/` 文件夹，重新运行不带 `-Update` 的导出脚本即可
（注意：这会让提交哈希全部变化，别在已经推送过的仓库上这么做，否则要 force push）。

**Q：私有仓库别人能下载 Release 附件吗？**
不能。Private 仓库的 Releases 只有你自己可见；想公开分享就设为 Public。

**Q：LICENSE 想换一个？**
把 `LICENSE` 换成你选的许可证（<https://choosealicense.com/> 上挑一个），
并同步修改 README 末尾的许可证说明。
