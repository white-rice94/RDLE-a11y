# 添加轨道信息编辑支持

## TL;DR

> **快速摘要**: 使用 Shift+Enter 快捷键编辑当前选中的轨道信息，复用现有的 IPC 协议和 Helper。
> 
> **交付物**:
> - Mod 端添加 Shift+Enter 快捷键检测
> - Mod 端提取轨道属性并发送到 Helper
> - Helper 端显示轨道编辑界面
> - Mod 端应用轨道属性修改
> 
> **预估工作量**: Medium

---

## Context

### 轨道数据结构

**LevelEvent_MakeRow** 是轨道的数据类，包含以下可编辑属性：

| 属性 | 类型 | 说明 |
|-----|------|------|
| `rowType` | RowType | 轨道类型 (Classic/Oneshot) |
| `player` | RDPlayer | 玩家 (P1/P2/CPU) |
| `character` | Character | 角色 |
| `cpuMarker` | Character | CPU 标记角色 |
| `customCharacterName` | string | 自定义角色名称 |
| `hideAtStart` | bool | 开始时隐藏 |
| `muteBeats` | bool | 静音节拍 |
| `muteIn1P` | bool | 单人模式静音 |
| `rowToMimic` | int | 模仿的轨道索引 |
| `mimicsRow` | bool | 是否模仿其他轨道 |
| `length` | int? | 轨道长度 (1-7) |
| `pulseSound` | SoundData | 节拍音效 |
| `room` | int | 所在房间 |

### 关键 API

```csharp
// 获取当前选中的轨道索引
int selectedIndex = scnEditor.instance.selectedRowIndex;

// 获取轨道数据
LevelEvent_MakeRow rowData = scnEditor.instance.rowsData[selectedRowIndex];

// 获取当前页面的轨道列表
List<LevelEvent_MakeRow> pageRows = scnEditor.instance.currentPageRowsData;
```

### IPC 协议复用

现有的 IPC 协议用于事件编辑，可以扩展用于轨道编辑：

**source.json**:
```json
{
  "token": "session-guid",
  "editType": "row",  // 新增：区分事件编辑和轨道编辑
  "eventType": "MakeRow",  // 轨道类型
  "properties": [...]
}
```

**result.json**:
```json
{
  "token": "session-guid",
  "action": "ok",
  "updates": {...}
}
```

---

## Work Objectives

### Core Objective
使用 Shift+Enter 快捷键编辑当前选中的轨道信息。

### Definition of Done
- [ ] Shift+Enter 快捷键正确触发轨道编辑
- [ ] Helper 正确显示轨道属性编辑界面
- [ ] 修改后正确应用到轨道
- [ ] 构建成功

---

## TODOs

- [ ] 1. Mod端 - 添加 Shift+Enter 快捷键检测

  **What to do**:
  - 在 `AccessLogic.Update()` 中检测 Shift+Enter
  - 检查当前是否有选中的轨道
  - 调用 FileIPC 启动轨道编辑

  **代码位置**: `EditorAccess.cs:Update()`

  **代码示例**:
  ```csharp
  // 在 Update() 中添加
  if (Input.GetKeyDown(KeyCode.Return) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
  {
      if (scnEditor.instance != null && scnEditor.instance.selectedRowIndex >= 0)
      {
          FileIPC.Instance.StartRowEdit();
      }
  }
  ```

  **Commit**: NO

---

