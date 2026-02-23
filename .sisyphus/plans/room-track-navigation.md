# 房间切换语音 + 轨道导航计划

## TL;DR

> **快速摘要**: 添加 Ctrl+1-4 房间切换语音反馈，以及在 Rows/Sprites Tab 中使用上下箭头切换选中的轨道。
> 
> **交付物**:
> - 房间切换语音反馈（Harmony Patch `TabSection.ChangePage`）
> - 上下箭头切换轨道功能
> - 轨道选择语音反馈
> 
> **预估工作量**: Short（单个文件，约150行代码新增）
> **并行执行**: NO - 单文件顺序修改
> **关键路径**: 无

---

## Context

### 原始请求
用户希望：
1. 按下 Ctrl+1-4 时朗读切换到的房间
2. 在 Rows/Sprites Tab 中，上下箭头切换选中的轨道
3. 左右箭头在当前轨道的事件之间切换

### 访谈摘要
**用户决策**:
- 房间名称：使用游戏本地化系统 (`editor.room`)
- 轨道朗读：轨道索引 + 角色信息
- 上下箭头行为：总是切换轨道

### 研究发现

**房间切换机制**:
- `scnEditor.cs:1571-1590`: Ctrl+1-4 检测
- `TabSection.ChangePage(int index)`: 切换页面
- `TabSection.pageIndex`: 当前页面索引

**轨道选择机制**:
- `RowHeader.ShowPanel(int rowIndex)`: 选择 row
- `SpriteHeader.ShowPanel(string spriteId)`: 选择 sprite
- `scnEditor.currentPageRowsData`: 当前页面的 rows
- `scnEditor.currentPageSpritesData`: 当前页面的 sprites

**本地化**:
- 房间：`RDString.Get("editor.room")`
- 角色：`RDString.Get($"enum.Character.{character}.short")`

---

## Work Objectives

### Core Objective
为房间切换和轨道选择添加语音反馈和键盘导航支持。

### Concrete Deliverables
- Harmony Patch `TabSection.ChangePage` (房间切换语音)
- 上下箭头切换轨道逻辑
- 轨道选择语音反馈

### Definition of Done
- [x] Ctrl+1-4 切换房间时朗读房间名称
- [x] Rows Tab 上下箭头切换轨道
- [x] Sprites Tab 上下箭头切换精灵
- [x] 轨道切换时朗读轨道信息
- [x] 构建成功
- [x] 游戏内测试通过

### Must Have
- 房间切换语音反馈
- 上下箭头切换轨道
- 轨道选择语音反馈

### Must NOT Have (Guardrails)
- 不要修改游戏原生导航逻辑
- 不要影响左右箭头在事件间切换的功能
- 不要影响其他 Tab 的行为

---

## Verification Strategy (MANDATORY)

### Test Decision
- **Infrastructure exists**: NO（无测试框架）
- **Automated tests**: None
- **Agent-Executed QA**: 游戏内手动测试

---

## Execution Strategy

### Sequential Execution (Single File)

```
Task 1: 添加 TabSectionPatch (房间切换语音)
    ↓
Task 2: 添加轨道切换辅助方法
    ↓
Task 3: 添加 HandleTimelineNavigation 中的上下箭头处理
    ↓
Task 4: 构建验证
```

---

## TODOs

- [ ] 1. 添加 TabSectionPatch (房间切换语音)

  **What to do**:
  - 在 `EditorAccess.cs` 中添加 `TabSectionPatch` 类
  - Patch `TabSection.ChangePage` 方法
  - 当 tab 是 Rows 或 Sprites 时朗读房间名称

  **Must NOT do**:
  - 不要修改 `ChangePage` 方法的参数或返回值

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential
  - **Blocks**: Task 2
  - **Blocked By**: None

  **References**:
  - `agents references/Assembly-CSharp/RDLevelEditor/TabSection.cs:119-168` - ChangePage 方法
  - `RDLevelEditorAccess/EditorAccess.cs:554-570` - 现有 Patch 模式

  **Acceptance Criteria**:
  - [ ] Ctrl+1-4 切换房间时朗读房间名称

  **QA Scenarios**:
  ```
  Scenario: Rows Tab 房间切换
    Tool: N/A (游戏内测试)
    Steps:
      1. 切换到 Rows Tab
      2. 按 Ctrl+1
      3. 确认朗读 "房间 1"
      4. 按 Ctrl+2
      5. 确认朗读 "房间 2"
    Expected Result: 房间切换时正确朗读
  ```

  **Commit**: NO（与后续任务一起提交）

