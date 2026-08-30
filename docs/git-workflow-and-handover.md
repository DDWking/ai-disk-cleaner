# Git 团队协作与交接文档（2 人版）

> 目标：让「你 + 朋友 + AI」三方之间的代码交接不靠口头约定，而靠一套固定流程。
> 这份文档既是你俩的协作约定，也是你**把项目交给 AI / 交接给队友时的"交代模板"**。

---

## 0. 一句话原则

- `main` 分支**永远可运行**，是唯一"能交付"的状态。
- 任何新功能 / 修复都从**最新的 `main`** 开分支，改完用 **Pull Request（PR）** 合并。
- **AI 是提建议的人，不是替你按回车的人**——每条命令自己看懂再执行。

---

## 1. 仓库结构约定（统一后 AI 才能精准定位）

```
ai-disk-cleaner/
├─ src/              # 源码（扫描 / 分析 / 界面）
├─ docs/             # 文档、设计决策、交接记录
├─ tests/            # 测试
├─ .gitignore        # 规定什么不上传
├─ README.md         # 项目是什么、怎么跑起来
└─ .env.example      # 密钥的模板（真密钥只放 .env，永不提交）
```

---

## 2. 分支命名（固定格式）

| 前缀 | 用途 | 例子 |
|---|---|---|
| `feature/` | 新功能 | `feature/mft-scan` |
| `fix/` | 修 bug | `fix/decode-crash` |
| `docs/` | 文档 | `docs/handover` |

**两人永远不要直接在 `main` 上写代码。**

---

## 3. 一次改动的标准流程（背下来）

```bash
# 1. 开工前同步最新
git pull origin main

# 2. 从最新 main 开分支
git checkout -b feature/xxx

# 3. 写代码，小步提交（改一点提交一点，别攒一大坨）
git add .
git commit -m "feat: 实现 MFT 读取"

# 4. 首次推送到远程
git push -u origin feature/xxx

# 5. 去 GitHub/Gitee 网页上开 PR，@对方 review

# 6. 对方 review 通过 → 网页上点 Merge → 删远程分支

# 7. 回到本地，切回 main 并同步，删掉本地分支
git checkout main
git pull origin main
git branch -d feature/xxx
```

---

## 4. 常用命令速查

| 场景 | 命令 |
|---|---|
| 看当前状态/分支 | `git status` |
| 看改动内容 | `git diff` |
| 看提交历史 | `git log --oneline --graph` |
| 暂存并提交 | `git add . && git commit -m "..."` |
| 拉取+合并远程 | `git pull origin main` |
| 推送 | `git push` |
| 放弃**未提交**的改动 | `git checkout -- <文件>` |
| 放弃**已暂存**的改动 | `git reset HEAD <文件>` |
| 看远程地址 | `git remote -v` |

---

## 5. Commit 规范（Conventional Commits）

格式：`类型: 简短说明`，首字母小写、不用句号。

| 类型 | 含义 |
|---|---|
| `feat` | 新功能 |
| `fix` | 修 bug |
| `docs` | 只改文档 |
| `refactor` | 重构，不改功能 |
| `test` | 只改测试 |
| `chore` | 杂活（配置、依赖） |

好例子：`feat: 按大小排序后输出 Top 100 文件`
坏例子：`更新`、`改了点东西`、`111`

---

## 6. .gitignore 必须忽略的（本项目特别重要）

- 编译产物：`bin/`、`obj/`、`__pycache__/`、`node_modules/`
- IDE 配置：`.vs/`、`.idea/`、`.vscode/`
- **密钥**：`.env`（只提交 `.env.example` 模板）
- **扫描结果缓存**：磁盘扫描会生成几百 MB 的缓存，且含用户隐私路径，**严禁提交**

---

## 7. 如何跟 AI 沟通 Git（重点）

### 7.1 每次提问先给 AI 这三样

1. **当前分支**：`我在 feature/mft-scan 分支`
2. **你想做什么**：`我想把扫描结果合并回 main`
3. **粘贴 `git status` 和 `git log --oneline -5`** 的输出

> AI 看不见你的屏幕，你不贴状态，它只能瞎猜。

### 7.2 常用提问模板（直接抄）

**写提交信息：**
> 我改了 `src/scanner/mft.py`，实现了从 MFT 读取文件路径和大小，请帮我写一条规范的 commit message。

**解决冲突：**
> 我和队友都改了 `scanner.py`，git 报冲突了。我的意图是 [A]，对方的改动看上下文是 [B]，请帮我分析每段冲突应该保留哪边，并解释为什么。

**写 PR 描述：**
> 请帮我把 `feature/mft-scan` 分支相对 `main` 的改动整理成一份 PR 描述：做了什么、怎么测试、有什么风险。

**看懂命令：**
> 网上说要执行 `git reset --hard HEAD~1`，请解释这条命令具体会删掉什么，有没有更安全的替代。

### 7.3 红线：别让 AI 直接执行、也别盲从的命令

| 危险命令 | 为什么危险 |
|---|---|
| `git push --force` / `-f` | 覆盖远程历史，队友的提交会被冲掉 |
| `git reset --hard` | 彻底删掉未提交改动，救不回来 |
| `git checkout .` / `git clean -fd` | 丢弃所有改动 / 删除未跟踪文件 |
| `git rebase` 改已推送的历史 | 团队协作的大忌 |
| 删除分支前未确认已合并 | 可能丢代码 |

> 约定：AI 给的命令，**要求它逐条解释**，你自己看懂再敲。任何带 `--force`、`--hard`、`clean` 的命令，先问一句"会不会丢东西"。

### 7.4 交接清单模板（给队友 / AI / 未来的自己）

```text
【本次交接】
- 分支：feature/mft-scan
- 我做了什么：实现了 MFT 读取，能拿到路径/大小/时间戳
- 还没做完的：目录树结构没建、权限判断没写
- 卡在哪：读系统盘 C: 需要管理员权限，不知道用哪个 API 绕
- 下一步建议：先用普通递归遍历跑通全流程，MFT 留到第二阶段
```

---

## 8. 常见坑与急救

| 坑 | 急救 |
|---|---|
| 忘 pull 就提交，push 被拒 | `git pull --rebase origin main` 后再 push |
| 提交了大文件/密钥 | 见下方"泄密急救" |
| 在 `main` 上直接写了代码 | `git checkout -b feature/xxx`（把改动带过去），再把 main 切回干净 |
| 冲突不知怎么解 | 把冲突文件 + 双方意图喂给 AI（见 7.2） |

**泄密急救（密钥已提交）：** 立即去平台吊销该 key，再 `git rm --cached 文件` 并轮换新 key——**只删提交记录不够，key 一旦公开就必须作废重发**。

---

## 附：初始化仓库（第一次做）

```bash
git init
git branch -M main
git remote add origin <你的仓库地址>
# 先放好 .gitignore 和 README，再首次提交
git add .
git commit -m "chore: 初始化项目结构"
git push -u origin main
```
