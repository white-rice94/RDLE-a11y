# 时间轴键盘导航功能计划

## TL;DR

> **快速总结**：为 Rhythm Doctor 关卡编辑器添加键盘时间轴导航功能，支持翻页、按小节/按拍跳转、缩放，并带有详细的语音位置反馈。

> **交付物**：
> - Mod 端时间轴导航逻辑（EditorAccess.cs）
> - 语音位置反馈（使用 Narration.Say）
> - 快捷键绑定（J/K 翻页、[/] 缩放、Alt+←→ 按小节跳转）

> **预计工作量**：Short（2-3 小时）
> **并行执行**：YES - 2 waves

---

## Context

### 原始需求
用户希望实现用键盘进行时间轴导航，善用组合键+光标键之类的不容易发生冲突的快捷键。

### 研究发现

**Timeline.cs 关键 API：**
- `ScrollTo(float x, float duration)` - 滚动到指定 X 坐标
- `CenterOnPosition(float x, float duration)` - 居中到指定位置
- `PreviousPage()` / `NextPage()` - 前后翻页
- `ZoomIn()` / `ZoomOut()` - 水平缩放
- `GetPosXFromBarAndBeat(BarAndBeat position)` - Bar/Beat 转 X 坐标

**scnEditor.cs 关键字段/方法：**
- `startBar` - 当前起始小节
- `timelineScript` - Timeline 引用
- `ScrubToBar(int bar)` - 跳转到指定小节并擦唱

**事件定位字段：**
- `LevelEvent_Base.bar` - 小节
- `LevelEvent_Base.beat` - 拍
- `LevelEvent_Base.y` - 垂直位置

### Metis 审查发现

**已识别的问题（已解决）：**
- 需要明确边界处理逻辑 → 添加边界检查和语音提示
- 需要定义语音反馈格式 → 使用"X小节 Y拍"格式

**边界设置：**
- 仅实现基础功能（翻页、跳转、缩放），不包含事件选择
- 不修改关卡数据，仅导航
- 仅在时间轴 Tab 激活时生效

---

## Work Objectives

### 核心目标
为视障关卡制作者提供完整的键盘时间轴导航能力，无需使用鼠标即可在时间轴上快速移动和查看。

### 具体交付物
1. **翻页导航**：J（向前）/ K（向后）翻页
2. **小节跳转**：Alt+←/→ 按小节跳转
3. **精细跳转**：Ctrl+←/→ 按拍跳转（可选）
4. **缩放控制**：[/] 放大/缩小时间轴
5. **归位功能**：Home 跳到时间轴开始
6. **语音反馈**：每次导航朗读当前位置（格式："X小节 Y拍"）

### 完成定义
- [ ] J/K 翻页功能正常，语音反馈正确
- [ ] Alt+←/→ 按小节跳转，语音反馈正确
- [ ] [/] 缩放功能正常
- [ ] Home 跳转功能正常
- [ ] 边界情况（第一小节、最后一小节）有语音提示
- [ ] 构建成功，无编译错误

### 必须有
- 所有导航操作带语音反馈
- 边界检查防止越界
- 与现有功能不冲突

### 禁止有
- 不添加事件选择功能
- 不修改关卡数据
- 不使用已占用的快捷键（Ctrl+N/O/S/Enter/Insert 等）

---

## Verification Strategy

### 测试决策
- **基础设施存在**：NO（项目无单元测试）
- **自动化测试**：NO（需要手动测试）
- **Agent-Executed QA**：ALWAYS（每次任务后执行验证）

### QA 政策
每个任务必须包含 agent-executed QA 场景。验证方式：在游戏中测试快捷键是否生效，语音是否正确播放。

---

## Execution Strategy

### 并行执行 Waves

