using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RDLevelEditor;
using RDLevelEditorAccess.IPC;
using UnityEngine;

namespace RDLevelEditorAccess
{
    // ===================================================================================
    // 1. 公共入口 (API)
    // ===================================================================================
    public static class AccessibilityBridge
    {
        private static bool _isInitialized;
        private static PipeServer _pipeServer;

        public static void Initialize(GameObject host)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            _pipeServer = new PipeServer();
            
            // 尝试连接 Helper（异步尝试，不阻塞）
            // 实际连接会在用户按 Ctrl+Enter 时进行
            
            Debug.Log("[RDEditorAccess] AccessibilityBridge 已初始化");
        }

        public static void EditEvent(LevelEvent_Base levelEvent)
        {
            if (!_isInitialized)
            {
                Debug.LogError("请先调用 AccessibilityBridge.Initialize() !");
                return;
            }

            if (levelEvent == null) return;

            Debug.Log($"[RDEditorAccess] 打开事件编辑器: {levelEvent.type}");
            
            // 通过管道发送打开编辑器消息
            _pipeServer?.SendOpenEditor(levelEvent);
            
            Narration.Say($"正在打开 {levelEvent.type} 属性编辑器", NarrationCategory.Instruction);
        }

        public static void Shutdown()
        {
            _pipeServer?.Disconnect();
            _pipeServer = null;
            _isInitialized = false;
        }
    }

    // ===================================================================================
    // 2. 调度器 (Unity Main Thread Dispatcher) - 保留用于将来扩展
    // ===================================================================================
    public class UnityDispatcher : MonoBehaviour
    {
        public static UnityDispatcher Instance;
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Update()
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogError($"[Dispatcher] 更新异常: {e}"); }
            }
        }

        public void Enqueue(Action action) => _queue.Enqueue(action);
    }
}
