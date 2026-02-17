using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using RDLevelEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using Process = System.Diagnostics.Process;

namespace RDLevelEditorAccess.IPC
{
    // ===================================================================================
    // 命名管道客户端 - 连接 Helper 并发送/接收消息
    // ===================================================================================
    public class PipeServer
    {
        private const string PipeName = "RDEventEditor";
        private const string HelperExeName = "RDEventEditorHelper.exe";

        private NamedPipeClientStream _pipeClient;
        private Thread _receiveThread;
        private bool _isConnected;
        private LevelEvent_Base _currentEvent;

        public bool IsConnected => _isConnected;

        /// <summary>
        /// 快速尝试连接，不阻塞太久
        /// </summary>
        public bool TryConnect(int timeoutMs = 200)
        {
            if (_isConnected) return true;

            try
            {
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                _pipeClient.Connect(timeoutMs);

                _isConnected = true;
                Debug.Log("[Pipe] 已连接到 Helper");

                // 启动接收线程
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                return true;
            }
            catch (Exception)
            {
                // 快速失败，不记录日志
                return false;
            }
        }

        /// <summary>
        /// 启动 Helper 并等待连接
        /// </summary>
        public bool TryStartHelperAndConnect(int timeoutMs = 500)
        {
            // 先尝试启动 Helper
            TryStartHelper();

            // 等待 Helper 启动后连接（轮询）
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (TryConnect(200)) return true;
                Thread.Sleep(50);
            }

            Debug.LogWarning("[Pipe] 启动 Helper 后仍无法连接");
            Narration.Say("无法启动事件编辑器", NarrationCategory.Notification);
            return false;
        }

        public void TryStartHelper()
        {
            string helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HelperExeName);

