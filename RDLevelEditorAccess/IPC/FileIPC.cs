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
        private bool _isPolling;
        private string _sessionToken;  // 会话特征码

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
                    ApplyUpdates(_currentEvent, resultData.updates);
                    Debug.Log("[FileIPC] 已应用更改");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileIPC] 解析 result.json 失败: {ex.Message}");
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
                    if (propInfo == null) continue;

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
                    else valToSet = strVal;

                    if (valToSet != null)
                    {
                        propInfo.propertyInfo.SetValue(ev, valToSet);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileIPC] 属性 {key} 转换失败: {ex.Message}");
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

                if (prop.enableIf != null && !prop.enableIf(ev)) continue;

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
                        methodName = buttonAttr?.methodName
                    });
                    continue;
                }

                var rawValue = prop.propertyInfo.GetValue(ev);

                var dto = new PropertyData
                {
                    name = prop.propertyInfo.Name,
                    displayName = localizedName,
                    value = ConvertPropertyValue(rawValue)
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
                else dto.type = "String";

                list.Add(dto);
            }

            return list;
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
                if (value is UnityEngine.Vector2 v2) return $"{v2.x},{v2.y}";
                if (value is UnityEngine.Vector3 v3) return $"{v3.x},{v3.y},{v3.z}";
                if (value is UnityEngine.Color c)
                {
                    try { return $"#{UnityEngine.ColorUtility.ToHtmlStringRGB(c)}"; }
                    catch { return c.ToString(); }
                }
                // ColorOrPalette 类型
                if (value?.GetType().Name == "ColorOrPalette")
                {
                    try
                    {
                        var colorObj = value.GetType().GetProperty("color")?.GetValue(value);
                        if (colorObj is UnityEngine.Color colorVal)
                        {
                            return $"#{UnityEngine.ColorUtility.ToHtmlStringRGB(colorVal)}";
                        }
                        var paletteIndex = value.GetType().GetProperty("paletteIndex")?.GetValue(value);
                        if (paletteIndex != null)
                        {
                            return $"pal{paletteIndex}";
                        }
                    }
                    catch { }
                }
                // Float2 类型
                if (value?.GetType().Name == "Float2")
                {
                    float x = (float)(value.GetType().GetField("x")?.GetValue(value) ?? 0f);
                    float y = (float)(value.GetType().GetField("y")?.GetValue(value) ?? 0f);
                    return $"{x},{y}";
                }
                // FloatExpression 类型
                if (value?.GetType().Name == "FloatExpression")
                {
                    return value.ToString();
                }
                // FloatExpression2 类型
                if (value?.GetType().Name == "FloatExpression2")
                {
                    var xExpr = value.GetType().GetField("x")?.GetValue(value);
                    var yExpr = value.GetType().GetField("y")?.GetValue(value);
                    string xStr = xExpr?.ToString() ?? "";
                    string yStr = yExpr?.ToString() ?? "";
                    return $"{xStr},{yStr}";
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
        }
    }
}
