# 怎么一起开发

日常开发都在分支 **`ddw-develop`** 上，直接 push。不要改 `main`。

仓库：https://github.com/DDWking/ai-disk-cleaner

唯一硬规则：**推之前先 `git pull`**。

---

## 0. 主人先做一次（DDWking）

已邀请协作者 **bissensei**（Write）。如果他还进不去：邮箱里点 Accept，或打开 https://github.com/DDWking/ai-disk-cleaner/invitations

---

## 1. 朋友第一次（只做一次）

装 Git：https://git-scm.com/download/win  
装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0

```powershell
git clone https://github.com/DDWking/ai-disk-cleaner.git
cd ai-disk-cleaner
git checkout ddw-develop
git pull origin ddw-develop
```

能跑起来：

```powershell
cd src\AiDiskCleaner
dotnet build
# 右键 exe → 以管理员身份运行
```

---

## 2. 每次改东西

```powershell
git checkout ddw-develop
git pull origin ddw-develop

# 改代码，必要时先改 PROGRESS.md 占坑
git add .
git commit -m "feat: 一句话说清楚改了什么"
git pull origin ddw-develop
git push origin ddw-develop
```

如果提示有冲突：把文件里的 `<<<<<<<` 解开，留该留的，再：

```powershell
git add .
git commit -m "merge: 解决冲突"
git push origin ddw-develop
```

建议前缀：`feat:` / `fix:` / `ui:` / `docs:`。

---

## 进度怎么同步

[PROGRESS.md](PROGRESS.md) 是两人共用看板。

- 开工前：占坑（名字、任务）
- 做完 / 卡住：日志最上面加一段，改状态表

不要只在微信里说做到哪了。

---

## 不要做的事

- 不要 `git push origin main`
- 推之前不 `git pull`
- 两人同时改同一个文件还不沟通（先看 PROGRESS.md）
- 提交 `bin/`、`obj/`、`*.log`、扫描结果（已在 `.gitignore`）
