# 改进时间轴导航语音反馈（精确位置）

## TL;DR

> **快速总结**：使用 playhead 的精确位置替代 startBar，获取更准确的小节和拍号。

> **交付物**：
> - 使用 `timeline.playhead.anchoredPosition.x` 和 `timeline.GetBarAndBeatWithPosX()` 获取精确位置
> - 语音反馈：精确播报当前位置（格式："X小节 Y拍"）

---

## Context

### 问题分析

当前代码使用 `scnEditor.instance.startBar` 来获取位置，这是时间轴视图的起始小节，不是播放头的精确位置。

### 解决方案

使用 `timeline.playhead` 获取精确位置：

```csharp
// 获取播放头的 X 坐标
float posX = timeline.playhead.anchoredPosition.x;

// 转换为小节和拍
BarAndBeat barAndBeat = timeline.GetBarAndBeatWithPosX(posX);

// 播报
Narration.Say($"{barAndBeat.bar}小节 {barAndBeat.beat}拍", NarrationCategory.Navigation);
```

### 关键代码引用

**Timeline.cs:24-26**:
```csharp
public RectTransform playhead;
public RectTransform playheadLine;
```

**Timeline.cs:1166-1187** - `GetBarAndBeatWithPosX` 方法：
```csharp
public BarAndBeat GetBarAndBeatWithPosX(float posX, List<LevelEvent_SetCrotchetsPerBar> cpbEvents = null, float minX = 0f)
{
    // ... 计算精确的 bar 和 beat
    return new BarAndBeat(barNumber, beatNumber);
}
```

**scnEditor.cs:3668** - 使用示例：
```csharp
BarAndBeat barAndBeatWithPosX = base.timeline.GetBarAndBeatWithPosX(base.timeline.playhead.anchoredPosition.x);
```

**BarAndBeat.cs** - 结构体定义：
```csharp
public struct BarAndBeat
{
    public int bar;    // 小节号（从1开始）
    public float beat; // 拍号（从1开始，可以是小数）
}
```

---

## Work Objectives

### 核心目标
使用 playhead 精确位置替代 startBar，提供准确的小节和拍号播报。

### 语音反馈格式
- **精确位置**："X小节 Y拍"（例如："5小节 2.5拍"）
- beat 是 float 类型，可以显示小数

### 必须有
- 使用 `timeline.playhead.anchoredPosition.x` 获取精确位置
- 使用 `timeline.GetBarAndBeatWithPosX()` 转换为 BarAndBeat
- 播报精确的 bar 和 beat 值

---

## TODOs

- [ ] 1. 修改 TimelinePatch 的 Postfix 方法，使用 playhead 精确位置
  - 在 PreviousPagePostfix 中使用 `timeline.playhead.anchoredPosition.x`
  - 调用 `timeline.GetBarAndBeatWithPosX()` 获取精确位置

- [ ] 2. 修改 TimelineNavigationPatch 的 Postfix 方法
  - 使用 `__instance.timeline.playhead.anchoredPosition.x`
  - 调用 `__instance.timeline.GetBarAndBeatWithPosX()` 获取精确位置

- [ ] 3. 构建验证

- [ ] 4. Git 提交

---

## Success Criteria

- [ ] 按 J/K 翻页后播报精确位置
- [ ] 按 PageUp/PageDown 跳转后播报精确位置
- [ ] 构建成功
