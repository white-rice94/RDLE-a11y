# 修复 SoundDataStruct IPC 应用问题

## TL;DR

> **快速摘要**: 修复 `SoundDataStruct` 和 `SoundDataStruct?` 类型在 IPC 中的序列化/反序列化问题。
> 
> **交付物**:
> - 正确识别 `NullablePropertyInfo` 包装的 `SoundDataPropertyInfo`
> - 正确获取和设置 `SoundDataStruct` 值
> - 处理可空类型的 null 值情况
> 
> **预估工作量**: Short（约50行代码修改）

---

## Context

### 问题分析

用户报告编辑 `SoundDataStruct` 类型的属性后，修改不会被应用。测试了 `PlaySong.song` 和 `AddClassicBeat.sound` 两种属性。

### 根本原因

#### 问题1：NullablePropertyInfo 包装

当属性类型是 `SoundDataStruct?` 时，游戏使用 `NullablePropertyInfo` 包装 `SoundDataPropertyInfo`：

```
NullablePropertyInfo
├── underlyingPropertyInfo: SoundDataPropertyInfo
├── offByDefault: bool
└── toggleOnValue: object
```

当前代码只检查 `propInfo is SoundDataPropertyInfo`，无法匹配可空类型。

#### 问题2：类型获取可能失败

`Type.GetType("RDLevelEditor.SoundDataStruct")` 在 Unity 环境中可能返回 null，需要程序集限定名。

#### 问题3：序列化反射问题

`SoundDataStruct` 的字段是 `readonly`，虽然 `GetField().GetValue()` 应该能读取，但需要确认。

### 代码位置

**Mod 端 (`FileIPC.cs`)**：
- `ExtractProperties()` 第 500-519 行：类型识别
- `ConvertPropertyValue()` 第 723-732 行：序列化
- `ApplyUpdates()` 第 324-339 行：反序列化

---

## Work Objectives

### Core Objective
确保 `SoundDataStruct` 和 `SoundDataStruct?` 类型能够正确序列化、传输、反序列化并应用。

### Definition of Done
- [ ] `PlaySong.song` 编辑后正确应用
- [ ] `AddClassicBeat.sound` 编辑后正确应用
- [ ] 构建成功
- [ ] 测试通过

### Must Have
- 识别 `NullablePropertyInfo` 包装的 `SoundDataPropertyInfo`
- 正确处理 `SoundDataStruct?` 的 null 值

---

## Execution Strategy

顺序执行，每个任务依赖前一个完成。

---

## TODOs

- [ ] 1. 修复 ExtractProperties - 识别 NullablePropertyInfo 包装的类型

  **What to do**:
  - 检查 `propInfo is NullablePropertyInfo`
  - 获取 `underlyingPropertyInfo` 并检查是否为 `SoundDataPropertyInfo`
  - 设置正确的类型标记

  **代码位置**: `FileIPC.cs:500-519`

  **代码示例**:
  ```csharp
  else if (prop is SoundDataPropertyInfo soundProp)
  {
      dto.type = "SoundData";
      dto.itsASong = soundProp.itsASong;
  }
  else if (prop is NullablePropertyInfo nullableProp)
  {
      // 检查底层类型
      var underlying = nullableProp.underlyingPropertyInfo;
      if (underlying is SoundDataPropertyInfo underlyingSoundProp)
      {
          dto.type = "SoundData";
          dto.itsASong = underlyingSoundProp.itsASong;
          dto.isNullable = true;  // 新增字段
      }
      // 其他可空类型...
  }
  ```

  **Commit**: NO

---

- [ ] 2. 修复 ConvertPropertyValue - 处理 null 值和可空类型

  **What to do**:
  - 检查 `Nullable<T>` 类型
  - 处理 null 值情况（返回空字符串或特殊标记）
  - 确保反射正确获取 `SoundDataStruct` 字段

  **代码位置**: `FileIPC.cs:671-742`

  **代码示例**:
  ```csharp
  // 处理 Nullable<T> 类型
  if (value != null && value.GetType().IsGenericType && 
      value.GetType().GetGenericTypeDefinition() == typeof(Nullable<>))
  {
      // 获取 HasValue 属性
      var hasValue = (bool)(value.GetType().GetProperty("HasValue")?.GetValue(value) ?? false);
      if (!hasValue) return "";  // null 值返回空字符串
      // 获取 Value 属性
      value = value.GetType().GetProperty("Value")?.GetValue(value);
  }
  
  // SoundDataStruct 类型（包括从 Nullable 提取后的值）
  if (value?.GetType().Name == "SoundDataStruct")
  {
      // ... 现有逻辑
  }
  ```

  **Commit**: NO

