# 数据模型

## 概述
当前仓库无持久化数据库模型；核心数据以插件配置与运行时消息体为主。

---

## 运行时模型

### BridgeOptions
**描述:** 插件桥接配置（端点、鉴权、重试、上下行开关等）。
- 关键词过滤支持两层结构：
  - `KeywordChannelRules`: 关键词-频道映射规则（优先使用）
  - `KeywordRules + 所有频道`: 兼容旧配置的回退结构（不再暴露全局频道白名单）
- `UploadAllChannelList + UploadAllCustomChannelList`: 指定频道全量上报列表，支持枚举频道与手动输入的 raw channel id，命中后跳过关键词匹配。
- 当 `KeywordMatchMode=Any` 时，`KeywordUseRegex` 在运行时默认启用。

### BridgeKeywordChannelRule
**描述:** 单条关键词映射规则，包含 `Keyword`、`ChannelAllowList` 与 `CustomChannelAllowList`，用于实现“每个关键词独立选择频道”，并允许补充任意 raw channel id。

### BridgePayload
**描述:** 上行消息载荷，包含频道、发送者、内容、事件 ID 等。

### BridgePullResponse
**描述:** 下行拉取响应，包含消息列表。

### GameChatSender
**描述:** 下行发送适配层（移植自 `XSZToolbox/OmenTools` 实现）。
- 普通文本发送：`UIModule.ProcessChatBoxEntry`。
- 斜杠命令发送：`ShellCommandModule.ExecuteCommandInner`。
