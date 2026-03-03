using System;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace RDEventEditorHelper
{
    static class Program
    {
        private static readonly string TempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
        private static readonly string SourcePath = Path.Combine(TempDir, "source.json");
        private static readonly string ResultPath = Path.Combine(TempDir, "result.json");
        private static readonly string LogPath = Path.Combine(TempDir, "RDEventEditorHelper.log");

        [STAThread]
        static void Main()
        {
#if DEBUG
            // Debug 模式：覆盖之前的日志
            if (File.Exists(LogPath))
            {
                File.Delete(LogPath);
            }
            Log("=== Helper 启动 ===");
#endif

            if (!Directory.Exists(TempDir))
            {
                Directory.CreateDirectory(TempDir);
            }

            if (!File.Exists(SourcePath))
            {
#if DEBUG
                Log("source.json 不存在，退出");
#endif
                return;
            }

            string json = File.ReadAllText(SourcePath);
            File.Delete(SourcePath);
#if DEBUG
            Log($"已读取 source.json 内容:\n{json}");
#endif

            var sourceData = JsonConvert.DeserializeObject<SourceData>(json);
#if DEBUG
            Log($"编辑类型: {sourceData?.editType ?? "event"}, 事件类型: {sourceData?.eventType}, 特征码: {sourceData?.token}, 属性数量: {sourceData?.properties?.Length ?? 0}");
#endif

            // 保存特征码，必须在所有响应中回传
            string sessionToken = sourceData?.token ?? "";
            string editType = sourceData?.editType ?? "event";
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            EditorForm editorForm = new EditorForm();
            
            // 根据编辑类型设置标题
            string title = editType == "settings"
                ? "编辑关卡元数据 (Edit Level Settings)"
                : editType == "row"
                    ? "编辑轨道 (Edit Row)"
                    : editType == "jump"
                        ? "跳转到位置 (Jump to Position)"
                        : $"编辑事件 (Edit Event): {sourceData?.eventType}";
            editorForm.SetData(sourceData?.eventType, sourceData?.properties, title, sourceData?.levelAudioFiles, sourceData?.levelDirectory);

            editorForm.OnOK += (updates) =>
            {
                var result = new ResultData { token = sessionToken, action = "ok", updates = updates };
                string resultJson = JsonConvert.SerializeObject(result, Formatting.Indented);
                File.WriteAllText(ResultPath, resultJson);
#if DEBUG
                Log($"已写入 result.json (ok), token: {sessionToken}，退出");
#endif
            };

            editorForm.OnCancel += () =>
            {
                var result = new ResultData { token = sessionToken, action = "cancel" };
                string resultJson = JsonConvert.SerializeObject(result, Formatting.Indented);
                File.WriteAllText(ResultPath, resultJson);
#if DEBUG
                Log($"已写入 result.json (cancel), token: {sessionToken}，退出");
#endif
            };

            editorForm.OnExecute += (methodName) =>
            {
                var result = new ResultData { token = sessionToken, action = "execute", methodName = methodName };
                string resultJson = JsonConvert.SerializeObject(result, Formatting.Indented);
                File.WriteAllText(ResultPath, resultJson);
#if DEBUG
                Log($"已写入 result.json (execute: {methodName}), token: {sessionToken}，退出");
#endif
            };

#if DEBUG
            Log("显示编辑器窗口");
#endif
            Application.Run(editorForm);

#if DEBUG
            Log("=== Helper 退出 ===");
#endif
        }

        private static void Log(string msg)
        {
#if DEBUG
            try
            {
                using var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs);
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
                sw.Flush();
            }
            catch { }
#endif
        }

        private class SourceData
        {
            public string editType;  // "event" 或 "row"
            public string eventType;
            public string token;  // 会话特征码
            public PropertyData[] properties;
            public string[] levelAudioFiles;  // 关卡目录中的音频文件名列表
            public string levelDirectory;  // 关卡目录路径
        }

        private class ResultData
        {
            public string token;  // 会话特征码（必须回传）
            public string action;
            public System.Collections.Generic.Dictionary<string, string> updates;
            public string methodName;  // 当 action 为 "execute" 时使用
        }
    }
}
