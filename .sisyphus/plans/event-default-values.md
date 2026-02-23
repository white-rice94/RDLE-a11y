# 优化添加事件默认值设定计划

## TL;DR

> **快速总结**：优化添加事件时的默认值设定，让创建的事件位置更精确，- 默认轨道和房间使用当前选中的轨道和房间
- 修复节拍音效默认值问题，- 知道原因，调用时机
- 提供解决方案

> **交付物**：
> - 修改 CreateEventAndEdit 方法，- bar 和 beat 使用 playhead 精确位置
- 轨道和房间使用当前选中的值
- 研究 AddClassicBeat 的节拍音效默认值设置机制
- - 研究 OnCreate 的作用和流程

> **预计工作量**：Short（30 分钟）

---

## Context

### 当前实现分析

在 `EditorAccess.cs:656-660`，当前创建事件的代码：

```csharp
int bar = editor.startBar + 1;
float beat = 1f;
int row = editor.selectedRowIndex >= 0 ? editor.selectedRowIndex : 0;
```

**问题**：使用 `startBar` 获取的是视图的起始小节，不是播放头的精确位置。

### 原生编辑器对比

**原生编辑器 (SetLevelEventControlType)**：
```csharp
// 从已有事件复制属性
levelEvent_Base.CopyBasePropertiesFrom(levelEvent, copyBarAndBeat: true, copyRow);

// 调用 OnCreate
levelEvent_Base.OnCreate();
```

原生编辑器从已选中的事件复制属性（包括精确的 bar/beat），然后调用 OnCreate。

**Mod 当前实现**：
```csharp
// 直接设置属性（使用 startBar + 1 估算）
levelEvent.bar = bar;
levelEvent.beat = beat;

// 调用 OnCreate
levelEvent.OnCreate();
```

### 节拍音效默认值问题

在 `LevelEvent_AddClassicBeat.cs:47`，默认值是 `new SoundDataStruct("Shaker")`

而 `LevelEvent_MakeRow.cs:109-112` 定义了 `pulseSound`：
```csharp
if (pulseSound == null)
{
    pulseSound = new SoundData(itsASong: false, "Shaker", ...);
}
```

`pulseSound` 是轨道的默认节拍音效设置。

但 **当前问题**：当通过 Mod 创建事件时，`sound` 属性被初始化为 `new SoundDataStruct("Shaker")`，覆盖了轨道的 `pulseSound`

### 解决方案

在设置属性后，需要从对应轨道获取 `pulseSound` 并复制到事件的 `sound` 属性

---

## Work Objectives

### 优化点 1：使用 playhead 精确位置
- 获取 playhead 的 BarAndBeat
- 设置 `levelEvent.bar` 和 `levelEvent.beat`

### 优化点 2：轨道和房间（已实现）
- `row = editor.selectedRowIndex`
- `room` 从 `rowsData[row].room` 获取

### 优化点 3：节拍音效默认值
- 从 `rowsData[row].pulseSound` 复制到事件的 `sound` 属性
- 魁：如果 `sound` 有值，设置为轨道的 pulseSound

### 必须有
- 保持现有轨道和房间选择逻辑
- 使用 playhead 精确位置
- 不修改 OnCreate 调用顺序

### 禁止有
- 不改变现有 UI 逻辑
- 不修改已有的事件创建流程

---

## TODOs

- [ ] 1. 修改 CreateEventAndEdit 使用 playhead 精确位置

  **What to do**:
  - 获取 playhead 位置：`editor.timeline.GetBarAndBeatWithPosX(editor.timeline.playhead.anchoredPosition.x)`
  - 设置 bar 和 beat

- [ ] 2. 修复节拍音效默认值问题

  **What to do**:
  - 创建事件后，检查事件类型是否需要节拍音效（如 AddClassicBeat、AddOneshotBeat）
  - 如果需要，从 `rowsData[levelEvent.row].pulseSound` 获取默认值
  - 设置 `sound` 属性
  - 确保 `sound` 属性值类型正确（SoundDataStruct）

- [ ] 3. 构建验证

- [ ] 4. Git 提交

---

## Success Criteria

- [ ] 创建事件后 bar 和 beat 为 playhead 玷取的精确位置
- [ ] 创建 AddClassicBeat 后 sound 属性正确设置为轨道的默认节拍音效
- [ ] 构建成功
