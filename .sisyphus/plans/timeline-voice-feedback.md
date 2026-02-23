# 时间轴导航语音反馈计划

## TL;DR

> **快速总结**：为原生时间轴导航功能添加语音反馈，只播报当前位置，不播报操作类型。

> **交付物**：
> - Harmony Patch 添加语音反馈到原生的 PreviousPage/NextPage/PreviousButtonClick/NextButtonClick 方法
> - 语音反馈：仅朗读当前位置（格式："X小节 Y拍"）

> **预计工作量**：Quick（15 分钟）

---

## Context

### 原生编辑器时间轴导航快捷键

| 快捷键 | 方法 | 功能 |
|--------|------|------|
| J | `Timeline.PreviousPage()` | 向前翻页 |
| K | `Timeline.NextPage()` | 向后翻页 |
| PageUp | `scnEditor.PreviousButtonClick()` | 上一小节 |
| PageDown | `scnEditor.NextButtonClick()` | 下一小节 |

### 代码位置

- `agents references/Assembly-CSharp/RDLevelEditor/scnEditor.cs:1455-1462` - J/K 快捷键
- `agents references/Assembly-CSharp/RDLevelEditor/scnEditor.cs:1479-1486` - PageUp/PageDown 快捷键
- `agents references/Assembly-CSharp/RDLevelEditor/Timeline.cs:421-431` - PreviousPage/NextPage 方法

---

## Work Objectives

### 核心目标
在玩家使用时间轴导航后播报当前位置。

### 语音反馈格式
- **仅位置**："X小节 Y拍"（例如："5小节 1拍"）
- **不播报操作类型**（不需要"向前翻页"等描述）

### 必须有
- 语音反馈在功能执行后播放
- 只播报位置，简洁高效

### 禁止有
- 不播报操作类型
- 不添加新的快捷键

---

## TODOs

- [ ] 1. 添加 Harmony Postfix Patch 到 Timeline.PreviousPage 和 Timeline.NextPage
  - 创建 `[HarmonyPatch(typeof(Timeline))]` 类
  - Postfix 方法获取 `scnEditor.instance.startBar` 播报位置

- [ ] 2. 添加 Harmony Postfix Patch 到 scnEditor.PreviousButtonClick 和 scnEditor.NextButtonClick
  - 创建 `[HarmonyPatch(typeof(scnEditor))]` 类
  - Postfix 方法播报当前位置

- [ ] 3. 构建验证

- [ ] 4. Git 提交

---

## Commit Strategy

- 提交信息：`添加时间轴导航语音反馈`
- 文件：`RDLevelEditorAccess/EditorAccess.cs`

---

## Success Criteria

- [ ] 按 J/K 翻页后播报当前位置
- [ ] 按 PageUp/PageDown 跳转后播报当前位置
- [ ] 构建成功
