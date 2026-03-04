# AGENTS.md - Development Guidelines for RDMods

This file provides guidelines for AI agents working on this codebase.

## Project Overview

A Unity C# modding project for **Rhythm Doctor**. Contains two projects:

| Project | Framework | Purpose |
|---------|-----------|---------|
| `RDLevelEditorAccess` | .NET Standard 2.1 | BepInEx mod for level editor accessibility |
| `RDEventEditorHelper` | .NET Framework 4.8 | WinForms helper for event property editing (IPC client) |

**Architecture**: The mod runs inside Unity, the helper is a standalone EXE. They communicate via file-based IPC (`temp/source.json`, `temp/result.json`).

## Game Code Reference

**CRITICAL**: Check `agents references/` BEFORE starting work. This folder contains decompiled game code.

```
agents references/Assembly-CSharp/
├── RDLevelEditor/           # Level editor classes (170+ files)
│   ├── scnEditor.cs         # Main editor controller (~4000 lines)
│   ├── LevelEvent_Base.cs   # Base class for all events
│   ├── LevelEventInfo.cs    # Event metadata system
│   ├── BasePropertyInfo.cs  # Property type system
│   ├── InspectorPanel.cs    # Property panel base
│   └── Timeline.cs          # Timeline component
└── Narration.cs             # Screen reader API
```

Key concepts:
- **Tab system**: Song(0), Rows(1), Actions(2), Rooms(3), Sprites(4), Windows(5)
- **onlyUI properties**: Properties marked `onlyUI = true` are NOT saved to level files (Description, Button)
- **PropertyInfo types**: Bool, Int, Float, String, Enum, Color, SoundData, Nullable, Array

## Build Commands

```bash
# Build everything (Debug)
dotnet build RDLE-a11y.sln

# Build Release
dotnet build RDLE-a11y.sln -c Release

# Build single project
dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj
dotnet build RDEventEditorHelper/RDEventEditorHelper.csproj

# Clean
dotnet clean RDLE-a11y.sln
```

**Auto-deployment**: `Directory.Build.props` copies output to game folder automatically:
- Mod DLL → `{GameDir}/BepInEx/plugins/`
- Helper EXE → `{GameDir}/`

**Tests**: None currently. To add tests:
```bash
dotnet new xunit -n RDMods.Tests -o RDMods.Tests
dotnet sln add RDMods.Tests/RDMods.Tests.csproj
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"
```

## File Organization

```
RDLevelEditorAccess/
├── EditorAccess.cs           # Plugin entry + AccessLogic + Harmony patches
├── AccessibilityModule.cs    # AccessibilityBridge public API
├── CustomUINavigator.cs      # Disables native UI navigation
├── InputFieldReader.cs.cs    # Input field text-to-speech
└── IPC/
    └── FileIPC.cs            # File-based IPC with Helper

RDEventEditorHelper/
├── Program.cs                # Entry point, reads source.json
└── EditorForm.cs             # WinForms property editor UI
```

## Code Style

### Naming

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `EditorAccess`, `FileIPC` |
| Methods | PascalCase | `HandleGeneralUINavigation` |
| Properties | PascalCase | `Instance`, `TargetEventSystem` |
| Private fields | camelCase or _prefix | `allControls`, `_isPolling` |
| Constants | PascalCase or UPPER | `TempDirName`, `SOURCE_FILE` |
| Parameters | camelCase | `menuName`, `rootObject` |

### Imports

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
using Debug = UnityEngine.Debug;                // When conflicting
```

### Region Blocks

Use double-line style for major sections:

```csharp
// ===================================================================================
// 第一部分：加载器 (Loader)
// ===================================================================================

// ===================================================================================
// 第二部分：核心逻辑 (Worker)
// ===================================================================================
```

### Comments

- Chinese comments are standard in this codebase
- Use XML docs for public APIs
- Explain "why" not "what"
- Use `[ModuleName]` prefix in log messages

```csharp
/// <summary>
/// 检查菜单是否激活并执行导航
/// </summary>
private bool CheckAndNavigate(GameObject menuObj, string name)
{
    // 检查菜单是否存在且可见
    if (menuObj != null && menuObj.activeInHierarchy)
    {
        HandleGeneralUINavigation(menuObj, name);
        return true;
    }
    return false;
}
```

## Unity Guidelines

### Null Checking (MANDATORY)

```csharp
// ALWAYS check before access
if (scnEditor.instance == null) return;
if (menuObj != null && menuObj.activeInHierarchy) { }

// NEVER skip null checks on Unity objects - they can be "fake null"
```

### MonoBehaviour Lifecycle

```csharp
public class MyComponent : MonoBehaviour
{
    public static MyComponent Instance { get; private set; }

    private void Awake()
    {
        Instance = this;  // Singleton pattern
        // Initialize components
    }

    private void Update()
    {
        if (scnEditor.instance == null) return;  // Guard clause first
        // Per-frame logic
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;  // Cleanup singleton
    }
}
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
        if (newControl?.levelEvent == null) return;  // Always null-check
        Narration.Say(ModUtils.eventSelectI18n(newControl.levelEvent), NarrationCategory.Navigation);
    }
}

// Register in Awake:
var harmony = new Harmony("com.hzt.rd-editor-access");
harmony.PatchAll();
```

## WinForms (RDEventEditorHelper)

```csharp
using Button = System.Windows.Forms.Button;
using Control = System.Windows.Forms.Control;
using Form = System.Windows.Forms.Form;

// Set accessibility properties for screen readers
var btn = new Button
{
    Text = "确定",
    AccessibleName = "确定按钮",
    AccessibleRole = AccessibleRole.PushButton
};
```

## Error Handling

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

## Logging

```csharp
Debug.Log("[ModuleName] 信息消息");
Debug.LogWarning("[ModuleName] 警告");
Debug.LogError("[ModuleName] 错误");
Debug.LogException(ex);
```

## IPC Protocol

Data flow for external editor:

```
1. Mod → temp/source.json (event type + properties)
   {
     "eventType": "AddClassicBeat",
     "properties": [
       { "name": "bar", "type": "Int", "value": "1" },
       { "name": "btn", "type": "Button", "methodName": "DoSomething" }
     ]
   }

2. Mod launches RDEventEditorHelper.exe

3. Helper shows WinForms editor

4. Helper → temp/result.json
   - Save: { "action": "save", "updates": { "bar": "2" } }
   - Execute: { "action": "execute", "methodName": "DoSomething" }
   - Cancel: {}

5. Mod polls for result, applies changes, deletes result file
```

## Git Commits

- **Timing**: Commit immediately after completing a task chain
- **Message**: Short Chinese description

```
添加 XX 功能
修复 XX 问题
重构 XX 模块
```

## Accessibility Patterns

Use the game's `Narration` class for screen reader support:

```csharp
// Navigation feedback (immediate)
Narration.Say("已选中按钮", NarrationCategory.Navigation);

// Instruction (can be queued)
Narration.Say("按回车确认", NarrationCategory.Instruction);

// With position info
Narration.Say("菜单项", NarrationCategory.Navigation, itemIndex: 2, itemsLength: 5, 
              elementType: ElementType.Button);
```
