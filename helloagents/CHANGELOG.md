# Changelog

本文件记录项目所有重要变更。
格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/),
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

- 扩展掉线弹窗关键词匹配：新增 `已断开连接。`、`与服务器的连接已中断。`，并在提醒正文中记录实际命中的文案。
- 设置页新增 `Server酱` 说明页签：内置使用步骤、SendKey/API/安装文档链接，点击可直接在系统浏览器打开。
- 新增双目标推送能力：聊天上行与掉线提醒现在都可独立推送到机器人和 / 或 Server酱；机器人未配置时可仅通过 Server酱完成推送。
- 新增自定义频道 ID 支持：上行全量频道与关键词频道规则现在都可手动追加 raw channel id，并在运行时按原始频道值匹配。
- 优化 WS 同 key 冲突告警：增加 `1012/1013` 冲突识别与重复告警节流，降低持续刷警告噪音。
- 优化 WS 冲突重连策略：当会话关闭原因为 `replaced/already_connected` 时，插件标记为“同 key 连接冲突”并提高重连退避等级，减少告警刷屏。
- 下行发送实现切换为普通文本走 `UIModule.ProcessChatBoxEntry`，斜杠命令走 `ShellCommandModule.ExecuteCommandInner`，不再依赖 `ICommandManager.ProcessCommand`。
- 修复下行成功日志误报：仅当 `ProcessCommand` 返回 `true` 时记录“下行执行成功”，未被命令系统接管时改为告警并回退本地显示。
- 调整上行过滤策略：移除“全局频道白名单”入口，改为“指定频道消息全部上传（无视关键词）”与关键词映射组合。
- 调整关键词匹配默认行为：当“关键词匹配模式”为“任意命中”时，自动启用正则匹配。
- 修复下行消息执行路径：非命令文本不再仅本地 `Print`，改为默认执行 `/e <消息>` 发送到游戏聊天。
- 新增“全量上行频道”能力：可单独指定频道，命中后忽略关键词规则并将该频道全部消息上报到机器人。
- 修复 WebSocket 下行稳定性：补齐应用层心跳（主动 `ping`、空闲超时检测），并在连接关闭时输出关闭状态与描述，降低 `ws closed by peer` 排障成本。
- 增加桥接启动配置自检：当上行/下行关键字段缺失时输出明确告警（如 `BridgeSecret`、`IngestEndpoint`、`PullEndpoint`），避免“无法推送但无明确原因”。
- 重构关键词管理界面为双栏布局：左侧关键词列表与加减按钮，右侧频道筛选与勾选面板。
- 新增“关键词-频道映射”能力：每个关键词可独立配置频道，且保留旧版全局频道+关键词配置的兼容回退。
- 初始化 HelloAGENTS 知识库与基础 Wiki。
- 新增 GitHub Actions 工作流：`main` 推送自动构建、打包并发布 Release。
- 新增 `scripts/update-pluginmaster.ps1`，自动新增/更新 `ShoOtaku/DalamudPlugins` 的 `pluginmaster.json` 条目。
- 更新 `README.md` 与 `plugin/XSZRemoteChatBridge.json`，补充自动发布配置与元数据维护约定。
- 修复 CI 构建依赖：工作流改为先下载 `dalamud-distrib/latest.zip` 并设置 `DALAMUD_HOME`，避免 GitHub Runner 缺失 Dalamud 安装导致编译失败。
- 调整 CI 依赖源：改为从 `Dalamud-DailyRoutines/Dalamud` 最新 release 下载 `latest.7z` 并解压，匹配国服 Dalamud 发行源。
- 调整 pluginmaster 标签策略：自动写入 `Tags/CategoryTags` 时固定为 `["utility"]`，不再附加其他标签。
