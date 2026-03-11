# 迁移说明

## 目标

将 XSZToolbox 内嵌的远程聊天能力拆分为独立仓库插件，并完成 WebSocket 下行改造。

## 关键改造

1. 插件工程独立化
   - `Dalamud.CN.NET.Sdk/14.0.1`
   - `net10.0-windows`
   - 标准 `IDalamudPlugin` 入口
2. 通信链路升级
   - 上行：`POST /ff14/bridge/ingest`
   - 下行：`WS /ff14/bridge/ws`（优先）
   - 回退：`POST /ff14/bridge/pull`
3. UI 与配置能力增强
   - 自动保存/自动应用
   - 基础/高级分页
   - 关键词正则匹配
   - 聊天调试开关
   - 下行/上行/WS 重试参数独立

## 配置映射（旧 -> 新）

- `LocalChatBridgeEndpoint` -> `IngestEndpoint`
- `LocalChatBridgePullEndpoint` -> `PullEndpoint`
- `LocalChatBridgeKey` -> `BridgeKey`
- `LocalChatBridgeSecret` -> `BridgeSecret`
- 新增：`WebSocketEndpoint`

## 切换步骤

1. 机器人端先支持 WS 路由（同时保留 pull）。
2. 插件端升级到独立仓库版本。
3. 用 `ff14bot send <text>` 验证：
   - WS push
   - 游戏端 ACK
   - WS 异常时 pull 回退可用
4. 观察稳定后，将 pull 间隔调大，仅保留兜底。

## Nginx 要点（WS）

- 反代 `/ff14/bridge/ws` 时需开启：
  - `proxy_http_version 1.1`
  - `Upgrade` / `Connection: upgrade`

## 回滚策略

1. 插件端关闭 `EnableWebSocketDownstream`。
2. 继续使用 Pull 下行。
3. 机器人端无需回滚（Pull 接口兼容保留）。
