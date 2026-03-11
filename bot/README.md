# Bot 对接说明

机器人插件已独立维护在：

- 仓库：`https://github.com/ShoOtaku/nonebot-plugin-ff14bot-bridge`
- Python 包：`nonebot_plugin_ff14bot_bridge`

主要接口（与本插件配套）：

- `POST /ff14/bridge/ingest`
- `POST /ff14/bridge/pull`
- `WS /ff14/bridge/ws`

对接建议：

1. 优先启用 WS 下行。
2. 保留 Pull 回退链路。
3. `FF14_BRIDGE_PUBLIC_ENDPOINT` 配置为外网 HTTPS 地址（ingest）。
