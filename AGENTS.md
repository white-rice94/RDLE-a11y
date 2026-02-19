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

- `agents references/Assembly-CSharp/RDLevelEditor/` - Level editor classes
- `agents references/Assembly-CSharp/Narration.cs` - Screen reader API

## Build Commands

```bash
# Build everything (Debug)
dotnet build RDMods.sln

# Build Release
dotnet build RDMods.sln -c Release

# Build single project
dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj
dotnet build RDEventEditorHelper/RDEventEditorHelper.csproj
```

**Auto-deployment**: `Directory.Build.props` copies output to game folder automatically.

**Tests**: None currently. To add: `dotnet new xunit -n RDMods.Tests`, then `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`.

## File Organization

```
RDLevelEditorAccess/
├── EditorAccess.cs           # Plugin entry + AccessLogic (combined)
├── AccessibilityModule.cs    # AccessibilityBridge API
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
| Private fields | camelCase | `allControls`, `_isPolling` |
| Parameters | camelCase | `menuName`, `rootObject` |

### Imports

Order alphabetically within groups:

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

// NEVER skip null checks on Unity objects
```

### MonoBehaviour Lifecycle

```csharp
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
        Narration.Say(ModUtils.eventSelectI18n(newControl.levelEvent), NarrationCategory.Navigation);
    }
}

// Register in Awake:
var harmony = new Harmony("com.hzt.rd-editor-access");
harmony.PatchAll();
```

## WinForms (RDEventEditorHelper)

Use type aliases for conflicts:

```csharp
using Button = System.Windows.Forms.Button;
using Control = System.Windows.Forms.Control;
using Form = System.Windows.Forms.Form;
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

1. Mod writes `temp/source.json` (event type + properties)
2. Mod launches `RDEventEditorHelper.exe`
3. Helper shows WinForms editor
4. Helper writes `temp/result.json` (updates or `{}` for cancel)
5. Mod polls for result, applies changes

## Git Commits

- **提交时机**: 完成任务链后立即提交
- **提交信息**: 简短中文描述

```
添加 XX 功能
修复 XX 问题
重构 XX 模块
```
