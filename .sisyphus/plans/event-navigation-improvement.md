# 事件导航改进计划

## TL;DR

> **快速摘要**: 修复 `chooseNearestEvent()` 方法，使其根据当前 Tab 使用正确的事件列表，解决特定 Tab（Rows、Sprites）事件无法浏览的问题。
> 
> **交付物**:
> - 修改后的 `EditorAccess.cs` 中的 `chooseNearestEvent()` 方法
> - 新增辅助方法：`GetEventListForCurrentTab()`、`GetSelectedRowList()`、`GetSelectedSpriteList()`、`FindNearestToViewCenter()`
> 
> **预估工作量**: Short（单个文件，约100行代码修改）
> **并行执行**: NO - 单文件顺序修改
> **关键路径**: 无

---

## Context

### 原始请求
用户报告"有些事件浏览不到"，经过调查发现是 Mod 使用了错误的事件列表。

### 访谈摘要
**关键讨论**:
- 游戏原生编辑器已有方向键导航支持
- Mod 只需在没有选中事件时帮忙选中一个最近的事件
- 不同 Tab 使用不同的事件列表（Rows/Sprites 是嵌套列表）
- 用户决策：只选当前选中的 row/sprite，优先当前小节

**研究发现**:
- `container` 属性：不同 Tab 对应不同事件列表
- `selectedRowIndex`、`selectedSprite`：获取当前选中 row/sprite
- `EventIsVisible()`：Actions Tab 需要额外过滤

### Metis 审查
（咨询超时，但研究充分）

---

## Work Objectives

### Core Objective
修复事件导航，使所有 Tab 的事件都能被正确选择。

### Concrete Deliverables
- 修改 `EditorAccess.cs` 中的 `chooseNearestEvent()` 方法
- 新增 4 个辅助方法

### Definition of Done
- [x] 所有 6 个 Tab 都能正确选择事件
- [x] 当前小节无事件时能选择其他小节的事件
- [x] 构建成功：`dotnet build RDMods.sln` 无错误
- [x] 游戏内测试通过

### Must Have
- 使用正确的事件列表（根据 Tab 类型）
- Rows Tab 使用当前选中 row 的事件列表
- Sprites Tab 使用当前选中 sprite 的事件列表
- 优先选择当前小节的事件

### Must NOT Have (Guardrails)
- 不要修改原生导航逻辑
- 不要添加新的键盘快捷键
- 不要修改事件选择的行为（只改选择哪个事件的逻辑）

---

## Verification Strategy (MANDATORY)

> **零人工干预** — 所有验证由代理执行。

### Test Decision
- **Infrastructure exists**: NO（无测试框架）
- **Automated tests**: None
- **Framework**: none
- **Agent-Executed QA**: 游戏内手动测试（代理指导用户）

### QA Policy
每个任务必须包含代理执行的 QA 场景。

---

## Execution Strategy

### Sequential Execution (Single File)

```
Task 1: 重写 chooseNearestEvent() 方法核心逻辑
    ↓
Task 2: 添加辅助方法
    ↓
Task 3: 构建验证
```

### Dependency Matrix

- **1**: — 2
- **2**: 1 — 3
- **3**: 2 —

### Agent Dispatch Summary

- **1**: `quick` - 核心方法重写
- **2**: `quick` - 辅助方法添加
- **3**: `quick` - 构建验证

---

## TODOs

- [ ] 1. 重写 chooseNearestEvent() 方法核心逻辑

  **What to do**:
  - 修改 `EditorAccess.cs` 中的 `chooseNearestEvent()` 方法
  - 根据 `currentTab` 获取正确的事件列表
  - 过滤 isBase 事件，按 sortOrder 排序
  - 优先选择当前小节事件，回退到最近事件

  **Must NOT do**:
  - 不要修改方法签名
  - 不要添加新的按键检测

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 单方法修改，逻辑清晰
  - **Skills**: []（无特殊技能需求）

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential
  - **Blocks**: Task 2
  - **Blocked By**: None

  **References**:

  **Pattern References**:
  - `agents references/Assembly-CSharp/RDLevelEditor/scnEditor.cs:3474-3516` - GetControlToTheLeft/Right 的排序和过滤逻辑
  - `agents references/Assembly-CSharp/RDLevelEditor/LevelEventControl_Base.cs:117-134` - container 属性映射

  **API/Type References**:
  - `agents references/Assembly-CSharp/RDLevelEditor/scnEditor.cs:346-349` - selectedRowIndex, selectedSprite
  - `agents references/Assembly-CSharp/RDLevelEditor/scnEditor.cs:4166-4197` - EventIsVisible 方法

  **WHY Each Reference Matters**:
  - GetControlToTheLeft 演示了如何排序和过滤事件
  - container 属性展示了 Tab 到事件列表的映射关系
  - selectedRowIndex/selectedSprite 是获取当前选中 row/sprite 的关键

  **Acceptance Criteria**:
  - [ ] 方法能根据 Tab 类型获取正确的事件列表
  - [ ] 方法按 sortOrder 排序事件
  - [ ] 方法优先选择当前小节事件

  **QA Scenarios**:

  ```
  Scenario: Song Tab 事件选择
    Tool: Bash (构建验证)
    Preconditions: 代码已修改
    Steps:
      1. dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj
    Expected Result: Build succeeded
    Evidence: .sisyphus/evidence/task-1-build.txt

  Scenario: Rows Tab 事件选择逻辑
    Tool: Bash (构建验证)
    Preconditions: 代码已修改
    Steps:
      1. 检查代码中是否引用了 eventControls_rows
      2. 检查代码中是否使用了 selectedRowIndex
    Expected Result: 代码正确引用嵌套列表
    Evidence: .sisyphus/evidence/task-1-rows-logic.txt
  ```

  **Commit**: NO（与 Task 2 一起提交）

