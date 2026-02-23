# Helper 音效下拉选择 + 文件名输入支持

## TL;DR

> **快速摘要**: 在 Helper 中实现与原生编辑器一致的音效选择方式——下拉菜单选择预设音效 + 文本框输入自定义文件名。
> 
> **交付物**:
> - Mod 端提取 `SoundAttribute` 的选项列表配置
> - Mod 端调用 `optionsMethod` 获取动态选项
> - Helper 端显示下拉菜单 + 文件名输入框
> 
> **预估工作量**: Medium

---

## Context

### 原生编辑器机制

**SoundAttribute** 定义：
```csharp
public class SoundAttribute : ControlAttribute
{
    public readonly string[] options;        // 静态选项数组
    public readonly bool customFile;         // 是否允许自定义文件
    public readonly bool playSoundOnEdit;    // 编辑时播放音效
    public readonly bool updateTimeline;     // 是否更新时间线
    public readonly string optionsMethod;    // 动态获取选项的方法名
}
```

**选项获取流程**：
1. 如果 `optionsMethod` 不为空 → 调用事件实例上的方法获取 `string[]`
2. 否则使用 `options` 数组
3. 如果无选项或 `customFile = true` → 显示文件浏览按钮

**示例属性**：

| 事件类型 | 属性 | options | optionsMethod | customFile |
|---------|------|---------|---------------|------------|
| PlaySong | song | null | null | true |
| AddClassicBeat | sound | null | "GetBeatSounds" | true |
| PlaySound | sound | null | null | true |
| SetBeatSound | sound | null | null | true |

**RDEditorConstants 中的预设列表**：
- `BeatSounds` (34种): Shaker, Stick, Kick, KickChroma, etc.
- `PlaySounds` (32种): MistakeSmall3, Switch, Vibraslap, etc.
- `ClapSoundsP1/P2/CPU` (7种): ClapHit, ReverbClap, etc.

---

## Work Objectives

### Core Objective
让 Helper 的音效编辑体验与原生编辑器一致：
- 可从下拉菜单选择预设音效
- 可手动输入自定义文件名
- 两者可切换

### Definition of Done
- [ ] Mod 端正确提取 SoundAttribute 配置
- [ ] Mod 端正确调用 optionsMethod 获取动态选项
- [ ] Helper 端正确显示下拉菜单和输入框
- [ ] 选择和输入功能正常工作
- [ ] 构建成功

---

## Execution Strategy

顺序执行，Mod 端先完成数据提取，再更新 Helper 端 UI。

---

## TODOs

- [ ] 1. Mod端 - PropertyData 添加新字段

  **What to do**:
  - 添加 `soundOptions` 字段：预设音效选项列表
  - 添加 `allowCustomFile` 字段：是否允许自定义文件名

  **代码位置**: `FileIPC.cs:PropertyData` 类

  **代码示例**:
  ```csharp
  [Serializable]
  private class PropertyData
  {
      // 现有字段...
      public bool itsASong;      // SoundData 类型专用
      public bool isNullable;    // 是否为可空类型
      
      // 新增字段
      public string[] soundOptions;   // 预设音效选项
      public bool allowCustomFile;    // 是否允许自定义文件名
  }
  ```

  **Commit**: NO

---

- [ ] 2. Mod端 - ExtractProperties 提取 SoundAttribute 配置

  **What to do**:
  - 获取 `SoundAttribute` 实例
  - 提取 `options`、`customFile`、`optionsMethod`
  - 如果有 `optionsMethod`，调用获取动态选项

  **代码位置**: `FileIPC.cs:ExtractProperties()`

  **代码示例**:
  ```csharp
  else if (prop is SoundDataPropertyInfo soundProp)
  {
      dto.type = "SoundData";
      dto.itsASong = soundProp.itsASong;
      
      // 获取 SoundAttribute 配置
      var soundAttr = prop.controlAttribute as SoundAttribute;
      if (soundAttr != null)
      {
          dto.allowCustomFile = soundAttr.customFile;
          
          // 获取选项列表
          if (!string.IsNullOrEmpty(soundAttr.optionsMethod))
          {
              // 动态获取选项
              dto.soundOptions = GetSoundOptions(ev, soundAttr.optionsMethod, prop.propertyInfo.DeclaringType);
          }
          else if (soundAttr.options != null && soundAttr.options.Length > 0)
          {
              dto.soundOptions = soundAttr.options;
          }
      }
  }
  ```

  **Commit**: NO

---

- [ ] 3. Mod端 - 实现 GetSoundOptions 方法

  **What to do**:
  - 反射调用事件实例上的选项方法
  - 返回 string[]

  **代码位置**: `FileIPC.cs`

  **代码示例**:
  ```csharp
  private string[] GetSoundOptions(LevelEvent_Base ev, string methodName, Type declaringType)
  {
      try
      {
          var method = declaringType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
          if (method == null)
          {
              Debug.LogWarning($"[FileIPC] 找不到选项方法: {methodName}");
              return null;
          }
          
          // 实例方法需要事件实例，静态方法传 null
          object instance = method.IsStatic ? null : ev;
          var result = method.Invoke(instance, new object[0]) as string[];
          Debug.Log($"[FileIPC] 获取音效选项: {methodName} -> {(result != null ? result.Length + "项" : "null")}");
          return result;
      }
      catch (Exception ex)
      {
          Debug.LogWarning($"[FileIPC] 获取音效选项失败: {ex.Message}");
          return null;
      }
  }
  ```

  **Commit**: NO

---

