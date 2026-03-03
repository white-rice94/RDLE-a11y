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
        private static FileIPC _fileIPC;

        public static void Initialize(GameObject host)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            _fileIPC = new FileIPC();
            _fileIPC.Initialize();
            
            Debug.Log("[RDEditorAccess] AccessibilityBridge 已初始化");
        }

        public static void Update()
        {
            _fileIPC?.Update();
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

            _fileIPC.StartEditing(levelEvent);
            
            //Narration.Say(string.Format(RDString.Get("eam.editor.openEventEditor"), levelEvent.type), NarrationCategory.Instruction);// 废话太多了。
        }

        public static void EditRow(int rowIndex)
        {
            if (!_isInitialized)
            {
                Debug.LogError("请先调用 AccessibilityBridge.Initialize() !");
                return;
            }

            if (rowIndex < 0) return;

            var editor = scnEditor.instance;
            if (editor == null || editor.rowsData == null || rowIndex >= editor.rowsData.Count)
            {
                Debug.LogWarning($"[RDEditorAccess] 无效的轨道索引: {rowIndex}");
                return;
            }

            var rowData = editor.rowsData[rowIndex];
            Debug.Log($"[RDEditorAccess] 打开轨道编辑器: 轨道 {rowIndex}, 角色 {rowData.character}");

            _fileIPC.StartRowEditing(rowData, rowIndex);
            
            //Narration.Say(string.Format(RDString.Get("eam.editor.openRowEditor"), rowIndex + 1), NarrationCategory.Instruction);// 废话太多了。
        }

        public static void Shutdown()
        {
            _fileIPC = null;
            _isInitialized = false;
        }

        public static void EditSettings()
        {
            if (!_isInitialized) return;
            _fileIPC.StartSettingsEditing();
            Narration.Say(RDString.Get("eam.editor.openSettingsEditor"), NarrationCategory.Instruction);
        }

        /// <summary>
        /// 打开跳转到位置对话框
        /// </summary>
        public static void JumpToCursor()
        {
            if (!_isInitialized)
            {
                Debug.LogError("[AccessibilityBridge] 未初始化");
                return;
            }
            _fileIPC.StartJumpToCursorEdit();
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
