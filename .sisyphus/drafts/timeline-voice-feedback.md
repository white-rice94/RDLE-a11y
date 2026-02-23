# Draft: 时间轴导航语音反馈

## 研究发现

### 原生编辑器时间轴导航快捷键

在 `scnEditor.cs` 中发现的原生快捷键：

| 快捷键 | 方法 | 功能 |
|--------|------|------|
| J | `timeline.PreviousPage()` | 向前翻页 |
| K | `timeline.NextPage()` | 向后翻页 |
| G | `timeline.PreviousBookmark()` | 上一个书签 |
| H | `timeline.NextBookmark()` | 下一个书签 |
| Home | `RewindButtonClick()` | 回到开始 |
| PageUp | `PreviousButtonClick()` | 上一个 |
| PageDown | `NextButtonClick()` | 下一个 |

### Timeline.cs 关键方法

```csharp
public void PreviousPage()
{
    followPlayhead = false;
    ScrollTo(0f - scrollviewContent.anchoredPosition.x - scrollview.rect.width);
}

public void NextPage()
{
    followPlayhead = false;
    ScrollTo(0f - scrollviewContent.anchoredPosition.x + scrollview.rect.width);
}

public void PreviousBookmark() { ... }
public void NextBookmark() { ... }
```

### scnEditor.cs 关键字段

- `startBar` - 当前起始小节
- `timeline` - Timeline 引用

## 用户需求确认

- **功能范围**：不添加新快捷键，仅在原生功能被调用时给予语音反馈
- **语音反馈**：朗读当前位置（格式："X小节 Y拍"）

## 技术决策

- 使用 Harmony Postfix Patch 在以下方法后添加语音反馈：
  - `Timeline.PreviousPage()`
  - `Timeline.NextPage()`
  - `Timeline.PreviousBookmark()`
  - `Timeline.NextBookmark()`
  - `scnEditor.RewindButtonClick()`
  - `scnEditor.PreviousButtonClick()`
  - `scnEditor.NextButtonClick()`
- 语音反馈格式：优先位置信息
