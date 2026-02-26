using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RDLevelEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using Process = System.Diagnostics.Process;

namespace RDLevelEditorAccess.IPC
{
    public class FileIPC
    {
        private const string TempDirName = "temp";
        private const string SourceFileName = "source.json";
        private const string ResultFileName = "result.json";
        private const string HelperExeName = "RDEventEditorHelper.exe";

        private string _tempPath;
        private string _sourcePath;
        private string _resultPath;
        private LevelEvent_Base _currentEvent;
        private LevelEvent_MakeRow _currentRow;  // 当前编辑的轨道
        private int _currentRowIndex;  // 当前编辑的轨道索引
        private bool _isPolling;
        private string _sessionToken;  // 会话特征码
        private string _currentEditType = "event";  // 当前编辑类型: "event" 或 "row"

        public void Initialize()
        {
            string gameDir = AppDomain.CurrentDomain.BaseDirectory;
            _tempPath = Path.Combine(gameDir, TempDirName);
            _sourcePath = Path.Combine(_tempPath, SourceFileName);
            _resultPath = Path.Combine(_tempPath, ResultFileName);

            if (!Directory.Exists(_tempPath))
            {
                Directory.CreateDirectory(_tempPath);
            }

            Debug.Log($"[FileIPC] 初始化完成，临时目录: {_tempPath}");
        }

        public void StartEditing(LevelEvent_Base levelEvent)
        {
            if (levelEvent == null) return;

            _currentEvent = levelEvent;
            
            // 生成新的会话特征码
            _sessionToken = System.Guid.NewGuid().ToString();
            Debug.Log($"[FileIPC] 生成会话特征码: {_sessionToken}");

            Debug.Log($"[FileIPC] 开始编辑事件: {levelEvent.type}");

            var properties = ExtractProperties(levelEvent);

            var sourceData = new SourceData
            {
                eventType = levelEvent.type.ToString(),
                token = _sessionToken,
                properties = properties
            };

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
                string json = JsonSerializer.Serialize(sourceData, options);
                File.WriteAllText(_sourcePath, json);
                Debug.Log($"[FileIPC] 已写入 source.json: {json.Length} 字符");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 写入 source.json 失败: {ex.Message}");
                return;
            }

            LaunchHelper();

            LockKeyboard();

            _isPolling = true;
        }

        /// <summary>
        /// 开始编辑轨道
        /// </summary>
        public void StartRowEditing(LevelEvent_MakeRow rowData, int rowIndex)
        {
            if (rowData == null) return;

            _currentRow = rowData;
            _currentRowIndex = rowIndex;
            _currentEvent = null;  // 清除事件引用
            _currentEditType = "row";
            
            // 生成新的会话特征码
            _sessionToken = System.Guid.NewGuid().ToString();
            Debug.Log($"[FileIPC] 生成会话特征码: {_sessionToken}");

            Debug.Log($"[FileIPC] 开始编辑轨道: 索引 {rowIndex}, 角色 {rowData.character}");

            var properties = ExtractRowProperties(rowData);

            var sourceData = new SourceData
            {
                editType = "row",
                eventType = "MakeRow",
                token = _sessionToken,
                properties = properties
            };

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
                string json = JsonSerializer.Serialize(sourceData, options);
                File.WriteAllText(_sourcePath, json);
                Debug.Log($"[FileIPC] 已写入 source.json (轨道编辑): {json.Length} 字符");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 写入 source.json 失败: {ex.Message}");
                return;
            }

