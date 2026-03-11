# Changelog

本文件记录项目所有重要变更。
格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/),
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

- 初始化 HelloAGENTS 知识库与基础 Wiki。
- 新增 GitHub Actions 工作流：`main` 推送自动构建、打包并发布 Release。
- 新增 `scripts/update-pluginmaster.ps1`，自动新增/更新 `ShoOtaku/DalamudPlugins` 的 `pluginmaster.json` 条目。
- 更新 `README.md` 与 `plugin/XSZRemoteChatBridge.json`，补充自动发布配置与元数据维护约定。
- 修复 CI 构建依赖：工作流改为先下载 `dalamud-distrib/latest.zip` 并设置 `DALAMUD_HOME`，避免 GitHub Runner 缺失 Dalamud 安装导致编译失败。
- 调整 CI 依赖源：改为从 `AtmoOmen/Dalamud` 最新 release 下载 `latest.7z` 并解压，匹配国服 Dalamud 发行源。
- 调整 pluginmaster 标签策略：自动写入 `Tags/CategoryTags` 时固定为 `["utility"]`，不再附加其他标签。
