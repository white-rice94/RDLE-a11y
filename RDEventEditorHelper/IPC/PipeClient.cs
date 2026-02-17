using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace RDEventEditorHelper.IPC
{
    // ===================================================================================
    // 命名管道客户端 - 监听来自主 Mod 的连接
    // ===================================================================================
    public class PipeClient
    {
        private const string PipeName = "RDEventEditor";
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RDEventEditorHelper.log");
        private static readonly object LogLock = new object();

        private Thread _listenThread;
        private NamedPipeServerStream _pipeServer;
        private bool _isRunning;

        // 当前活动连接
        private NamedPipeServerStream _currentPipe;
        private StreamWriter _currentWriter;

        public event Action<PipeMessage> OnMessageReceived;
        public EditorForm EditorForm { get; set; }

        private static void Log(string msg)
        {
            try
            {
                lock (LogLock)
                {
                    using var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var sw = new StreamWriter(fs);
                    sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
                    sw.Flush();
                }
            }
            catch { }
        }

        public void Start()
        {
            Log("PipeClient 启动");
            _isRunning = true;
            _listenThread = new Thread(ListenForConnections);
            _listenThread.IsBackground = true;
            _listenThread.Start();
            Log("等待主 Mod 连接...");
        }

        public void Stop()
        {
            _isRunning = false;
            Log("PipeClient 停止");
            _pipeServer?.Dispose();
            _currentPipe?.Dispose();
        }

        public void SendMessage(PipeMessage message)
        {
            try
            {
                if (_currentWriter != null && _currentPipe?.IsConnected == true)
                {
                    string json = message.ToJson();
                    _currentWriter.WriteLine(json);
                    Log($"发送消息: {message.Type}");
                }
                else
                {
                    Log("无法发送，管道未连接");
                }
            }
            catch (Exception ex)
            {
                Log($"发送消息失败: {ex.Message}");
            }
        }

        private void ListenForConnections()
        {
            while (_isRunning)
            {
                try
                {
                    Log("创建命名管道...");
                    _pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    Log("等待连接...");
                    // 等待连接
                    _pipeServer.WaitForConnection();
                    Log("主 Mod 已连接!");

                    // 保存当前连接
                    _currentPipe = _pipeServer;

                    // 处理连接
                    HandleConnection(_pipeServer);
                }
                catch (IOException ex)
                {
                    Log($"IO异常: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log($"监听异常: {ex.Message}");
                }
                finally
                {
                    _currentWriter = null;
                    _currentPipe?.Dispose();
                    _currentPipe = null;
                    _pipeServer = null;
                }
            }
        }

        private void HandleConnection(NamedPipeServerStream pipeServer)
        {
            try
            {
                Log("开始处理连接...");
                using var reader = new StreamReader(pipeServer);
                _currentWriter = new StreamWriter(pipeServer) { AutoFlush = true };

                while (pipeServer.IsConnected)
                {
                    Log("等待读取消息...");
                    // 读取消息
                    string json = reader.ReadLine();
                    if (string.IsNullOrEmpty(json))
                    {
                        Log("收到空消息，退出循环");
                        break;
                    }

                    Log($"收到消息: {json.Substring(0, Math.Min(100, json.Length))}...");

                    var message = PipeMessage.FromJson(json);
                    if (message == null)
                    {
                        Log("消息解析失败");
                        continue;
                    }

                    Log($"处理消息类型: {message.Type}");

                    // 处理消息
                    string response = ProcessMessage(message);

                    // 发送响应
                    if (!string.IsNullOrEmpty(response))
                    {
                        _currentWriter.WriteLine(response);
                        Log($"发送响应: {message.Type}");
                    }
                }
                Log("连接已关闭");
            }
            catch (IOException ex)
            {
                Log($"IO异常 (连接关闭): {ex.Message}");
            }
            catch (Exception ex)
            {
                Log($"处理连接异常: {ex.Message}");
            }
        }

        private string ProcessMessage(PipeMessage message)
        {
            Log($"ProcessMessage: {message.Type}");

            switch (message.Type)
            {
                case MessageType.OpenEditor:
                    Log($"OpenEditor: {message.EventType}, 属性数量: {message.Properties?.Count ?? 0}");
                    // 在 UI 线程上显示编辑器
                    if (EditorForm != null)
                    {
                        EditorForm.Invoke(new Action(() =>
                        {
                            Log("调用 ShowEditor...");
                            EditorForm.ShowEditor(message.EventType, message.Properties);
                            Log("ShowEditor 调用完成");
                        }));
                    }
                    else
                    {
                        Log("EditorForm 为空!");
                    }
                    return null; // 无需响应

                case MessageType.CloseEditor:
                    if (EditorForm != null)
                    {
                        EditorForm.Invoke(new Action(() =>
                        {
                            EditorForm.HideEditor();
                        }));
                    }
                    return null;

                default:
                    Log($"未知消息类型: {message.Type}");
                    return null;
            }
        }
    }
}
