# XSZRemoteChatBridge

> 本文件包含项目级别的核心信息。详细模块文档见 `modules/` 目录。

---

## 1. 项目概述

### 目标与背景
为 FF14 提供远程聊天桥接能力，支持游戏聊天上行到机器人，并将机器人消息下行回游戏端。

### 范围
- **范围内:** 插件构建、桥接协议、插件发布与分发自动化。
- **范围外:** 机器人服务端业务逻辑实现。

### 干系人
- **负责人:** 仓库维护者（XSZYYS/ShoOtaku）

---

## 2. 模块索引

| 模块名称 | 职责 | 状态 | 文档 |
|---------|------|------|------|
| plugin | Dalamud 插件核心实现与配置 UI | ✅稳定 | [plugin](modules/plugin.md) |
| bot | 机器人侧对接说明与接口约定 | ✅稳定 | [bot](modules/bot.md) |
| docs | 迁移与部署说明文档 | ✅稳定 | [docs](modules/docs.md) |

---

## 3. 快速链接
- [技术约定](../project.md)
- [架构设计](arch.md)
- [API 手册](api.md)
- [数据模型](data.md)
- [变更历史](../history/index.md)

