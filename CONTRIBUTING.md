# 怎么一起开发

仓库公开后，任何人都能看代码。真正改代码走 **Fork + Pull Request**，不要直接往 `main` 推。

## 你是仓库主人（DDWking）

1. 把小伙伴的 GitHub 用户名发过来，在仓库 **Settings → Collaborators** 点 Invite（可选：给写权限，方便他们直接推分支）。
2. 更稳妥的方式和开源项目一样：**不给写权限**，让他们 Fork 后提 PR，你点 Merge。
3. `main` 保持可运行。功能都在分支上做。

## 小伙伴第一次加入

### 1. 装工具

- Git：https://git-scm.com/download/win
- .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0
- 一个 GitHub 账号

### 2. Fork + 克隆

在网页打开本仓库，点右上角 **Fork**，fork 到自己账号下。然后：

```powershell
git clone https://github.com/<小伙伴用户名>/ai-disk-cleaner.git
cd ai-disk-cleaner
git remote add upstream https://github.com/DDWking/ai-disk-cleaner.git
```

之后每次开工先同步上游：

```powershell
git checkout main
git fetch upstream
git merge upstream/main
```

### 3. 开分支改东西

```powershell
git checkout -b feature/xxx
# 改代码
git add .
git commit -m "feat: 一句话说清楚改了什么"
git push -u origin feature/xxx
```

到 GitHub 自己的 fork 页，点 **Compare & pull request**，目标仓库选 `DDWking/ai-disk-cleaner` 的 `main`。

### 4. 你（主人）合并

打开 PR → 看 diff → 没问题点 **Merge pull request**。合并后告诉对方：

```powershell
git checkout main
git fetch upstream
git merge upstream/main
git branch -d feature/xxx
```

## 分支命名

| 前缀 | 用途 | 例子 |
|---|---|---|
| `feature/` | 新功能 | `feature/ai-clean-hints` |
| `fix/` | 修 bug | `fix/mft-size` |
| `docs/` | 文档 | `docs/readme` |

## 提交说明

用简短中文或英文都可以，建议：

- `feat: ...` 新功能
- `fix: ...` 修 bug
- `ui: ...` 界面
- `docs: ...` 文档

一次 PR 只做一件事，方便 review。

## 本地怎么跑

需要**管理员权限**（读 `$MFT`）：

```powershell
cd src\AiDiskCleaner
dotnet build
# 右键 exe → 以管理员身份运行
```

不要提交 `bin/`、`obj/`、`*.log`、扫描结果。这些已经在 `.gitignore` 里。
