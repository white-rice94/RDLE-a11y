# Helper 音效选择器 - ListView + 搜索 + 文件浏览

## TL;DR

> **快速摘要**: 在 Helper 中使用 ListView 显示音效列表，支持关键字搜索过滤，并提供文件浏览按钮选择外部音频文件。
> 
> **交付物**:
> - Mod 端提取 `SoundAttribute` 的选项列表配置
> - Mod 端调用 `optionsMethod` 获取动态选项
> - Helper 端 ListView 显示音效列表 + 搜索框 + 文件浏览按钮
> 
> **预估工作量**: Medium

---

## Context

### 需求确认

1. **音效来源**：预设音效列表 + 外部音频文件（文件浏览对话框）
2. **UI 控件**：ListView 显示列表，支持搜索过滤

### 原生编辑器机制

**SoundAttribute** 定义：
```csharp
public class SoundAttribute : ControlAttribute
{
    public readonly string[] options;        // 静态选项数组
    public readonly bool customFile;         // 是否允许自定义文件
    public readonly string optionsMethod;    // 动态获取选项的方法名
}
```

**RDEditorConstants 中的预设列表**：
- `BeatSounds` (34种): Shaker, Stick, Kick, etc.
- `PlaySounds` (32种): MistakeSmall3, Switch, etc.
- `ClapSoundsP1/P2/CPU` (7种): ClapHit, ReverbClap, etc.

---

## Work Objectives

### Core Objective
让 Helper 的音效选择体验更好：
- ListView 显示所有可用音效
- 搜索框过滤列表
- 文件浏览按钮选择外部音频

### Definition of Done
- [ ] Mod 端正确提取 SoundAttribute 配置
- [ ] Helper 端 ListView 正确显示和过滤
- [ ] 文件浏览功能正常
- [ ] 构建成功

---

## TODOs

