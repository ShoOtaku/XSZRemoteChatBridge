# 项目技术约定

---

## 技术栈
- **核心:** C# / .NET 10（`net10.0-windows`）/ Dalamud API 14
- **构建:** `dotnet build`（Windows 目标）
- **发布目标:** GitHub Release + `ShoOtaku/DalamudPlugins` 仓库分发

---

## 开发约定
- **代码规范:** 启用 `Nullable` 与 `ImplicitUsings`，保持现有命名与日志风格。
- **命名约定:** C# 类型 PascalCase，私有字段 `_camelCase`。
- **CI 约定:** 主分支推送触发自动构建，发布产物统一为 `latest.zip`。

---

## 错误与日志
- **策略:** 网络与序列化异常本地捕获并记录，避免影响主线程。
- **日志:** 使用 Dalamud 日志服务输出 `Information/Warning/Debug`。

---

## 测试与流程
- **测试:** 以构建成功与基本产物检查为最小准入。
- **提交:** 变更涉及发布链路时必须同步更新知识库与方案记录。