---

- [ ] 2. 添加轨道切换辅助方法

  **What to do**:
  - 添加 `SelectRowByIndex(int index)` 方法
  - 添加 `SelectSpriteByIndex(int index)` 方法
  - 添加 `GetRowCharacterName(LevelEvent_MakeRow)` 方法
  - 添加 `GetSpriteDisplayName(LevelEvent_MakeSprite)` 方法
  - 添加 `GetCurrentRowIndexInPage()` 方法

  **Must NOT do**:
  - 不要直接修改 `selectedRowIndex`，使用 `RowHeader.ShowPanel`

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential
  - **Blocks**: Task 3
  - **Blocked By**: Task 1

  **References**:
  - `agents references/Assembly-CSharp/RDLevelEditor/RowHeader.cs:77-90` - ShowPanel 方法
  - `agents references/Assembly-CSharp/RDLevelEditor/SpriteHeader.cs:49-66` - ShowPanel 方法
  - `agents references/Assembly-CSharp/RDLevelEditor/SpriteHeader.cs:77` - 角色名本地化

  **Acceptance Criteria**:
  - [ ] SelectRowByIndex 能正确选择轨道并朗读
  - [ ] SelectSpriteByIndex 能正确选择精灵并朗读
  - [ ] 边界情况处理（无效索引）

  **QA Scenarios**:
  ```
  Scenario: 边界情况 - 第一条轨道
    Steps:
      1. 选择第一条轨道
      2. 按上箭头
    Expected Result: 不切换，保持在第一条轨道
  ```

  **Commit**: NO（与后续任务一起提交）

---

- [ ] 3. 添加 HandleTimelineNavigation 中的上下箭头处理

  **What to do**:
  - 在 `HandleTimelineNavigation()` 中添加上下箭头检测
  - 仅在 Rows 或 Sprites Tab 时处理
  - 调用辅助方法切换轨道

  **Must NOT do**:
  - 不要在 Song/Actions/Rooms/Windows Tab 中处理上下箭头
  - 不要干扰左右箭头的事件导航

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential
  - **Blocks**: Task 4
  - **Blocked By**: Task 2

  **References**:
  - `RDLevelEditorAccess/EditorAccess.cs:379-404` - 现有 HandleTimelineNavigation

  **Acceptance Criteria**:
  - [ ] Rows Tab 中上下箭头切换轨道
  - [ ] Sprites Tab 中上下箭头切换精灵
  - [ ] 不影响其他 Tab 的行为
  - [ ] 不影响左右箭头的事件导航

  **Commit**: NO（与 Task 4 一起提交）

---

- [ ] 4. 构建验证

  **What to do**:
  - 执行完整构建
  - 生成测试指南

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential
  - **Blocks**: None
  - **Blocked By**: Task 3

  **Acceptance Criteria**:
  - [ ] 构建成功无错误

  **Commit**: YES
  - Message: `添加房间切换语音反馈和轨道键盘导航`
  - Files: `RDLevelEditorAccess/EditorAccess.cs`

---

## Final Verification Wave (MANDATORY)

- [ ] F1. **Plan Compliance Audit** — 验证所有 Must Have 已实现
- [ ] F2. **Code Quality Review** — 构建验证
- [ ] F3. **Real Manual QA** — 游戏内测试
- [ ] F4. **Scope Fidelity Check** — 确认只修改了 EditorAccess.cs

---

## Success Criteria

### Verification Commands
```bash
dotnet build RDMods.sln  # Expected: Build succeeded
```

### Final Checklist
- [ ] 所有 "Must Have" 已实现
- [ ] 所有 "Must NOT Have" 未违反
- [ ] 构建成功
- [ ] 游戏内测试通过
