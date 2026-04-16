# XSZRemoteChatBridge

FF14 远程聊天桥接独立卫月插件仓库。  
支持游戏聊天与掉线提醒推送到机器人 / Server酱，以及机器人下行到游戏（优先 WebSocket，失败回退 Pull）。
纯Vibe产物

## 功能概览

- 上行：监听指定聊天频道与关键词，支持“每个关键词独立配置频道”，并可推送到机器人和 / 或 Server酱
- 掉线提醒：检测到连接中断弹窗时，可推送到机器人和 / 或 Server酱
- 下行：
  - 首选 `WS /ff14/bridge/ws`
  - 失败自动回退 `POST /ff14/bridge/pull`
- 签名鉴权：`X-Bridge-Key / Timestamp / Signature / Nonce`
- UI 设置：
  - 基础/高级分页
  - 自动保存与自动应用（防抖）
  - 关键词普通匹配/正则匹配
  - 关键词-频道独立映射（每个关键词可单独勾选频道）
  - 关键词管理双栏界面（左侧关键词列表 + 右侧频道配置）
  - 频道中文名显示（可搜索）
  - 支持手动添加自定义频道 ID
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
2. 在“基础开关”中按需启用：
   - `启用上行消息推送`
   - `启用机器人推送目标`
   - `启用 Server酱 推送目标`
   - `掉线提醒（检测到连接中断弹窗时推送到已启用目标）`
3. 若启用机器人推送目标，填写：
   - `IngestEndpoint`
   - `PullEndpoint`
   - `WebSocketEndpoint`
   - `BridgeKey`
   - `BridgeSecret`
4. 若启用 Server酱 推送目标，填写官方 `Server酱 Send URL`
   - 格式示例：`https://<uid>.push.ft07.com/send/<sendkey>.send`
5. 根据需要设置关键词规则，并为每个关键词单独勾选频道（支持正则）。
6. 保存为自动生效，无需手动点击“保存并应用”。

说明：
- 机器人与 Server酱 是两个独立推送目标，可以只开其中一个。
- 即使未配置机器人，只要 Server酱 配置完整，聊天上行和掉线提醒仍可正常推送。


## 机器人仓库

机器人插件已拆分并同步到：  
`https://github.com/ShoOtaku/nonebot-plugin-ff14bot-bridge`

## 相关文档

- 迁移说明：[docs/MIGRATION.md](./docs/MIGRATION.md)
- 机器人侧说明：[bot/README.md](./bot/README.md)