```
Wave 1 (立即开始 - 基础):
├── Task 1: 添加时间轴导航处理方法到 HandleTimelineNavigation() [quick]
├── Task 2: 实现翻页功能 (J/K) + 语音反馈 [quick]
├── Task 3: 实现缩放功能 ([/]) + 语音反馈 [quick]
└── Task 4: 实现 Home 跳转功能 + 语音反馈 [quick]

Wave 2 (Wave 1 完成后 - 增强):
├── Task 5: 实现 Alt+←/→ 按小节跳转 + 语音反馈 [quick]
├── Task 6: 实现边界检查和边界提示 [quick]
└── Task 7: 构建验证 [quick]
```

### 依赖关系
- Task 1 → Task 2, 3, 4（基础方法）
- Task 2, 3, 4 → Task 5, 6（功能增强）
- Task 5, 6 → Task 7（最终验证）

---

## TODOs

- [ ] 1. 添加时间轴导航处理方法

  **What to do**:
  - 在 `HandleTimelineNavigation()` 中添加时间轴导航的检测逻辑
  - 检测 J/K、[/]、Home、Alt+←/→、Ctrl+←/→ 按键
  - 调用对应的 Timeline 方法

  **Must NOT do**:
  - 不修改现有轨道/事件导航逻辑

  **References**:
  - `RDLevelEditorAccess/EditorAccess.cs:394` - HandleTimelineNavigation() 方法位置
  - `agents references/Assembly-CSharp/RDLevelEditor/Timeline.cs` - ScrollTo, CenterOnPosition, PreviousPage, NextPage, ZoomIn, ZoomOut

- [ ] 2. 实现翻页功能 (J/K) + 语音反馈

  **What to do**:
  - 按 J 调用 `timelineScript.PreviousPage()`
  - 按 K 调用 `timelineScript.NextPage()`
  - 语音反馈："X小节 Y拍，向前/向后翻页"（如："5小节 2拍，向前翻页"）

  **References**:
  - `Timeline.cs` - PreviousPage(), NextPage() 方法
  - `EditorAccess.cs` - Narration.Say() 使用示例

- [ ] 3. 实现缩放功能 ([/]) + 语音反馈

  **What to do**:
  - 按 [ 调用 `timelineScript.ZoomOut()`
  - 按 ] 调用 `timelineScript.ZoomIn()`
  - 语音反馈："缩小" / "放大"

  **References**:
  - `Timeline.cs` - ZoomIn(), ZoomOut() 方法

- [ ] 4. 实现 Home 跳转功能 + 语音反馈

  **What to do**:
  - 按 Home 调用 `timelineScript.ScrollTo(0)` 或 `timelineScript.FirstPage()`
  - 语音反馈："已跳到开始"

- [ ] 5. 实现 Alt+←/→ 按小节跳转 + 语音反馈

  **What to do**:
  - Alt+←: 计算上一小节的 X 坐标，调用 `CenterOnPosition()`
  - Alt+→: 计算下一小节的 X 坐标，调用 `CenterOnPosition()`
  - 使用 `GetPosXFromBarAndBeat()` 计算坐标
  - 语音反馈："X小节"

- [ ] 6. 实现边界检查和边界提示

  **What to do**:
  - 到达第一小节时：语音提示"已是第一小节"
  - 到达最后一小节时：语音提示"已是最后小节"
  - 缩放到达极限时：语音提示"已到最大/最小缩放"

- [ ] 7. 构建验证

  **What to do**:
  - 运行 `dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj`
  - 确保 0 个错误
  - 确保 Mod 正常工作

---

## Commit Strategy

- **单次提交**：完成所有任务后提交
- 提交信息：`添加时间轴键盘导航功能`
- 文件：`RDLevelEditorAccess/EditorAccess.cs`
- 构建：`dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj`

---

## Success Criteria

### 验证命令
```bash
dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj
# 期望：0 个错误
```

### 最终检查清单
- [ ] J/K 翻页功能正常
- [ ] [/] 缩放功能正常
- [ ] Home 跳转功能正常
- [ ] Alt+←/→ 按小节跳转功能正常
- [ ] 所有导航操作有语音反馈
- [ ] 边界情况有提示
- [ ] 构建成功