- [ ] 2. Mod端 - FileIPC 添加轨道编辑支持

  **What to do**:
  - 添加 `StartRowEdit()` 方法
  - 提取轨道属性到 PropertyData 列表
  - 修改 source.json 格式，添加 `editType` 字段

  **代码位置**: `FileIPC.cs`

  **代码示例**:
  ```csharp
  public void StartRowEdit()
  {
      var editor = scnEditor.instance;
      if (editor == null || editor.selectedRowIndex < 0) return;
      
      var rowData = editor.rowsData.ElementAtOrDefault(editor.selectedRowIndex);
      if (rowData == null) return;
      
      _currentEditType = EditType.Row;  // 新增枚举
      _currentRow = rowData;
      
      // 提取轨道属性
      var properties = ExtractRowProperties(rowData);
      
      // 生成 source.json
      var sourceData = new SourceData
      {
          token = _sessionToken,
          editType = "row",  // 新增字段
          eventType = "MakeRow",
          properties = properties
      };
      
      WriteSourceFile(sourceData);
      LaunchHelper();
      StartPolling();
  }
  
  private List<PropertyData> ExtractRowProperties(LevelEvent_MakeRow row)
  {
      var list = new List<PropertyData>();
      
      // rowType (Enum)
      list.Add(new PropertyData
      {
          name = "rowType",
          displayName = "轨道类型",
          type = "Enum",
          value = row.rowType.ToString(),
          options = Enum.GetNames(typeof(RowType))
      });
      
      // player (Enum)
      list.Add(new PropertyData
      {
          name = "player",
          displayName = "玩家",
          type = "Enum",
          value = row.player.ToString(),
          options = Enum.GetNames(typeof(RDPlayer))
      });
      
      // character (Enum + Custom)
      list.Add(new PropertyData
      {
          name = "character",
          displayName = "角色",
          type = "Character",  // 新类型：角色选择
          value = row.character.ToString(),
          customName = row.customCharacterName
      });
      
      // hideAtStart (Bool)
      list.Add(new PropertyData
      {
          name = "hideAtStart",
          displayName = "开始时隐藏",
          type = "Bool",
          value = row.hideAtStart ? "true" : "false"
      });
      
      // muteBeats (Bool)
      list.Add(new PropertyData
      {
          name = "muteBeats",
          displayName = "静音节拍",
          type = "Bool",
          value = row.muteBeats ? "true" : "false"
      });
      
      // pulseSound (SoundData)
      list.Add(new PropertyData
      {
          name = "pulseSound",
          displayName = "节拍音效",
          type = "SoundData",
          value = $"{row.pulseSound.filename}|{row.pulseSound.volume}|{row.pulseSound.pitch}|{row.pulseSound.pan}|{row.pulseSound.offset}",
          soundOptions = RDEditorConstants.BeatSounds.Select(s => s.ToString()).ToArray(),
          allowCustomFile = true,
          itsASong = false
      });
      
      // room (Enum)
      list.Add(new PropertyData
      {
          name = "room",
          displayName = "房间",
          type = "Enum",
          value = row.room.ToString(),
          options = new[] { "房间1", "房间2", "房间3", "房间4" }
      });
      
      return list;
  }
  ```

  **Commit**: NO

---

- [ ] 3. Mod端 - SourceData 添加 editType 字段

  **What to do**:
  - 修改 `SourceData` 类添加 `editType` 字段
  - 修改 `ResultData` 处理逻辑

  **代码位置**: `FileIPC.cs`

  **Commit**: NO

---

- [ ] 4. Mod端 - 应用轨道属性修改

  **What to do**:
  - 添加 `ApplyRowUpdates()` 方法
  - 处理各种属性类型的修改

  **代码位置**: `FileIPC.cs`

  **代码示例**:
  ```csharp
  private void ApplyRowUpdates(LevelEvent_MakeRow row, Dictionary<string, string> updates)
  {
      if (row == null || updates == null) return;
      
      foreach (var update in updates)
      {
          string key = update.Key;
          string strVal = update.Value;
          
          try
          {
              switch (key)
              {
                  case "rowType":
                      row.rowType = (RowType)Enum.Parse(typeof(RowType), strVal);
                      break;
                  case "player":
                      row.player = (RDPlayer)Enum.Parse(typeof(RDPlayer), strVal);
                      break;
                  case "character":
                      // 处理角色选择
                      if (strVal == "Custom")
                      {
                          row.character = Character.Custom;
                          // customCharacterName 需要单独处理
                      }
                      else
                      {
                          row.character = (Character)Enum.Parse(typeof(Character), strVal);
                      }
                      break;
                  case "customCharacterName":
                      row.customCharacterName = strVal;
                      break;
                  case "hideAtStart":
                      row.hideAtStart = strVal == "true";
                      break;
                  case "muteBeats":
                      row.muteBeats = strVal == "true";
                      break;
                  case "pulseSound":
                      // 解析 SoundData 格式
                      var parts = strVal.Split('|');
                      row.pulseSound.filename = parts.Length > 0 ? parts[0] : "Shaker";
                      row.pulseSound.volume = parts.Length > 1 && int.TryParse(parts[1], out int v) ? v : 100;
                      row.pulseSound.pitch = parts.Length > 2 && int.TryParse(parts[2], out int p) ? p : 100;
                      row.pulseSound.pan = parts.Length > 3 && int.TryParse(parts[3], out int pn) ? pn : 0;
                      row.pulseSound.offset = parts.Length > 4 && int.TryParse(parts[4], out int o) ? o : 0;
                      break;
                  case "room":
                      row.room = int.Parse(strVal);
                      break;
              }
          }
          catch (Exception ex)
          {
              Debug.LogWarning($"[FileIPC] 轨道属性 {key} 转换失败: {ex.Message}");
          }
      }
      
      // 更新 UI
      scnEditor.instance.tabSection_rows.UpdateUI();
  }
  ```

  **Commit**: NO