            if (!File.Exists(helperPath))
            {
                Debug.LogWarning($"[Pipe] 找不到 Helper: {helperPath}");
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
                Debug.Log("[Pipe] 已启动 RDEventEditorHelper.exe");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipe] 启动 Helper 失败: {ex.Message}");
                Narration.Say("启动事件编辑器失败", NarrationCategory.Notification);
            }
        }

        public bool Connect(int timeoutMs = 5000)
        {
            if (_isConnected) return true;

            try
            {
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                _pipeClient.Connect(timeoutMs);

                _isConnected = true;
                Debug.Log("[Pipe] 已连接到 Helper");

                // 启动接收线程
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                return true;
            }
            catch (TimeoutException)
            {
                Debug.LogWarning("[Pipe] 连接超时，Helper 可能未启动");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipe] 连接失败: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            _isConnected = false;
            _pipeClient?.Dispose();
            _pipeClient = null;
        }

        public void SendOpenEditor(LevelEvent_Base levelEvent)
        {
            if (!_isConnected)
            {
                Debug.LogWarning("[Pipe] 未连接，尝试连接...");
                if (!Connect())
                {
                    Debug.LogWarning("[Pipe] 无法连接，跳过打开编辑器");
                    return;
                }
            }

            Debug.Log("[Pipe] 开始提取属性...");
            _currentEvent = levelEvent;

            var properties = ExtractProperties(levelEvent);
            Debug.Log($"[Pipe] 属性提取完成，共 {properties.Count} 个属性");

            var message = new PipeMessage
            {
                Type = MessageType.OpenEditor,
                EventId = levelEvent.GetHashCode().ToString(),
                EventType = levelEvent.type.ToString(),
                Properties = properties
            };

            Debug.Log("[Pipe] 正在发送消息...");
            SendMessage(message);
            Debug.Log("[Pipe] 消息已发送");
        }

        public void SendCloseEditor()
        {
            if (!_isConnected) return;

            var message = new PipeMessage
            {
                Type = MessageType.CloseEditor
            };

            SendMessage(message);
            _currentEvent = null;
        }

        private void SendMessage(PipeMessage message)
        {
            try
            {
                if (_pipeClient == null || !_pipeClient.IsConnected)
                {
                    Debug.LogWarning("[Pipe] 管道未连接");
                    return;
                }

                string json = message.ToJson();
                Debug.Log($"[Pipe] 发送消息内容: {json.Substring(0, Math.Min(100, json.Length))}...");
                
                var bytes = System.Text.Encoding.UTF8.GetBytes(json + "\n");
                _pipeClient.Write(bytes, 0, bytes.Length);
                _pipeClient.Flush();
                
                Debug.Log($"[Pipe] 发送消息完成: {message.Type}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipe] 发送消息失败: {ex.Message}");
                _isConnected = false;
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                var buffer = new byte[4096];
                var messageBuffer = new System.Collections.Generic.List<byte>();

                while (_isConnected && _pipeClient.IsConnected)
                {
                    int bytesRead = _pipeClient.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;

                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] == '\n')
                        {
                            string json = System.Text.Encoding.UTF8.GetString(messageBuffer.ToArray());
                            messageBuffer.Clear();

                            if (string.IsNullOrEmpty(json)) continue;
                            Debug.Log($"[Pipe] 收到消息: {json.Substring(0, Math.Min(50, json.Length))}...");

                            var message = PipeMessage.FromJson(json);
                            if (message == null) continue;

                            ProcessMessage(message);
                        }
                        else
                        {
                            messageBuffer.Add(buffer[i]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipe] 接收循环异常: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                Debug.Log("[Pipe] 连接已关闭");
            }
        }

        private void ProcessMessage(PipeMessage message)
        {
            switch (message.Type)
            {
                case MessageType.ApplyChanges:
                    if (_currentEvent != null)
                    {
                        ApplyChangesToEvent(_currentEvent, message.Updates);
                    }
                    break;

                case MessageType.EditorClosed:
                    _currentEvent = null;
                    break;

                default:
                    Debug.LogWarning($"[Pipe] 未知消息类型: {message.Type}");
                    break;
            }
        }

        private List<PropertyData> ExtractProperties(LevelEvent_Base ev)
        {
            var list = new List<PropertyData>();

            LevelEventInfo info = ev.info;
            if (info == null)
            {
                Debug.LogWarning("[Pipe] 事件没有 info");
                return list;
            }

            foreach (var prop in info.propertiesInfo)
            {
                if (prop.enableIf != null && !prop.enableIf(ev)) continue;

                var rawValue = prop.propertyInfo.GetValue(ev);

                var dto = new PropertyData
                {
                    Name = prop.propertyInfo.Name,
                    DisplayName = prop.name,
                    Value = ConvertPropertyValue(rawValue)
                };

                if (prop is IntPropertyInfo) dto.Type = "Int";
                else if (prop is FloatPropertyInfo) dto.Type = "Float";
                else if (prop is BoolPropertyInfo) dto.Type = "Bool";
                else if (prop is StringPropertyInfo) dto.Type = "String";
                else if (prop is EnumPropertyInfo enumProp)
                {
                    dto.Type = "Enum";
                    dto.Options = Enum.GetNames(enumProp.enumType);
                }
                else if (prop is ColorPropertyInfo) dto.Type = "Color";
                else if (prop is Vector2PropertyInfo) dto.Type = "Vector2";
                else dto.Type = "String";

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
            catch (Exception ex)
            {
                Debug.LogWarning($"[Pipe] 属性转换异常: {ex.Message}");
            }

            return value.ToString();
        }

        private void ApplyChangesToEvent(LevelEvent_Base ev, Dictionary<string, object> updates)
        {
            if (ev == null || updates == null) return;

            var info = ev.info;
            if (info == null)
            {
                Debug.LogWarning("[Pipe] ApplyChanges: 事件没有 info");
                return;
            }

            foreach (var update in updates)
            {
                var propInfo = info.propertiesInfo.FirstOrDefault(p => p.propertyInfo.Name == update.Key);
                if (propInfo == null) continue;

                try
                {
                    object valToSet = null;
                    string strVal = update.Value?.ToString();

                    if (propInfo is IntPropertyInfo) valToSet = int.Parse(strVal);
                    else if (propInfo is FloatPropertyInfo) valToSet = float.Parse(strVal);
                    else if (propInfo is BoolPropertyInfo) valToSet = (bool)update.Value;
                    else if (propInfo is StringPropertyInfo) valToSet = strVal;
                    else if (propInfo is EnumPropertyInfo enumProp) valToSet = Enum.Parse(enumProp.enumType, strVal);

                    if (valToSet != null)
                    {
                        propInfo.propertyInfo.SetValue(ev, valToSet);
                        Debug.Log($"[Pipe] 已设置属性 {update.Key} = {valToSet}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Pipe] 属性 {update.Key} 转换失败: {ex.Message}");
                }
            }

            // 刷新 UI
            if (scnEditor.instance.selectedControl != null && 
                scnEditor.instance.selectedControl.levelEvent == ev)
            {
                scnEditor.instance.selectedControl.UpdateUI();
                scnEditor.instance.inspectorPanelManager.GetCurrent()?.UpdateUI(ev);
            }

            Debug.Log("[Pipe] 属性已更新");
        }
    }
}
