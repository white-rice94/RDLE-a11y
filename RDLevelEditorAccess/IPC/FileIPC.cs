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

            Debug.Log($"[FileIPC] 开始编辑事件: {levelEvent.type}");

            var properties = ExtractProperties(levelEvent);

            var sourceData = new SourceData
            {
                eventType = levelEvent.type.ToString(),
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
                    File.Delete(_resultPath);
                    Debug.Log($"[FileIPC] 已读取 result.json");

                    ProcessResult(json);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FileIPC] 读取 result.json 失败: {ex.Message}");
                }
                finally
                {
                    _isPolling = false;

                    UnlockKeyboard();
                }
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

        private void ApplyUpdates(LevelEvent_Base ev, Dictionary<string, string> updates)
        {
            if (ev == null || updates == null) return;

            var info = ev.info;
            if (info == null) return;

            foreach (var update in updates)
            {
                var propInfo = info.propertiesInfo.FirstOrDefault(p => p.propertyInfo.Name == update.Key);
                if (propInfo == null) continue;

                try
                {
                    object valToSet = null;
                    string strVal = update.Value;

                    if (propInfo is IntPropertyInfo) valToSet = int.Parse(strVal);
                    else if (propInfo is FloatPropertyInfo) valToSet = float.Parse(strVal);
                    else if (propInfo is BoolPropertyInfo) valToSet = strVal == "true";
                    else if (propInfo is StringPropertyInfo) valToSet = strVal;
                    else if (propInfo is EnumPropertyInfo enumProp) valToSet = Enum.Parse(enumProp.enumType, strVal);
                    else valToSet = strVal;

                    if (valToSet != null)
                    {
                        propInfo.propertyInfo.SetValue(ev, valToSet);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileIPC] 属性 {update.Key} 转换失败: {ex.Message}");
                }
            }

            if (scnEditor.instance.selectedControl != null && 
                scnEditor.instance.selectedControl.levelEvent == ev)
            {
                scnEditor.instance.selectedControl.UpdateUI();
                scnEditor.instance.inspectorPanelManager.GetCurrent()?.UpdateUI(ev);
            }
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

            foreach (var prop in info.propertiesInfo)
            {
                if (prop.enableIf != null && !prop.enableIf(ev)) continue;

                var rawValue = prop.propertyInfo.GetValue(ev);

                var dto = new PropertyData
                {
                    name = prop.propertyInfo.Name,
                    displayName = prop.name,
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
                else dto.type = "String";

                list.Add(dto);
            }

            return list;
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
            public List<PropertyData> properties;
        }

        [Serializable]
        private class ResultData
        {
            public string action;
            public Dictionary<string, string> updates;
        }

        [Serializable]
        private class PropertyData
        {
            public string name;
            public string displayName;
            public string value;
            public string type;
            public string[] options;
        }
    }
}
