# Helper 支持游戏特殊类型计划

## TL;DR

> **快速摘要**: 在 Helper 中支持编辑 `SoundDataStruct` 等游戏特有的特殊类型。
> 
> **交付物**:
> - Mod端：识别并序列化 `SoundDataStruct` 类型
> - Helper端：提供音效编辑UI（文件选择、音量、音调、声道、偏移）
> - 双向转换逻辑
> 
> **预估工作量**: Medium（两个项目，约200行代码）

---

## Context

### 游戏特殊类型分析

#### SoundDataStruct
```csharp
public readonly struct SoundDataStruct
{
    public readonly string filename;   // 音效文件名
    public readonly int volume;        // 音量 (0-300, 默认100)
    public readonly int pitch;         // 音调 (0-300, 默认100)
    public readonly int pan;           // 声道 (-100 to 100, 默认0)
    public readonly int offset;        // 偏移 (毫秒, 默认0)
}
```

#### 使用场景

| 事件类型 | 属性 | 类型 |
|---------|------|------|
| PlaySong | song | SoundDataStruct |
| PlaySound | sound | SoundDataStruct |
| SetBeatSound | sound | SoundDataStruct |
| AddClassicBeat | sound | SoundDataStruct? |
| AddOneshotBeat | sound | SoundDataStruct? |
| SetCountingSound | sounds | SoundDataStruct[] |
| SetClapSounds | p1Sound, p2Sound, cpuSound | SoundDataStruct? |

#### PropertyInfo 类型层次

```
BasePropertyInfo
├── IntPropertyInfo
├── FloatPropertyInfo
├── BoolPropertyInfo
├── StringPropertyInfo
├── EnumPropertyInfo
├── ColorPropertyInfo
├── Vector2PropertyInfo
├── Float2PropertyInfo
├── FloatExpressionPropertyInfo
├── FloatExpression2PropertyInfo
├── SoundDataPropertyInfo        ← 需要支持
├── NullablePropertyInfo         ← 需要支持（包装其他类型）
└── ArrayPropertyInfo<T>         ← 需要支持（数组）
```

### 现有代码状态

**Mod端 (FileIPC.cs)**:
- `ExtractProperties()` 中没有处理 `SoundDataPropertyInfo`
- `ConvertPropertyValue()` 没有处理 `SoundDataStruct`
- `ApplyUpdates()` 没有处理 `SoundDataStruct` 类型

**Helper端 (EditorForm.cs)**:
- `BuildUI()` 中没有处理 `SoundData` 类型
- `GetCurrentUpdates()` 中没有处理 `SoundData` 控件

---

## Work Objectives

### Core Objective
支持 `SoundDataStruct` 类型的完整编辑流程。

### Definition of Done
- [ ] Mod端识别 `SoundDataPropertyInfo`
- [ ] Mod端序列化 `SoundDataStruct` 为 JSON 对象
- [ ] Helper端显示音效编辑 UI
- [ ] Helper端返回正确的 JSON 格式
- [ ] Mod端反序列化并应用更改
- [ ] 构建成功
- [ ] 测试通过

### Must Have
- SoundDataStruct 的序列化/反序列化
- Helper 的音效编辑 UI
- 文件名输入（或下拉选择）

### Nice to Have
- 文件选择对话框
- 音效预览播放
- 音量/音调滑块

### Must NOT Have
- 不破坏现有功能
- 不修改游戏代码

---

## Execution Strategy

### Sequential Execution

```
Task 1: Mod端 - 识别 SoundDataPropertyInfo
    ↓
Task 2: Mod端 - 序列化 SoundDataStruct
    ↓
Task 3: Helper端 - 音效编辑 UI
    ↓
Task 4: Helper端 - 返回 SoundData 格式
    ↓
Task 5: Mod端 - 反序列化并应用
    ↓
Task 6: 构建验证
```

---

## TODOs

- [ ] 1. Mod端 - 识别 SoundDataPropertyInfo

  **What to do**:
  - 在 `ExtractProperties()` 中添加 `SoundDataPropertyInfo` 判断
  - 设置 `dto.type = "SoundData"`
  - 设置 `dto.itsASong` 属性（用于区分歌曲/音效）

  **References**:
  - `FileIPC.cs:484-498` - 现有类型判断
  - `SoundDataPropertyInfo.cs:8` - itsASong 属性

  **代码示例**:
  ```csharp
  else if (prop is SoundDataPropertyInfo soundProp)
  {
      dto.type = "SoundData";
      dto.itsASong = soundProp.itsASong;
  }
  ```

  **Commit**: NO

---

