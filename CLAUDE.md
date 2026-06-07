# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RDMods: Unity C# modding project for **Rhythm Doctor**, adds accessibility to level editor. Two projects via IPC:

- **RDLevelEditorAccess** (.NET Standard 2.1): BepInEx mod inside Unity, screen reader support
- **RDEventEditorHelper** (.NET Framework 4.8): Standalone WinForms app for accessible event editing

**Architecture**: Mod ↔ Helper via file-based IPC: `temp/source.json` / `temp/result.json`.

## Setup

Copy `Directory.Build.user.props.example` → `Directory.Build.user.props`, set `<GameDir>` to RD install path.

## Build Commands

```bash
dotnet build RDLE-a11y.sln              # Debug (auto-deploys to GameDir)
dotnet build RDLE-a11y.sln -c Release
dotnet build RDLevelEditorAccess/RDLevelEditorAccess.csproj  # Individual project
dotnet build RDEventEditorHelper/RDEventEditorHelper.csproj
dotnet clean RDLE-a11y.sln
./release.sh                            # Build Release + package into release/main/
```

**Auto-deployment**: `Directory.Build.props` copies on every build:
- Mod DLL → `{GameDir}/BepInEx/plugins/`
- Helper EXE → `{GameDir}/`

## Project Structure

```
RDLevelEditorAccess/
├── EditorAccess.cs           # BepInEx plugin entry, Harmony patches, AccessLogic MonoBehaviour (core, ~4000 lines)
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

docs/
├── manual-cn.md / manual-en.md        # User manuals (also .html versions)
├── changelog-cn.txt / changelog-en.txt
```

## Keyboard Shortcuts

| Shortcut | Function |
|----------|----------|
| **Insert** or **F2** | Add event at current timeline position |
| **Ctrl+Insert** or **Ctrl+F2** | Add row/sprite (context-dependent) |
| **Return** | Activate selected item / Open property editor |
| **Arrow Keys** | Navigate timeline / Move events |
| **Alt+Arrow** | Fine adjustment (0.01 beat) |
| **Shift+Arrow** | Medium adjustment (0.1 beat) |
| **Plain Arrow** | Coarse adjustment (1/denominator beat) |
| **Alt+G** | Open grid size menu |
| **Tab** | Navigate UI elements in menus |

`virtualMenuState != None`: arrow keys navigate virtual menus, not timeline.

## Key Architecture Concepts

### AccessLogic — core of mod

`AccessLogic` (`EditorAccess.cs`): MonoBehaviour injected into scene. `Update()` dispatches to one of three mutually exclusive handlers:

- **`HandleGeneralUINavigation`** — Unity UI menu open; Tab/Arrow/Enter in UI elements
- **`HandleTimelineNavigation`** — default; event selection, movement, insertion/deletion
- **`HandleVirtualMenu`** — `virtualMenuState != None`; arrow keys drive keyboard menu

Key fields: `_editCursor` (BarAndBeat), `virtualMenuState` (VirtualMenuState enum), `virtualMenuIndex`, `virtualSelection` (multi-event set).

### VirtualMenuState

```csharp
private enum VirtualMenuState
{
    None,
    CharacterSelect,   // Adding row/sprite
    EventTypeSelect,   // Selecting event type
    LinkSelect,        // Selecting hyperlink target
    EventChainSelect,  // Selecting saved event chain (;)
    ConditionalSelect, // Browsing/toggling conditions on an event
    GridSelect         // Grid size selection
}
```

### Game Code Reference

**CRITICAL**: Check `agents references/Assembly-CSharp/` before modifying code. Old版 subfolders = old decompiled code; unmarked = latest.

Key concepts:
- **Tab system**: Song(0), Rows(1), Actions(2), Rooms(3), Sprites(4), Windows(5)
- **onlyUI properties**: `onlyUI = true` → NOT saved to level files
- **PropertyInfo types**: Bool, Int, Float, String, Enum, Color, SoundData, Nullable, Array

### SoundData Panel Sentinel Values

`CreateSoundDataPanel()` in `EditorForm.cs`:

| Tag | Meaning | Serialized as |
|-----|---------|---------------|
| `"__track_default__"` | Use track default (nullable SoundData) | `""` |
| `"__manual__"` | Manual filename input mode | value from `ManualInput` TextBox |

Guard against both sentinels in ListView selection / sound preview.

### Localization

Game built-in keys in `agents references/localization/` (`.bytes` files). Check before creating `eam.*` keys; use native keys when available.

Native key locations:
- Enum names: `Enums.bytes` — e.g. `enum.ConditionalType.Custom`
- Editor UI labels: `LevelEditor.bytes` — e.g. `editor.Conditionals.expression`
- Character names: `Enums.bytes` — e.g. `enum.Character.Ian.short`

**Supported languages for `eam.*` keys**: Chinese (`_zh`), English (`_en`), French (`_fr`). French detection via `IsFrench` property (checks if `RDString.Get("editor.continue") == "Continuer"`). Selection logic in `RDStringPatch.GetPrefix`:

```csharp
var dict = RDString.isChinese ? _zh : (IsFrench ? _fr : _en);
```

- `RDString.Get(key)` — goes through `RDStringPatch`, supports `eam.*`
- `RDString.GetWithCheck(key, out bool exists)` — bypasses patch; use for native keys existence check. Do **not** use for `eam.*` keys.

**Helper `displayName` localization**: pass single-language text via `RDString.Get("eam.*")` — no bilingual concatenation needed.

### IPC Protocol

