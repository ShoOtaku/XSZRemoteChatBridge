# API 手册

## 概述
本项目主要消费机器人桥接 API；当前仓库不提供独立对外 HTTP 服务。

## 认证方式
请求头签名鉴权：
- `X-Bridge-Key`
- `X-Bridge-Timestamp`
- `X-Bridge-Signature`
- `X-Bridge-Nonce`

---

## 接口列表

### bridge

#### `POST /ff14/bridge/ingest`
**描述:** 上行推送游戏聊天到机器人。

#### `POST /ff14/bridge/pull`
**描述:** 下行拉取待执行消息（WS 异常时回退）。

#### `WS /ff14/bridge/ws`
**描述:** 下行实时推送与 ACK。

