# IPC 特征码机制计划

## TL;DR

> **快速摘要**: 为IPC通信添加特征码验证机制，确保Mod只处理当前会话的响应，防止旧文件或多会话冲突。
> 
> **交付物**:
> - Mod端：生成GUID特征码，存储并验证
> - Helper端：回传特征码
> - 验证逻辑：不匹配则删除继续轮询
> 
> **预估工作量**: Short（两个文件，约50行代码）
> **并行执行**: NO - 两个项目需要协调修改

---

## Context

### 现有IPC流程

```
Mod                          Helper
 │                              │
 ├─ 1. 写入 source.json ────────►
 │                              │
 ├─ 2. 启动 Helper              │
 │                              │
 ├─ 3. 锁定键盘                  │
 │                              │
 ├─ 4. 开始轮询                  ├─ 5. 读取 source.json
 │                              ├─ 6. 删除 source.json
 │                              ├─ 7. 显示编辑器
 │                              │
 │   ◄───── 8. 写入 result.json ─┤
 │                              │
 ├─ 9. 检测到 result.json        │
 ├─ 10. 应用更改                 │
 ├─ 11. 解锁键盘                 │
 │                              │
```

### 问题场景

1. **旧文件残留**: 上次编辑的 result.json 未被处理，下次启动时误读
2. **多会话冲突**: 快速连续调用编辑器，响应错乱
3. **异常退出**: Helper崩溃后残留文件

### 解决方案

添加特征码（Session ID）验证：

```
Mod                          Helper
 │                              │
 ├─ 1. 生成 GUID                │
 ├─ 2. 存储到内存 _sessionToken │
 ├─ 3. 写入 source.json ────────►
 │   {token: "xxx", ...}        │
 │                              │
 ├─ 4. 启动 Helper              │
 ├─ 5. 锁定键盘                  │
 ├─ 6. 开始轮询                  ├─ 7. 读取 source.json
 │                              ├─ 8. 提取 token
 │                              ├─ 9. 显示编辑器
 │                              │
 │   ◄───── 10. 写入 result.json ─┤
 │        {token: "xxx", ...}    │
 │                              │
 ├─ 11. 检测到 result.json       │
 ├─ 12. 读取 token              │
 ├─ 13. 比对 token              │
 │   ├─ 匹配 → 应用更改，解锁    │
 │   └─ 不匹配 → 删除，继续轮询  │
```

---

## Work Objectives

### Core Objective
为IPC通信添加会话验证机制，防止文件冲突。

### Definition of Done
- [ ] Mod端生成并存储特征码
- [ ] source.json 包含 token 字段
- [ ] Helper端回传 token
- [ ] result.json 包含 token 字段
- [ ] Mod端验证逻辑
- [ ] 构建成功
- [ ] 测试通过

### Must Have
- 特征码生成（GUID）
- 特征码验证
- 不匹配时删除文件继续轮询

### Must NOT Have
- 不改变现有数据结构（只是添加字段）
- 不影响现有功能

---

## Execution Strategy

### Sequential Execution

```
Task 1: Mod端 - 添加特征码生成和存储
    ↓
Task 2: Mod端 - source.json 添加 token 字段
    ↓
Task 3: Helper端 - 读取并回传 token
    ↓
Task 4: Mod端 - 验证逻辑
    ↓
Task 5: 构建验证
```

---

## TODOs

- [ ] 1. Mod端 - 添加特征码生成和存储

  **What to do**:
  - 在 `FileIPC` 类中添加 `_sessionToken` 字段
  - 在 `StartEditing()` 中生成 GUID
  - 使用 `System.Guid.NewGuid().ToString()`

  **References**:
  - `FileIPC.cs:24-25` - 现有字段定义
  - `FileIPC.cs:42-76` - StartEditing 方法

  **Commit**: NO

---

- [ ] 2. Mod端 - source.json 添加 token 字段

  **What to do**:
  - 修改 `SourceData` 类，添加 `token` 字段
  - 在写入 source.json 时包含 token

  **References**:
  - `FileIPC.cs:695-700` - SourceData 类定义
  - `FileIPC.cs:52-56` - 创建 SourceData

  **Commit**: NO

---

- [ ] 3. Helper端 - 读取并回传 token

  **What to do**:
  - 修改 `SourceData` 类，添加 `token` 字段
  - 修改 `ResultData` 类，添加 `token` 字段
  - 在写入 result.json 时回传 token

  **References**:
  - `Program.cs:84-88` - SourceData 类
  - `Program.cs:90-95` - ResultData 类
  - `Program.cs:44-64` - 写入 result.json

  **Commit**: NO

---

- [ ] 4. Mod端 - 验证逻辑

  **What to do**:
  - 修改 `ResultData` 类，添加 `token` 字段
  - 在 `Update()` 方法中添加验证逻辑
  - 不匹配时删除文件继续轮询（不停止轮询，不解锁键盘）

  **References**:
  - `FileIPC.cs:702-708` - ResultData 类
  - `FileIPC.cs:120-145` - Update 方法

  **代码示例**:
  ```csharp
  public void Update()
  {
      if (!_isPolling) return;

      if (File.Exists(_resultPath))
      {
          try
          {
              string json = File.ReadAllText(_resultPath);
              var resultData = JsonSerializer.Deserialize<ResultData>(json);
              
              // 验证特征码
              if (resultData?.token != _sessionToken)
              {
                  Debug.LogWarning($"[FileIPC] 特征码不匹配，删除文件继续轮询");
                  File.Delete(_resultPath);
                  return; // 继续轮询
              }
              
              // 特征码匹配，处理结果
              File.Delete(_resultPath);
              ProcessResult(json);
          }
          finally
          {
              // 只有在验证成功后才停止轮询和解锁
          }
      }
  }
  ```

  **Commit**: NO

---

- [ ] 5. 构建验证

  **What to do**:
  - 构建 Mod 项目
  - 构建 Helper 项目
  - 测试完整流程

  **Commit**: YES
  - Message: `IPC协议添加特征码验证机制`
  - Files: `RDLevelEditorAccess/IPC/FileIPC.cs`, `RDEventEditorHelper/Program.cs`

---

## Final Verification

- [ ] 特征码正确生成
- [ ] source.json 包含 token
- [ ] result.json 包含 token
- [ ] 验证逻辑正确（匹配/不匹配）
- [ ] 不匹配时继续轮询

---

## Success Criteria

### 测试场景

1. **正常流程**:
   - 调用编辑器 → 修改 → 确定
   - 特征码匹配，更改正确应用

2. **旧文件场景**:
   - 手动放置一个旧的 result.json
   - 调用编辑器 → 新的 token
   - 旧文件被删除，等待新响应

3. **取消操作**:
   - 调用编辑器 → 取消
   - result.json 的 token 匹配，正确处理取消
