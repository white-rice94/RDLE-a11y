# RDLE-a11y

[English](#english) | [中文](#中文)

---

## English

### About

RDLE-a11y is an accessibility mod for **Rhythm Doctor** level editor, providing full keyboard navigation and screen reader support.

### Features

- Full keyboard navigation support
- Full screen reader support
- Bilingual localization (Chinese/English)

### Installation

1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) for Rhythm Doctor
2. Download the latest release from [Releases](../../releases)
3. Extract to your Rhythm Doctor installation directory
4. Launch the game

### Development Setup

#### Prerequisites

- Rhythm Doctor (Steam version)
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) installed
- [.NET SDK 9.0](https://dotnet.microsoft.com/download) or later
- Git

#### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/white-rice94/RDMods.git
   cd RDMods
   ```

2. Configure your game path:
   ```bash
   cp Directory.Build.user.props.example Directory.Build.user.props
   ```
   Edit `Directory.Build.user.props` and set `<GameDir>` to your Rhythm Doctor installation path.

3. Build:
   ```bash
   dotnet build RDLE-a11y.sln
   ```

### Project Structure

- **RDLevelEditorAccess** (.NET Standard 2.1): BepInEx mod running inside Unity
- **RDEventEditorHelper** (.NET Framework 4.8): Standalone WinForms property editor

Communication via file-based IPC using JSON files in `temp/` directory.

### Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes (see `CLAUDE.md` for code style)
4. Test in-game
5. Submit a Pull Request

### Documentation

- User manual: `docs/manual-en.md` | `docs/manual-cn.md`
- Changelog: `docs/changelog-en.txt` | `docs/changelog-cn.txt`
- Developer guide: `CLAUDE.md`

### License

MIT License

---

## 中文

### 关于

RDLE-a11y 是 **Rhythm Doctor** 关卡编辑器的无障碍 mod，提供完整的键盘导航和屏幕阅读器支持。

### 功能特性

- 完整的键盘导航支持
- 完整的屏幕阅读器支持
- 中英双语本地化支持

### 安装

1. 为 Rhythm Doctor 安装 [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases)
2. 从 [Releases](../../releases) 下载最新版本
3. 解压到 Rhythm Doctor 安装目录
4. 启动游戏

### 开发环境配置

#### 先决条件

- Rhythm Doctor（Steam 版本）
- 已安装 [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases)
- [.NET SDK 9.0](https://dotnet.microsoft.com/download) 或更高版本
- Git

#### 从源码构建

1. 克隆仓库：
   ```bash
   git clone https://github.com/white-rice94/RDMods.git
   cd RDMods
   ```

2. 配置游戏路径：
   ```bash
   cp Directory.Build.user.props.example Directory.Build.user.props
   ```
   编辑 `Directory.Build.user.props`，设置 `<GameDir>` 为你的 Rhythm Doctor 安装路径。

3. 构建：
   ```bash
   dotnet build RDLE-a11y.sln
   ```

### 项目结构

- **RDLevelEditorAccess** (.NET Standard 2.1)：在 Unity 中运行的 BepInEx mod
- **RDEventEditorHelper** (.NET Framework 4.8)：独立的 WinForms 属性编辑器

通过 `temp/` 目录中的 JSON 文件进行基于文件的 IPC 通信。

### 贡献指南

1. Fork 本仓库
2. 创建功能分支
3. 进行修改（代码风格参见 `CLAUDE.md`）
4. 在游戏中测试
5. 提交 Pull Request

### 文档

- 用户手册：`docs/manual-en.md` | `docs/manual-cn.md`
- 更新日志：`docs/changelog-en.txt` | `docs/changelog-cn.txt`
- 开发者指南：`CLAUDE.md`

### 许可证

MIT License
