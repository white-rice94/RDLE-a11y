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
        private StreamReader _reader;
        private StreamWriter _writer;
        private Thread _receiveThread;
        private Thread _connectionThread;
        private bool _isConnected;
        private bool _isConnecting;
        private LevelEvent_Base _currentEvent;
        private readonly object _lock = new object();

        public bool IsConnected
        {
            get { lock (_lock) { return _isConnected; } }
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

        public bool Connect(int timeoutMs = 2000)
        {
            lock (_lock)
            {
                if (_isConnected) return true;
                if (_isConnecting) return false;
                _isConnecting = true;
            }

            try
            {
                Debug.Log("[Pipe] 尝试连接 Helper...");
                
                _pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                _pipeClient.Connect(timeoutMs);

                _reader = new StreamReader(_pipeClient);
                _writer = new StreamWriter(_pipeClient) { AutoFlush = true };

                lock (_lock)
                {
                    _isConnected = true;
                }
                Debug.Log("[Pipe] 已连接到 Helper");

                // 启动接收线程
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                return true;
            }
            catch (TimeoutException)
            {
                Debug.LogWarning("[Pipe] 连接超时，尝试启动 Helper...");
                TryStartHelper();
                return false;
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[Pipe] IO 异常: {ex.Message}，尝试启动 Helper...");
                TryStartHelper();
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipe] 连接失败: {ex.Message}");
                return false;
            }
            finally
            {
                lock (_lock)
                {
                    _isConnecting = false;
                }
            }
        }

        public bool TryConnectWithRetry(int maxRetries = 3, int retryDelayMs = 1000)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (Connect(1500))
                {
                    return true;
                }
                
                if (i < maxRetries - 1)
                {
                    Debug.Log($"[Pipe] 等待 Helper 启动，{retryDelayMs}ms 后重试... ({i + 1}/{maxRetries})");
                    Thread.Sleep(retryDelayMs);
                }
            }

            Debug.LogWarning("[Pipe] 无法连接到 Helper");
            Narration.Say("无法连接到事件编辑器，请重启游戏后重试", NarrationCategory.Notification);
            return false;
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                _isConnected = false;
            }
            _pipeClient?.Dispose();
            _pipeClient = null;
        }

        public void SendOpenEditor(LevelEvent_Base levelEvent)
        {
            if (levelEvent == null) return;

            // 先尝试连接（带重试）
            if (!IsConnected)
            {
                if (!TryConnectWithRetry())
                {
                    return;
                }
            }

            _currentEvent = levelEvent;

            var properties = ExtractProperties(levelEvent);

            var message = new PipeMessage
            {
                Type = MessageType.OpenEditor,
                EventId = levelEvent.GetHashCode().ToString(),
                EventType = levelEvent.type.ToString(),
                Properties = properties
            };

            SendMessage(message);
            Debug.Log($"[Pipe] 已发送打开编辑器消息: {levelEvent.type}");
        }

        public void SendCloseEditor()
        {
            if (!IsConnected) return;

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
                if (_writer == null)
                {
                    Debug.LogWarning("[Pipe] _writer 为空，无法发送消息");
                    return;
                }

                string json = message.ToJson();
                _writer.WriteLine(json);
                Debug.Log($"[Pipe] 发送消息: {message.Type}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipe] 发送消息失败: {ex.Message}");
                lock (_lock)
                {
                    _isConnected = false;
                }
            }
        }

        private void ReceiveLoop()
        {
            try
            {
                while (IsConnected && _pipeClient != null && _pipeClient.IsConnected)
                {
                    string json = _reader?.ReadLine();
                    if (string.IsNullOrEmpty(json)) break;

                    Debug.Log($"[Pipe] 收到消息: {json.Substring(0, Math.Min(50, json.Length))}...");

                    var message = PipeMessage.FromJson(json);
                    if (message == null) continue;

                    ProcessMessage(message);
                }
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[Pipe] 接收 IO 异常: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Pipe] 接收循环异常: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _isConnected = false;
                }
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

                var dto = new PropertyData
                {
                    Name = prop.propertyInfo.Name,
                    DisplayName = prop.name,
                    Value = prop.propertyInfo.GetValue(ev)
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
