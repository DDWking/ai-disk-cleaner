# 怎么一起开发

两人开发：**不要直接改 `main`**。每人开自己的分支，改完提 Pull Request，合并进 `main`。

推荐流程：主人把朋友加成协作者 → 朋友克隆**这个仓库** → 开分支 → 推分支 → 提 PR。  
（不需要 Fork。Fork 是给路人用的。）

仓库：https://github.com/DDWking/ai-disk-cleaner

---

## 0. 主人先做一次（DDWking）

1. 打开 https://github.com/DDWking/ai-disk-cleaner/settings/access
2. **Add people**，填朋友的 GitHub 用户名，权限选 **Write**
3. 朋友邮箱里点 Accept invitation

加好之后，朋友对这个仓库有推分支的权限，但约定：**仍然不许推 `main`**。

---

## 1. 朋友第一次（只做一次）

装：

- Git：https://git-scm.com/download/win
- .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0
- GitHub 账号（已接受邀请）

克隆**主仓库**（不是 fork）：

```powershell
git clone https://github.com/DDWking/ai-disk-cleaner.git
cd ai-disk-cleaner
```

能跑起来：

```powershell
cd src\AiDiskCleaner
dotnet build
# 右键 exe → 以管理员身份运行
```

---

## 2. 每次改东西（朋友日常）

### ① 先同步最新 main

```powershell
git checkout main
git pull origin main
```

### ② 在 PROGRESS.md 占坑

打开 `PROGRESS.md`，在「谁在做什么」填上自己的名字、任务、分支名。避免两人改同一块。

### ③ 开新分支（不要在 main 上改）

```powershell
git checkout -b feature/xxx
```

命名：

| 前缀 | 用途 | 例子 |
|---|---|---|
| `feature/` | 新功能 | `feature/ai-clean-hints` |
| `fix/` | 修 bug | `fix/mft-size` |
| `docs/` | 文档 / 进度 | `docs/progress` |

### ④ 改代码，小步提交

```powershell
git add .
git commit -m "feat: 一句话说清楚改了什么"
```

建议前缀：`feat:` / `fix:` / `ui:` / `docs:`。一次 PR 只做一件事。

做完在 `PROGRESS.md` 日志最上面加几行。

### ⑤ 把分支推上去

```powershell
git push -u origin feature/xxx
```

**不要** `git push origin main`。

### ⑥ 提 Pull Request

1. 打开 https://github.com/DDWking/ai-disk-cleaner
2. 会看到黄条 **Compare & pull request**，点它  
   没有黄条就点 **Pull requests → New pull request**，head 选你的 `feature/xxx`，base 选 `main`
3. 标题写清楚做了什么，点 **Create pull request**

### ⑦ 主人合并

主人打开 PR，看 diff，没问题点 **Merge pull request**。

朋友本地收尾：

```powershell
git checkout main
git pull origin main
git branch -d feature/xxx
```

下一件事先再 `git pull`，再开新分支。不要复用旧分支。

---

## 进度怎么同步

[PROGRESS.md](PROGRESS.md) 是两人共用看板。

- 开工前：占坑（名字、任务、分支）
- 做完 / 卡住：日志最上面加一段，改状态表
- 进度文件可以跟功能放同一次提交，也可以单独 `docs: 更新进度`

不要只在微信里说做到哪了。

---

## 不要做的事

- 不要在 `main` 上直接改、直接 push
- 不要两个人用同一个功能分支
- 不要提交 `bin/`、`obj/`、`*.log`、扫描结果（已在 `.gitignore`）
- 开工前不看 `PROGRESS.md` 就开写

---

## 路人 / 没被加成协作者

走 Fork：网页点 Fork → clone 自己的 fork → 开分支 → 对 `DDWking/ai-disk-cleaner` 的 `main` 提 PR。两人协作不必走这条。