            LaunchHelper();
            LockKeyboard();
            _isPolling = true;
        }

        /// <summary>
        /// 提取轨道属性
        /// </summary>
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
                options = new[] { "Classic", "Oneshot" }
            });

            // player (Enum)
            list.Add(new PropertyData
            {
                name = "player",
                displayName = "玩家",
                type = "Enum",
                value = row.player.ToString(),
                options = new[] { "P1", "P2", "CPU" }
            });

            // character (Character类型 - 需要特殊处理)
            var availableChars = RDEditorConstants.AvailableCharacters.Select(c => c.ToString()).ToList();
            availableChars.Add("Custom");
            list.Add(new PropertyData
            {
                name = "character",
                displayName = "角色",
                type = "Character",
                value = row.character.ToString(),
                options = availableChars.ToArray(),
                customName = row.character == Character.Custom ? row.customCharacterName : ""
            });

            // cpuMarker (Character类型)
            var cpuChars = RDEditorConstants.AvailableCPUCharacters.Select(c => c.ToString()).ToArray();
            list.Add(new PropertyData
            {
                name = "cpuMarker",
                displayName = "CPU标记",
                type = "Enum",
                value = row.cpuMarker.ToString(),
                options = cpuChars
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

            // muteIn1P (Bool)
            list.Add(new PropertyData
            {
                name = "muteIn1P",
                displayName = "单人模式静音",
                type = "Bool",
                value = row.muteIn1P ? "true" : "false"
            });

            // pulseSound (SoundData)
            // 注意：pulseSound 是 SoundData 类型，不是 SoundDataStruct
            string pulseSoundValue = $"{row.pulseSound?.filename ?? "Shaker"}|{row.pulseSound?.volume ?? 100}|{row.pulseSound?.pitch ?? 100}|{row.pulseSound?.pan ?? 0}|{row.pulseSound?.offset ?? 0}";
            list.Add(new PropertyData
            {
                name = "pulseSound",
                displayName = "节拍音效",
                type = "SoundData",
                value = pulseSoundValue,
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

        private void LockKeyboard()
        {
            try
            {
                var editor = scnEditor.instance;
                if (editor == null || editor.eventSystem == null) return;

                var go = new GameObject("RDMods_LockInput");
                var inputField = go.AddComponent<UnityEngine.UI.InputField>();
                go.transform.SetParent(editor.transform);
                
                editor.eventSystem.SetSelectedGameObject(go);
                
                Debug.Log("[FileIPC] 已锁定键盘 (创建隐藏 InputField)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 锁定键盘失败: {ex.Message}");
            }
        }

        private void UnlockKeyboard()
        {
            try
            {
                var editor = scnEditor.instance;
                if (editor == null || editor.eventSystem == null) return;

                var selected = editor.eventSystem.currentSelectedGameObject;
                if (selected != null && selected.name == "RDMods_LockInput")
                {
                    UnityEngine.Object.Destroy(selected);
                }
                
                Debug.Log("[FileIPC] 已解锁键盘");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 解锁键盘失败: {ex.Message}");
            }
        }

        public void Update()
        {
            if (!_isPolling) return;

            // NEW: 处理validateVisibility请求（与result.json处理独立，不中断轮询和键盘锁定）
            PollPropertyValidationRequests();

            if (File.Exists(_resultPath))
            {
                try
                {
                    string json = File.ReadAllText(_resultPath);
                    Debug.Log($"[FileIPC] 已读取 result.json");
                    
                    // 先解析验证特征码
                    var options = new JsonSerializerOptions { IncludeFields = true };
                    var resultData = JsonSerializer.Deserialize<ResultData>(json, options);
                    
                    // 特征码验证
                    if (resultData?.token != _sessionToken)
                    {
                        Debug.LogWarning($"[FileIPC] 特征码不匹配，期望: {_sessionToken}，实际: {resultData?.token}，删除文件继续轮询");
                        File.Delete(_resultPath);
                        return; // 继续轮询，不停止不解锁
                    }
                    
                    Debug.Log($"[FileIPC] 特征码验证通过: {_sessionToken}");
                    File.Delete(_resultPath);

                    ProcessResult(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FileIPC] 读取 result.json 失败: {ex.Message}");
                }
                
                // 只有在验证成功或处理完成后才停止轮询和解锁
                _isPolling = false;
                UnlockKeyboard();
            }
        }

        private void ProcessResult(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            {
                Debug.Log("[FileIPC] 用户取消编辑");
                return;
            }

            Debug.Log($"[FileIPC] 解析 result.json: {json}");

            try
            {
                var options = new JsonSerializerOptions { IncludeFields = true };
                var resultData = JsonSerializer.Deserialize<ResultData>(json, options);
                Debug.Log($"[FileIPC] 解析结果: action={resultData?.action}, updates={(resultData?.updates != null ? resultData.updates.Count : 0)}项");

                if (resultData?.action == "cancel")
                {
                    Debug.Log("[FileIPC] 用户取消编辑");
                    return;
                }

                // 处理操作按钮执行
                if (resultData?.action == "execute" && !string.IsNullOrEmpty(resultData.methodName))
                {
                    ExecuteButtonAction(_currentEvent, resultData.methodName);
                    return;
                }

                if (resultData.updates != null)
                {
                    // 根据编辑类型选择处理方式
                    if (_currentEditType == "row" && _currentRow != null)
                    {
                        ApplyRowUpdates(_currentRow, resultData.updates);
                        Debug.Log("[FileIPC] 已应用轨道更改");
                    }
                    else if (_currentEvent != null)
                    {
                        ApplyUpdates(_currentEvent, resultData.updates);
                        Debug.Log("[FileIPC] 已应用事件更改");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 解析 result.json 失败: {ex.Message}");
            }
        }

        // NEW: 处理Helper的属性验证请求
        private void PollPropertyValidationRequests()
        {
            string validationPath = Path.Combine(_tempPath, "validateVisibility.json");
            if (!File.Exists(validationPath)) return;

            try
            {
                string json = File.ReadAllText(validationPath);
                var options = new JsonSerializerOptions { IncludeFields = true };
                var request = JsonSerializer.Deserialize<PropertyUpdateRequest>(json, options);

                // 获取当前正被编辑的事件（使用selectedControl）
                var currentEvent = scnEditor.instance?.selectedControl?.levelEvent;
                if (currentEvent == null)
                {
                    // Helper可能在试图验证一个已关闭的对话框的事件，忽略此请求
                    File.Delete(validationPath);
                    return;
                }

                var response = HandlePropertyUpdateRequest(request, currentEvent);

                string responsePath = Path.Combine(_tempPath, "validateVisibilityResponse.json");
                string responseJson = JsonSerializer.Serialize(response, options);
                File.WriteAllText(responsePath, responseJson);

                File.Delete(validationPath);  // 处理完删除请求
                Debug.Log($"[FileIPC] 已处理enableIf验证请求");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] Failed to process visibility validation: {ex.Message}");
                // 错误时也删除文件，避免反复尝试
                try { File.Delete(validationPath); } catch { }
            }
        }

        // NEW: 处理属性更新请求，执行enableIf判断
        private PropertyUpdateResponse HandlePropertyUpdateRequest(PropertyUpdateRequest request, LevelEvent_Base currentEvent)
        {
            if (currentEvent == null)
                return new PropertyUpdateResponse { token = request.token, visibilityChanges = new Dictionary<string, bool>() };

            var visibilityChanges = new Dictionary<string, bool>();

            // 应用Helper发来的值更新到event对象（临时，不保存）
            foreach (var kvp in request.updates)
            {
                string propName = kvp.Key;
                string newValue = kvp.Value;

                try
                {
                    var prop = currentEvent.GetType().GetProperty(propName);
                    if (prop != null)
                    {
                        var convertedValue = ConvertStringToPropertyValue(newValue, prop.PropertyType);
                        prop.SetValue(currentEvent, convertedValue);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileIPC] Failed to apply property {propName}: {ex.Message}");
                }
            }

            // 对所有属性重新评估enableIf条件
            if (currentEvent.info != null && currentEvent.info.propertiesInfo != null)
            {
                foreach (var property in currentEvent.info.propertiesInfo)
                {
                    if (property != null && property.enableIf != null)
                    {
                        try
                        {
                            bool shouldShow = property.enableIf(currentEvent);

                            // 仅返回**状态发生变化**的属性（优化网络消息）
                            var existingProp = request.currentProperties
                                .FirstOrDefault(p => p.name == property.propertyInfo.Name);
                            if (existingProp != null && existingProp.isVisible != shouldShow)
                            {
                                visibilityChanges[property.propertyInfo.Name] = shouldShow;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[FileIPC] Failed to evaluate enableIf for {property.propertyInfo.Name}: {ex.Message}");
                        }
                    }
                }
            }

            return new PropertyUpdateResponse
            {
                token = request.token,
                visibilityChanges = visibilityChanges
            };
        }

        // NEW: 将字符串值转换为目标类型
        private object ConvertStringToPropertyValue(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value)) return null;

            try
            {
                if (targetType == typeof(string)) return value;
                if (targetType == typeof(bool)) return value == "true";
                if (targetType == typeof(int)) return int.Parse(value);
                if (targetType == typeof(float)) return float.Parse(value);
                if (targetType == typeof(double)) return double.Parse(value);
                if (targetType.IsEnum) return Enum.Parse(targetType, value);
            }
            catch { }

            return value;
        }
        private void ApplyRowUpdates(LevelEvent_MakeRow row, Dictionary<string, string> updates)
        {
            if (row == null || updates == null) return;

            var editor = scnEditor.instance;
            bool rowTypeChanged = false;
            RowType oldRowType = row.rowType;

            foreach (var update in updates)
            {
                string key = update.Key;
                string strVal = update.Value;

                try
                {
                    // ★ 首先尝试应用基础属性（bar, beat, y, row, active, tag, tagRunNormally）
                    // 这些属性对MakeRow也是适用的（MakeRow继承自LevelEvent_Base）
                    if (ApplyBaseProperty(row, key, strVal))
                    {
                        continue; // 基础属性已处理，跳过
                    }

                    switch (key)
                    {
                        case "rowType":
                            var newRowType = (RowType)Enum.Parse(typeof(RowType), strVal);
                            if (newRowType != row.rowType)
                            {
                                rowTypeChanged = true;
                                // 轨道类型切换需要确认对话框
                                if (editor != null)
                                {
                                    var rowControlsList = editor.eventControls_rows.ElementAtOrDefault(_currentRowIndex);
                                    if (rowControlsList != null && rowControlsList.Count > 0)
                                    {
                                        // 显示确认对话框
                                        string message = $"切换轨道类型将删除轨道上的所有事件（{rowControlsList.Count}个），是否继续？";
                                        Debug.Log($"[FileIPC] 轨道类型切换警告: {message}");
                                        // TODO: 实现确认对话框
                                        // 暂时直接切换
                                    }
                                }
                                row.rowType = newRowType;
                                Debug.Log($"[FileIPC] 轨道类型切换: {oldRowType} -> {newRowType}");
                            }
                            break;

                        case "player":
                            row.player = (RDPlayer)Enum.Parse(typeof(RDPlayer), strVal);
                            break;

                        case "character":
                            if (strVal == "Custom")
                            {
                                row.character = Character.Custom;
                                // customCharacterName 由单独字段处理
                            }
                            else
                            {
                                row.character = (Character)Enum.Parse(typeof(Character), strVal);
                            }
                            break;

                        case "customCharacterName":
                            row.customCharacterName = strVal;
                            break;

                        case "cpuMarker":
                            row.cpuMarker = (Character)Enum.Parse(typeof(Character), strVal);
                            break;

                        case "hideAtStart":
                            row.hideAtStart = strVal == "true";
                            break;

                        case "muteBeats":
                            row.muteBeats = strVal == "true";
                            break;

                        case "muteIn1P":
                            row.muteIn1P = strVal == "true";
                            break;

                        case "pulseSound":
                            // 解析 SoundData 格式
                            var parts = strVal.Split('|');
                            if (row.pulseSound != null)
                            {
                                row.pulseSound.filename = parts.Length > 0 ? parts[0] : "Shaker";
                                row.pulseSound.volume = parts.Length > 1 && int.TryParse(parts[1], out int v) ? v : 100;
                                row.pulseSound.pitch = parts.Length > 2 && int.TryParse(parts[2], out int p) ? p : 100;
                                row.pulseSound.pan = parts.Length > 3 && int.TryParse(parts[3], out int pn) ? pn : 0;
                                row.pulseSound.offset = parts.Length > 4 && int.TryParse(parts[4], out int o) ? o : 0;
                            }
                            break;

                        case "room":
                            int newRoom = int.Parse(strVal);
                            if (newRoom != row.room)
                            {
                                // 检查目标房间是否已满
                                if (editor != null)
                                {
                                    int countInRoom = editor.rowsData.Count(r => r.room == newRoom);
                                    if (countInRoom >= 4)
                                    {
                                        Debug.LogWarning($"[FileIPC] 目标房间 {newRoom + 1} 已满，无法移动轨道");
                                        Narration.Say($"房间 {newRoom + 1} 已满，无法移动轨道", NarrationCategory.Notification);
                                        break;
                                    }
                                }
                                row.room = newRoom;
                                Debug.Log($"[FileIPC] 轨道移动到房间 {newRoom + 1}");
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileIPC] 轨道属性 {key} 转换失败: {ex.Message}");
                }
            }

            // ★ 关键：调用SaveState()保存修改到undo/redo栈
            // 这与游戏原始的SaveAndUpdateUI()行为一致，确保属性修改被永久保存而不是只在内存中
            if (editor != null)
            {
                try
                {
                    using (new SaveStateScope())
                    {
                        Debug.Log("[FileIPC] 已调用SaveState()持久化轨道属性修改");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileIPC] SaveState()调用失败: {ex.Message}");
                }
            }

            // 更新 UI
            if (editor != null)
            {
                editor.tabSection_rows?.UpdateUI();

                // 更新轨道头部显示
                var inspectorPanel = editor.inspectorPanelManager?.Get<InspectorPanel_MakeRow>();
                if (inspectorPanel != null && inspectorPanel.currentLevelEvent == row)
                {
                    inspectorPanel.UpdateUI(row);
                }
            }
        }

        private void ExecuteButtonAction(LevelEvent_Base ev, string methodName)
        {
            if (ev == null || string.IsNullOrEmpty(methodName))
            {
                Debug.LogWarning("[FileIPC] 无法执行操作：事件或方法名为空");
                return;
            }

            try
            {
                var method = ev.GetType().GetMethod(methodName);
                if (method == null)
                {
                    Debug.LogError($"[FileIPC] 找不到方法: {methodName}");
                    return;
                }

                Debug.Log($"[FileIPC] 执行操作: {methodName}");
                method.Invoke(ev, null);
                Debug.Log($"[FileIPC] 操作执行完成: {methodName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 执行操作 {methodName} 失败: {ex.Message}");
            }
        }

        private void ApplyUpdates(LevelEvent_Base ev, Dictionary<string, string> updates)
        {
            if (ev == null || updates == null) return;

            var info = ev.info;
            if (info == null) return;

            foreach (var update in updates)
            {
                string key = update.Key;
                string strVal = update.Value;

                try
                {
                    // 首先尝试应用基础属性（bar, beat, y, row, active, tag, tagRunNormally）
                    if (ApplyBaseProperty(ev, key, strVal))
                    {
                        continue; // 基础属性已处理，跳过
                    }

                    // 处理事件特有属性
                    var propInfo = info.propertiesInfo.FirstOrDefault(p => p.propertyInfo.Name == key);
                    if (propInfo == null)
                    {
                        Debug.LogWarning($"[FileIPC] 未找到属性: {key}");
                        continue;
                    }

                    Debug.Log($"[FileIPC] 处理属性 {key}: propInfo类型={propInfo.GetType().Name}, 值={strVal?.Substring(0, Math.Min(50, strVal?.Length ?? 0)) ?? "null"}");

                    object valToSet = null;

                    if (propInfo is IntPropertyInfo) valToSet = int.Parse(strVal);
                    else if (propInfo is FloatPropertyInfo) valToSet = float.Parse(strVal);
                    else if (propInfo is BoolPropertyInfo) valToSet = strVal == "true";
                    else if (propInfo is StringPropertyInfo) valToSet = strVal;
                    else if (propInfo is EnumPropertyInfo enumProp) valToSet = Enum.Parse(enumProp.enumType, strVal);
                    else if (propInfo is Vector2PropertyInfo)
                    {
                        // 解析 "x,y" 格式
                        var parts = strVal.Split(',');
                        if (parts.Length == 2 &&
                            float.TryParse(parts[0], out float vx) &&
                            float.TryParse(parts[1], out float vy))
                        {
                            valToSet = new UnityEngine.Vector2(vx, vy);
                        }
                    }
                    else if (propInfo is ColorPropertyInfo)
                    {
                        // 使用 ColorOrPalette.FromString 解析颜色
                        var colorType = Type.GetType("RDLevelEditor.ColorOrPalette");
                        if (colorType != null)
                        {
                            var fromStringMethod = colorType.GetMethod("FromString", new[] { typeof(string) });
                            if (fromStringMethod != null)
                            {
                                valToSet = fromStringMethod.Invoke(null, new object[] { strVal });
                            }
                        }
                    }
                    else if (propInfo is Float2PropertyInfo)
                    {
                        // 解析 "x,y" 格式
                        var parts = strVal.Split(',');
                        if (parts.Length == 2 &&
                            float.TryParse(parts[0], out float fx) &&
                            float.TryParse(parts[1], out float fy))
                        {
                            var float2Type = Type.GetType("RDLevelEditor.Float2");
                            if (float2Type != null)
                            {
                                valToSet = Activator.CreateInstance(float2Type, fx, fy);
                            }
                        }
                    }
                    else if (propInfo is FloatExpressionPropertyInfo)
                    {
                        // 使用 RDEditorUtils.DecodeFloatExpression 解析表达式
                        valToSet = ParseFloatExpression(strVal);
                    }
                    else if (propInfo is FloatExpression2PropertyInfo)
                    {
                        // 解析 "x,y" 格式的表达式
                        var parts = strVal.Split(',');
                        string xExpr = parts.Length > 0 ? parts[0].Trim() : "";
                        string yExpr = parts.Length > 1 ? parts[1].Trim() : "";
                        
                        var xVal = ParseFloatExpression(xExpr);
                        var yVal = ParseFloatExpression(yExpr);
                        
                        var floatExpr2Type = Type.GetType("RDLevelEditor.FloatExpression2");
                        if (floatExpr2Type != null && xVal != null && yVal != null)
                        {
                            valToSet = Activator.CreateInstance(floatExpr2Type, xVal, yVal);
                        }
                    }
                    else if (propInfo is SoundDataPropertyInfo || 
                             (propInfo is NullablePropertyInfo nullableProp && 
                              nullableProp.underlyingPropertyInfo is SoundDataPropertyInfo))
                    {
                        Debug.Log($"[FileIPC] ★ 接收 SoundData: key={key}, strVal=\"{strVal}\" , 是否可空: {propInfo is NullablePropertyInfo}");
                        
                        // 处理空字符串 -> 设置为 null（如果是可空类型）
                        if (string.IsNullOrEmpty(strVal) && propInfo is NullablePropertyInfo)
                        {
                            Debug.Log($"[FileIPC] ★ 设置为 null (strVal为空)");
                            valToSet = null;
                        }
                        else
                        {
                            // 解析 "filename|volume|pitch|pan|offset" 格式
                            var parts = strVal.Split('|');
                            string filename = parts.Length > 0 ? parts[0] : "";
                            int volume = parts.Length > 1 && int.TryParse(parts[1], out int v) ? v : 100;
                            int pitch = parts.Length > 2 && int.TryParse(parts[2], out int p) ? p : 100;
                            int pan = parts.Length > 3 && int.TryParse(parts[3], out int pn) ? pn : 0;
                            int offset = parts.Length > 4 && int.TryParse(parts[4], out int o) ? o : 0;
                            
                            Debug.Log($"[FileIPC] ★ 创建 SoundDataStruct: filename={filename}, volume={volume}, pitch={pitch}, pan={pan}, offset={offset}");
                            
                            // 使用 typeof 直接获取类型，避免 Type.GetType 失败
                            valToSet = new SoundDataStruct(filename, volume, pitch, pan, offset);
                        }
                    }
                    else if (propInfo is NullablePropertyInfo nullableProp2)
                    {
                        // 处理其他可空类型
                        if (string.IsNullOrEmpty(strVal))
                        {
                            valToSet = null;
                        }
                        else if (nullableProp2.underlyingPropertyInfo is IntPropertyInfo)
                        {
                            valToSet = int.Parse(strVal);
                        }
                        else if (nullableProp2.underlyingPropertyInfo is FloatPropertyInfo)
                        {
                            valToSet = float.Parse(strVal);
                        }
                        else
                        {
                            valToSet = strVal;
                        }
                    }
                    else valToSet = strVal;

                    // 设置值（null 也是合法值，用于可空类型）
                    propInfo.propertyInfo.SetValue(ev, valToSet);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileIPC] 属性 {key} 转换失败: {ex.Message}");
                }
            }

            // ★ 关键：调用SaveState()保存修改到undo/redo栈
            // 这与游戏原始的SaveAndUpdateUI()行为一致，确保属性修改被永久保存而不是只在内存中
            if (scnEditor.instance != null)
            {
                try
                {
                    using (new SaveStateScope())
                    {
                        Debug.Log("[FileIPC] 已调用SaveState()持久化属性修改");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileIPC] SaveState()调用失败: {ex.Message}");
                }
            }

            if (scnEditor.instance.selectedControl != null &&
                scnEditor.instance.selectedControl.levelEvent == ev)
            {
                scnEditor.instance.selectedControl.UpdateUI();
                scnEditor.instance.inspectorPanelManager.GetCurrent()?.UpdateUI(ev);
            }
        }

        private bool ApplyBaseProperty(LevelEvent_Base ev, string key, string value)
        {
            try
            {
                switch (key)
                {
                    case "bar":
                        ev.bar = int.Parse(value);
                        return true;
                    case "beat":
                        ev.beat = float.Parse(value);
                        return true;
                    case "y":
                        ev.y = int.Parse(value);
                        return true;
                    case "row":
                        ev.row = int.Parse(value);
                        return true;
                    case "active":
                        ev.active = value == "true";
                        return true;
                    case "tag":
                        ev.tag = string.IsNullOrEmpty(value) ? null : value;
                        return true;
                    case "tagRunNormally":
                        ev.tagRunNormally = value == "true";
                        return true;
                    // PlaySong 特有属性
                    case "beatsPerMinute":
                    case "bpm":
                        // 使用反射设置，因为属性名可能是 beatsPerMinute 或 bpm
                        var bpmProp = ev.GetType().GetProperty("beatsPerMinute");
                        if (bpmProp != null && bpmProp.PropertyType == typeof(float))
                        {
                            bpmProp.SetValue(ev, float.Parse(value));
                            Debug.Log($"[FileIPC] 设置 beatsPerMinute = {value}");
                            return true;
                        }
                        return false;
                    case "loop":
                        var loopProp = ev.GetType().GetProperty("loop");
                        if (loopProp != null && loopProp.PropertyType == typeof(bool))
                        {
                            loopProp.SetValue(ev, value == "true");
                            Debug.Log($"[FileIPC] 设置 loop = {value}");
                            return true;
                        }
                        return false;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileIPC] 基础属性 {key} 设置失败: {ex.Message}");
                return true; // 标记为已处理（虽然是失败的）
            }
        }

        private object ParseFloatExpression(string expr)
        {
            try
            {
                // 尝试解析为简单浮点数
                if (float.TryParse(expr, out float simpleVal))
                {
                    var floatExprType = Type.GetType("RDLevelEditor.FloatExpression");
                    if (floatExprType != null)
                    {
                        return Activator.CreateInstance(floatExprType, simpleVal);
                    }
                }

                // 使用 RDEditorUtils.DecodeFloatExpression 解析复杂表达式
                var decodeMethod = Type.GetType("RDLevelEditor.RDEditorUtils")?.GetMethod("DecodeFloatExpression", new[] { typeof(object) });
                if (decodeMethod != null)
                {
                    return decodeMethod.Invoke(null, new object[] { expr });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileIPC] 解析表达式 '{expr}' 失败: {ex.Message}");
            }
            return null;
        }

        private void LaunchHelper()
        {
            string helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HelperExeName);

            if (!File.Exists(helperPath))
            {
                Debug.LogWarning($"[FileIPC] 找不到 Helper: {helperPath}");
                Narration.Say("无法启动事件编辑器，请确保 RDEventEditorHelper.exe 存在", NarrationCategory.Notification);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = helperPath,
                    UseShellExecute = true
                });
                Debug.Log("[FileIPC] 已启动 RDEventEditorHelper.exe");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 启动 Helper 失败: {ex.Message}");
            }
        }

        private List<PropertyData> ExtractProperties(LevelEvent_Base ev)
        {
            var list = new List<PropertyData>();

            LevelEventInfo info = ev.info;
            if (info == null) return list;

            // 添加基础属性（位置、行、房间等）
            AddBaseProperties(ev, list);

            foreach (var prop in info.propertiesInfo)
            {
                // 检查是否为 Button 类型（通过 controlAttribute 判断）
                bool isButton = prop.controlAttribute is ButtonAttribute;

                // 跳过仅用于 UI 的非 Button 属性（如 Description）
                // Button 类型需要保留，作为操作按钮显示
                if (prop.onlyUI && !isButton) continue;

                // 计算初始的可见性状态，但不跳过任何属性
                // 所有属性都应该被发送到Helper，由Helper动态控制可见性
                bool shouldBeVisible = prop.enableIf == null || prop.enableIf(ev);

                // 获取本地化的显示名称
                string localizedName = GetLocalizedPropertyName(ev, prop);

                // 处理 Button 类型
                if (isButton)
                {
                    var buttonAttr = prop.controlAttribute as ButtonAttribute;
                    list.Add(new PropertyData
                    {
                        name = prop.propertyInfo.Name,
                        displayName = localizedName,
                        type = "Button",
                        methodName = buttonAttr?.methodName,
                        isVisible = shouldBeVisible  // 初始可见性
                    });
                    continue;
                }

                var rawValue = prop.propertyInfo.GetValue(ev);

                var dto = new PropertyData
                {
                    name = prop.propertyInfo.Name,
                    displayName = localizedName,
                    value = ConvertPropertyValue(rawValue),
                    isVisible = shouldBeVisible  // 初始可见性
                };

                if (prop is IntPropertyInfo) dto.type = "Int";
                else if (prop is FloatPropertyInfo) dto.type = "Float";
                else if (prop is BoolPropertyInfo) dto.type = "Bool";
                else if (prop is StringPropertyInfo) dto.type = "String";
                else if (prop is EnumPropertyInfo enumProp)
                {
                    dto.type = "Enum";
                    dto.options = Enum.GetNames(enumProp.enumType);
                }
                else if (prop is ColorPropertyInfo) dto.type = "Color";
                else if (prop is Vector2PropertyInfo) dto.type = "Vector2";
                else if (prop is Float2PropertyInfo) dto.type = "Float2";
                else if (prop is FloatExpressionPropertyInfo) dto.type = "FloatExpression";
                else if (prop is FloatExpression2PropertyInfo) dto.type = "FloatExpression2";
                else if (prop is SoundDataPropertyInfo soundProp)
                {
                    dto.type = "SoundData";
                    dto.itsASong = soundProp.itsASong;
                    
                    // 提取 SoundAttribute 配置
                    ExtractSoundAttributeConfig(prop, ev, dto);
                }
                else if (prop is NullablePropertyInfo nullableProp)
                {
                    // 检查底层类型
                    var underlying = nullableProp.underlyingPropertyInfo;
                    if (underlying is SoundDataPropertyInfo underlyingSoundProp)
                    {
                        dto.type = "SoundData";
                        dto.itsASong = underlyingSoundProp.itsASong;
                        dto.isNullable = true;
                        
                        // 提取 SoundAttribute 配置（从 NullablePropertyInfo 获取）
                        ExtractSoundAttributeConfig(prop, ev, dto);
                    }
                    else if (underlying is IntPropertyInfo)
                    {
                        dto.type = "Int";
                        dto.isNullable = true;
                    }
                    else if (underlying is FloatPropertyInfo)
                    {
                        dto.type = "Float";
                        dto.isNullable = true;
                    }
                    else
                    {
                        dto.type = "String";
                        dto.isNullable = true;
                    }
                }
                else dto.type = "String";

                list.Add(dto);
            }

            return list;
        }

        /// <summary>
        /// 提取 SoundAttribute 配置（选项列表、是否允许自定义文件）
        /// </summary>
        private void ExtractSoundAttributeConfig(BasePropertyInfo prop, LevelEvent_Base ev, PropertyData dto)
        {
            // controlAttribute 在 BasePropertyInfo 上，需要从底层类型获取
            var controlAttr = prop.controlAttribute;
            if (controlAttr == null && prop is NullablePropertyInfo nullableProp)
            {
                controlAttr = nullableProp.underlyingPropertyInfo?.controlAttribute;
            }
            
            if (controlAttr == null)
            {
                dto.allowCustomFile = true;
                Debug.Log($"[FileIPC] SoundAttribute not found, using defaults");
                return;
            }
            
            // 使用反射获取 SoundAttribute 的字段（避免版本兼容问题）
            var attrType = controlAttr.GetType();
            if (attrType.Name != "SoundAttribute")
            {
                dto.allowCustomFile = true;
                Debug.Log($"[FileIPC] ControlAttribute is not SoundAttribute: {attrType.Name}");
                return;
            }
            
            try
            {
                // 获取 customFile 字段
                var customFileField = attrType.GetField("customFile");
                if (customFileField != null)
                {
                    dto.allowCustomFile = (bool)(customFileField.GetValue(controlAttr) ?? true);
                }
                else
                {
                    dto.allowCustomFile = true;
                }
                
                // 获取 optionsMethod 字段
                var optionsMethodField = attrType.GetField("optionsMethod");
                string optionsMethod = optionsMethodField?.GetValue(controlAttr) as string;
                
                // 获取 options 字段
                var optionsField = attrType.GetField("options");
                string[] options = optionsField?.GetValue(controlAttr) as string[];
                
                // 获取选项列表
                if (!string.IsNullOrEmpty(optionsMethod))
                {
                    dto.soundOptions = GetSoundOptions(ev, optionsMethod, prop.propertyInfo.DeclaringType);
                }
                else if (options != null && options.Length > 0)
                {
                    dto.soundOptions = options;
                }
                
                Debug.Log($"[FileIPC] SoundAttribute: customFile={dto.allowCustomFile}, optionsMethod={optionsMethod ?? "null"}, optionsCount={dto.soundOptions?.Length ?? 0}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileIPC] 获取 SoundAttribute 字段失败: {ex.Message}");
                dto.allowCustomFile = true;
            }
        }

        /// <summary>
        /// 调用事件实例上的选项方法获取音效选项列表
        /// </summary>
        private string[] GetSoundOptions(LevelEvent_Base ev, string methodName, Type declaringType)
        {
            try
            {
                var method = declaringType.GetMethod(methodName, 
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.Static);
                    
                if (method == null)
                {
                    Debug.LogWarning($"[FileIPC] 找不到选项方法: {methodName} in {declaringType.Name}");
                    return null;
                }
                
                // 实例方法需要事件实例，静态方法传 null
                object instance = method.IsStatic ? null : ev;
                var result = method.Invoke(instance, new object[0]) as string[];
                
                Debug.Log($"[FileIPC] 获取音效选项: {methodName} -> {(result != null ? result.Length + "项" : "null")}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileIPC] 获取音效选项失败: {ex.Message}");
                return null;
            }
        }

        private void AddBaseProperties(LevelEvent_Base ev, List<PropertyData> list)
        {
            // Bar (小节)
            list.Add(new PropertyData
            {
                name = "bar",
                displayName = RDString.Get("editor.bar"),
                value = ev.bar.ToString(),
                type = "Int"
            });

            // Beat (拍子)
            if (ev.usesBeat)
            {
                list.Add(new PropertyData
                {
                    name = "beat",
                    displayName = RDString.Get("editor.beat"),
                    value = ev.beat.ToString(),
                    type = "Float"
                });
            }

            // Y 位置
            if (ev.usesY)
            {
                list.Add(new PropertyData
                {
                    name = "y",
                    displayName = "Y Position",
                    value = ev.y.ToString(),
                    type = "Int"
                });
            }

            // Row (行)
            if (ev.info.usesRow)
            {
                list.Add(new PropertyData
                {
                    name = "row",
                    displayName = RDString.Get("editor.row"),
                    value = ev.row.ToString(),
                    type = "Int"
                });
            }

            // Active (激活状态)
            list.Add(new PropertyData
            {
                name = "active",
                displayName = "Active",
                value = ev.active.ToString().ToLower(),
                type = "Bool"
            });

            // Tag (标签)
            list.Add(new PropertyData
            {
                name = "tag",
                displayName = RDString.Get("editor.tag"),
                value = ev.tag ?? "",
                type = "String"
            });

            // TagRunNormally (标签正常运行)
            if (!string.IsNullOrEmpty(ev.tag))
            {
                list.Add(new PropertyData
                {
                    name = "tagRunNormally",
                    displayName = "Tag Run Normally",
                    value = ev.tagRunNormally.ToString().ToLower(),
                    type = "Bool"
                });
            }
        }

        private string GetLocalizedPropertyName(LevelEvent_Base ev, BasePropertyInfo prop)
        {
            string propertyName = prop.name;
            string localized;

            // 如果有自定义本地化键，直接使用
            if (!string.IsNullOrEmpty(prop.customLocalizationKey))
            {
                localized = RDString.Get(prop.customLocalizationKey);
            }
            else
            {
                // 尝试特定于事件类型的键: editor.{eventType}.{propertyName}
                string specificKey = $"editor.{ev.type}.{propertyName}";
                localized = RDString.GetWithCheck(specificKey, out bool exists);
                if (!exists)
                {
                    // 尝试通用键: editor.{propertyName}
                    string genericKey = $"editor.{propertyName}";
                    localized = RDString.GetWithCheck(genericKey, out exists);
                    if (!exists)
                    {
                        // 如果都没有找到，返回原始属性名
                        Debug.LogWarning($"[FileIPC] 未找到属性 '{propertyName}' 的本地化键");
                        localized = propertyName;
                    }
                }
            }

            // 过滤富文本颜色标签: <color=#...>...</color> 和 </color>
            return StripRichTextTags(localized);
        }

        private string StripRichTextTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // 移除 <color=#...>...</color> 标签
            // 使用简单的字符串操作而不是正则，避免性能问题
            string result = text;
            int colorStart = result.IndexOf("<color=");
            while (colorStart >= 0)
            {
                int colorEnd = result.IndexOf(">", colorStart);
                if (colorEnd > colorStart)
                {
                    // 移除 <color=#...> 标签
                    result = result.Substring(0, colorStart) + result.Substring(colorEnd + 1);
                    
                    // 查找并移除对应的 </color> 结束标签
                    int closeTagStart = result.IndexOf("</color>", colorStart);
                    if (closeTagStart >= 0)
                    {
                        result = result.Substring(0, closeTagStart) + result.Substring(closeTagStart + 8);
                    }
                }
                else
                {
                    break;
                }
                colorStart = result.IndexOf("<color=", colorStart);
            }

            return result;
        }

        private string ConvertPropertyValue(object value)
        {
            if (value == null) return "";

            try
            {
                // 处理 Nullable<T> 类型
                var valueType = value.GetType();
                if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    var hasValueProp = valueType.GetProperty("HasValue");
                    var hasValue = hasValueProp != null && (bool)hasValueProp.GetValue(value);
                    if (!hasValue) return "";  // null 值返回空字符串
                    
                    var valueProp = valueType.GetProperty("Value");
                    if (valueProp != null)
                    {
                        value = valueProp.GetValue(value);
                        if (value == null) return "";
                        valueType = value.GetType();
                    }
                }
                
                if (value is UnityEngine.Vector2 v2) return $"{v2.x},{v2.y}";
                if (value is UnityEngine.Vector3 v3) return $"{v3.x},{v3.y},{v3.z}";
                if (value is UnityEngine.Color c)
                {
                    try { return $"#{UnityEngine.ColorUtility.ToHtmlStringRGB(c)}"; }
                    catch { return c.ToString(); }
                }
                // ColorOrPalette 类型
                if (valueType.Name == "ColorOrPalette")
                {
                    try
                    {
                        var colorObj = valueType.GetProperty("color")?.GetValue(value);
                        if (colorObj is UnityEngine.Color colorVal)
                        {
                            return $"#{UnityEngine.ColorUtility.ToHtmlStringRGB(colorVal)}";
                        }
                        var paletteIndex = valueType.GetProperty("paletteIndex")?.GetValue(value);
                        if (paletteIndex != null)
                        {
                            return $"pal{paletteIndex}";
                        }
                    }
                    catch { }
                }
                // Float2 类型
                if (valueType.Name == "Float2")
                {
                    float x = (float)(valueType.GetField("x")?.GetValue(value) ?? 0f);
                    float y = (float)(valueType.GetField("y")?.GetValue(value) ?? 0f);
                    return $"{x},{y}";
                }
                // FloatExpression 类型
                if (valueType.Name == "FloatExpression")
                {
                    return value.ToString();
                }
                // FloatExpression2 类型
                if (valueType.Name == "FloatExpression2")
                {
                    var xExpr = valueType.GetField("x")?.GetValue(value);
                    var yExpr = valueType.GetField("y")?.GetValue(value);
                    string xStr = xExpr?.ToString() ?? "";
                    string yStr = yExpr?.ToString() ?? "";
                    return $"{xStr},{yStr}";
                }
                // SoundDataStruct 类型
                if (valueType.Name == "SoundDataStruct")
                {
                    var filename = valueType.GetField("filename")?.GetValue(value);
                    var volume = valueType.GetField("volume")?.GetValue(value);
                    var pitch = valueType.GetField("pitch")?.GetValue(value);
                    var pan = valueType.GetField("pan")?.GetValue(value);
                    var offset = valueType.GetField("offset")?.GetValue(value);
                    var result = $"{filename}|{volume}|{pitch}|{pan}|{offset}";
                    Debug.Log($"[FileIPC] ConvertPropertyValue SoundDataStruct: property=?, result={result}");
                    return result;
                }
                if (value is Enum e) return e.ToString();
                if (value is bool b) return b ? "true" : "false";
                if (value is int i) return i.ToString();
                if (value is float f) return f.ToString();
                if (value is double d) return d.ToString();
            }
            catch { }

            return value.ToString();
        }

        [Serializable]
        private class SourceData
        {
            public string editType;  // "event" 或 "row"
            public string eventType;
            public string token;  // 会话特征码
            public List<PropertyData> properties;
        }

        [Serializable]
        private class ResultData
        {
            public string token;  // 会话特征码（必须回传）
            public string action;
            public Dictionary<string, string> updates;
            public string methodName;  // 当 action 为 "execute" 时使用
        }

        [Serializable]
        private class PropertyData
        {
            public string name;
            public string displayName;
            public string value;
            public string type;
            public string[] options;
            public string methodName;  // Button 类型专用：要调用的方法名
            public bool itsASong;      // SoundData 类型专用：区分歌曲/音效
            public bool isNullable;    // 是否为可空类型
            public string[] soundOptions;   // SoundData 类型专用：预设音效选项列表
            public bool allowCustomFile;    // SoundData 类型专用：是否允许浏览外部文件
            public bool isVisible = true;   // NEW: 该属性是否应该显示（enableIf判断结果）
            public string customName;       // Character 类型专用：自定义角色名称
        }

        // NEW: Helper → Mod 请求数据类
        [Serializable]
        private class PropertyUpdateRequest
        {
            public string token;                   // 关联原有的session token
            public string action = "validateVisibility";
            public Dictionary<string, string> updates;  // 修改的属性名 → 新值
            public PropertyData[] currentProperties;    // 当前的完整属性列表（含所有值）
        }

        // NEW: Mod → Helper 响应数据类
        [Serializable]
        private class PropertyUpdateResponse
        {
            public string token;
            public Dictionary<string, bool> visibilityChanges;  // 属性名 → 是否应该显示
        }
    }
}
