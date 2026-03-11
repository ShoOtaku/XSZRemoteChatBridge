# plugin

## 目的
提供 FF14 与机器人之间的双向聊天桥接插件能力。

## 模块概述
- **职责:** 监听游戏聊天、执行关键词与频道过滤、上行发送、下行执行与配置管理。
- **状态:** ✅稳定
- **最后更新:** 2026-03-11

## 规范

### 需求: 自动化发布与分发
**模块:** plugin
保证插件产物可被持续构建并可被 Dalamud 插件源消费。

#### 场景: 主分支推送后自动发布
触发条件：主分支出现新提交。
- 自动完成编译并生成可分发压缩包。
- 分发仓库中的 `pluginmaster.json` 与插件包地址保持同步。

## API 接口
- `POST /ff14/bridge/ingest`
- `POST /ff14/bridge/pull`
- `WS /ff14/bridge/ws`

## 依赖
- Dalamud API
- .NET SDK
- 机器人桥接服务

## 变更历史
- [202603111630_main_push_release_sync](../../history/2026-03/202603111630_main_push_release_sync/) - 新增主分支自动发布与跨仓库同步 pluginmaster 流水线
