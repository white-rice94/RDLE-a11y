# Draft: 时间轴键盘导航功能

## 研究发现

### 已有的时间轴导航机制

**Timeline.cs 关键 API：**
- `ScrollTo(float x, float duration = 0f)` - 滚动到指定 X 坐标
- `CenterOnPosition(float x, float duration = 0f)` - 居中到指定位置
- `PreviousPage()` / `NextPage()` - 前后翻页
- `ZoomIn()` / `ZoomOut()` - 水平缩放
- `ZoomVert()` - 垂直缩放
- `GetPosXFromBarAndBeat(BarAndBeat position)` - Bar/Beat 转 X 坐标

**scnEditor.cs 关键字段/方法：**
- `startBar` - 当前起始小节
- `timelineScript` - Timeline 引用
- `ScrubToBar(int bar)` - 跳转到指定小节

**事件定位字段：**
- `LevelEvent_Base.bar` - 小节
- `LevelEvent_Base.beat` - 拍
- `LevelEvent_Base.y` - 垂直位置（行号）

### 已占用的快捷键

| 快捷键 | 功能 |
|--------|------|
| J | PreviousPage（向前翻页）|
| K | NextPage（向后翻页）|
| Home | Rewind（回到起点）|
| PageUp | PreviousButtonClick |
| PageDown | NextButtonClick |
| Ctrl + N | New File |
| Ctrl + O | Open Most Recent File |
| Ctrl + S | Save File |
| Ctrl + +/- | 水平缩放 |
| Ctrl + Enter | 打开事件编辑器 |
| Shift + Enter | 打开轨道编辑器 |
| Insert | 添加事件 |
| Ctrl + Insert | 添加轨道/精灵 |
| Up/Down Arrow | 轨道导航（Rows/Sprites Tab）|
| Left/Right Arrow | 事件导航 |

### 可用的快捷键组合

以下组合未被占用：
- `Alt + Left/Right` - 可用于小节导航
- `Ctrl + Shift + Left/Right` - 可用于精细导航
- `Alt + Up/Down` - 可用于垂直缩放
- `Ctrl + G` - 跳转到指定小节
- `[` / `]` - 可用于缩放

## 用户需求确认

- **功能范围**：基础功能（跳转小节、缩放、翻页）
- **快捷键风格**：沿用游戏风格（J/K 翻页、PageUp/Down 跳转、Ctrl+/- 缩放）
- **语音反馈**：详细朗读（每次导航都朗读当前位置）

## 技术决策

- 使用 Timeline.cs 的 `ScrollTo`, `CenterOnPosition`, `PreviousPage`, `NextPage`, `ZoomIn`, `ZoomOut` 等方法
- 语音反馈使用 `Narration.Say()` 朗读当前位置（格式："第X小节 第Y拍"）
- 在 `HandleTimelineNavigation()` 中添加时间轴导航处理逻辑
- 经过代码搜索，游戏原生代码中未找到 J/K 的快捷键绑定，可以安全使用
