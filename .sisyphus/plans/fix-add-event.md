# 修复添加事件功能计划

## TL;DR

> **快速摘要**: 修复快捷键冲突，简化添加事件流程为"选择类型 → 创建 → 自动打开 Helper 编辑"。
> 
> **交付物**:
> - 更换快捷键：Ctrl+Alt+R（轨道）/ Ctrl+Alt+S（精灵）
> - 简化添加事件：移除位置输入，用默认值创建后自动打开 Helper
> 
> **预估工作量**: Quick（单文件，约50行代码修改）
> **并行执行**: NO

---

## Context

### 发现的问题

| 问题 | 描述 |
|------|------|
| **快捷键冲突** | Ctrl+Shift+N 与原生"新建关卡"冲突 |
| **数字键冲突** | 按 1-6 会切换 Tab |
| **无法删除** | 已输入数字无法删除 |
| **多位数字** | 无法输入如 "22" 的数字 |
| **Tab 切换体验** | 切换字段体验不佳 |

### 解决方案（参考 Helper）

**Helper 工作方式**:
1. Mod 通过 `AccessibilityBridge.EditEvent(levelEvent)` 启动 Helper
2. Helper 显示 WinForms 属性编辑器
3. 编辑完成后 Helper 写入结果文件
4. Mod 读取结果并应用更改

**简化流程**:
```
原: Insert → 选择类型 → 输入 bar/beat/row → 创建
新: Insert → 选择类型 → 创建（默认值）→ 自动打开 Helper
```

---

## Work Objectives

### Core Objective
修复快捷键冲突，简化添加事件流程。

### Definition of Done
- [x] Ctrl+Alt+R 添加轨道（无冲突）
- [x] Ctrl+Alt+S 添加精灵（无冲突）
- [x] Insert → 选择类型 → 自动创建 + 打开 Helper
- [x] 构建成功
- [x] 游戏内测试通过

### Must Have
- 新快捷键不与原生冲突
- 添加事件后自动打开 Helper 编辑

### Must NOT Have
- 不覆盖编辑器原有快捷键
- 不修改游戏原生 UI

---

## Execution Strategy

### Sequential Execution

```
Task 1: 更换快捷键（Ctrl+Shift+N/M → Ctrl+Alt+R/S）
    ↓
Task 2: 移除位置输入对话框逻辑
    ↓
Task 3: 修改创建事件流程（默认值 + 自动打开 Helper）
    ↓
Task 4: 构建验证
```

---

## TODOs

- [ ] 1. 更换快捷键

  **What to do**:
  - Ctrl+Shift+N → **Ctrl+Alt+R**（添加轨道）
  - Ctrl+Shift+M → **Ctrl+Alt+S**（添加精灵）
  - 检测方式：`Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)`

  **References**:
  - `EditorAccess.cs:443-471` - 现有快捷键检测

  **Commit**: NO

---

- [ ] 2. 移除位置输入对话框逻辑

  **What to do**:
  - 移除 `VirtualMenuState.PositionInput` 相关代码
  - 移除 `StartPositionInput()` 方法
  - 移除 `HandlePositionInput()` 方法
  - 移除相关状态变量

  **References**:
  - `EditorAccess.cs:711-817` - 位置输入相关代码

  **Commit**: NO

---

- [ ] 3. 修改创建事件流程

  **What to do**:
  - 事件类型选择后直接创建事件（使用默认值）
  - 创建后自动调用 `AccessibilityBridge.EditEvent(levelEvent)`
  - 默认值：
    - bar = `editor.startBar + 1`
    - beat = `1f`
    - row = `editor.selectedRowIndex >= 0 ? editor.selectedRowIndex : 0`

  **References**:
  - `EditorAccess.cs:885-920` - CreateEvent 方法
  - `AccessibilityModule.cs:35-50` - EditEvent API

  **Commit**: NO

---

- [ ] 4. 构建验证

  **What to do**:
  - 执行完整构建
  - 生成测试指南

  **Commit**: YES
  - Message: `修复添加事件功能：更换快捷键并简化流程`
  - Files: `RDLevelEditorAccess/EditorAccess.cs`

---

## Success Criteria

### 测试场景

1. **添加轨道**:
   - 按 **Ctrl+Alt+R**
   - 选择角色
   - 确认轨道已添加

2. **添加事件**:
   - 按 **Insert**
   - 选择事件类型
   - 确认事件已创建 + Helper 自动打开
