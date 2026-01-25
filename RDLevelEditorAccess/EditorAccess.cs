using System;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using RDLevelEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

namespace RDLevelEditorAccess
{
    // ===================================================================================
    // 第一部分：加载器 (Loader)
    // ===================================================================================
    [BepInPlugin("com.hzt.rd-editor-access", "RDEditorAccess", "0.3")]
    public class EditorAccess : BaseUnityPlugin
    {
        public void Awake()
        {
            Logger.LogInfo(">>> 加载器启动 (Loader Awake)");

            SceneManager.sceneLoaded += StaticOnSceneLoaded;

            // 修正拼写：Harmony
            var harmony = new Harmony("com.hzt.rd-editor-access");
            harmony.PatchAll();
        }

        public void OnDestroy()
        {
            Logger.LogWarning(">>> 加载器被销毁");
        }

        private static void StaticOnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.IsNullOrEmpty(scene.name)) return;

            // 检查逻辑核心是否已经存在
            if (AccessLogic.Instance != null) return;

            GameObject logicObj = new GameObject("RDEditorAccess_Logic");
            AccessLogic logic = logicObj.AddComponent<AccessLogic>();
            DontDestroyOnLoad(logicObj);

            Debug.Log("[RDEditorAccess] 核心逻辑已注入");
        }
    }

    // ===================================================================================
    // 第二部分：核心逻辑 (Worker)
    // ===================================================================================
    public class AccessLogic : MonoBehaviour
    {
        public static AccessLogic Instance { get; private set; }

        private GameObject lastSelectedObj; // 记录上一次朗读的 UI 对象
        private EventSystem targetEventSystem = null;
        private int currentIndex = -1;
        private string currentMenu = "";
        private List<Graphic> allControls;

        private Tab currentTab;

        private float debugTimer = 0f;

        private InputFieldReader inputFieldReader;

        public void Awake()
        {
            Instance = this;
            inputFieldReader = new InputFieldReader();
            Debug.Log("无障碍核心逻辑已启动 (Logic Awake)");
        }

        public void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Update()
        {
            // --- 心跳检测 ---
            debugTimer += Time.unscaledDeltaTime;
            if (debugTimer > 10f)
            {
                debugTimer = 0f;
            }

            if (scnEditor.instance == null) return;

            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log($"编辑状态： {scnEditor.instance.userIsEditingAnInputField}， 当前菜单： {currentMenu}");
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                scnEditor.instance.MenuButtonClick();
            }

            // --- 菜单/弹窗拦截逻辑 (优先级从高到低) ---

            // 1. 免责声明 (最高优先级)
            if (CheckAndNavigate(scnEditor.instance.copyrightPopup, "免责声明")) return;

            // 2. 文本输入弹窗 (例如另存为、输入URL)
            // 注意：需要确保 rdStringPopup 变量名正确，如果 public 访问不到，可能需要反射或 Find
            if (CheckAndNavigate(scnEditor.instance.insertUrlContainer, "URL输入窗口")) return;
            if (scnEditor.instance.dialog != null && CheckAndNavigate(scnEditor.instance.dialog.gameObject, "确认对话框")) return;

            // 3. 选色器
            if (scnEditor.instance.colorPickerPopup != null && CheckAndNavigate(scnEditor.instance.colorPickerPopup.gameObject, "选色器")) return;

            // 4. 角色选择器
            if (scnEditor.instance.characterPickerPopup != null && CheckAndNavigate(scnEditor.instance.characterPickerPopup.gameObject, "角色选择器")) return;

            // 5. 发布/打包窗口
            if (scnEditor.instance.publishPopup != null && CheckAndNavigate(scnEditor.instance.publishPopup.gameObject, "发布窗口")) return;

            // 6. 设置菜单
            //if (scnEditor.instance.settingsMenu != null && CheckAndNavigate(scnEditor.instance.settingsMenu.gameObject, "设置菜单")) return;

            // 7. 顶部下拉菜单
            if (scnEditor.instance.mainMenu != null && CheckAndNavigate(scnEditor.instance.mainMenu, "下拉菜单")) return;

            // 8. 没有任何菜单打开时，进入时间轴逻辑 (这里交还给游戏原生)
            lastSelectedObj = null;
            HandleTimelineNavigation();
        }

        /// <summary>
        /// 辅助方法：检查菜单是否激活并执行导航
        /// </summary>
        private bool CheckAndNavigate(GameObject menuObj, string name)
        {
            if (menuObj != null && menuObj.activeInHierarchy)
            {
                HandleGeneralUINavigation(menuObj, name);
                return true; // 拦截成功
            }
            return false;
        }

        // ===================================================================================
        // 核心功能区域：通用 UI 导航逻辑 (已优化)
        // ===================================================================================

        private void HandleGeneralUINavigation(GameObject rootObject, string menuName)
        {
            if (rootObject == null) return;

            // --- [修复 1] 输入框防冲突保护 ---
            // 如果焦点当前在一个输入框内，并且正在编辑，绝对不要拦截方向键，否则玩家没法移动光标改字
            var es = scnEditor.instance.eventSystem ?? EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && scnEditor.instance.userIsEditingAnInputField)
            {
                inputFieldReader.UpdateReader(es.currentSelectedGameObject);
                //return;
            }

            // 检测导航按键
            bool isNavKey = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
                            Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
                            Input.GetKeyDown(KeyCode.Tab);
            bool isEnterKey = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

            if (!isNavKey && !isEnterKey) return;

            if (menuName != currentMenu)
            {
                currentMenu = menuName;
                // --- [修复 2] 智能过滤列表 ---
                // 查找所有可见的 UI 元素，但过滤掉纯背景图片
                allControls = rootObject.GetComponentsInChildren<Graphic>()
                    .Where(g => g.gameObject.activeInHierarchy)
                    .Where(g =>
                    {
                        // A. 如果是可交互的 (Selectable)，保留 (按钮、开关、输入框)
                        if (g.GetComponent<Selectable>() != null) return true;
                        // B. 如果是纯文本 (Text/TMP)，保留 (用于朗读标签)
                        if (g is Text || g is TMPro.TMP_Text) return true;
                        // C. 既不是按钮也不是字 (比如纯 Image 背景)，视为噪音，过滤掉
                        return false;
                    })
                    .ToList();
                foreach (var item in allControls)
                {
                    Debug.Log(item.name);
                }
                if (allControls.Count == 0) return;

                // 视觉排序 (从上到下，从左到右)
                allControls.Sort((a, b) =>
                {
                    var posA = a.transform.position;
                    var posB = b.transform.position;
                    int yComparison = posB.y.CompareTo(posA.y); // Y轴降序
                    if (yComparison != 0) return yComparison;
                    return posA.x.CompareTo(posB.x); // X轴升序
                });

                // 获取事件系统
                targetEventSystem = scnEditor.instance.eventSystem ?? EventSystem.current;
                if (targetEventSystem == null) return;

                CustomUINavigator.DisableNativeNavigation(targetEventSystem);
                Narration.Say(menuName, NarrationCategory.Instruction);

                // 确定当前位置
                currentIndex = -1;

                // 初始化选中
                if (currentIndex == -1)
                {
                    if (allControls.Count > 0) SelectUIElement(allControls[0], targetEventSystem);
                    return;
                }
            }

            // 处理方向逻辑
            int direction = 0;
            bool isTab = false;

            if ((Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow)) && !scnEditor.instance.userIsEditingAnInputField) direction = -1;
            else if ((Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow)) && !scnEditor.instance.userIsEditingAnInputField) direction = 1;
            else if (Input.GetKeyDown(KeyCode.Tab))
            {
                isTab = true;
                direction = (Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.LeftShift)) ? -1 : 1;
            }

            // 执行导航
            if (direction != 0)
            {
                int newIndex = currentIndex;

                if (!isTab)
                {
                    // 普通模式：逐个遍历
                    newIndex += direction;
                }
                else
                {
                    // [修复] Tab 模式：使用循环算法查找下一个可交互对象
                    // 之前的 while 循环撞墙就停了，现在改用取模运算来实现首尾相接
                    int count = allControls.Count;
                    int foundIndex = -1;

                    // 从下一个位置开始，最多找一圈 (1 到 count-1)
                    // 避免死循环，也避免重复选中自己
                    for (int i = 1; i < count; i++)
                    {
                        // 核心算法：(当前位置 + 偏移量) % 总数
                        // 这样算出来的 index 永远会在 0 到 count-1 之间循环
                        int checkIndex = (currentIndex + direction * i) % count;

                        // C# 的取模可能是负数，需要修正 (例如 -1 % 5 = -1，我们需要的是 4)
                        if (checkIndex < 0) checkIndex += count;

                        // 检查是否有效
                        if (checkIndex >= 0 && checkIndex < count)
                        {
                            var element = allControls[checkIndex];
                            // 只有当它是 Selectable (按钮/输入框) 且 激活状态 时才停下
                            if (element != null && element.GetComponent<Selectable>() != null && element.isActiveAndEnabled)
                            {
                                foundIndex = checkIndex;
                                break;
                            }
                        }
                    }

                    // 如果找到了合法的目标，才更新位置；没找到就保持原地不动
                    if (foundIndex != -1)
                    {
                        newIndex = foundIndex;
                    }
                }

                // 循环列表 (Looping)
                if (newIndex >= allControls.Count) newIndex = 0;
                if (newIndex < 0) newIndex = allControls.Count - 1;

                SelectUIElement(allControls[newIndex], targetEventSystem);
                currentIndex = newIndex;
            }

            // 处理确认键
            if (isEnterKey)
            {
                var currentGraphic = allControls[currentIndex];
                var item = currentGraphic.GetComponent<Selectable>();

                if (item != null && item.interactable)
                {
                    if (item is Button btn) btn.onClick.Invoke();
                    else if (item is Toggle tgl)
                    {
                        tgl.isOn = !tgl.isOn;
                        Narration.Say(tgl.isOn ? "已选中" : "未选中", NarrationCategory.Notification);
                    }
                    else if (item is InputField input)
                    {
                        input.ActivateInputField();
                        Narration.Say("编辑框已激活", NarrationCategory.Notification);
                    }
                    else if (item is TMP_InputField tmpInput)
                    {
                        tmpInput.ActivateInputField();
                        Narration.Say("编辑框已激活", NarrationCategory.Notification);
                    }
                }
            }
        }

        private void SelectUIElement(Graphic element, EventSystem es)
        {
            if (element == null) return;

            var selectableComponent = element.GetComponent<Selectable>();

            // 1. 如果是可交互控件，通知 Unity 系统选中它
            if (selectableComponent != null && es != null)
            {
                selectableComponent.Select();
                es.SetSelectedGameObject(selectableComponent.gameObject);
            }
            // 如果是纯文本，不设置系统焦点，只由本 Mod 记录位置

            // 2. 朗读逻辑
            if (lastSelectedObj != element.gameObject)
            {
                lastSelectedObj = element.gameObject;
                string textToSay = "";

                // 提取文本
                var tmComp = element.GetComponentInChildren<TMP_Text>();
                if (tmComp != null) textToSay = tmComp.text;

                var textComp = element.GetComponentInChildren<Text>();
                if (element is Text selfText) textComp = selfText;
                if (textComp != null) textToSay = textComp.text;

                // 修饰文本
                if (selectableComponent is InputField inputField)
                {
                    textToSay = $"编辑框 {inputField.text}";
                    if (string.IsNullOrEmpty(inputField.text) && inputField.placeholder is Text ph)
                        textToSay = $"编辑框 {ph.text}";
                }
                else if (selectableComponent is TMP_InputField tmpInputField)
                {
                    textToSay = $"编辑框 {tmpInputField.text}";
                }
                else if (selectableComponent is Toggle toggle)
                {
                    textToSay = $"{textToSay} " + (toggle.isOn ? "已选中" : "未选中");
                }

                if (string.IsNullOrEmpty(textToSay)) textToSay = element.name;

                // 发送朗读
                Debug.Log($"[朗读] {textToSay}");
                Narration.Say(textToSay, NarrationCategory.Navigation);
            }
        }

        // ===================================================================================
        // 时间轴导航
        // ===================================================================================

        private void HandleTimelineNavigation()
        {
                if (scnEditor.instance.currentTab != currentTab)
            {
                currentTab = scnEditor.instance.currentTab;
                Narration.Say(RDString.Get($"editor.{currentTab.ToString().ToLower().Replace("song", "sounds")}"),NarrationCategory.Navigation);
            }
        }
    }



}