---

- [ ] 2. 添加辅助方法

  **What to do**:
  - 添加 `GetEventListForCurrentTab(scnEditor editor)` 方法
  - 添加 `GetSelectedRowList(scnEditor editor)` 方法
  - 添加 `GetSelectedSpriteList(scnEditor editor)` 方法
  - 添加 `FindNearestToViewCenter(List<LevelEventControl_Base>, scnEditor)` 方法

  **Must NOT do**:
  - 不要修改现有 Harmony Patch
  - 不要添加新的 public 方法

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: 简单辅助方法添加
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential
  - **Blocks**: Task 3
  - **Blocked By**: Task 1

  **References**:

  **Pattern References**:
  - `agents references/Assembly-CSharp/RDLevelEditor/LevelEventControl_Base.cs:117-134` - container 属性模式

  **API/Type References**:
  - `agents references/Assembly-CSharp/RDLevelEditor/scnEditor.cs:313-331` - 所有事件列表定义

  **Acceptance Criteria**:
  - [ ] GetEventListForCurrentTab 能正确映射 6 个 Tab
  - [ ] GetSelectedRowList 正确处理边界情况（无效索引）
  - [ ] GetSelectedSpriteList 正确查找 sprite 对应索引
  - [ ] FindNearestToViewCenter 返回最近事件

  **QA Scenarios**:

  ```
  Scenario: 构建验证
    Tool: Bash
    Preconditions: 所有方法已添加
    Steps:
      1. dotnet build RDMods.sln
    Expected Result: Build succeeded, 0 errors
    Evidence: .sisyphus/evidence/task-2-build.txt

  Scenario: 边界情况 - selectedRowIndex 为 -1
    Tool: Bash (代码检查)
    Preconditions: 代码已修改
    Steps:
      1. 检查 GetSelectedRowList 方法中是否有 rowIndex < 0 的检查
    Expected Result: 代码正确处理无效索引，返回 null 而不崩溃
    Evidence: .sisyphus/evidence/task-2-edge-row.txt

  Scenario: 边界情况 - spritesData 为空
    Tool: Bash (代码检查)
    Preconditions: 代码已修改
    Steps:
      1. 检查 GetSelectedSpriteList 方法中是否有空列表检查
    Expected Result: 代码正确处理空列表，返回 null
    Evidence: .sisyphus/evidence/task-2-edge-sprite.txt

  Scenario: 边界情况 - 事件列表为空
    Tool: Bash (代码检查)
    Preconditions: 代码已修改
    Steps:
      1. 检查 chooseNearestEvent 方法中是否有 targetList.Count == 0 的检查
    Expected Result: 代码正确处理空事件列表，朗读"无可用事件"
    Evidence: .sisyphus/evidence/task-2-edge-empty.txt
  ```

  **Commit**: YES（与 Task 1 一起）
  - Message: `修复事件导航：使用正确的 Tab 事件列表`
  - Files: `RDLevelEditorAccess/EditorAccess.cs`
  - Pre-commit: `dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj`

---

- [ ] 3. 构建验证和游戏内测试

  **What to do**:
  - 执行完整构建
  - 生成测试指南供用户验证

  **Must NOT do**:
  - 不要跳过构建验证

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Sequential
  - **Blocks**: None
  - **Blocked By**: Task 2

  **References**:

  **External References**:
  - AGENTS.md 构建命令: `dotnet build RDMods.sln`

  **Acceptance Criteria**:
  - [ ] 构建成功无错误
  - [ ] DLL 正确部署到游戏目录

  **QA Scenarios**:

  ```
  Scenario: 完整构建
    Tool: Bash
    Preconditions: 所有代码已修改
    Steps:
      1. dotnet build RDMods.sln -c Debug
    Expected Result: Build succeeded
    Evidence: .sisyphus/evidence/task-3-full-build.txt

  Scenario: 游戏内测试指南
    Tool: N/A（用户手动测试）
    Preconditions: Mod 已部署
    Steps:
      1. 启动游戏，打开关卡编辑器
      2. 切换到 Song Tab，按方向键，确认事件被选中
      3. 切换到 Rows Tab，选择一个 row，按方向键，确认事件被选中
      4. 切换到 Actions Tab，按方向键，确认事件被选中
      5. 切换到 Rooms Tab，按方向键，确认事件被选中
      6. 切换到 Sprites Tab，选择一个 sprite，按方向键，确认事件被选中
      7. 切换到 Windows Tab，按方向键，确认事件被选中
    Expected Result: 所有 Tab 都能正确选择事件
    Evidence: .sisyphus/evidence/task-3-game-test.txt（用户记录）
  ```

  **Commit**: NO（已在 Task 2 提交）

---

## Final Verification Wave (MANDATORY)

- [ ] F1. **Plan Compliance Audit** — `oracle`
  验证所有 Must Have 已实现，Must NOT Have 未违反。

- [ ] F2. **Code Quality Review** — `unspecified-high`
  运行 `dotnet build`，检查代码风格一致性。

- [ ] F3. **Real Manual QA** — `unspecified-high`
  用户在游戏内测试所有 6 个 Tab。

- [ ] F4. **Scope Fidelity Check** — `deep`
  确认只修改了 chooseNearestEvent 相关代码。

---

## Commit Strategy

- **1**: `修复事件导航：使用正确的 Tab 事件列表` — `RDLevelEditorAccess/EditorAccess.cs`

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
- [ ] 游戏内测试通过（所有 6 个 Tab）
