# 添加事件和轨道功能计划

## TL;DR

> **快速摘要**: 添加键盘快捷键支持创建轨道、精灵和事件，包括事件类型选择菜单和位置输入对话框。
> 
> **交付物**:
> - Ctrl+Shift+N: 添加轨道（带角色选择）
> - Ctrl+Shift+M: 添加精灵（带角色选择）
> - Insert: 打开添加事件菜单
> - 事件类型选择菜单（方向键 + 首字母导航）
> - 位置输入对话框（bar, beat, row）
> 
> **预估工作量**: Medium（单文件，约300行代码新增）
> **并行执行**: NO - 单文件顺序修改

---

## Context

### 原始请求
用户希望 Mod 支持添加事件和轨道等功能。

### 访谈摘要
**用户决策**:
- 添加轨道：快捷键 + 角色选择菜单
- 添加精灵：快捷键 + 角色选择菜单
- 添加事件：快捷键呼出菜单 + 选择事件类型 + 输入位置
- 事件菜单导航：方向键 + 首字母跳转
- 默认值：bar=当前视图, row=当前轨道, beat=1

**重要提示**: 虚拟 UI 交互时设置 `userIsEditingAnInputField = true` 防止快捷键冲突

### 研究发现

**添加轨道**: `scnEditor.instance.AddNewRow(LevelEvent_MakeRow)`
**添加精灵**: `scnEditor.instance.AddNewSprite(LevelEvent_MakeSprite)`
**添加事件**: `scnEditor.instance.CreateEventControl(LevelEvent_Base, Tab)`

**事件类型列表**: `RDEditorConstants.levelEventTabs[Tab]`

---

## Work Objectives

### Core Objective
为视障用户提供完整的关卡编辑能力，包括创建轨道、精灵和事件。

### Definition of Done
- [ ] Ctrl+Shift+N 添加轨道（带角色选择）
- [ ] Ctrl+Shift+M 添加精灵（带角色选择）
- [ ] Insert 打开添加事件菜单
- [ ] 事件类型菜单支持方向键和首字母导航
- [ ] 位置输入对话框支持 bar/beat/row 输入
- [x] 构建成功
- [ ] 游戏内测试通过

### 发现的问题（待修复）

1. **快捷键冲突**: Ctrl+Shift+N 与原生编辑器新建关卡冲突
   - 需要更换快捷键

2. **位置输入体验不佳**:
   - 数字键与 Tab 切换快捷键冲突（按 1-6 会切换 Tab）
   - Tab 切换字段体验不好
   - 无法删除已输入的数字
   - 无法输入多位数字（如第 22 小节）
   - **建议方案**: 先用默认值创建事件，再用外部编辑器编辑

### Must Have
- 添加轨道功能
- 添加精灵功能
- 添加事件功能
- 事件类型菜单
- 位置输入对话框

### Must NOT Have (Guardrails)
- 不覆盖编辑器原有快捷键
- 不修改游戏原生 UI
- 不影响其他功能

---

## Execution Strategy

### Sequential Execution

```
Task 1: 添加快捷键检测框架
    ↓
Task 2: 实现添加轨道/精灵功能（含角色选择）
    ↓
Task 3: 实现事件类型选择菜单
    ↓
Task 4: 实现位置输入对话框
    ↓
Task 5: 整合添加事件流程
    ↓
Task 6: 构建验证
```

---

## TODOs

- [ ] 1. 添加快捷键检测框架

  **What to do**:
  - 在 `HandleTimelineNavigation()` 中添加快捷键检测
  - Ctrl+Shift+N: 添加轨道
  - Ctrl+Shift+M: 添加精灵
  - Insert: 打开添加事件菜单

  **References**:
  - `EditorAccess.cs:379-407` - 现有 HandleTimelineNavigation

  **Acceptance Criteria**:
  - [ ] 快捷键正确检测
  - [ ] 不与原生快捷键冲突

  **Commit**: NO

---

- [ ] 2. 实现添加轨道/精灵功能（含角色选择）

  **What to do**:
  - 创建 `HandleAddRow()` 方法
  - 创建 `HandleAddSprite()` 方法
  - 实现角色选择菜单（复用现有 UI 导航模式）
  - 设置 `userIsEditingAnInputField = true` 防止冲突

  **References**:
  - `scnEditor.cs:3174-3234` - AddNewRow/AddNewSprite
  - `EditorAccess.cs:155-321` - 现有 UI 导航模式

  **Acceptance Criteria**:
  - [ ] 角色选择菜单正常工作
  - [ ] 轨道/精灵正确添加
  - [ ] 朗读确认信息

  **Commit**: NO

---

- [ ] 3. 实现事件类型选择菜单

  **What to do**:
  - 创建 `ShowEventTypeMenu()` 方法
  - 从 `RDEditorConstants.levelEventTabs[currentTab]` 获取事件列表
  - 支持方向键导航
  - 支持首字母跳转
  - 朗读事件名称（使用本地化）

  **References**:
  - `RDEditorConstants.cs:111-115` - levelEventTabs 定义
  - `EditorAccess.cs:155-321` - 现有 UI 导航模式

  **Acceptance Criteria**:
  - [ ] 显示当前 Tab 可用的事件类型
  - [ ] 方向键导航正常
  - [ ] 首字母跳转正常
  - [ ] 朗读事件名称

  **Commit**: NO

---

- [ ] 4. 实现位置输入对话框

  **What to do**:
  - 创建 `ShowPositionInputDialog()` 方法
  - 输入 bar（默认当前视图中心）
  - 输入 beat（默认 1）
  - 输入 row（默认当前轨道，仅 Rows Tab）
  - 数字键输入 + 方向键调整

  **References**:
  - `EditorAccess.cs:155-321` - 现有 UI 导航模式

  **Acceptance Criteria**:
  - [ ] bar/beat/row 输入正常
  - [ ] 默认值正确
  - [ ] 朗读当前输入值

  **Commit**: NO

---

- [ ] 5. 整合添加事件流程

  **What to do**:
  - 创建 `HandleAddEvent()` 方法
  - 整合事件类型选择 + 位置输入
  - 创建事件并添加到编辑器

  **References**:
  - `scnEditor.cs:2046-2110` - CreateEventControl
  - `scnEditor.cs:2320-2365` - 事件创建示例

  **Acceptance Criteria**:
  - [ ] 完整流程正常工作
  - [ ] 事件正确创建

  **Commit**: NO

---

- [ ] 6. 构建验证

  **What to do**:
  - 执行完整构建
  - 生成测试指南

  **Commit**: YES
  - Message: `添加事件和轨道创建功能`
  - Files: `RDLevelEditorAccess/EditorAccess.cs`

---

## Final Verification Wave

- [ ] F1. Plan Compliance Audit
- [ ] F2. Code Quality Review
- [ ] F3. Real Manual QA
- [ ] F4. Scope Fidelity Check

---

## Success Criteria

### 测试场景

1. **添加轨道**:
   - 按 Ctrl+Shift+N
   - 选择角色
   - 确认轨道已添加

2. **添加事件**:
   - 按 Insert
   - 选择事件类型
   - 输入位置
   - 确认事件已添加