- [ ] 1. Mod端 - PropertyData 添加新字段

  **What to do**:
  - 添加 `soundOptions` 字段：预设音效选项列表
  - 添加 `allowCustomFile` 字段：是否允许浏览外部文件

  **代码位置**: `FileIPC.cs:PropertyData` 类

  ```csharp
  [Serializable]
  private class PropertyData
  {
      // 现有字段...
      public bool itsASong;
      public bool isNullable;
      
      // 新增字段
      public string[] soundOptions;   // 预设音效选项
      public bool allowCustomFile;    // 是否允许浏览外部文件
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

  ```csharp
  else if (prop is SoundDataPropertyInfo soundProp)
  {
      dto.type = "SoundData";
      dto.itsASong = soundProp.itsASong;
      
      var soundAttr = prop.controlAttribute as SoundAttribute;
      if (soundAttr != null)
      {
          dto.allowCustomFile = soundAttr.customFile;
          
          if (!string.IsNullOrEmpty(soundAttr.optionsMethod))
          {
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

  **代码位置**: `FileIPC.cs`

  ```csharp
  private string[] GetSoundOptions(LevelEvent_Base ev, string methodName, Type declaringType)
  {
      try
      {
          var method = declaringType.GetMethod(methodName, 
              BindingFlags.Public | BindingFlags.NonPublic | 
              BindingFlags.Instance | BindingFlags.Static);
          if (method == null) return null;
          
          object instance = method.IsStatic ? null : ev;
          return method.Invoke(instance, new object[0]) as string[];
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

- [ ] 5. Helper端 - BuildUI 实现 ListView + 搜索 + 浏览

  **What to do**:
  - 创建搜索框
  - 创建 ListView 显示音效列表
  - 创建文件浏览按钮（如果允许）
  - 显示音量/音调/声道/偏移设置

  **代码位置**: `EditorForm.cs:BuildUI()`

  **UI 设计**:
  ```
  ┌─ 音效名 ──────────────────────────────────────┐
  │ 搜索: [________] [浏览文件...]                 │
  │ ┌────────────────────────────────────────────┐│
  │ │ Shaker          (内置音效)                 ││
  │ │ ShakerHi        (内置音效)                 ││
  │ │ Stick           (内置音效)                 ││
  │ │ Kick            (内置音效)                 ││
  │ │ custom.wav      (外部文件)                 ││
  │ │ ...                                        ││
  │ └────────────────────────────────────────────┘│
  │                                                │
  │ 音量: [100]  音调: [100]  声道: [0]  偏移: [0] │
  └────────────────────────────────────────────────┘
  ```

  **代码示例**:
  ```csharp
  case "SoundData":
      // 解析现有值
      var soundParts = (prop.value ?? "|||").Split('|');
      string currentFilename = soundParts.Length > 0 ? soundParts[0] : "";
      
      bool hasOptions = prop.soundOptions != null && prop.soundOptions.Length > 0;
      bool canBrowse = prop.allowCustomFile;
      
      group.Height = 220;
      
      var soundPanel = new Panel { Width = 420, Height = 190, Top = 20, Left = 10 };
      
      // 第一行：搜索框 + 浏览按钮
      var lblSearch = new Label { Text = "搜索:", Width = 45, Top = 5, Left = 0 };
      var txtSearch = new TextBox { Width = 200, Top = 3, Left = 45, Name = "SearchBox" };
      
      var btnBrowse = new Button
      {
          Text = "浏览文件...", Width = 100, Top = 2, Left = 260,
          Visible = canBrowse, Name = "BrowseButton"
      };
      btnBrowse.Click += (s, e) =>
      {
          using (var ofd = new OpenFileDialog())
          {
              ofd.Filter = "音频文件|*.wav;*.ogg;*.mp3|所有文件|*.*";
              if (ofd.ShowDialog() == DialogResult.OK)
              {
                  // 添加到 ListView 并选中
                  var lv = soundPanel.Controls.Find("SoundListView", false).FirstOrDefault() as ListView;
                  if (lv != null)
                  {
                      var fileName = Path.GetFileName(ofd.FileName);
                      // 复制到游戏目录？或直接使用文件名？
                      // 这里简化处理，直接使用文件名
                      var item = new ListViewItem(fileName);
                      item.SubItems.Add("(外部文件)");
                      item.Tag = fileName;  // 存储实际值
                      lv.Items.Add(item);
                      item.Selected = true;
                  }
              }
          }
      };
      
      // 第二行：ListView
      var listView = new ListView
      {
          Width = 405, Height = 100, Top = 30, Left = 5,
          View = View.Details, FullRowSelect = true,
          HideSelection = false, Name = "SoundListView"
      };
      listView.Columns.Add("音效名称", 280);
      listView.Columns.Add("类型", 100);
      
      // 填充数据
      if (hasOptions)
      {
          foreach (var opt in prop.soundOptions)
          {
              var item = new ListViewItem(opt);
              item.SubItems.Add("(内置)");
              item.Tag = opt;
              listView.Items.Add(item);
              if (opt == currentFilename) item.Selected = true;
          }
      }
      
      // 搜索过滤
      txtSearch.TextChanged += (s, e) =>
      {
          var keyword = txtSearch.Text.ToLower();
          foreach (ListViewItem item in listView.Items)
          {
              item.Hidden = !string.IsNullOrEmpty(keyword) && 
                            !item.Text.ToLower().Contains(keyword);
          }
      };
      
      // 音量/音调/声道/偏移控件...
      
      soundPanel.Controls.AddRange(new Control[] { lblSearch, txtSearch, btnBrowse, listView });
      inputCtrl = soundPanel;
      break;
  ```

  **Commit**: NO

---

- [ ] 6. Helper端 - GetCurrentUpdates 获取 ListView 选中项

  **What to do**:
  - 获取 ListView 选中的项目
  - 组装完整值

  **代码位置**: `EditorForm.cs:GetCurrentUpdates()`

  ```csharp
  else if (ctrl is Panel soundPanel)
  {
      var listView = soundPanel.Controls.Find("SoundListView", false).FirstOrDefault() as ListView;
      
      string filename = "";
      if (listView != null && listView.SelectedItems.Count > 0)
      {
          filename = listView.SelectedItems[0].Tag as string ?? listView.SelectedItems[0].Text;
      }
      
      // 获取音量/音调/声道/偏移
      var txtVolume = soundPanel.Controls.Find("Volume", false).FirstOrDefault() as TextBox;
      // ...
      
      value = $"{filename}|{volume}|{pitch}|{pan}|{offset}";
  }
  ```

  **Commit**: NO

---

- [ ] 7. 构建验证

  **What to do**:
  - 构建两个项目
  - 测试音效选择和保存

  **Commit**: YES
  - Message: `Helper 添加音效 ListView 选择器 + 搜索 + 文件浏览`
  - Files: `RDLevelEditorAccess/IPC/FileIPC.cs`, `RDEventEditorHelper/EditorForm.cs`

---

## 关键问题

### 文件浏览后的处理

当用户通过浏览按钮选择外部文件后，有两种处理方式：

1. **直接使用文件名**：将文件名存储，假设文件已在游戏目录
2. **复制到游戏目录**：自动将文件复制到游戏的音频目录

**建议**：使用方式1，与原生编辑器行为一致。原生编辑器也是让用户自己管理文件位置。

### 音效名称本地化

原生编辑器使用 `RDString.Get($"enum.SoundEffect.{value}")` 进行本地化。

Helper 可以：
- 简化：直接显示英文名称
- 或：传入本地化字典（需要 Mod 端额外处理）

**建议**：先简化处理，直接显示英文名称。

---

## Success Criteria

### 测试场景

1. **AddClassicBeat.sound**:
   - ListView 显示 BeatSounds 列表
   - 搜索功能过滤列表
   - 浏览按钮可选择外部文件
   - 选择后正确保存

2. **PlaySong.song**:
   - ListView 不显示预设（无选项）
   - 浏览按钮可选择歌曲文件
   - 选择后正确保存

3. **搜索功能**:
   - 输入关键字过滤列表
   - 清空关键字恢复完整列表
