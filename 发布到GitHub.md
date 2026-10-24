# 发布到 GitHub 步骤

本地仓库已初始化并完成提交（主分支 main，已打 v0.2.0 标签）。

## 方式一：用 GitHub CLI（推荐）

1. 安装 gh：`winget install GitHub.cli`
2. 登录：`gh auth login`
3. 创建公开仓库并推送（在本目录执行）：

```bat
gh repo create ServerMaster --public --source=. --push
```

## 方式二：网页创建 + 命令推送

1. 打开 https://github.com/new
   - Repository name: `ServerMaster`
   - 选择 **Public**（公开，允许他人 Fork 二创）
   - **不要**勾选 README/.gitignore/License 初始化（本仓库已自带）
2. 在本目录执行（替换 `<你的用户名>`）：

```bat
git remote add origin https://github.com/<你的用户名>/ServerMaster.git
git push -u origin main
git push origin v0.2.0
```

## 推送后建议设置

- 仓库 About 栏填写描述与标签（rdp, remote-desktop, server-management, winforms）
- Settings → General → Features 允许 Issues（便于收到二创反馈）
- 二创规范由 MIT LICENSE 约定：保留版权声明即可自由修改与再分发
