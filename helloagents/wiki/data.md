# 数据模型

## 概述
当前仓库无持久化数据库模型；核心数据以插件配置与运行时消息体为主。

---

## 运行时模型

### BridgeOptions
**描述:** 插件桥接配置（端点、鉴权、重试、上下行开关等）。
- 关键词过滤支持两层结构：
  - `KeywordChannelRules`: 关键词-频道映射规则（优先使用）
  - `KeywordRules + ChannelAllowList`: 兼容旧配置的回退结构

### BridgeKeywordChannelRule
**描述:** 单条关键词映射规则，包含 `Keyword` 与 `ChannelAllowList`，用于实现“每个关键词独立选择频道”。

### BridgePayload
**描述:** 上行消息载荷，包含频道、发送者、内容、事件 ID 等。

### BridgePullResponse
**描述:** 下行拉取响应，包含消息列表。