---

- [ ] 5. Helper端 - PropertyData 添加新字段

  **What to do**:
  - 添加 `customName` 字段（角色选择专用）
  - 添加 `editType` 字段到 SourceData

  **代码位置**: `EditorForm.cs`, `Program.cs`

  **Commit**: NO

---

- [ ] 6. Helper端 - BuildUI 添加角色选择控件

  **What to do**:
  - 添加 `Character` 类型处理
  - 显示角色下拉菜单 + 自定义名称输入框

  **代码位置**: `EditorForm.cs:BuildUI()`

  **代码示例**:
  ```csharp
  case "Character":
      // 角色选择：下拉菜单 + 自定义名称输入框
      var charPanel = new Panel { Width = 420, Height = 55, Top = 20, Left = 10 };
      
      var cmbChar = new ComboBox
      {
          Width = 200, Top = 3, Left = 75, DropDownStyle = ComboBoxStyle.DropDownList,
          Name = "CharacterDropdown"
      };
      // 添加常用角色
      cmbChar.Items.AddRange(new[] { "Samurai", "Boy", "Girl", "Custom", "..." });
      cmbChar.SelectedItem = prop.value;
      
      var txtCustomName = new TextBox
      {
          Text = prop.customName ?? "",
          Width = 200, Top = 28, Left = 75, Name = "CustomCharacterName",
          Visible = prop.value == "Custom"
      };
      
      cmbChar.SelectedIndexChanged += (s, e) =>
      {
          txtCustomName.Visible = cmbChar.SelectedItem?.ToString() == "Custom";
      };
      
      charPanel.Controls.Add(new Label { Text = "角色:", Width = 70, Top = 5, Left = 0 });
      charPanel.Controls.Add(cmbChar);
      charPanel.Controls.Add(new Label { Text = "自定义名:", Width = 70, Top = 30, Left = 0, Visible = txtCustomName.Visible });
      charPanel.Controls.Add(txtCustomName);
      
      inputCtrl = charPanel;
      break;
  ```

  **Commit**: NO

---

- [ ] 7. Helper端 - Program.cs 处理 editType

  **What to do**:
  - 读取 `editType` 字段
  - 根据类型设置窗口标题

  **代码位置**: `Program.cs`

  **Commit**: NO

---

- [ ] 8. 构建验证

  **What to do**:
  - 构建两个项目
  - 测试轨道编辑功能

  **Commit**: YES
  - Message: `添加轨道信息编辑支持 (Shift+Enter)`
  - Files: `RDLevelEditorAccess/EditorAccess.cs`, `RDLevelEditorAccess/IPC/FileIPC.cs`, `RDEventEditorHelper/EditorForm.cs`, `RDEventEditorHelper/Program.cs`

---

## 关键问题

### 1. 角色列表

游戏有大量角色（47+），Helper 需要显示完整的角色列表。

**解决方案**: 从 `RDEditorConstants.AvailableCharacters` 获取角色列表，传递给 Helper。

### 2. pulseSound 是 SoundData 类型

`LevelEvent_MakeRow.pulseSound` 是 `SoundData` 类型（不是 `SoundDataStruct`），需要特殊处理。

**解决方案**: 使用反射或直接访问属性。

### 3. 轨道类型切换

切换 `rowType` 会删除轨道上的所有事件，需要确认对话框。

**解决方案**: 先实现基本编辑，后续再添加确认对话框。

---

## Success Criteria

### 测试场景

1. **基本编辑**:
   - 选中一个轨道
   - 按 Shift+Enter 打开编辑器
   - 修改角色、静音等属性
   - 确认修改正确应用

2. **节拍音效编辑**:
   - 编辑 pulseSound 属性
   - 选择预设音效或浏览外部文件
   - 确认修改正确应用

3. **无选中轨道**:
   - 没有选中轨道时按 Shift+Enter
   - 不应该有任何反应
