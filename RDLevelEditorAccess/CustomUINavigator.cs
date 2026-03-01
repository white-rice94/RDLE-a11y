using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class CustomUINavigator : MonoBehaviour
{
    // 用于存储原生的InputModule引用
    private static StandaloneInputModule _nativeInputModule;

    // 存储原始的轴名称，以便恢复
    private static string _originalHorizontalAxis = "Horizontal";
    private static string _originalVerticalAxis = "Vertical";
    private static bool _isDisabled = false;

    /// <summary>
    /// 禁用Unity原生的方向键导航功能，但保留鼠标点击功能
    /// </summary>
    public static void DisableNativeNavigation(EventSystem eventSystem)
    {
        if (_isDisabled) return; // 已经禁用，避免重复操作

        // 获取标准输入模块 (如果是旧版Input Manager)
        _nativeInputModule = eventSystem.GetComponent<StandaloneInputModule>();

        if (_nativeInputModule != null)
        {
            // 保存原始轴名称
            _originalHorizontalAxis = _nativeInputModule.horizontalAxis;
            _originalVerticalAxis = _nativeInputModule.verticalAxis;

            // 核心操作：将绑定的输入轴名称改为空字符串
            // 这样InputModule就读取不到任何键盘输入来移动光标了
            _nativeInputModule.horizontalAxis = string.Empty;
            _nativeInputModule.verticalAxis = string.Empty;

            _isDisabled = true;

            // 可选：同时也禁用提交/取消按键的映射，如果你也想完全接管确认键
            // _nativeInputModule.submitButton = string.Empty;
            // _nativeInputModule.cancelButton = string.Empty;

            Debug.Log("已禁用原生UI键盘导航。");
        }
        else
        {
            // 注意：如果游戏使用的是New Input System，获取的组件应该是 InputSystemUIInputModule
            // 处理方式略有不同（通常需要Unassign Action），但大多数Mod场景还是旧版居多
            Debug.LogWarning("未找到 StandaloneInputModule，可能是使用了新版输入系统或自定义模块。");
        }
    }

    /// <summary>
    /// 恢复Unity原生的方向键导航功能
    /// </summary>
    public static void EnableNativeNavigation()
    {
        if (!_isDisabled) return; // 没有被禁用，无需恢复

        if (_nativeInputModule != null)
        {
            // 恢复原始轴名称
            _nativeInputModule.horizontalAxis = _originalHorizontalAxis;
            _nativeInputModule.verticalAxis = _originalVerticalAxis;

            _isDisabled = false;

            Debug.Log("已恢复原生UI键盘导航。");
        }
    }
}