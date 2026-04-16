# XSZRemoteChatBridge Server酱3 消息接入 — 任务分解

## 任务列表
- [x] 任务1：改造上行配置模型（涉及文件：`plugin/BridgeOptions.cs`；完成标准：新增 bot / Server酱3 目标级开关与 Server酱3 发送地址，Normalize 后支持“仅 Server酱3 可用”的配置组合；验证方式：`dotnet build plugin/XSZRemoteChatBridge.csproj -c Debug` + 配置模型代码审查）
- [x] 任务2：调整设置页信息结构与文案（涉及文件：`plugin/SettingsWindow.cs`；完成标准：上行总开关、bot 目标、Server酱3 目标和发送地址均可编辑并自动保存，文案明确 Server酱3 使用官方 send URL；验证方式：`dotnet build plugin/XSZRemoteChatBridge.csproj -c Debug` + 设置页手工检查）
- [x] 任务3：实现聊天上行多目标分发（涉及文件：`plugin/RemoteChatBridgeModule.cs`、`plugin/ServerChanPushClient.cs`；完成标准：过滤命中的聊天消息可按配置发送到 bot、Server酱3 或两者；任一目标异常不影响另一目标；验证方式：`dotnet build plugin/XSZRemoteChatBridge.csproj -c Debug` + 组合场景手工验证）
- [x] 任务4：实现掉线提醒多目标分发（涉及文件：`plugin/RemoteChatBridgeModule.cs`、`plugin/ServerChanPushClient.cs`；完成标准：掉线提醒可按配置发送到 bot、Server酱3 或两者，且与聊天上行使用一致的目标可用性判断；验证方式：`dotnet build plugin/XSZRemoteChatBridge.csproj -c Debug` + 掉线提醒场景手工验证）
- [x] 任务5：补充配置校验与日志边界（涉及文件：`plugin/RemoteChatBridgeModule.cs`、`plugin/ServerChanPushClient.cs`；完成标准：分别输出 bot / Server酱3 目标缺失告警，且不记录完整敏感地址或密钥；验证方式：代码审查 + 日志输出检查）
- [x] 任务6：同步用户文档与知识沉淀（涉及文件：`README.md`、`helloagents/CHANGELOG.md`、`helloagents/wiki/modules/plugin.md`；完成标准：文档准确描述 bot / Server酱3 对聊天上行和掉线提醒的双目标能力、Server酱3 配置方式和“bot 可不配置”约束；验证方式：文档审查）

## 进度
- [x] 已完成需求澄清与方案设计。
- [x] 已确认推荐架构为“聊天上行与掉线提醒都通过统一分发层发送到独立目标”。
- [x] 已完成实现并通过构建验证。
