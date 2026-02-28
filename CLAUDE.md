# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RDMods is a Unity C# modding project for **Rhythm Doctor** that adds accessibility features to the level editor. The solution contains two projects that work together via IPC:

- **RDLevelEditorAccess** (.NET Standard 2.1): BepInEx mod that runs inside Unity, providing accessibility features and screen reader support
- **RDEventEditorHelper** (.NET Framework 4.8): Standalone WinForms application for editing event properties with an accessible UI

**Architecture**: The mod runs inside Unity's level editor and communicates with the helper via file-based IPC using `temp/source.json` and `temp/result.json`.

## Build Commands

```bash
# Build entire solution (Debug)
dotnet build RDMods.sln

# Build Release
dotnet build RDMods.sln -c Release

# Build individual projects
dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj
dotnet build RDEventEditorHelper/RDEventEditorHelper.csproj

# Clean
dotnet clean RDMods.sln
```

**Auto-deployment**: `Directory.Build.props` automatically copies build outputs to the game directory:
- Mod DLL → `{GameDir}/BepInEx/plugins/`
- Helper EXE → `{GameDir}/`

The game directory is configured in `Directory.Build.props` as `D:\SteamLibrary\steamapps\common\Rhythm Doctor`.

## Project Structure

```
RDLevelEditorAccess/
├── EditorAccess.cs           # BepInEx plugin entry, Harmony patches, core logic
├── AccessibilityModule.cs    # Public API (AccessibilityBridge) + UnityDispatcher
├── CustomUINavigator.cs      # Disables native UI navigation
├── InputFieldReader.cs.cs    # Text-to-speech for input fields
└── IPC/
    └── FileIPC.cs            # File-based IPC with Helper

RDEventEditorHelper/
├── Program.cs                # Entry point, reads source.json, writes result.json
└── EditorForm.cs             # WinForms property editor UI

agents references/Assembly-CSharp/
└── RDLevelEditor/            # Decompiled game code (170+ files)
    ├── scnEditor.cs          # Main editor controller (~4000 lines)
    ├── LevelEvent_Base.cs    # Base class for all events
    ├── LevelEventInfo.cs     # Event metadata system
    ├── BasePropertyInfo.cs   # Property type system
    └── InspectorPanel.cs     # Property panel base
```

## Key Architecture Concepts

### Game Code Reference

**CRITICAL**: Always check `agents references/Assembly-CSharp/` before modifying code. This folder contains decompiled game code that shows how the level editor works internally.

Key concepts from the game:
- **Tab system**: Song(0), Rows(1), Actions(2), Rooms(3), Sprites(4), Windows(5)
- **onlyUI properties**: Properties marked `onlyUI = true` are NOT saved to level files
- **PropertyInfo types**: Bool, Int, Float, String, Enum, Color, SoundData, Nullable, Array

### IPC Protocol

The mod and helper communicate via JSON files:

1. **Mod → temp/source.json**: Event type and properties
   ```json
   {
     "editType": "event",
     "eventType": "AddClassicBeat",
     "token": "unique-session-id",
     "properties": [
       { "name": "bar", "type": "Int", "value": "1" },
       { "name": "btn", "type": "Button", "methodName": "DoSomething" }
     ]
   }
   ```
   For row editing, use `"editType": "row"` and `"eventType": "MakeRow"`.
   For level settings editing, use `"editType": "settings"`.

2. **Mod launches** `RDEventEditorHelper.exe`

3. **Helper shows** WinForms editor

4. **Helper → temp/result.json**: User action
   - Save: `{ "token": "...", "action": "ok", "updates": { "bar": "2" } }`
   - Execute: `{ "token": "...", "action": "execute", "methodName": "DoSomething" }`
   - Cancel: `{ "token": "...", "action": "cancel" }`

5. **Mod polls** for result, applies changes, deletes result file

The `token` field is used to match responses to requests and prevent race conditions.

### AccessibilityBridge (Public API)

`AccessibilityBridge` in `AccessibilityModule.cs` is the entry point — do NOT call `FileIPC` directly:

```csharp
AccessibilityBridge.Initialize(gameObject);  // Call once on startup (from AccessLogic.Awake)
AccessibilityBridge.EditEvent(levelEvent);   // Open event property editor
AccessibilityBridge.EditRow(rowIndex);       // Open row property editor
AccessibilityBridge.Update();                // Called every frame from AccessLogic.Update()
```

