# Helper 打开时自动选中 ListView 当前值计划

## TL;DR

> **快速总结**：修复 Helper 端 ListView 选中当前值的问题，确保打开属性编辑器时自动选中已有值的项目。

> **交付物**：
> - 修复 SoundData ListView 选中逻辑
> - 修复 Character ListView 选中逻辑
> - 添加刷新/聚焦逻辑确保选中状态可见

> **预计工作量**：Short（15 分钟）

---

## Context

### 问题分析

**Helper 端现有代码**：

1. **SoundData** (EditorForm.cs:428):
```csharp
if (opt == soundFilename) item.Selected = true;
```

2. **Character** (EditorForm.cs:570):
```csharp
if (charName == prop.value) item.Selected = true;
```

**问题**：代码逻辑存在，但在 ListView 项目添加到控件之前设置 `Selected = true`，可能导致选中状态不生效或不可见。

### 解决方案

在 ListView 项目添加完成后，重新遍历并设置选中状态，或者在 ListView 添加到 Panel 之后调用 `Refresh()` 方法。

---

## Work Objectives

### 优化点 1：修复 SoundData ListView 选中逻辑
- 在所有项目添加完成后，重新遍历并设置正确的选中项
- 可选：添加 `lv.Focus()` 确保选中状态可见

### 优化点 2：修复 Character ListView 选中逻辑
- 同样的修复逻辑

### 优化点 3：调试日志
- 添加日志输出当前值和选中状态，方便调试

---

## TODOs

- [ ] 1. 修复 SoundData ListView 选中逻辑
  - 在所有项目添加完成后，检查 `soundFilename` 并设置 `Selected = true`
  - 添加 `lv.Focus()` 确保可见

- [ ] 2. 修复 Character ListView 选中逻辑
  - 同样的修复逻辑

- [ ] 3. 构建验证

- [ ] 4. Git 提交

---

## Success Criteria

- [ ] 打开 Helper 时 SoundData ListView 自动选中已有值
- [ ] 打开 Helper 时 Character ListView 自动选中已有值
- [ ] 构建成功