1. **Mod → temp/source.json**:
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
   - Row editing: `"editType": "row"`, `"eventType": "MakeRow"`
   - Level settings: `"editType": "settings"`
   - `levelAudioFiles`: audio files in level dir (for SoundData properties)

2. Mod launches `RDEventEditorHelper.exe`
3. Helper shows WinForms editor
4. **Helper → temp/result.json**:
   - Save: `{ "token": "...", "action": "ok", "updates": { "bar": "2" } }`
   - Execute: `{ "token": "...", "action": "execute", "methodName": "DoSomething" }`
   - Cancel: `{ "token": "...", "action": "cancel" }`
5. Mod polls for result, applies changes, deletes result file

`token` matches responses to requests, prevents race conditions.

`action: "execute"`: mod looks for `methodName` on `LevelEvent` (reflection), falls back to `scnEditor.instance.inspectorPanelManager.GetCurrent()` (hardcoded panel buttons like `BreakIntoOneshotBeats`). Hardcoded panel buttons registered in `HardcodedButtons` dict in `FileIPC.cs`, appended with `type: "Button"`.

#### Dynamic UI Visibility

1. **Helper → temp/validateVisibility.json**:
   ```json
   { "token": "...", "enableIfExpression": "rhythm == 'X'", "currentValues": { "rhythm": "X", "bar": "1" } }
   ```
2. **Mod → temp/validateVisibilityResponse.json**:
   ```json
   { "token": "...", "isVisible": true }
   ```

Properties show/hide real-time without losing focus. Mod announces changes via low-priority screen reader notifications.

### AccessibilityBridge (Public API)

`AccessibilityBridge` in `AccessibilityModule.cs` — entry point; do NOT call `FileIPC` directly:

```csharp
AccessibilityBridge.Initialize(gameObject);       // Call once on startup (from AccessLogic.Awake)
AccessibilityBridge.EditEvent(levelEvent);         // Open event property editor
AccessibilityBridge.EditRow(rowIndex);             // Open row property editor
AccessibilityBridge.EditSettings();               // Open level settings editor
AccessibilityBridge.CreateCondition(targetEvent); // Open condition creator (attaches to targetEvent)
AccessibilityBridge.EditCondition(localId);       // Open condition editor for existing condition
AccessibilityBridge.GridCustomInput(denominator); // Open custom grid size dialog
AccessibilityBridge.Update();                     // Called every frame from AccessLogic.Update()
AccessibilityBridge.IsEditing                     // True while Helper window is open
AccessibilityBridge.SetConditionalSavedCallback(Action<int> callback); // Notify when condition saved
```

### ModUtils Utilities

```csharp
ModUtils.eventNameI18n(LevelEvent_Base evt)      // Get localized event name
ModUtils.eventSelectI18n(LevelEvent_Base evt)    // Get selection announcement text
ModUtils.FormatBarAndBeat(BarAndBeat bb)         // Format bar/beat display
ModUtils.FormatBeat(float beat)                  // Format beat with smart rounding
```

### InputFieldReader

TTS system for input fields (`InputFieldReader.cs`):
- State diffing: compare prev/current text + caret
- Character-by-character reading on type/delete
- Caret movement: read char at cursor
- Password: announce "星号"
- Focus detection: prevent false announcements on focus change

### SaveState Pattern

Wrap programmatic property changes in `SaveStateScope` for undo:

```csharp
using (new SaveStateScope())
{
    levelEvent.someProperty = newValue;
}
```

Call `UpdateUIInternal()` **outside** scope — UI updates must not be part of saved state.

### Unity + BepInEx Pattern

Two-part init: `EditorAccess` (BepInEx plugin, Harmony patches in `Awake`) + `AccessLogic` (MonoBehaviour, per-frame `Update`). All patches use `[HarmonyPostfix]`. `RDStringPatch` intercepts `RDString.Get` to inject `eam.*` keys.

### Grid Quantization

Snap (C / Ctrl+/) and cursor step (`,`/`.`) use `scnEditor.instance.denominator` (1/N beat). Alt+G opens `GridSelect` virtual menu. Custom denominator via Helper dialog (`editType: "gridCustom"`). Access private fields via reflection cache in `EditorAccess.cs`; write via public `CycleSnapValues(int)`.

## Code Style

- Chinese comments standard; XML docs for public APIs; `[ModuleName]` prefix in logs
- Private fields: camelCase or `_prefix`; methods/classes/properties: PascalCase
- Import groups: System → BepInEx/Harmony → Unity → RDLevelEditor, alphabetical within
- `using Button = UnityEngine.UI.Button;` / `using Button = System.Windows.Forms.Button;` for namespace conflicts
- Section separators: `// ==================...`

## Unity-Specific

### Null Checking (MANDATORY)

Unity objects can be "fake null":

```csharp
if (scnEditor.instance == null) return;
if (menuObj != null && menuObj.activeInHierarchy) { }
```

### Screen Reader

```csharp
Narration.Say("已选中按钮", NarrationCategory.Navigation);
Narration.Say("菜单项", NarrationCategory.Navigation,
              itemIndex: 2, itemsLength: 5,
              elementType: ElementType.Button);
```

## Debugging

- Mod logs: `{GameDir}/BepInEx/LogOutput.log`
- Helper logs: `{GameDir}/temp/RDEventEditorHelper.log` (overwrites on each launch)
- Manual Helper test: create `temp/source.json`, run `RDEventEditorHelper.exe` from `{GameDir}`

## Git Commit Messages

Short Chinese: `添加 XX 功能` / `修复 XX 问题` / `重构 XX 模块`
