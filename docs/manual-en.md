# Rhythm Doctor Level Editor Accessibility Mod User Manual

------

## Table of Contents

- [Rhythm Doctor Level Editor Accessibility Mod User Manual](#rhythm-doctor-level-editor-accessibility-mod-user-manual)
  - [Table of Contents](#table-of-contents)
  - [0. TL;DR Version](#0-tldr-version)
  - [1. Basic Introduction](#1-basic-introduction)
    - [1.1 What is this?](#11-what-is-this)
    - [1.2 Main Features](#12-main-features)
    - [1.3 Important Notes](#13-important-notes)
  - [2. Installation](#2-installation)
    - [2.1 System Requirements](#21-system-requirements)
    - [2.2 Installation Steps](#22-installation-steps)
  - [3. Quick Start](#3-quick-start)
    - [3.1 Editor Basic Concepts](#31-editor-basic-concepts)
      - [3.1.1 Tabs](#311-tabs)
      - [3.1.2 Events](#312-events)
      - [3.1.3 Rows](#313-rows)
      - [3.1.4 Rooms](#314-rooms)
    - [3.2 First Launch](#32-first-launch)
    - [3.3 Preparation Before Creating a Level](#33-preparation-before-creating-a-level)
  - [4. Operation Instructions](#4-operation-instructions)
    - [4.1 Menu/Dialog Navigation](#41-menudialog-navigation)
    - [4.2 Timeline Navigation](#42-timeline-navigation)
    - [4.3 Creating Rows and Events](#43-creating-rows-and-events)
    - [4.4 Edit Cursor](#44-edit-cursor)
    - [4.5 Quick Event Movement](#45-quick-event-movement)
    - [4.6 Editing Level Metadata](#46-editing-level-metadata)
  - [5. External Editor (RDEventEditorHelper)](#5-external-editor-rdeventeditorhelper)
    - [5.1 What is this?](#51-what-is-this)
    - [5.2 How to use?](#52-how-to-use)
  - [6. Known Issues and Limitations](#6-known-issues-and-limitations)
  - [7. FAQ](#7-faq)
    - [Why isn't the mod loading?](#why-isnt-the-mod-loading)
    - [Why won't the helper open?](#why-wont-the-helper-open)
    - [Why does the game freeze after the save dialog pops up?](#why-does-the-game-freeze-after-the-save-dialog-pops-up)
    - [Why do all native editor shortcuts stop working after I click cancel in the save dialog when exiting the level editor?](#why-do-all-native-editor-shortcuts-stop-working-after-i-click-cancel-in-the-save-dialog-when-exiting-the-level-editor)
    - [What's in the levels folder?](#whats-in-the-levels-folder)
  - [8. Appendix](#8-appendix)
    - [8.1 Native Editor Common Shortcuts Reference](#81-native-editor-common-shortcuts-reference)
    - [8.2 Mod Shortcuts Reference](#82-mod-shortcuts-reference)
    - [8.3 Contact Information](#83-contact-information)


------
## 0. TL;DR Version

If you don't like reading my ~~time-wasting nonsense~~ professional hardcore user manual, or if you prefer to explore on your own, here are some important sections. I think it's still better to read them.

- [1.3 Important Notes](#13-important-notes)
- [6. Known Issues and Limitations](#6-known-issues-and-limitations)
- [8. Appendix](#8-appendix)

## 1. Basic Introduction

### 1.1 What is this?

That's a good question. This proves you've touched upon the most fundamental and important core concept of this project. So what exactly is it?

Isn't that obvious? The name says it all. No need to ask such simple questions. What? You thought I would actually write the user manual in a serious and formal tone? No, no, no, that's not my style. If I really wanted to write seriously, I might as well let AI help me write it - it would do a better job than me anyway.

Well, after my detailed introduction, I'm sure you already know what this project is for, so let's continue. ~~Actually, I didn't say anything at all~~

### 1.2 Main Features

- No mouse required (full keyboard navigation support)
- Complete voice feedback (shares the same narration module as the native game)
- Provides Chinese and English localization support (automatically determined based on game settings)
- More to be added when I think of them

### 1.3 Important Notes

To prevent you from thinking the manual is too long and not wanting to read it, I need to state some important notes upfront.

1. This mod currently has certain limitations, especially for visual features (such as window dance, sprites, etc.) which are not yet fully supported.
2. Although the mod is relatively stable, it's still recommended that you develop a habit of **saving frequently**. I've encountered inexplicable crashes before (see details [here](#why-does-the-game-freeze-after-the-save-dialog-pops-up)), but fortunately I had this good habit. Well, I was forced to develop it.

## 2. Installation

### 2.1 System Requirements

- Windows system (don't ask me which specific version, as long as the game can run, the mod should probably run too)
- Rhythm Doctor game (1.0+, standalone editor not supported)
- Your screen reader (this goes without saying)

### 2.2 Installation Steps

After extracting, besides this manual, you should see two folders: **levels** and **main**. Go into the main folder, select all, copy to the directory where your rhythm doctor.exe is located, and paste. That's it. The next time you run the game, the mod will run smoothly.

You ask what's in the levels folder? Well, go in and see for yourself. Or wait for me to tell you later.

## 3. Quick Start

### 3.1 Editor Basic Concepts

Before really starting to explain how to use the mod, you need to understand some basic concepts of the native editor. I'm actually not very knowledgeable in this area either, so I'll just briefly summarize. If you don't want to read it, just skip ahead.

#### 3.1.1 Tabs

There are six tabs in the editor, which can be switched by pressing number keys 1-6 on the main keyboard. They are: 1 Sounds, 2 Rows, 3 Actions, 4 Rooms, 5 Sprites, and 6 Windows. Each tab manages different types of events.

#### 3.1.2 Events

You can think of events as instructions that tell the game what to do and when.

Various actions in a level can be called events. For example, playing music, nurse voice prompts, various effects, dialogues, and various beats.

#### 3.1.3 Rows

Rows are equivalent to the “patients” you need to “treat”. All beat events occur on rows.

#### 3.1.4 Rooms

Rooms are used to control screen splitting and layout display, somewhat similar to split-screen. A level can only have four rooms at the same time. Each room can only have a maximum of four rows. In other words, a level can have a maximum of 16 rows.

### 3.2 First Launch

In the main menu, find **Level Editor, this feature is still not compatible with narration.**, and press enter to enter.

Wait, didn't you say this mod makes it support narration? Well, don't worry about those details.

Anyway, if this is your first time entering, you'll need to agree to a terms of service. Press down arrow to read it, then find agree and press enter. PS: Wait, what does this have to do with "A Dance of Fire and Ice"?

Ahem, let's get back to the topic. When you press enter, you'll definitely notice that the music from level1 suddenly starts playing, but don't panic. This is because the editor loads a simple demo level by default when opening, and the level starts directly mainly because the game's operation keys conflict with the mod's. But it's not a big deal, it only happens occasionally with a few dialogs. You just need to calmly press P, and the level will pause. Or would you rather listen to the song?

After agreeing to the terms, you can try pressing left and right arrows to browse the events in the current tab. However, this demo level only has three events, located in the Sounds, Rows, and Actions tabs. If you can hear the screen reader announce the event name and location, that proves you've successfully entered the editor.

### 3.3 Preparation Before Creating a Level

If you can't wait to show off your skills and create a level, please don't rush. Before that, you need to understand the following preparation work:

1. Create a new folder anywhere on your computer that you like.
2. Put the music, sound effects, and other resources used in the level into this folder.
3. In the editor, press ctrl+n to create a new level, and set the save location to the folder you just created.
4. Alright, now you can start.

## 4. Operation Instructions

### 4.1 Menu/Dialog Navigation

In any dialog/menu, you can press arrow keys to browse controls, and enter to confirm. Press tab to switch between clickable items.

When in the level editor main interface, you can press f10 to open/close the editor's main menu. In this menu, you can create new levels, open levels, publish levels, and other operations. Of course, most of these operations have more convenient shortcuts, see the appendix.

### 4.2 Timeline Navigation

On the editing page, left/right arrow keys switch between events in the current tab, enter jumps to the event's location and plays, ctrl+enter edits event properties.

However, there are two exceptions. When you're in the Rows or Sprites tab, you need to first press up/down arrows to switch to a row or sprite, then you can press left/right arrows to browse events. At the same time, you can also press shift+enter to edit rows. Editing sprites is not currently supported.

### 4.3 Creating Rows and Events

Press insert under a tab, and a menu will pop up listing the available event types for the current tab. Use up/down arrows to select, and press enter to create. After creation, the property editor will automatically open. If you're in the Rows tab, the created event will be placed on the currently selected row by default.

In the Rows or Sprites tab, press ctrl+insert to create a new row or sprite, the operation is the same as creating an event.

### 4.4 Edit Cursor

To make it more convenient for visually impaired players (actually, it's just me for now), the mod introduces the edit cursor feature. The edit cursor is a player-controllable temporary anchor point that doesn't move with playback and can be freely adjusted in position, making it convenient for players to quickly locate when creating events and other scenarios. Below are its shortcut key instructions.
PS: I know some people definitely didn't understand the definition of edit cursor above, but trust me, after reading the shortcut key instructions you should understand... I hope...

- / (slash), move the edit cursor to the current playback position
- shift+slash, announce the current position of the edit cursor
- ctrl+slash, snap the edit cursor to the nearest half beat
- alt+slash, jump to the edit cursor's position and start playing
- , (comma), move the edit cursor forward by 1 beat (add shift for 0.1 beat, add alt for 0.01 beat)
- . (period), move the edit cursor backward by 1 beat (add shift for 0.1 beat, add alt for 0.01 beat)

In addition to quick jumping, the edit cursor's applicable scope also includes:

- The default position for newly created events
- The target position when pasting events
- More scenarios to be expanded in the future

### 4.5 Quick Event Movement

If you don't want to press ctrl+enter just to fine-tune an event's position, the mod also provides simple adjustment shortcuts. This set of shortcuts, aside from different keys and different adjustment targets, has a lot in common with the edit cursor. As follows:

- z, move event forward by 1 beat (also supports shift or alt modifier keys)
- x, move event backward by 1 beat (also supports shift or alt modifier keys)
- c, snap event to the nearest half beat

Note 1: If the selected event doesn't have a beat property, pressing z and x will change to moving forward/backward by 1 bar, and modifier keys are not supported.

Note 2: The above shortcuts are effective for all selected events.

Note 3: If the selected events include both events with beat properties and events without beat properties, they cannot be moved.

### 4.6 Editing Level Metadata

On the main page, you can press number key 0 on the main keyboard at any time to open the metadata editing page. Here you can edit some basic information about the level.

## 5. External Editor (RDEventEditorHelper)

### 5.1 What is this?

Because the official inspector panel is quite complex and troublesome to adapt, I simply made a separate external editor. This editor is completely rendered using Windows native UI, so screen readers can operate it very conveniently. It is currently used to edit events, rows, and metadata.

### 5.2 How to use?

When you select an event or row in the level editor, or press number key 0 on the main keyboard, the helper will launch. It will generate UI based on the object currently being edited, and you can directly use the screen reader to navigate between properties. After editing is complete, just click the OK button to apply it to the game. If you want to abandon the edit, just press esc or click the Cancel button.

## 6. Known Issues and Limitations

Since this is only the first version, there are still many features waiting to be improved. The following lists currently known issues and limitations. The issues in the list will be attempted to be resolved one by one in future versions.

But then again, I hope the official version will support accessibility soon, so this mod won't need v1.1+.

1. Most events under the Rooms tab are generally poorly supported, and many properties cannot be edited.
2. The Sprites tab is even more out of the question.
3. Window dance? What's that?
4. Some custom options are not supported (such as custom characters).
5. Some metadata cannot be edited yet (such as rating text).
6. Bookmark functionality is not supported.
7. Event multi-selection support is not flexible enough.
8. The helper's browse file button currently seems to be useless. It won't help you copy files to the level directory.
9. Since a separate property editor was written, the operation should not be as smooth as the original version (although I don't know exactly how smooth the original version is).

## 7. FAQ

### Why isn't the mod loading?

Please make sure your execution steps are: enter the main folder, select all and copy, paste in the game's main program directory, rather than directly copying the entire main folder over.

### Why won't the helper open?

If you're just randomly trying to open it by pressing enter, it's perfectly normal that it won't open; if you can't open it when you want to edit an event in the game, and there's an error dialog, please download
[.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
and try installing it.

### Why does the game freeze after the save dialog pops up?

This is a low-probability event, and I'm not sure what the problem is. This is why I mentioned earlier about developing the good habit of saving frequently. If you have this habit, then you're lucky; if not...

### Why do all native editor shortcuts stop working after I click cancel in the save dialog when exiting the level editor?

After my testing, this seems to be a problem with the game itself? Because I tried the same operation without running the mod, and the problem can still be reproduced. You can only press alt+f4, then save or discard, exit the game and re-enter to solve it. But this is much better than the previous situation, isn't it?

### What's in the levels folder?

Inside is a demo level I made myself using this mod. Feel free to import it and play around with it. What? You ask me why I added an 's' to the folder name when there's only one level? Because I want to, is that not okay?

## 8. Appendix

### 8.1 Native Editor Common Shortcuts Reference

Note: This only lists some commonly used shortcuts, not a complete list. If you want to see a more complete one, it's better to look for official documentation.

| Shortcut | Function | Notes |
| --- | --- | --- |
| ctrl+n | New level | None |
| ctrl+o | Open level | None |
| ctrl+shift+o | Open last edited level | None |
| ctrl+u | Open URL | None |
| ctrl+s | Save | None |
| ctrl+shift+s | Save as | Strange, why doesn't it work for me? |
| ctrl+shift+r | Run current level as standalone | None |
| ctrl+shift+p | Export for publishing | None |
| alt+s | Level editor settings | None |
| alt+q | Exit | None |
| ctrl+z | Undo | None |
| ctrl+shift+z/ctrl+y | Redo | None |
| ctrl+x | Cut | None |
| ctrl+c | Copy | None |
| ctrl+v | Paste | The original game behavior is to paste at the center of the view, the mod changes it to paste at the edit cursor. |
| ctrl+b/ctrl+shift+v | Paste to next bar | None |
| ctrl+d | Clone selected event | None |
| a | Toggle auto mode | None |
| f | Toggle fullscreen | In fullscreen mode, the timeline is hidden, mainly used for previewing levels. In this state, the edit cursor may not work as expected. |
| m | Toggle metronome | None |
| p | Play/Pause | None |
| delete/backspace | Delete selected event | None |
| Number keys 1-6 | Switch between six different tabs | None |
| ctrl+Number keys 1-4 | Switch to different rooms | If currently in another tab, will automatically switch to the Rows tab. |
| home | Return to level start | None |
| pageup/pagedown | Rewind/Fast forward? | I personally don't find it very useful. |
| shift+home | Select all events before the currently selected event in the current tab (or row) | I know this is a bit convoluted. |
| shift+end | Select all events after the currently selected event in the current tab (or row) | I also know this is convoluted too. |
| ctrl+shift+home | Select all events before the currently selected event | This is much better. |
| ctrl+shift+end | Select all events after the currently selected event | This too. |
| esc | Exit Full Screen/Deselect | None |

### 8.2 Mod Shortcuts Reference

| Shortcut | Function | Notes |
| --- | --- | --- |
| f10 | Open/close editor main menu | None |
| Left/Right arrows | Browse events in current tab (or row) | None |
| Up/Down arrows | Switch rows/sprites in Rows/Sprites tab | None |
| enter | Jump to selected event's position and start playing | None |
| ctrl+enter | Open helper to edit event | None |
| shift+enter | Open helper to edit row | None |
| Number key 0 | Open helper to edit metadata | None |
| insert | Insert event at edit cursor | None |
| ctrl+insert | Add row/sprite to room (only valid in Rows/Sprites tab) | None |
| / (slash) | Move edit cursor to playhead position | None |
| shift+/ | Announce edit cursor's current position | None |
| ctrl+/ | Snap edit cursor to nearest half beat | None |
| alt+slash | Jump to edit cursor's position and start playing | None |
| , (comma) and . (period) | Move edit cursor forward/backward by 1 beat | Add shift for 0.1 beat, add alt for 0.01 beat. |
| z/x | Move selected event forward/backward by 1 beat (or 1 bar) | Add shift for 0.1 beat, add alt for 0.01 beat. See [4.5 Quick Event Movement](#45-quick-event-movement) for details. |
| c | Snap event to nearest half beat | None |

### 8.3 Contact Information

Email: [huangzitong94@gmail.com](mailto:huangzitong94@gmail.com)

QQ: 1528344627