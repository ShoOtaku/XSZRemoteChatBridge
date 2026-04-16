# 恢复快照

## 主线目标
为 XSZRemoteChatBridge 规划将游戏聊天上行消息与掉线提醒并行接入 Server酱3，且机器人链路可不配置。

## 正在做什么
已完成 Server酱 双目标推送接入实现，并通过 `dotnet build plugin/XSZRemoteChatBridge.csproj -c Debug` 验证。

## 关键上下文
- 用户确认：命中过滤规则的上行消息需要同时支持机器人和 Server酱3 两条输出通道。
- 用户确认：Server酱3 消息采用简洁通知样式，标题展示频道与发送者，详情展示世界信息和完整正文。
- 用户补充：即使未配置机器人，也必须允许仅通过 Server酱3 完成消息推送。
- 用户补充：掉线提醒也要接入 Server酱3。
- 已完成实现：聊天上行与掉线提醒都通过统一分发层发送到 bot / Server酱目标；各目标独立校验配置、独立告警、互不阻塞。

## 下一步
如需继续验收，可在实际游戏环境中验证“仅 bot / 仅 Server酱 / bot+Server酱”三种组合下的聊天上行与掉线提醒行为。

## 阻塞项
（无）

## 方案
plans/202604161248_serverchan_upstream_fanout

## 已标记技能
hello-security
