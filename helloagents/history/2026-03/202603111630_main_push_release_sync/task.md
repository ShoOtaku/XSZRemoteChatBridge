# 任务清单: 主分支自动发布并同步 DalamudPlugins

目录: `helloagents/history/2026-03/202603111630_main_push_release_sync/`

---

## 1. 工作流与构建打包
- [√] 1.1 在 `.github/workflows/release_publish.yml` 中新增主分支 push 触发的工作流，包含 `checkout`、`setup-dotnet`、`dotnet build -c Release`，验证 `why.md#需求自动发布插件产物-场景主分支推送自动完成构建与发布`
- [√] 1.2 在 `.github/workflows/release_publish.yml` 中加入打包步骤，生成 `artifacts/latest.zip`（包含插件运行所需文件），验证 `why.md#需求自动发布插件产物-场景主分支推送自动完成构建与发布`，依赖任务1.1
- [√] 1.3 在 `.github/workflows/release_publish.yml` 中加入 release 步骤（按 `run_number` 生成版本并上传 `latest.zip`），验证 `why.md#需求自动发布插件产物-场景主分支推送自动完成构建与发布`，依赖任务1.2

## 2. pluginmaster 自动更新
- [√] 2.1 在 `scripts/update-pluginmaster.ps1` 中实现“按 `InternalName=XSZRemoteChatBridge` 查找并更新或创建条目”的逻辑，验证 `why.md#需求自动同步分发仓库-场景分发仓库已有不存在插件条目均可处理`
- [√] 2.2 在 `.github/workflows/release_publish.yml` 中增加跨仓库同步步骤：克隆 `ShoOtaku/DalamudPlugins`、复制 `latest.zip` 到 `plugins/XSZRemoteChatBridge/latest.zip`、执行 `scripts/update-pluginmaster.ps1`，验证 `why.md#需求自动同步分发仓库-场景分发仓库已有不存在插件条目均可处理`，依赖任务2.1
- [√] 2.3 在 `.github/workflows/release_publish.yml` 中补充 commit/push（作者信息、幂等判断、分支可配置默认 `main`），验证 `why.md#需求自动同步分发仓库-场景分发仓库已有不存在插件条目均可处理`，依赖任务2.2

## 3. 文档与配置收口
- [√] 3.1 在 `README.md` 中补充自动发布链路说明（触发、Secrets、目标仓库路径），验证 `why.md#需求自动发布插件产物-场景主分支推送自动完成构建与发布`
- [√] 3.2 在 `plugin/XSZRemoteChatBridge.json` 中补充 `RepoUrl/IconUrl/Changelog` 等可发布字段的维护约定（若本次需要），验证 `why.md#需求自动同步分发仓库-场景分发仓库已有不存在插件条目均可处理`

## 4. 安全检查
- [√] 4.1 执行安全检查（按G9: Secret 仅来自 Actions Secrets、禁止明文写入 Token、限制 workflow 权限、校验目标仓库地址）

## 5. 测试
- [-] 5.1 在 GitHub Actions 中验证场景测试: 主分支 push 后成功生成 release 且上传 `latest.zip`，验证点: release 版本可追溯、产物可下载
> 备注: 本地已完成 `dotnet build -c Release` 与打包链路验证；需在远端 main 分支 push 后完成线上验证。
- [-] 5.2 在 GitHub Actions 中验证场景测试: 目标仓库 `pluginmaster.json` 被正确新增/更新，验证点: JSON 合法、`AssemblyVersion` 与本次发布一致、`LastUpdate` 更新
> 备注: 本地已通过 `scripts/update-pluginmaster.ps1` 对新增与覆盖更新进行模拟验证；需在远端工作流中验证真实跨仓库推送。