---

- [ ] 3. 修复 ApplyUpdates - 处理 NullablePropertyInfo 的设置

  **What to do**:
  - 检查 `propInfo is NullablePropertyInfo`
  - 获取底层类型并创建正确的值
  - 处理空字符串（设置为 null）

  **代码位置**: `FileIPC.cs:324-339`

  **代码示例**:
  ```csharp
  else if (propInfo is SoundDataPropertyInfo || 
           (propInfo is NullablePropertyInfo nullable && 
            nullable.underlyingPropertyInfo is SoundDataPropertyInfo))
  {
      // 处理空字符串 -> 设置为 null（如果是可空类型）
      if (string.IsNullOrEmpty(strVal) && propInfo is NullablePropertyInfo)
      {
          valToSet = null;
      }
      else
      {
          // 解析 "filename|volume|pitch|pan|offset" 格式
          var parts = strVal.Split('|');
          // ...
          
          // 使用完整类型名
          var soundDataType = Type.GetType("RDLevelEditor.SoundDataStruct, Assembly-CSharp");
          if (soundDataType == null)
          {
              // 回退：尝试从值获取类型
              soundDataType = typeof(SoundDataStruct);
          }
          if (soundDataType != null)
          {
              valToSet = Activator.CreateInstance(soundDataType, filename, volume, pitch, pan, offset);
          }
      }
  }
  ```

  **Commit**: NO

---

- [ ] 4. 添加调试日志

  **What to do**:
  - 在关键位置添加日志
  - 帮助诊断问题

  **代码示例**:
  ```csharp
  // ExtractProperties 中
  Debug.Log($"[FileIPC] 属性 {prop.propertyInfo.Name}: 类型={prop.GetType().Name}, 值类型={rawValue?.GetType().Name ?? "null"}");
  
  // ConvertPropertyValue 中
  Debug.Log($"[FileIPC] 序列化 SoundDataStruct: filename={filename}, volume={volume}");
  
  // ApplyUpdates 中
  Debug.Log($"[FileIPC] 应用属性 {key}: propInfo类型={propInfo.GetType().Name}, 值={strVal}");
  Debug.Log($"[FileIPC] 创建 SoundDataStruct: filename={filename}, volume={volume}");
  ```

  **Commit**: NO

---

- [ ] 5. 构建验证

  **What to do**:
  - 构建两个项目
  - 测试 PlaySong.song 属性编辑
  - 测试 AddClassicBeat.sound 属性编辑

  **Commit**: YES
  - Message: `修复 SoundDataStruct 类型 IPC 应用问题`
  - Files: `RDLevelEditorAccess/IPC/FileIPC.cs`

---

## 关键发现

### PropertyInfo 类型层次

```
BasePropertyInfo
├── IntPropertyInfo
├── FloatPropertyInfo
├── BoolPropertyInfo
├── StringPropertyInfo
├── EnumPropertyInfo
├── ColorPropertyInfo
├── SoundDataPropertyInfo          ← 直接使用
├── NullablePropertyInfo           ← 包装类型
│   └── underlyingPropertyInfo     ← 实际类型在这里
└── ArrayPropertyInfo<T>
```

### SoundDataStruct 定义

```csharp
public readonly struct SoundDataStruct
{
    public readonly string filename;
    public readonly int volume;    // 0-300, 默认100
    public readonly int pitch;     // 0-300, 默认100
    public readonly int pan;       // -100 to 100, 默认0
    public readonly int offset;    // 毫秒, 默认0
}
```

### 游戏中的使用

| 事件类型 | 属性名 | 类型 | PropertyInfo |
|---------|--------|------|--------------|
| PlaySong | song | SoundDataStruct | SoundDataPropertyInfo |
| AddClassicBeat | sound | SoundDataStruct? | NullablePropertyInfo |
| PlaySound | sound | SoundDataStruct | SoundDataPropertyInfo |
| SetBeatSound | sound | SoundDataStruct | SoundDataPropertyInfo |

---

## Success Criteria

### 测试场景

1. **PlaySong 事件**:
   - 编辑 song 属性，修改文件名和音量
   - 确认修改正确应用到事件

2. **AddClassicBeat 事件**:
   - 编辑 sound 属性，修改文件名和音量
   - 确认修改正确应用到事件
   - 测试清空文件名（设置为 null）

### 验证命令
```bash
dotnet build RDMods.sln
```