- [ ] 4. Helper端 - PropertyData 添加新字段

  **What to do**:
  - 同步 Mod 端的新字段

  **代码位置**: `EditorForm.cs:PropertyData` 类

  **Commit**: NO

---

- [ ] 5. Helper端 - BuildUI 实现下拉菜单 + 输入框

  **What to do**:
  - 显示下拉菜单（如果有选项）
  - 显示文件名输入框（如果允许自定义）
  - 添加切换按钮（如果有选项且允许自定义）

  **代码位置**: `EditorForm.cs:BuildUI()`

  **UI 设计**:
  ```
  ┌─ 音效名 ──────────────────────────────────────┐
  │ [下拉菜单: Shaker ▼]  [切换到输入]             │
  │                                                │
  │ 音量: [100]  (0-300, 默认100)                  │
  │ 音调: [100]  (0-300, 默认100)                  │
  │ 声道: [0] (-100~100)   偏移: [0] 毫秒          │
  └────────────────────────────────────────────────┘
  
  切换后：
  ┌─ 音效名 ──────────────────────────────────────┐
  │ [文本输入框: sndCustom.wav]  [切换到选择]      │
  │                                                │
  │ 音量/音调/声道/偏移...                         │
  └────────────────────────────────────────────────┘
  ```

  **代码示例**:
  ```csharp
  case "SoundData":
      // 解析现有值
      var soundParts = (prop.value ?? "|||").Split('|');
      string soundFilename = soundParts.Length > 0 ? soundParts[0] : "";
      // ...
      
      bool hasOptions = prop.soundOptions != null && prop.soundOptions.Length > 0;
      bool canCustom = prop.allowCustomFile;
      
      if (hasOptions)
      {
          // 下拉菜单 + 可选输入框
          group.Height = 160;
          
          var soundPanel = new Panel { Width = 420, Height = 130, Top = 20, Left = 10 };
          
          // 第一行：下拉菜单 + 切换按钮
          var cmbSound = new ComboBox
          {
              Width = 280, Top = 3, Left = 75, DropDownStyle = ComboBoxStyle.DropDownList,
              Name = "SoundDropdown", Tag = prop.soundOptions
          };
          cmbSound.Items.AddRange(prop.soundOptions);
          
          var btnSwitch = new Button
          {
              Text = "输入文件名", Width = 100, Top = 2, Left = 360,
              Name = "SwitchToInput"
          };
          
          // 第二行：文件名输入框（初始隐藏）
          var txtFilename = new TextBox
          {
              Text = soundFilename, Width = 280, Top = 3, Left = 75,
              Name = "Filename", Visible = !hasOptions || !prop.soundOptions.Contains(soundFilename)
          };
          
          var btnSwitchBack = new Button
          {
              Text = "选择预设", Width = 100, Top = 2, Left = 360,
              Name = "SwitchToDropdown", Visible = !cmbSound.Visible
          };
          
          // 切换逻辑
          btnSwitch.Click += (s, e) => { cmbSound.Visible = false; txtFilename.Visible = true; };
          btnSwitchBack.Click += (s, e) => { txtFilename.Visible = false; cmbSound.Visible = true; };
          
          // ... 音量/音调/声道/偏移控件
      }
      else
      {
          // 仅文件名输入框（现有逻辑）
          // ...
      }
  ```

  **Commit**: NO

---

- [ ] 6. Helper端 - GetCurrentUpdates 处理下拉菜单值

  **What to do**:
  - 检查下拉菜单是否可见
  - 获取下拉菜单选中项或文本框值

  **代码位置**: `EditorForm.cs:GetCurrentUpdates()`

  **代码示例**:
  ```csharp
  else if (ctrl is Panel soundPanel)
  {
      var cmbSound = soundPanel.Controls.Find("SoundDropdown", false).FirstOrDefault() as ComboBox;
      var txtFilename = soundPanel.Controls.Find("Filename", false).FirstOrDefault() as TextBox;
      
      string filename;
      if (cmbSound != null && cmbSound.Visible)
      {
          filename = cmbSound.SelectedItem?.ToString() ?? "";
      }
      else if (txtFilename != null)
      {
          filename = txtFilename.Text ?? "";
      }
      else
      {
          filename = "";
      }
      
      // ... 组装完整值
      value = $"{filename}|{volume}|{pitch}|{pan}|{offset}";
  }
  ```

  **Commit**: NO

---

- [ ] 7. 构建验证

  **What to do**:
  - 构建两个项目
  - 确认无编译错误

  **Commit**: YES
  - Message: `Helper 添加音效下拉选择 + 文件名输入支持`
  - Files: `RDLevelEditorAccess/IPC/FileIPC.cs`, `RDEventEditorHelper/EditorForm.cs`

---

## 参考代码位置

| 功能 | 文件 | 说明 |
|-----|------|------|
| SoundAttribute | `agents references/.../SoundAttribute.cs` | 属性定义 |
| PropertyControl_Sound | `agents references/.../PropertyControl_Sound.cs` | 原生 UI 实现 |
| RDEditorConstants | `agents references/.../RDEditorConstants.cs` | 预设音效列表 |
| SoundDataPropertyInfo | `agents references/.../SoundDataPropertyInfo.cs` | PropertyInfo 类型 |

---

## Success Criteria

### 测试场景

1. **AddClassicBeat.sound**:
   - 显示下拉菜单（BeatSounds 列表）
   - 可切换到文件名输入
   - 选择/输入后正确保存

2. **PlaySong.song**:
   - 仅显示文件名输入框（无预设选项）
   - 输入歌曲文件名后正确保存

3. **PlaySound.sound**:
   - 仅显示文件名输入框
   - 正常工作