### VirtualMenuState

`AccessLogic` maintains a virtual menu system for keyboard-accessible selection dialogs:

```csharp
private enum VirtualMenuState
{
    None,
    CharacterSelect,   // Adding row/sprite
    EventTypeSelect    // Selecting event type
}
```

When `virtualMenuState != None`, arrow keys navigate the virtual menu instead of the timeline.

### SaveState Pattern

When modifying event or row properties programmatically, always call `SaveState` first to enable undo:

```csharp
scnEditor.instance.SaveState("修改属性");
levelEvent.someProperty = newValue;
```

Without `SaveState`, changes are not persisted to the level file.

### Unity + BepInEx Pattern

The mod uses a two-part initialization:
1. **EditorAccess** (BepInEx plugin): Loads on game start, applies Harmony patches
2. **AccessLogic** (MonoBehaviour): Injected into scene, handles per-frame logic

```csharp
[BepInPlugin("com.hzt.rd-editor-access", "RDEditorAccess", "0.3")]
public class EditorAccess : BaseUnityPlugin
{
    public void Awake()
    {
        var harmony = new Harmony("com.hzt.rd-editor-access");
        harmony.PatchAll();
    }
}

public class AccessLogic : MonoBehaviour
{
    public static AccessLogic Instance { get; private set; }

    public void Awake() { Instance = this; }
    public void Update() { /* per-frame logic */ }
}
```

## Code Style Guidelines

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `EditorAccess`, `FileIPC` |
| Methods | PascalCase | `HandleGeneralUINavigation` |
| Properties | PascalCase | `Instance`, `TargetEventSystem` |
| Private fields | camelCase or _prefix | `allControls`, `_isPolling` |
| Parameters | camelCase | `menuName`, `rootObject` |

### Import Organization

Order alphabetically within groups, separated by blank lines:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

using BepInEx;
using HarmonyLib;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using RDLevelEditor;
```

Use aliases for conflicts:
```csharp
using Button = UnityEngine.UI.Button;           // In Unity code
using Button = System.Windows.Forms.Button;     // In WinForms code
```

### Comments and Documentation

- Chinese comments are standard in this codebase
- Use XML docs for public APIs
- Explain "why" not "what"
- Use `[ModuleName]` prefix in log messages

### Region Blocks

Use double-line style for major sections:

```csharp
// ===================================================================================
// 第一部分：加载器 (Loader)
// ===================================================================================
```

## Unity-Specific Guidelines

### Null Checking (MANDATORY)

Unity objects can be "fake null" - always check before access:

```csharp
if (scnEditor.instance == null) return;
if (menuObj != null && menuObj.activeInHierarchy) { }
```

### Harmony Patching

```csharp
[HarmonyPatch(typeof(scnEditor))]
public static class EditorPatch
{
    [HarmonyPatch("SelectEventControl")]
    [HarmonyPostfix]
    public static void SelectEventControlPostfix(LevelEventControl_Base newControl)
    {
        if (newControl?.levelEvent == null) return;
        Narration.Say(ModUtils.eventSelectI18n(newControl.levelEvent),
                     NarrationCategory.Navigation);
    }
}
```

### Accessibility (Screen Reader Support)

Use the game's `Narration` class:

```csharp
// Navigation feedback (immediate)
Narration.Say("已选中按钮", NarrationCategory.Navigation);

// With position info
Narration.Say("菜单项", NarrationCategory.Navigation,
              itemIndex: 2, itemsLength: 5,
              elementType: ElementType.Button);
```

## WinForms Guidelines

Set accessibility properties for screen readers:

```csharp
using Button = System.Windows.Forms.Button;
using Control = System.Windows.Forms.Control;

var btn = new Button
{
    Text = "确定",
    AccessibleName = "确定按钮",
    AccessibleRole = AccessibleRole.PushButton
};
```

## Error Handling and Logging

```csharp
try
{
    // Risky operation
}
catch (Exception ex)
{
    Debug.LogError($"[ModuleName] 操作失败: {ex.Message}");
    Debug.LogException(ex);  // Full stack trace
}
```

## Git Commit Messages

Use short Chinese descriptions:
```
添加 XX 功能
修复 XX 问题
重构 XX 模块
优化 XX 性能
```