- [ ] 2. Mod端 - 序列化 SoundDataStruct

  **What to do**:
  - 在 `ConvertPropertyValue()` 中添加 `SoundDataStruct` 处理
  - 输出格式：`"filename|volume|pitch|pan|offset"`

  **References**:
  - `FileIPC.cs:632-693` - ConvertPropertyValue 方法

  **代码示例**:
  ```csharp
  if (value?.GetType().Name == "SoundDataStruct")
  {
      var filename = value.GetType().GetField("filename")?.GetValue(value);
      var volume = value.GetType().GetField("volume")?.GetValue(value);
      var pitch = value.GetType().GetField("pitch")?.GetValue(value);
      var pan = value.GetType().GetField("pan")?.GetValue(value);
      var offset = value.GetType().GetField("offset")?.GetValue(value);
      return $"{filename}|{volume}|{pitch}|{pan}|{offset}";
  }
  ```

  **Commit**: NO

---

- [ ] 3. Helper端 - 音效编辑 UI

  **What to do**:
  - 在 `BuildUI()` 中添加 `case "SoundData"` 处理
  - 创建文件名输入框 + 音量/音调/声道/偏移输入
  - 使用 GroupBox 包含所有控件

  **References**:
  - `EditorForm.cs:148-327` - 现有 UI 构建

  **UI 设计**:
  ```
  ┌─ 音效 (SoundData) ────────────────────────┐
  │ 文件名: [________________]               │
  │ 音量:   [____] (0-300)                   │
  │ 音调:   [____] (0-300)                   │
  │ 声道:   [____] (-100 to 100)             │
  │ 偏移:   [____] (毫秒)                    │
  └───────────────────────────────────────────┘
  ```

  **Commit**: NO

---

- [ ] 4. Helper端 - 返回 SoundData 格式

  **What to do**:
  - 在 `GetCurrentUpdates()` 中处理 SoundData 控件
  - 返回格式：`"filename|volume|pitch|pan|offset"`

  **References**:
  - `EditorForm.cs:387-427` - GetCurrentUpdates 方法

  **Commit**: NO

---

- [ ] 5. Mod端 - 反序列化并应用

  **What to do**:
  - 在 `ApplyUpdates()` 中添加 SoundData 处理
  - 解析 `"filename|volume|pitch|pan|offset"` 格式
  - 创建新的 `SoundDataStruct` 并设置属性

  **References**:
  - `FileIPC.cs:215-325` - ApplyUpdates 方法
  - `SoundDataStruct.cs:45-52` - 构造函数

  **代码示例**:
  ```csharp
  if (propInfo is SoundDataPropertyInfo)
  {
      var parts = strVal.Split('|');
      string filename = parts[0];
      int volume = parts.Length > 1 ? int.Parse(parts[1]) : 100;
      int pitch = parts.Length > 2 ? int.Parse(parts[2]) : 100;
      int pan = parts.Length > 3 ? int.Parse(parts[3]) : 0;
      int offset = parts.Length > 4 ? int.Parse(parts[4]) : 0;
      
      var soundDataType = Type.GetType("RDLevelEditor.SoundDataStruct");
      valToSet = Activator.CreateInstance(soundDataType, filename, volume, pitch, pan, offset);
  }
  ```

  **Commit**: NO

---

- [ ] 6. 构建验证

  **What to do**:
  - 构建两个项目
  - 测试 PlaySong 事件的 song 属性

  **Commit**: YES
  - Message: `Helper支持SoundDataStruct类型编辑`
  - Files: `RDLevelEditorAccess/IPC/FileIPC.cs`, `RDEventEditorHelper/EditorForm.cs`

---

## 数据格式约定

### PropertyData 扩展

```csharp
public class PropertyData
{
    public string name;
    public string displayName;
    public string value;
    public string type;
    public string[] options;
    public string methodName;
    
    // 新增：用于 SoundData 类型
    public bool itsASong;  // 区分歌曲/音效
}
```

### SoundData 序列化格式

**传输格式** (value 字段):
```
"filename|volume|pitch|pan|offset"
```

**示例**:
```
"sndOrientalTechno|100|100|0|0"
"Shaker|80|100|0|50"
```

---

## Success Criteria

### 测试场景

1. **PlaySong 事件**:
   - 打开编辑器，修改 song 属性
   - 修改文件名、音量
   - 确认更改正确应用

2. **AddClassicBeat 事件**:
   - sound 是可空类型
   - 测试设置和清除 sound 属性

3. **音效范围验证**:
   - 音量 0-300
   - 音调 0-300
   - 声道 -100 to 100
