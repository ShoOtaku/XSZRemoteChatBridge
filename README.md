# XSZRemoteChatBridge

FF14 远程聊天桥接独立卫月插件仓库。  
支持游戏聊天上行到机器人，以及机器人下行到游戏（优先 WebSocket，失败回退 Pull）。

## 功能概览

- 上行：监听指定聊天频道与关键词，`POST /ff14/bridge/ingest`
- 下行：
  - 首选 `WS /ff14/bridge/ws`
  - 失败自动回退 `POST /ff14/bridge/pull`
- 签名鉴权：`X-Bridge-Key / Timestamp / Signature / Nonce`
- UI 设置：
  - 基础/高级分页
  - 自动保存与自动应用（防抖）
  - 关键词普通匹配/正则匹配
  - 频道中文名显示（可搜索）
  - 调试开关（可打印全部聊天频道名与 ID）

## 目录结构

- `plugin/`: 卫月插件源码（`.NET 10 + Dalamud API 14`）
- `bot/`: 机器人端对接说明与仓库链接
- `docs/`: 迁移与部署补充文档

## 环境要求

- .NET SDK 10
- Dalamud.CN SDK 环境（`Dalamud.CN.NET.Sdk/14.0.1`）

## 构建

```powershell
dotnet build .\plugin\XSZRemoteChatBridge.csproj -c Debug
```

产物位于 `plugin/bin/Debug/XSZRemoteChatBridge.dll`。

## 使用方式

1. 安装插件后，在游戏内输入 `/xszrcb` 打开设置。
2. 填写机器人返回的：
   - `IngestEndpoint`
   - `PullEndpoint`
   - `WebSocketEndpoint`
   - `BridgeKey`
   - `BridgeSecret`
3. 根据需要设置频道与关键词规则（支持正则）。
4. 保存为自动生效，无需手动点击“保存并应用”。

## 机器人仓库

机器人插件已拆分并同步到：  
`https://github.com/ShoOtaku/nonebot-plugin-ff14bot-bridge`

## 相关文档

- 迁移说明：[docs/MIGRATION.md](./docs/MIGRATION.md)
- 机器人侧说明：[bot/README.md](./bot/README.md)
