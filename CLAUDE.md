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
dotnet build RDLE-a11y.sln

# Build Release
dotnet build RDLE-a11y.sln -c Release

# Build individual projects
dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj
dotnet build RDEventEditorHelper/RDEventEditorHelper.csproj

# Clean
dotnet clean RDLE-a11y.sln
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
├── InputFieldReader.cs       # Text-to-speech for input fields
└── IPC/
    └── FileIPC.cs            # File-based IPC with Helper

RDEventEditorHelper/
├── Program.cs                # Entry point, reads source.json, writes result.json
└── EditorForm.cs             # WinForms property editor UI

agents references/Assembly-CSharp/
└── RDLevelEditor/            # Decompiled game code (349 files)
    ├── scnEditor.cs          # Main editor controller (~4000 lines)
    ├── LevelEvent_Base.cs    # Base class for all events
    ├── LevelEventInfo.cs     # Event metadata system
    ├── BasePropertyInfo.cs   # Property type system
    └── InspectorPanel.cs     # Property panel base
```

## Keyboard Shortcuts

The mod provides extensive keyboard navigation for accessibility:

| Shortcut | Function |
|----------|----------|
| **Insert** | Add event at current timeline position |
| **Ctrl+Insert** | Add row/sprite (context-dependent) |
| **Return** | Activate selected item / Open property editor |
| **Arrow Keys** | Navigate timeline / Move events |
| **Alt+Arrow** | Fine adjustment (0.01 beat) |
| **Shift+Arrow** | Medium adjustment (0.1 beat) |
| **Plain Arrow** | Coarse adjustment (1 beat) |
| **Tab** | Navigate UI elements in menus |

When `virtualMenuState != None`, arrow keys navigate virtual menus instead of the timeline.

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
     ],
     "levelAudioFiles": ["song.ogg", "sfx.wav"]
   }
   ```
   - For row editing: `"editType": "row"` and `"eventType": "MakeRow"`
   - For level settings: `"editType": "settings"`
   - `levelAudioFiles`: Array of audio files in level directory (for SoundData properties)

2. **Mod launches** `RDEventEditorHelper.exe`

3. **Helper shows** WinForms editor

4. **Helper → temp/result.json**: User action
   - Save: `{ "token": "...", "action": "ok", "updates": { "bar": "2" } }`
   - Execute: `{ "token": "...", "action": "execute", "methodName": "DoSomething" }`
   - Cancel: `{ "token": "...", "action": "cancel" }`

5. **Mod polls** for result, applies changes, deletes result file

The `token` field is used to match responses to requests and prevent race conditions.

#### Dynamic UI Visibility System

The Helper supports dynamic property visibility via bidirectional IPC:

1. **Helper → temp/validateVisibility.json**: Request to evaluate `enableIf` condition
   ```json
   {
     "token": "...",
     "enableIfExpression": "rhythm == 'X'",
     "currentValues": { "rhythm": "X", "bar": "1" }
   }
   ```

2. **Mod → temp/validateVisibilityResponse.json**: Evaluation result
   ```json
   {
     "token": "...",
     "isVisible": true
   }
   ```

This allows properties to show/hide in real-time as the user edits, without losing focus. The mod announces visibility changes via low-priority screen reader notifications.

### AccessibilityBridge (Public API)

`AccessibilityBridge` in `AccessibilityModule.cs` is the entry point — do NOT call `FileIPC` directly:

```csharp
AccessibilityBridge.Initialize(gameObject);  // Call once on startup (from AccessLogic.Awake)
AccessibilityBridge.EditEvent(levelEvent);   // Open event property editor
AccessibilityBridge.EditRow(rowIndex);       // Open row property editor
AccessibilityBridge.EditSettings();          // Open level settings editor
AccessibilityBridge.Update();                // Called every frame from AccessLogic.Update()
```

### ModUtils Utilities

Static helper class in `EditorAccess.cs` with formatting and localization methods:

```csharp
ModUtils.eventNameI18n(LevelEvent_Base evt)      // Get localized event name
ModUtils.eventSelectI18n(LevelEvent_Base evt)    // Get selection announcement text
ModUtils.FormatBarAndBeat(BarAndBeat bb)         // Format bar/beat display
ModUtils.FormatBeat(float beat)                  // Format beat with smart rounding
```

### Navigation System

`AccessLogic` implements three distinct navigation handlers:

1. **HandleGeneralUINavigation**: For Unity UI menus (Tab, Arrow keys, Enter)
   - Activates when Unity UI menus are open
   - Provides keyboard navigation for buttons, dropdowns, etc.

2. **HandleTimelineNavigation**: For timeline operations
   - Event selection and movement
   - Timeline scrolling
   - Event insertion/deletion

3. **HandleVirtualMenu**: For custom selection dialogs
   - Character/sprite selection
   - Event type selection
   - Uses `VirtualMenuState` enum to track active menu

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

### InputFieldReader

`InputFieldReader.cs` implements a sophisticated text-to-speech system for input fields:

- **State diffing**: Compares previous/current text and caret position
- **Character-by-character reading**: Announces typed/deleted characters
- **Caret movement**: Reads character at cursor when navigating
- **Password support**: Announces "星号" for password fields
- **Focus detection**: Prevents false announcements on focus changes

The reader monitors all TMP_InputField components and provides real-time feedback for screen reader users.

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
[BepInPlugin("com.hzt.rd-editor-access", "RDEditorAccess", "1.0")]
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

### Harmony Patches

The mod applies multiple Harmony patches to intercept game behavior:

| Patch Class | Target Method | Purpose |
|-------------|---------------|---------|
| **EditorPatch** | SelectEventControl | Announce event selection |
| **EditorPatch** | AddEventControlToSelection | Announce multi-selection |
| **TabSectionPatch** | ChangePage | Announce tab changes |
| **TimelinePatch** | PreviousPage/NextPage | Announce timeline navigation |
| **PastePatch** | Paste | Announce paste operations |
| **RDStringPatch** | Get | Inject localized strings |

All patches use `[HarmonyPostfix]` to run after the original method, ensuring game functionality is preserved.

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

**Debug Mode**: The Helper application supports DEBUG mode. When enabled, it writes detailed logs to `RDEventEditorHelper.log` in the game directory.

## Git Commit Messages

Use short Chinese descriptions:
```
添加 XX 功能
修复 XX 问题
重构 XX 模块
优化 XX 性能
```
