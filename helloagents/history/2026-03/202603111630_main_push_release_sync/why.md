# 变更提案: 主分支自动发布并同步 DalamudPlugins

## 需求背景
当前仓库缺少自动化发布链路。每次主分支更新后，插件构建、Release 发布、分发仓库同步与 `pluginmaster.json` 更新需要手工处理，存在漏发与元数据不一致风险。

## 变更内容
1. 新增 GitHub Actions 工作流：主分支推送后自动编译插件并打包。
2. 自动创建/更新当前仓库 Release（上传插件压缩包）。
3. 自动同步压缩包到 `ShoOtaku/DalamudPlugins` 仓库指定目录。
4. 自动新增或更新 `pluginmaster.json` 中 `XSZRemoteChatBridge` 条目。

## 影响范围
- **模块:** CI 发布链路、plugin 元数据、项目文档
- **文件:** `.github/workflows/*`、`scripts/*`、`README.md`（预期）
- **API:** 无新增业务 API
- **数据:** 更新 `pluginmaster.json`（外部仓库）

## 核心场景

### 需求: 自动发布插件产物
**模块:** plugin
在主分支出现新提交后，系统应自动完成构建、打包、发布与分发。

#### 场景: 主分支推送自动完成构建与发布
条件：`main` 分支有新的 push 事件。
- 产出 `latest.zip`，并上传到当前仓库 Release。
- Release 版本号、插件包、pluginmaster 信息三者一致。

### 需求: 自动同步分发仓库
**模块:** plugin
分发仓库中的插件包与 pluginmaster 需要自动更新。

#### 场景: 分发仓库已有/不存在插件条目均可处理
条件：工作流可访问 `ShoOtaku/DalamudPlugins`。
- 若条目存在，则更新版本、下载地址、时间戳、更新日志。
- 若条目不存在，则自动创建完整条目并写回。

## 风险评估
- **风险:** 目标仓库分支策略可能调整（`main` 或其他分支）。
- **缓解:** 工作流中将目标分支设为可配置变量（默认 `main`），并允许手动覆盖。
- **风险:** 跨仓库写入依赖 PAT 权限。
- **缓解:** 使用最小权限 PAT（`repo`），并仅在发布作业注入。
