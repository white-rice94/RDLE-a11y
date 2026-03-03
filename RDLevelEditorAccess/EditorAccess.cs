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
    [BepInPlugin("com.hzt.rd-editor-access", "RDEditorAccess", "1.0")]
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

        // ===================================================================================
        // 虚拟菜单状态
        // ===================================================================================
        private enum VirtualMenuState
        {
            None,
            CharacterSelect,      // 角色选择（添加轨道/精灵）
            EventTypeSelect       // 事件类型选择
        }

        private VirtualMenuState virtualMenuState = VirtualMenuState.None;
        private int virtualMenuIndex = 0;
        private string virtualMenuPurpose = "";  // "row", "sprite", "event"
        private LevelEventType selectedEventType;

        // 编辑光标：时间轴上的虚拟锚点，用于精确控制事件插入/粘贴位置
        internal BarAndBeat _editCursor = new BarAndBeat(1, 1f);

        public void Awake()
        {
            Instance = this;
            inputFieldReader = new InputFieldReader();
            AccessibilityBridge.Initialize(gameObject);
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

            // --- FileIPC 轮询 ---
            AccessibilityBridge.Update();

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
            currentMenu = ""; // 重置当前菜单状态
            CustomUINavigator.EnableNativeNavigation(); // 恢复原生导航
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
                // 安全检查：确保 allControls 已初始化
                if (allControls == null || allControls.Count == 0)
                {
                    Debug.LogWarning($"[HandleGeneralUINavigation] 无法导航：allControls 未初始化或为空");
                    return;
                }

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
                // 安全检查：确保 allControls 已初始化且 currentIndex 有效
                if (allControls == null || currentIndex < 0 || currentIndex >= allControls.Count)
                {
                    Debug.LogWarning($"[HandleGeneralUINavigation] 无法处理 Enter 键：allControls={allControls?.Count ?? -1}, currentIndex={currentIndex}");
                    return;
                }

                var currentGraphic = allControls[currentIndex];
                if (currentGraphic == null)
                {
                    Debug.LogWarning($"[HandleGeneralUINavigation] allControls[{currentIndex}] 为 null");
                    return;
                }

                var item = currentGraphic.GetComponent<Selectable>();

                if (item != null && item.interactable)
                {
                    if (item is Button btn) btn.onClick.Invoke();
                    else if (item is Toggle tgl)
                    {
                        tgl.isOn = !tgl.isOn;
                        Narration.Say(tgl.isOn ? RDString.Get("eam.check.checked") : RDString.Get("eam.check.unchecked"), NarrationCategory.Notification);
                    }
                    else if (item is InputField input)
                    {
                        input.ActivateInputField();
                        Narration.Say(RDString.Get("eam.input.activated"), NarrationCategory.Notification);
                    }
                    else if (item is TMP_InputField tmpInput)
                    {
                        tmpInput.ActivateInputField();
                        Narration.Say(RDString.Get("eam.input.activated"), NarrationCategory.Notification);
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
            var editor = scnEditor.instance;
            if (editor == null) return;

            // 虚拟菜单优先处理
            if (virtualMenuState != VirtualMenuState.None)
            {
                HandleVirtualMenu();
                return;
            }

            if (editor.currentTab != currentTab)
            {
                currentTab = editor.currentTab;
                Narration.Say(RDString.Get($"editor.{currentTab.ToString().ToLower().Replace("song", "sounds")}"),NarrationCategory.Navigation);
            }

            // 上下箭头切换轨道 (仅在 Rows 和 Sprites Tab)
            HandleTrackNavigation();

            // 左右箭头选择事件
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                // 如果没有选中事件，或者选中的事件不属于当前 Tab，则重新选择
                if (editor.selectedControls.Count <= 0 || !IsSelectedEventInCurrentTab(editor))
                {
                    chooseNearestEvent();
                }
            }

            // Ctrl+Enter: 打开事件属性编辑器
            if (Input.GetKeyDown(KeyCode.Return) && 
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                if (editor.selectedControl != null)
                {
                    AccessibilityBridge.EditEvent(editor.selectedControl.levelEvent);
                    //Narration.Say(RDString.Get("eam.editor.openPropEditor"), NarrationCategory.Instruction);// 过于冗余，去掉。
                }
            }

            // Shift+Enter: 编辑当前选中的轨道
            if (Input.GetKeyDown(KeyCode.Return) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                if (editor.currentTab == Tab.Rows && editor.selectedRowIndex >= 0)
                {
                    AccessibilityBridge.EditRow(editor.selectedRowIndex);
                    //Narration.Say(RDString.Get("eam.editor.openTrackEditor"), NarrationCategory.Instruction);// 过于冗余，去掉。
                }
                else if (editor.currentTab == Tab.Sprites && !string.IsNullOrEmpty(editor.selectedSprite))
                {
                    // TODO: 精灵编辑支持
                    Narration.Say(RDString.Get("eam.sprite.editNotSupported"), NarrationCategory.Navigation);
                }
            }

            // 大键盘 0：打开关卡元数据编辑器
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                AccessibilityBridge.EditSettings();
            }

            // NEW: Return (无修饰符)：跳转到选中事件所在的小节并开始播放
            if (Input.GetKeyDown(KeyCode.Return) &&
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                if (editor.selectedControl != null && editor.selectedControl.levelEvent != null)
                {
                    int eventBar = editor.selectedControl.levelEvent.bar;
                    editor.ScrubToBar(eventBar, playAfterScrubbing: true);
                    Narration.Say(string.Format(RDString.Get("eam.event.jumpAndPlay"), $"{RDString.Get("editor.bar")} {eventBar}"), NarrationCategory.Notification);
                }
            }

            // Insert: 添加事件
            if (Input.GetKeyDown(KeyCode.Insert) && 
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                StartEventTypeSelect();
            }

            // Ctrl+Insert: 添加轨道/精灵（取决于当前 Tab）
            if (Input.GetKeyDown(KeyCode.Insert) && 
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                if (editor.currentTab == Tab.Rows)
                {
                    StartCharacterSelect("row");
                }
                else if (editor.currentTab == Tab.Sprites)
                {
                    StartCharacterSelect("sprite");
                }
                else
                {
                    Narration.Say(RDString.Get("eam.action.addRowOrSprite"), NarrationCategory.Navigation);
                }
            }

            // ===================================================================================
            // 编辑光标快捷键
            // ===================================================================================

            // 斜杠：将编辑光标设置为当前播放头位置
            if (Input.GetKeyDown(KeyCode.Slash) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) &&
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                var tl = editor.timeline;
                _editCursor = tl.GetBarAndBeatWithPosX(tl.playhead.anchoredPosition.x);
                Narration.Say(FormatBarAndBeat(_editCursor), NarrationCategory.Navigation);
            }

            // Shift+斜杠：朗读编辑光标当前位置
            if (Input.GetKeyDown(KeyCode.Slash) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                Narration.Say(FormatBarAndBeat(_editCursor) + RDString.Get("eam.cursor.suffix"), NarrationCategory.Navigation);
            }

            // Ctrl+斜杠：将编辑光标吸附到最近的正拍或半拍
            // 使用像素空间运算：将当前 X 坐标四舍五入到最近的 0.5 * cellWidth 倍数
            if (Input.GetKeyDown(KeyCode.Slash) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                var tl = editor.timeline;
                float cursorX = tl.GetPosXFromBarAndBeat(_editCursor);
                float halfBeat = tl.cellWidth * 0.5f;
                float snappedX = Mathf.Max(0f, Mathf.Round(cursorX / halfBeat) * halfBeat);
                _editCursor = tl.GetBarAndBeatWithPosX(snappedX);
                Narration.Say(RDString.Get("eam.cursor.snapPrefix") + FormatBarAndBeat(_editCursor), NarrationCategory.Navigation);
            }

            // Alt+斜杠：跳转到编辑光标所在小节并播放
            // 使用 Alt 而非 Ctrl，因为 LevelSpeed 在 Ctrl 按下时返回 0.75 会导致播放变慢
            if (Input.GetKeyDown(KeyCode.Slash) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                editor.ScrubToBar(_editCursor.bar, playAfterScrubbing: true);
            }

            // Ctrl+Shift+斜杠：打开跳转到位置对话框
            if (Input.GetKeyDown(KeyCode.Slash) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                AccessibilityBridge.JumpToCursor();
                return;
            }

            // 逗号：编辑光标后退（Alt: 0.01拍，Shift: 0.1拍，无修饰: 1拍）
            if (Input.GetKeyDown(KeyCode.Comma))
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                MoveEditCursor(alt ? -0.01f : shift ? -0.1f : -1f);
            }

            // 句号：编辑光标前进（Alt: 0.01拍，Shift: 0.1拍，无修饰: 1拍）
            if (Input.GetKeyDown(KeyCode.Period))
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                MoveEditCursor(alt ? 0.01f : shift ? 0.1f : 1f);
            }

            // ===================================================================================
            // 快速移动事件（Z/C/X 键）
            // ===================================================================================

            // 检查是否按下了 Ctrl 键（避免与 Ctrl+X/Ctrl+C 冲突）
            bool ctrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // Z键：选中事件后退（Beat模式: Alt 0.01拍/Shift 0.1拍/无修饰 1拍；BarOnly模式: 1小节）
            if (Input.GetKeyDown(KeyCode.Z) && !ctrlPressed)
            {
                var moveMode = GetSelectedEventsMoveMode();
                if (moveMode == EventMoveMode.Mixed)
                {
                    Narration.Say(RDString.Get("eam.event.mixedMoveBlocked"), NarrationCategory.Navigation);
                }
                else if (moveMode == EventMoveMode.BarOnly)
                {
                    MoveSelectedEventsByBar(-1);
                }
                else
                {
                    bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    MoveSelectedEvents(alt ? -0.01f : shift ? -0.1f : -1f);
                }
            }

            // X键：选中事件前进（Beat模式: Alt 0.01拍/Shift 0.1拍/无修饰 1拍；BarOnly模式: 1小节）
            if (Input.GetKeyDown(KeyCode.X) && !ctrlPressed)
            {
                var moveMode = GetSelectedEventsMoveMode();
                if (moveMode == EventMoveMode.Mixed)
                {
                    Narration.Say(RDString.Get("eam.event.mixedMoveBlocked"), NarrationCategory.Navigation);
                }
                else if (moveMode == EventMoveMode.BarOnly)
                {
                    MoveSelectedEventsByBar(1);
                }
                else
                {
                    bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    MoveSelectedEvents(alt ? 0.01f : shift ? 0.1f : 1f);
                }
            }

            // C键：选中事件吸附到最近的正拍或半拍
            if (Input.GetKeyDown(KeyCode.C) && !ctrlPressed)
            {
                var moveMode = GetSelectedEventsMoveMode();
                if (moveMode == EventMoveMode.Mixed)
                    Narration.Say(RDString.Get("eam.event.mixedMoveBlocked"), NarrationCategory.Navigation);
                else if (moveMode != EventMoveMode.BarOnly)
                    SnapSelectedEventsToHalfBeat();
            }
        }

        /// <summary>
        /// 处理虚拟菜单导航
        /// </summary>
        private void HandleVirtualMenu()
        {
            switch (virtualMenuState)
            {
                case VirtualMenuState.CharacterSelect:
                    HandleCharacterSelectMenu();
                    break;
                case VirtualMenuState.EventTypeSelect:
                    HandleEventTypeSelectMenu();
                    break;
            }
        }

        /// <summary>
        /// 开始角色选择菜单
        /// </summary>
        private void StartCharacterSelect(string purpose)
        {
            virtualMenuState = VirtualMenuState.CharacterSelect;
            virtualMenuPurpose = purpose;
            virtualMenuIndex = 0;
            
            Narration.Say(RDString.Get("eam.char.selectPrompt"), NarrationCategory.Instruction);
            Narration.Say(GetCharacterName(RDEditorConstants.AvailableCharacters[0]), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 处理角色选择菜单
        /// </summary>
        private void HandleCharacterSelectMenu()
        {
            var characters = RDEditorConstants.AvailableCharacters;
            
            // 上下箭头导航
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                virtualMenuIndex = (virtualMenuIndex - 1 + characters.Length) % characters.Length;
                Narration.Say(GetCharacterName(characters[virtualMenuIndex]), NarrationCategory.Navigation);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                virtualMenuIndex = (virtualMenuIndex + 1) % characters.Length;
                Narration.Say(GetCharacterName(characters[virtualMenuIndex]), NarrationCategory.Navigation);
            }
            
            // 首字母跳转
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                string keyName = key.ToString();
                if (keyName.StartsWith("Alpha") || keyName.Length == 1)
                {
                    if (Input.GetKeyDown(key))
                    {
                        char pressedChar = keyName.Replace("Alpha", "")[0];
                        for (int i = 0; i < characters.Length; i++)
                        {
                            string charName = characters[i].ToString();
                            if (charName.StartsWith(pressedChar.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                virtualMenuIndex = i;
                                Narration.Say(GetCharacterName(characters[virtualMenuIndex]), NarrationCategory.Navigation);
                                break;
                            }
                        }
                    }
                }
            }
            
            // 回车确认
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Character selectedChar = characters[virtualMenuIndex];
                
                if (virtualMenuPurpose == "row")
                {
                    AddNewRow(selectedChar);
                }
                else if (virtualMenuPurpose == "sprite")
                {
                    AddNewSprite(selectedChar);
                }
                
                CloseVirtualMenu();
            }
            
            // Escape 取消
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                CloseVirtualMenu();
            }
        }

        /// <summary>
        /// 开始事件类型选择菜单
        /// </summary>
        private void StartEventTypeSelect()
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            var eventTypes = GetAvailableEventTypes(editor.currentTab);
            Debug.Log($"[StartEventTypeSelect] Tab: {editor.currentTab}, 事件类型数量: {eventTypes?.Count ?? 0}");
            
            if (eventTypes == null || eventTypes.Count == 0)
            {
                Narration.Say(RDString.Get("eam.event.noTypesAvailable"), NarrationCategory.Navigation);
                return;
            }

            // 打印前几个事件类型用于调试
            if (eventTypes.Count > 0)
            {
                Debug.Log($"[StartEventTypeSelect] 第一个事件类型: {eventTypes[0]}");
            }

            virtualMenuState = VirtualMenuState.EventTypeSelect;
            virtualMenuIndex = 0;

            Narration.Say(RDString.Get("eam.event.selectPrompt"), NarrationCategory.Instruction);
            Narration.Say(GetEventTypeName(eventTypes[0]), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 处理事件类型选择菜单
        /// </summary>
        private void HandleEventTypeSelectMenu()
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            var eventTypes = GetAvailableEventTypes(editor.currentTab);
            if (eventTypes == null || eventTypes.Count == 0) return;
            
            // 上下箭头导航
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                virtualMenuIndex = (virtualMenuIndex - 1 + eventTypes.Count) % eventTypes.Count;
                Narration.Say(GetEventTypeName(eventTypes[virtualMenuIndex]), NarrationCategory.Navigation);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                virtualMenuIndex = (virtualMenuIndex + 1) % eventTypes.Count;
                Narration.Say(GetEventTypeName(eventTypes[virtualMenuIndex]), NarrationCategory.Navigation);
            }
            
            // 首字母跳转
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                string keyName = key.ToString();
                if (keyName.Length == 1 && char.IsLetter(keyName[0]))
                {
                    if (Input.GetKeyDown(key))
                    {
                        char pressedChar = keyName[0];
                        for (int i = 0; i < eventTypes.Count; i++)
                        {
                            string typeName = eventTypes[i].ToString();
                            if (typeName.StartsWith(pressedChar.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                virtualMenuIndex = i;
                                Narration.Say(GetEventTypeName(eventTypes[virtualMenuIndex]), NarrationCategory.Navigation);
                                break;
                            }
                        }
                    }
                }
            }
            
            // 回车确认 - 直接创建事件（使用默认值）并打开 Helper
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                selectedEventType = eventTypes[virtualMenuIndex];
                CloseVirtualMenu();
                
                // 使用编辑光标位置创建事件
                var barAndBeat = _editCursor;
                int bar = barAndBeat.bar;
                float beat = barAndBeat.beat;
                int row = editor.selectedRowIndex >= 0 ? editor.selectedRowIndex : 0;
                
                CreateEventAndEdit(selectedEventType, bar, beat, row);
            }
            
            // Escape 取消
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                CloseVirtualMenu();
            }
        }

        /// <summary>
        /// 关闭虚拟菜单
        /// </summary>
        private void CloseVirtualMenu()
        {
            virtualMenuState = VirtualMenuState.None;
            virtualMenuPurpose = "";
        }

        /// <summary>
        /// 事件移动模式：Beat（支持拍内定位）、BarOnly（仅小节定位）、Mixed（混合，不可移动）
        /// </summary>
        private enum EventMoveMode { Beat, BarOnly, Mixed }

        /// <summary>
        /// 检查所有选中事件的 usesBeat 属性，返回移动模式。
        /// </summary>
        private EventMoveMode GetSelectedEventsMoveMode()
        {
            var editor = scnEditor.instance;
            if (editor?.selectedControls == null || editor.selectedControls.Count == 0)
                return EventMoveMode.Beat;

            bool hasBeat = false, hasBarOnly = false;
            foreach (var control in editor.selectedControls)
            {
                if (control.levelEvent.usesBeat) hasBeat = true;
                else hasBarOnly = true;
            }
            if (hasBeat && hasBarOnly) return EventMoveMode.Mixed;
            return hasBarOnly ? EventMoveMode.BarOnly : EventMoveMode.Beat;
        }

        /// <summary>
        /// 将编辑光标在时间轴上移动 deltaBeat 拍（正数向右，负数向左）。
        /// 使用像素空间运算以自动处理变速小节（SetCrotchetsPerBar）。
        /// </summary>
        private void MoveEditCursor(float deltaBeat)
        {
            var editor = scnEditor.instance;
            if (editor?.timeline == null) return;

            var tl = editor.timeline;
            int oldBar = _editCursor.bar;

            float cursorX = tl.GetPosXFromBarAndBeat(_editCursor);
            float newX = Mathf.Max(0f, cursorX + deltaBeat * tl.cellWidth);
            _editCursor = tl.GetBarAndBeatWithPosX(newX);

            string announcement = _editCursor.bar != oldBar
                ? FormatBarAndBeat(_editCursor)
                : FormatBeatOnly(_editCursor.beat);
            Narration.Say(announcement, NarrationCategory.Navigation);
        }

        /// <summary>
        /// 将所有选中事件在时间轴上移动 deltaBeat 拍（正数向右，负数向左）。
        /// 使用像素空间运算以自动处理变速小节（SetCrotchetsPerBar）。
        /// </summary>
        private void MoveSelectedEvents(float deltaBeat)
        {
            var editor = scnEditor.instance;
            if (editor?.timeline == null) return;
            if (editor.selectedControls == null || editor.selectedControls.Count == 0)
            {
                Narration.Say(RDString.Get("eam.event.noSelection"), NarrationCategory.Navigation);
                return;
            }

            var tl = editor.timeline;
            int oldBar = editor.selectedControls[0].bar;

            using (new SaveStateScope())
            {
                foreach (var control in editor.selectedControls)
                {
                    float posX = tl.GetPosXFromBarAndBeat(control.levelEvent.barAndBeat);
                    float newX = Mathf.Max(0f, posX + deltaBeat * tl.cellWidth);
                    var newPos = tl.GetBarAndBeatWithPosX(newX);
                    control.bar = newPos.bar;
                    control.beat = newPos.beat;
                    control.UpdateUI();
                }
                tl.UpdateUI();
            }

            var first = editor.selectedControls[0];
            // 更新 inspector 面板以持久化更改（防止取消选择时回退）
            editor.inspectorPanelManager.GetCurrent()?.UpdateUI(first.levelEvent);

            string announcement = first.bar != oldBar
                ? FormatBarAndBeat(first.levelEvent.barAndBeat)
                : FormatBeatOnly(first.beat);
            Narration.Say(announcement, NarrationCategory.Navigation);
        }

        /// <summary>
        /// 将所有选中事件吸附到最近的正拍或半拍（0.5 拍间隔）。
        /// 使用像素空间运算以自动处理变速小节。
        /// </summary>
        private void SnapSelectedEventsToHalfBeat()
        {
            var editor = scnEditor.instance;
            if (editor?.timeline == null) return;
            if (editor.selectedControls == null || editor.selectedControls.Count == 0)
            {
                Narration.Say(RDString.Get("eam.event.noSelection"), NarrationCategory.Navigation);
                return;
            }

            var tl = editor.timeline;
            float halfBeat = tl.cellWidth * 0.5f;

            using (new SaveStateScope())
            {
                foreach (var control in editor.selectedControls)
                {
                    float posX = tl.GetPosXFromBarAndBeat(control.levelEvent.barAndBeat);
                    float snappedX = Mathf.Max(0f, Mathf.Round(posX / halfBeat) * halfBeat);
                    var newPos = tl.GetBarAndBeatWithPosX(snappedX);
                    control.bar = newPos.bar;
                    control.beat = newPos.beat;
                    control.UpdateUI();
                }
                tl.UpdateUI();
            }

            var first = editor.selectedControls[0];
            // 更新 inspector 面板以持久化更改（防止取消选择时回退）
            editor.inspectorPanelManager.GetCurrent()?.UpdateUI(first.levelEvent);

            Narration.Say(RDString.Get("eam.cursor.snapPrefix") + FormatBarAndBeat(first.levelEvent.barAndBeat), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 将所有选中的 bar-only 事件按小节移动（+1/-1）。
        /// </summary>
        private void MoveSelectedEventsByBar(int deltaBar)
        {
            var editor = scnEditor.instance;
            if (editor?.selectedControls == null || editor.selectedControls.Count == 0)
            {
                Narration.Say(RDString.Get("eam.event.noSelection"), NarrationCategory.Navigation);
                return;
            }

            using (new SaveStateScope())
            {
                foreach (var control in editor.selectedControls)
                {
                    int newBar = Mathf.Max(1, control.bar + deltaBar);
                    control.bar = newBar;
                    control.UpdateUI();
                }
                editor.timeline?.UpdateUI();
            }

            var first = editor.selectedControls[0];
            editor.inspectorPanelManager.GetCurrent()?.UpdateUI(first.levelEvent);

            Narration.Say(FormatBarOnly(first.bar), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 将 BarAndBeat 格式化为本地化字符串（如"3小节2拍"或"Bar 3 Beat 2"）。
        /// </summary>
        private static string FormatBarAndBeat(BarAndBeat bb) => ModUtils.FormatBarAndBeat(bb);

        internal static string FormatBeat(float beat) => ModUtils.FormatBeat(beat);

        /// <summary>
        /// 将拍号格式化为带本地化单位的完整字符串（如"2拍"或"Beat 2"）。
        /// </summary>
        private static string FormatBeatOnly(float beat)
        {
            return string.Format(RDString.Get("eam.barbeat.beatOnly"), FormatBeat(beat));
        }

        /// <summary>
        /// 将小节号格式化为带本地化单位的字符串（如"3小节"或"Bar 3"）。
        /// </summary>
        private static string FormatBarOnly(int bar)
        {
            return string.Format(RDString.Get("eam.barbeat.barOnly"), bar);
        }

        /// <summary>
        /// 添加新轨道
        /// </summary>
        private void AddNewRow(Character character)
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            int roomIndex = editor.selectedRowsTabPageIndex;
            
            var rowData = new LevelEvent_MakeRow();
            rowData.rooms = new int[1] { roomIndex };
            rowData.character = character;
            
            editor.AddNewRow(rowData);
            editor.tabSection_rows.UpdateUI();
            
                Narration.Say(string.Format(RDString.Get("eam.track.added"), GetCharacterName(character)), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 添加新精灵
        /// </summary>
        private void AddNewSprite(Character character)
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            int roomIndex = editor.selectedSpritesTabPageIndex;
            
            var spriteData = new LevelEvent_MakeSprite();
            spriteData.rooms = new int[1] { roomIndex };
            spriteData.character = character;
            
            editor.AddNewSprite(spriteData);
            editor.tabSection_sprites.UpdateUI();
            
                Narration.Say(string.Format(RDString.Get("eam.sprite.added"), GetCharacterName(character)), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 创建事件并打开 Helper 编辑
        /// </summary>
        private void CreateEventAndEdit(LevelEventType eventType, int bar, float beat, int row)
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            // 构建完整的类型名称
            string typeName = $"RDLevelEditor.LevelEvent_{eventType}";
            Debug.Log($"[CreateEventAndEdit] 尝试创建事件，类型名: {typeName}");
            
            // 使用反射创建事件实例
            var eventTypeObj = Type.GetType(typeName);
            if (eventTypeObj == null)
            {
                // 尝试带程序集名称
                typeName = $"RDLevelEditor.LevelEvent_{eventType}, Assembly-CSharp";
                Debug.Log($"[CreateEventAndEdit] 重试带程序集: {typeName}");
                eventTypeObj = Type.GetType(typeName);
            }
            
            if (eventTypeObj == null)
            {
                Debug.LogError($"[CreateEventAndEdit] 无法找到类型: LevelEvent_{eventType}");
                Narration.Say(string.Format(RDString.Get("eam.event.createFailed"), eventType), NarrationCategory.Navigation);
                return;
            }

            var levelEvent = Activator.CreateInstance(eventTypeObj) as LevelEvent_Base;
            if (levelEvent == null)
            {
                Narration.Say(RDString.Get("eam.event.createError"), NarrationCategory.Navigation);
                return;
            }

            // 设置基本属性
            levelEvent.bar = bar;
            levelEvent.beat = beat;
            if (editor.currentTab == Tab.Rows)
            {
                levelEvent.row = row;
            }
            
            // 调用 OnCreate
            levelEvent.OnCreate();
            
            // 创建控件
            var control = editor.CreateEventControl(levelEvent, editor.currentTab);
            control.UpdateUI();
            
            // 选中新创建的事件
            editor.SelectEventControl(control, true);
            
            Narration.Say(string.Format(RDString.Get("eam.event.createdAndOpening"), GetEventTypeName(eventType)), NarrationCategory.Navigation);
            
            // 自动打开 Helper 编辑
            AccessibilityBridge.EditEvent(levelEvent);
        }

        /// <summary>
        /// 获取当前 Tab 可用的事件类型
        /// </summary>
        private List<LevelEventType> GetAvailableEventTypes(Tab tab)
        {
            Debug.Log($"[GetAvailableEventTypes] 查询 Tab: {tab}");
            Debug.Log($"[GetAvailableEventTypes] levelEventTabs 键: {string.Join(", ", RDEditorConstants.levelEventTabs.Keys)}");
            
            if (RDEditorConstants.levelEventTabs.ContainsKey(tab))
            {
                var result = RDEditorConstants.levelEventTabs[tab];
                Debug.Log($"[GetAvailableEventTypes] 找到 {result.Count} 个事件类型");
                return result;
            }
            Debug.Log($"[GetAvailableEventTypes] Tab {tab} 不在字典中");
            return new List<LevelEventType>();
        }

        /// <summary>
        /// 获取角色名称（本地化）
        /// </summary>
        private string GetCharacterName(Character character)
        {
            return RDString.Get($"enum.Character.{character}");
        }

        /// <summary>
        /// 获取事件类型名称（本地化）
        /// </summary>
        private string GetEventTypeName(LevelEventType eventType)
        {
            return RDString.Get($"editor.{eventType}");
        }

        /// <summary>
        /// 处理轨道导航（上下箭头切换轨道）
        /// </summary>
        private void HandleTrackNavigation()
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            // 仅在 Rows 和 Sprites Tab 时处理
            if (editor.currentTab != Tab.Rows && editor.currentTab != Tab.Sprites) return;

            bool upPressed = Input.GetKeyDown(KeyCode.UpArrow);
            bool downPressed = Input.GetKeyDown(KeyCode.DownArrow);

            if (!upPressed && !downPressed) return;

            if (editor.currentTab == Tab.Rows)
            {
                HandleRowNavigation(editor, upPressed ? -1 : 1);
            }
            else if (editor.currentTab == Tab.Sprites)
            {
                HandleSpriteNavigation(editor, upPressed ? -1 : 1);
            }
        }

        /// <summary>
        /// 处理 Row 导航
        /// </summary>
        private void HandleRowNavigation(scnEditor editor, int direction)
        {
            var pageRows = editor.currentPageRowsData;
            if (pageRows == null || pageRows.Count == 0)
            {
                Narration.Say(RDString.Get("eam.track.noAvailable"), NarrationCategory.Navigation);
                return;
            }

            int currentIndex = GetCurrentRowIndexInPage(editor);
            int newIndex = currentIndex + direction;

            // 边界检查 - 到达边界时重新朗读当前轨道信息
            if (newIndex < 0)
            {
                // 已是第一条轨道，重新朗读当前轨道信息
                ReadCurrentRowInfo(editor, currentIndex, pageRows);
                return;
            }
            if (newIndex >= pageRows.Count)
            {
                // 已是最后一条轨道，重新朗读当前轨道信息
                ReadCurrentRowInfo(editor, currentIndex, pageRows);
                return;
            }

            // 选择新轨道
            SelectRowByIndex(newIndex, pageRows);
        }

        /// <summary>
        /// 处理 Sprite 导航
        /// </summary>
        private void HandleSpriteNavigation(scnEditor editor, int direction)
        {
            var pageSprites = editor.currentPageSpritesData;
            if (pageSprites == null || pageSprites.Count == 0)
            {
                Narration.Say(RDString.Get("eam.sprite.noAvailable"), NarrationCategory.Navigation);
                return;
            }

            int currentIndex = GetCurrentSpriteIndexInPage(editor);
            int newIndex = currentIndex + direction;

            // 边界检查 - 到达边界时重新朗读当前精灵信息
            if (newIndex < 0)
            {
                // 已是第一个精灵，重新朗读当前精灵信息
                ReadCurrentSpriteInfo(editor, currentIndex, pageSprites);
                return;
            }
            if (newIndex >= pageSprites.Count)
            {
                // 已是最后一个精灵，重新朗读当前精灵信息
                ReadCurrentSpriteInfo(editor, currentIndex, pageSprites);
                return;
            }

            // 选择新精灵
            SelectSpriteByIndex(newIndex, pageSprites);
        }

        /// <summary>
        /// 获取当前选中的 Row 在当前页面中的索引
        /// </summary>
        private int GetCurrentRowIndexInPage(scnEditor editor)
        {
            if (editor.selectedRowIndex < 0) return -1;

            var pageRows = editor.currentPageRowsData;
            var selectedRow = editor.rowsData.ElementAtOrDefault(editor.selectedRowIndex);
            if (selectedRow == null) return -1;

            return pageRows.IndexOf(selectedRow);
        }

        /// <summary>
        /// 获取当前选中的 Sprite 在当前页面中的索引
        /// </summary>
        private int GetCurrentSpriteIndexInPage(scnEditor editor)
        {
            if (string.IsNullOrEmpty(editor.selectedSprite)) return -1;

            var pageSprites = editor.currentPageSpritesData;
            for (int i = 0; i < pageSprites.Count; i++)
            {
                if (pageSprites[i].spriteId == editor.selectedSprite)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 根据索引选择 Row
        /// </summary>
        private void SelectRowByIndex(int indexInPage, List<LevelEvent_MakeRow> pageRows)
        {
            if (indexInPage < 0 || indexInPage >= pageRows.Count) return;

            var rowData = pageRows[indexInPage];
            int globalIndex = editor.rowsData.IndexOf(rowData);

            // 使用 RowHeader.ShowPanel 选择轨道
            RowHeader.ShowPanel(globalIndex);

            // 获取事件数量
            int eventCount = 0;
            if (globalIndex >= 0 && globalIndex < editor.eventControls_rows.Count)
            {
                eventCount = editor.eventControls_rows[globalIndex].Count;
            }

            // 朗读轨道信息
            string characterName = GetRowCharacterName(rowData);
            Narration.Say(string.Format(RDString.Get("eam.track.info"), indexInPage + 1, characterName, eventCount), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 根据索引选择 Sprite
        /// </summary>
        private void SelectSpriteByIndex(int indexInPage, List<LevelEvent_MakeSprite> pageSprites)
        {
            if (indexInPage < 0 || indexInPage >= pageSprites.Count) return;

            var spriteData = pageSprites[indexInPage];

            // 使用 SpriteHeader.ShowPanel 选择精灵
            SpriteHeader.ShowPanel(spriteData.spriteId);

            // 获取事件数量
            int eventCount = 0;
            int spriteIndex = editor.spritesData.IndexOf(spriteData);
            if (spriteIndex >= 0 && spriteIndex < editor.eventControls_sprites.Count)
            {
                eventCount = editor.eventControls_sprites[spriteIndex].Count;
            }

            // 朗读精灵信息
            string displayName = GetSpriteDisplayName(spriteData);
            Narration.Say(string.Format(RDString.Get("eam.sprite.info"), indexInPage, displayName, eventCount), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 获取 Row 的角色名称
        /// </summary>
        private string GetRowCharacterName(LevelEvent_MakeRow rowData)
        {
            if (rowData.character == Character.Custom)
            {
                return rowData.customCharacterName ?? "自定义";
            }
            return RDString.Get($"enum.Character.{rowData.character}.short");
        }

        /// <summary>
        /// 获取 Sprite 的显示名称
        /// </summary>
        private string GetSpriteDisplayName(LevelEvent_MakeSprite spriteData)
        {
            if (spriteData.character == Character.Custom)
            {
                return spriteData.filename ?? "自定义";
            }
            return RDString.Get($"enum.Character.{spriteData.character}.short");
        }

        /// <summary>
        /// 重新朗读当前 Row 信息（边界处理时使用）
        /// </summary>
        private void ReadCurrentRowInfo(scnEditor editor, int currentIndex, List<LevelEvent_MakeRow> pageRows)
        {
            if (currentIndex < 0 || currentIndex >= pageRows.Count) return;

            var rowData = pageRows[currentIndex];
            int globalIndex = editor.rowsData.IndexOf(rowData);

            // 获取事件数量
            int eventCount = 0;
            if (globalIndex >= 0 && globalIndex < editor.eventControls_rows.Count)
            {
                eventCount = editor.eventControls_rows[globalIndex].Count;
            }

            // 朗读轨道信息
            string characterName = GetRowCharacterName(rowData);
            Narration.Say(string.Format(RDString.Get("eam.track.info"), currentIndex + 1, characterName, eventCount), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 重新朗读当前 Sprite 信息（边界处理时使用）
        /// </summary>
        private void ReadCurrentSpriteInfo(scnEditor editor, int currentIndex, List<LevelEvent_MakeSprite> pageSprites)
        {
            if (currentIndex < 0 || currentIndex >= pageSprites.Count) return;

            var spriteData = pageSprites[currentIndex];

            // 获取事件数量
            int eventCount = 0;
            int spriteIndex = editor.spritesData.IndexOf(spriteData);
            if (spriteIndex >= 0 && spriteIndex < editor.eventControls_sprites.Count)
            {
                eventCount = editor.eventControls_sprites[spriteIndex].Count;
            }

            // 朗读精灵信息
            string displayName = GetSpriteDisplayName(spriteData);
            Narration.Say(string.Format(RDString.Get("eam.sprite.info"), currentIndex, displayName, eventCount), NarrationCategory.Navigation);
        }

        // 辅助属性：快捷访问 editor
        private scnEditor editor => scnEditor.instance;

        private void chooseNearestEvent()
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            // 1. 根据当前 Tab 获取正确的事件列表
            var targetList = GetEventListForCurrentTab(editor);
            if (targetList == null || targetList.Count == 0)
            {
                Debug.Log($"[chooseNearestEvent] 当前 Tab ({currentTab}) 无事件列表或列表为空");
                Narration.Say(RDString.Get("eam.event.noAvailable"), NarrationCategory.Navigation);
                return;
            }

            // 2. 过滤和排序（与原生 GetControlToTheLeft 逻辑一致）
            var validEvents = targetList
                .Where(c => c != null && !c.isBase && editor.EventIsVisible(c.levelEvent))
                .OrderBy(c => c.levelEvent.sortOrder)
                .ThenBy(c => c.levelEvent.y)
                .ToList();

            if (validEvents.Count == 0)
            {
                Debug.Log($"[chooseNearestEvent] 过滤后无有效事件");
                Narration.Say(RDString.Get("eam.event.noAvailable"), NarrationCategory.Navigation);
                return;
            }

            // 3. 选择最接近编辑光标的事件
            LevelEventControl_Base toSelect = FindNearestToEditCursor(validEvents, editor);
            Debug.Log($"[chooseNearestEvent] 选择最接近编辑光标的事件: {toSelect.levelEvent.type} (bar={toSelect.bar}, beat={toSelect.beat:0.##})");

            editor.SelectEventControl(toSelect, true);
        }

        /// <summary>
        /// 根据当前 Tab 获取对应的事件列表
        /// </summary>
        private List<LevelEventControl_Base> GetEventListForCurrentTab(scnEditor editor)
        {
            switch (editor.currentTab)
            {
                case Tab.Song:
                    return editor.eventControls_sounds;
                case Tab.Actions:
                    return editor.eventControls_actions;
                case Tab.Rows:
                    return GetSelectedRowList(editor);
                case Tab.Rooms:
                    return editor.eventControls_rooms;
                case Tab.Sprites:
                    return GetSelectedSpriteList(editor);
                case Tab.Windows:
                    return editor.eventControls_windows;
                default:
                    return editor.eventControls;
            }
        }

        /// <summary>
        /// 获取当前选中 row 的事件列表
        /// </summary>
        private List<LevelEventControl_Base> GetSelectedRowList(scnEditor editor)
        {
            int rowIndex = editor.selectedRowIndex;
            // selectedRowIndex 为 -1 表示未选中任何 row
            if (rowIndex < 0 || rowIndex >= editor.eventControls_rows.Count)
            {
                Debug.Log($"[GetSelectedRowList] 无效的 rowIndex: {rowIndex}, rows 数量: {editor.eventControls_rows.Count}");
                return null;
            }
            return editor.eventControls_rows[rowIndex];
        }

        /// <summary>
        /// 获取当前选中 sprite 的事件列表
        /// </summary>
        private List<LevelEventControl_Base> GetSelectedSpriteList(scnEditor editor)
        {
            string spriteId = editor.selectedSprite;
            if (string.IsNullOrEmpty(spriteId))
            {
                Debug.Log($"[GetSelectedSpriteList] 未选中任何 sprite");
                return null;
            }

            // 根据 spriteId 查找对应的索引
            for (int i = 0; i < editor.spritesData.Count; i++)
            {
                if (editor.spritesData[i].spriteId == spriteId)
                {
                    if (i < editor.eventControls_sprites.Count)
                    {
                        return editor.eventControls_sprites[i];
                    }
                    break;
                }
            }

            Debug.Log($"[GetSelectedSpriteList] 未找到 sprite: {spriteId}");
            return null;
        }

        /// <summary>
        /// 找到最接近视图中心的事件
        /// </summary>
        private LevelEventControl_Base FindNearestToViewCenter(List<LevelEventControl_Base> events, scnEditor editor)
        {
            if (events == null || events.Count == 0) return null;
            if (events.Count == 1) return events[0];

            // 使用时间轴视图中心位置
            float centerX = editor.timelineScript.center;

            // 按 x 位置距离排序，选择最近的事件
            return events
                .OrderBy(c => Mathf.Abs(c.rt.anchoredPosition.x - centerX))
                .First();
        }

        /// <summary>
        /// 查找最接近编辑光标的事件
        /// </summary>
        private LevelEventControl_Base FindNearestToEditCursor(List<LevelEventControl_Base> events, scnEditor editor)
        {
            if (events == null || events.Count == 0) return null;
            if (events.Count == 1) return events[0];

            var timeline = editor.timeline;
            float cursorX = timeline.GetPosXFromBarAndBeat(_editCursor);  // 编辑光标的 X 坐标

            return events
                .OrderBy(c => Mathf.Abs(c.rt.anchoredPosition.x - cursorX))  // 按距离排序
                .First();
        }

        /// <summary>
        /// 检查选中的事件是否属于当前 Tab
        /// </summary>
        private bool IsSelectedEventInCurrentTab(scnEditor editor)
        {
            if (editor?.selectedControl?.levelEvent == null) return false;

            var selectedEvent = editor.selectedControl.levelEvent;
            var currentTab = editor.currentTab;

            // 对于 Rows 和 Sprites，需要额外检查是否在当前选中的 row/sprite 中
            if (currentTab == Tab.Rows)
            {
                int rowIndex = editor.selectedRowIndex;
                if (rowIndex < 0 || rowIndex >= editor.eventControls_rows.Count)
                    return false;
                var rowEvents = editor.eventControls_rows[rowIndex];
                return rowEvents != null && rowEvents.Contains(editor.selectedControl);
            }
            else if (currentTab == Tab.Sprites)
            {
                if (string.IsNullOrEmpty(editor.selectedSprite))
                    return false;

                // 根据 selectedSprite 查找对应的索引
                for (int i = 0; i < editor.spritesData.Count; i++)
                {
                    if (editor.spritesData[i].spriteId == editor.selectedSprite)
                    {
                        if (i < editor.eventControls_sprites.Count)
                        {
                            var spriteEvents = editor.eventControls_sprites[i];
                            return spriteEvents != null && spriteEvents.Contains(editor.selectedControl);
                        }
                        break;
                    }
                }
                return false;
            }
            else
            {
                // 对于其他 Tab，直接比较 tab 属性
                return selectedEvent.tab == currentTab;
            }
        }
    }

    public static class ModUtils
    {
        public static string eventNameI18n(LevelEvent_Base ev)
        {
            string text = ev.type.ToString();
            return RDString.Get("editor." + text);
        }
        public static string eventSelectI18n(LevelEvent_Base ev)
        {
            return eventNameI18n(ev);
        }

        public static string FormatBarAndBeat(BarAndBeat bb)
        {
            return string.Format(RDString.Get("eam.barbeat.format"), bb.bar, FormatBeat(bb.beat));
        }

        public static string FormatBeat(float beat)
        {
            float rounded = Mathf.Round(beat * 100f) / 100f;
            return rounded % 1f == 0f ? $"{(int)rounded}" : $"{rounded:0.##}";
        }
    }

    [HarmonyPatch(typeof(scnEditor))]
    public static class  EditorPatch
    {
        [HarmonyPatch("SelectEventControl")]
        [HarmonyPostfix]
        public static void SelectEventControlPostfix(LevelEventControl_Base newControl)
        {
            if (newControl?.levelEvent == null) return;

            var eventType = newControl.levelEvent.type;

            // 朗读事件名称
            Narration.Say(ModUtils.eventSelectI18n(newControl.levelEvent), NarrationCategory.Navigation);

            // 添加警告消息（朗读事件位置）
            AddEventWarning(newControl.levelEvent);

            // NEW：自动移动playhead到事件位置
            MovePlayheadToSelectedEvent();
        }

        /// <summary>
        /// 朗读事件位置。
        /// </summary>
        private static void AddEventWarning(LevelEvent_Base levelEvent)
        {
            var bb = new BarAndBeat(levelEvent.bar, levelEvent.beat);
            Narration.Say(ModUtils.FormatBarAndBeat(bb), NarrationCategory.Instruction);
        }

        // NEW：移动playhead到选中事件的位置
        private static void MovePlayheadToSelectedEvent()
        {
            try
            {
                var editor = scnEditor.instance;
                if (editor?.timeline == null) return;

                var selectedControl = editor.selectedControl;
                if (selectedControl?.levelEvent == null) return;

                // 获取事件的时间位置
                int eventBar = selectedControl.levelEvent.bar;
                float eventBeat = selectedControl.levelEvent.beat;

                // 创建BarAndBeat结构
                var barAndBeat = new BarAndBeat(eventBar, eventBeat);

                // 转换为playhead的像素X位置
                float posX = editor.timeline.GetPosXFromBarAndBeat(barAndBeat);

                // 移动playhead到该位置（仅更新UI，不重新加载游戏场景）
                editor.timeline.MovePlayHead(posX);

                Debug.Log($"[RDMods] Playhead moved to bar {eventBar}, beat {eventBeat} (event: {selectedControl.levelEvent.type})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RDMods] Failed to move playhead: {ex.Message}");
            }
        }

        [HarmonyPatch("AddEventControlToSelection")]
        [HarmonyPostfix]
        public static void AddEventControlToSelectionPostfix(LevelEventControl_Base newControl)
        {
            if (newControl?.levelEvent == null) return;
            Narration.Say("已选择" + ModUtils.eventSelectI18n(newControl.levelEvent), NarrationCategory.Navigation);
        }
    }

    // ===================================================================================
    // TabSection Patch: 房间切换语音反馈
    // ===================================================================================
    [HarmonyPatch(typeof(TabSection))]
    public static class TabSectionPatch
    {
        [HarmonyPatch("ChangePage")]
        [HarmonyPostfix]
        public static void ChangePagePostfix(TabSection __instance, int index)
        {
            // 只在 Rows 和 Sprites Tab 时朗读房间名称
            if (__instance.tab == Tab.Rows || __instance.tab == Tab.Sprites)
            {
                string roomText = RDString.Get("editor.room");
                Narration.Say($"{roomText} {index + 1}", NarrationCategory.Navigation);
            }
        }
    }

    // ===================================================================================
    // 时间轴导航语音反馈
    // ===================================================================================
    [HarmonyPatch(typeof(Timeline))]
    public static class TimelinePatch
    {
        [HarmonyPatch("PreviousPage")]
        [HarmonyPostfix]
        public static void PreviousPagePostfix(Timeline __instance)
        {
            // 使用 playhead 精确位置
            var barAndBeat = __instance.GetBarAndBeatWithPosX(__instance.playhead.anchoredPosition.x);
            Narration.Say(string.Format(RDString.Get("eam.barbeat.format"), barAndBeat.bar, AccessLogic.FormatBeat(barAndBeat.beat)), NarrationCategory.Navigation);
        }

        [HarmonyPatch("NextPage")]
        [HarmonyPostfix]
        public static void NextPagePostfix(Timeline __instance)
        {
            // 使用 playhead 精确位置
            var barAndBeat = __instance.GetBarAndBeatWithPosX(__instance.playhead.anchoredPosition.x);
            Narration.Say(string.Format(RDString.Get("eam.barbeat.format"), barAndBeat.bar, AccessLogic.FormatBeat(barAndBeat.beat)), NarrationCategory.Navigation);
        }
    }

    [HarmonyPatch(typeof(scnEditor))]
    public static class TimelineNavigationPatch
    {
        [HarmonyPatch("PreviousButtonClick")]
        [HarmonyPostfix]
        public static void PreviousButtonClickPostfix(scnEditor __instance)
        {
            // 使用 playhead 精确位置
            if (__instance.timeline != null)
            {
                var barAndBeat = __instance.timeline.GetBarAndBeatWithPosX(__instance.timeline.playhead.anchoredPosition.x);
                Narration.Say(string.Format(RDString.Get("eam.barbeat.format"), barAndBeat.bar, AccessLogic.FormatBeat(barAndBeat.beat)), NarrationCategory.Navigation);
            }
        }

        [HarmonyPatch("NextButtonClick")]
        [HarmonyPostfix]
        public static void NextButtonClickPostfix(scnEditor __instance)
        {
            // 使用 playhead 精确位置
            if (__instance.timeline != null)
            {
                var barAndBeat = __instance.timeline.GetBarAndBeatWithPosX(__instance.timeline.playhead.anchoredPosition.x);
                Narration.Say(string.Format(RDString.Get("eam.barbeat.format"), barAndBeat.bar, AccessLogic.FormatBeat(barAndBeat.beat)), NarrationCategory.Navigation);
            }
        }
    }

    // ===================================================================================
    // 粘贴后对齐到编辑光标
    // ===================================================================================
    // 让游戏自己处理粘贴（恢复视口中心粘贴），然后在 Postfix 中将粘贴的事件平移到编辑光标位置。
    // 这样可以避免复制游戏代码，同时保持事件间隔不变。

    [HarmonyPatch(typeof(scnEditor), "Paste", new[] { typeof(bool) })]
    public static class PasteAlignmentPatch
    {
        [HarmonyPostfix]
        public static void PastePostfix(scnEditor __instance, bool onNextBar)
        {
            // 仅在 onNextBar=false 时对齐到编辑光标
            if (onNextBar) return;

            // 检查必要条件
            if (AccessLogic.Instance == null) return;
            if (__instance?.selectedControls == null || __instance.selectedControls.Count == 0) return;
            if (__instance.timeline == null) return;

            var tl = __instance.timeline;
            var editCursor = AccessLogic.Instance._editCursor;

            // 找到第一个选中事件（按sortOrder排序，最小的是最早的）
            LevelEventControl_Base firstControl = null;
            int minSortOrder = int.MaxValue;

            foreach (var control in __instance.selectedControls)
            {
                if (control?.levelEvent == null) continue;
                if (control.levelEvent.sortOrder < minSortOrder)
                {
                    minSortOrder = control.levelEvent.sortOrder;
                    firstControl = control;
                }
            }

            if (firstControl == null) return;

            // 计算第一个事件到编辑光标的偏移（像素空间）
            float firstEventX = tl.GetPosXFromBarAndBeat(firstControl.levelEvent.barAndBeat);
            float cursorX = tl.GetPosXFromBarAndBeat(editCursor);
            float offsetX = cursorX - firstEventX;

            // 如果偏移为0，无需移动
            if (Mathf.Abs(offsetX) < 0.01f) return;

            // 移动所有选中事件
            using (new SaveStateScope())
            {
                foreach (var control in __instance.selectedControls)
                {
                    if (control?.levelEvent == null) continue;

                    // 获取当前位置的X坐标
                    float currentX = tl.GetPosXFromBarAndBeat(control.levelEvent.barAndBeat);

                    // 应用偏移
                    float newX = Mathf.Max(0f, currentX + offsetX);

                    // 转换回BarAndBeat
                    var newPos = tl.GetBarAndBeatWithPosX(newX);

                    // 更新位置
                    control.bar = newPos.bar;
                    control.beat = newPos.beat;
                    control.UpdateUI();
                }

                // 更新时间轴UI
                tl.UpdateUI();
            }
        }
    }

    // ===================================================================================
    // RDString 本地化补丁（eam. 命名空间）
    // ===================================================================================
    [HarmonyPatch(typeof(RDString), "Get")]
    public static class RDStringPatch
    {
        private static readonly Dictionary<string, string> _zh = new Dictionary<string, string>
        {
            ["eam.barbeat.format"]              = "{0}小节{1}拍",
            ["eam.barbeat.beatOnly"]             = "{0}拍",
            ["eam.barbeat.barOnly"]              = "{0}小节",
            ["eam.cursor.suffix"]                = " 编辑光标",
            ["eam.cursor.snapPrefix"]            = "吸附到",
            ["eam.action.cancelled"]             = "已取消",
            ["eam.check.checked"]                = "已选中",
            ["eam.check.unchecked"]              = "未选中",
            ["eam.input.activated"]              = "编辑框已激活",
            ["eam.editor.openPropEditor"]        = "正在打开属性编辑器",
            ["eam.editor.openTrackEditor"]       = "正在打开轨道编辑器",
            ["eam.editor.openEventEditor"]       = "正在打开 {0} 属性编辑器",
            ["eam.editor.openSettingsEditor"]    = "正在打开关卡元数据编辑器",
            ["eam.settings.song"]                = "歌曲名",
            ["eam.settings.artist"]              = "艺术家",
            ["eam.settings.author"]              = "作者",
            ["eam.settings.description"]         = "描述",
            ["eam.settings.tags"]                = "标签",
            ["eam.settings.difficulty"]          = "难度",
            ["eam.settings.seizureWarning"]      = "癫痫警告",
            ["eam.settings.canBePlayedOn"]       = "游戏模式",
            ["eam.settings.specialArtistType"]   = "特殊艺术家类型",
            ["eam.settings.artistPermission"]    = "艺术家授权文件",
            ["eam.settings.artistLinks"]         = "艺术家链接",
            ["eam.settings.previewImage"]        = "预览图",
            ["eam.settings.syringeIcon"]         = "注射器图标",
            ["eam.settings.previewSong"]         = "预览歌曲",
            ["eam.settings.previewSongStartTime"]= "预览开始时间",
            ["eam.settings.previewSongDuration"] = "预览时长",
            ["eam.settings.songLabelHue"]        = "标签色调",
            ["eam.settings.songLabelGrayscale"]  = "标签灰度",
            ["eam.settings.levelVolume"]         = "关卡音量",
            ["eam.settings.firstBeatBehavior"]   = "首拍行为",
            ["eam.settings.multiplayerAppearance"]= "多人外观",
            ["eam.settings.separate2PLevel"]     = "独立双人关卡",
            ["eam.editor.openRowEditor"]         = "正在打开轨道 {0} 属性编辑器",
            ["eam.sprite.editNotSupported"]      = "精灵编辑暂不支持",
            ["eam.event.jumpAndPlay"]            = "跳转到 {0} 并开始播放",
            ["eam.action.addRowOrSprite"]        = "请在 Rows 或 Sprites Tab 中添加轨道或精灵",
            ["eam.char.selectPrompt"]            = "选择角色，使用上下箭头导航，回车确认，Escape取消",
            ["eam.event.noTypesAvailable"]       = "当前 Tab 没有可用的事件类型",
            ["eam.event.selectPrompt"]           = "选择事件类型，使用上下箭头导航，回车确认，Escape取消",
            ["eam.event.createFailed"]           = "无法创建事件类型 {0}",
            ["eam.event.createError"]            = "创建事件失败",
            ["eam.event.createdAndOpening"]      = "已创建事件 {0}，正在打开属性编辑器",
            ["eam.track.noAvailable"]            = "无轨道",
            ["eam.sprite.noAvailable"]           = "无精灵",
            ["eam.track.info"]                   = "轨道 {0} {1} {2}事件",
            ["eam.sprite.info"]                  = "精灵 {0} {1} {2}事件",
            ["eam.event.noAvailable"]            = "无事件",
            ["eam.event.noSelection"]            = "未选中任何事件",
            ["eam.event.mixedMoveBlocked"]       = "无法移动：选中的事件类型不一致",
            ["eam.event.commentNote"]            = "（注释事件）",
            ["eam.event.levelEndNote"]           = "（结束关卡）",
            ["eam.event.customMethodNote"]       = "（需要配置自定义方法）",
            ["eam.event.tagNote"]                = "（标签操作）",
            ["eam.track.added"]                  = "已添加轨道，角色 {0}",
            ["eam.sprite.added"]                 = "已添加精灵，角色 {0}",
            ["eam.row.rowType"]                  = "轨道类型",
            ["eam.row.player"]                   = "玩家",
            ["eam.row.character"]                = "角色",
            ["eam.row.cpuMarker"]                = "CPU标记",
            ["eam.row.hideAtStart"]              = "开始时隐藏",
            ["eam.row.muteBeats"]                = "静音节拍",
            ["eam.row.muteInSinglePlayer"]       = "单人模式静音",
            ["eam.row.beatSound"]                = "节拍音效",
            ["eam.row.room"]                     = "房间",
            ["eam.room.option"]                  = "房间{0}",
            ["eam.confirm.changeRowType"]        = "切换轨道类型将删除轨道上的所有事件（{0}个），是否继续？",
            ["eam.error.roomFull"]               = "房间 {0} 已满，无法移动轨道",
            ["eam.error.helperNotFound"]         = "无法启动事件编辑器，请确保 RDEventEditorHelper.exe 存在",
            ["eam.cursor.jump.title"]            = "跳转到位置",
            ["eam.cursor.jump.bar"]              = "小节",
            ["eam.cursor.jump.beat"]             = "拍",
            ["eam.cursor.jump.success"]          = "已跳转到 {0}",
        };

        private static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            ["eam.barbeat.format"]              = "Bar {0} Beat {1}",
            ["eam.barbeat.beatOnly"]             = "Beat {0}",
            ["eam.barbeat.barOnly"]              = "Bar {0}",
            ["eam.cursor.suffix"]                = " Edit Cursor",
            ["eam.cursor.snapPrefix"]            = "Snapped to ",
            ["eam.action.cancelled"]             = "Cancelled",
            ["eam.check.checked"]                = "Checked",
            ["eam.check.unchecked"]              = "Unchecked",
            ["eam.input.activated"]              = "Input field activated",
            ["eam.editor.openPropEditor"]        = "Opening property editor",
            ["eam.editor.openTrackEditor"]       = "Opening track editor",
            ["eam.editor.openEventEditor"]       = "Opening property editor for {0}",
            ["eam.editor.openSettingsEditor"]    = "Opening level settings editor",
            ["eam.settings.song"]                = "Song Name",
            ["eam.settings.artist"]              = "Artist",
            ["eam.settings.author"]              = "Author",
            ["eam.settings.description"]         = "Description",
            ["eam.settings.tags"]                = "Tags",
            ["eam.settings.difficulty"]          = "Difficulty",
            ["eam.settings.seizureWarning"]      = "Seizure Warning",
            ["eam.settings.canBePlayedOn"]       = "Can Be Played On",
            ["eam.settings.specialArtistType"]   = "Special Artist Type",
            ["eam.settings.artistPermission"]    = "Artist Permission File",
            ["eam.settings.artistLinks"]         = "Artist Links",
            ["eam.settings.previewImage"]        = "Preview Image",
            ["eam.settings.syringeIcon"]         = "Syringe Icon",
            ["eam.settings.previewSong"]         = "Preview Song",
            ["eam.settings.previewSongStartTime"]= "Preview Start Time",
            ["eam.settings.previewSongDuration"] = "Preview Duration",
            ["eam.settings.songLabelHue"]        = "Label Hue",
            ["eam.settings.songLabelGrayscale"]  = "Label Grayscale",
            ["eam.settings.levelVolume"]         = "Level Volume",
            ["eam.settings.firstBeatBehavior"]   = "First Beat Behavior",
            ["eam.settings.multiplayerAppearance"]= "Multiplayer Appearance",
            ["eam.settings.separate2PLevel"]     = "Separate 2P Level",
            ["eam.editor.openRowEditor"]         = "Opening property editor for track {0}",
            ["eam.sprite.editNotSupported"]      = "Sprite editing not yet supported",
            ["eam.event.jumpAndPlay"]            = "Jump to {0} and play",
            ["eam.action.addRowOrSprite"]        = "Switch to Rows or Sprites tab to add a track or sprite",
            ["eam.char.selectPrompt"]            = "Select character, arrow keys to navigate, Enter to confirm, Escape to cancel",
            ["eam.event.noTypesAvailable"]       = "No event types available in current tab",
            ["eam.event.selectPrompt"]           = "Select event type, arrow keys to navigate, Enter to confirm, Escape to cancel",
            ["eam.event.createFailed"]           = "Cannot create event type {0}",
            ["eam.event.createError"]            = "Event creation failed",
            ["eam.event.createdAndOpening"]      = "Event {0} created, opening property editor",
            ["eam.track.noAvailable"]            = "No tracks available",
            ["eam.sprite.noAvailable"]           = "No sprites available",
            ["eam.track.info"]                   = "Track {0} {1} {2} events",
            ["eam.sprite.info"]                  = "Sprite {0} {1} {2} events",
            ["eam.event.noAvailable"]            = "No events available",
            ["eam.event.noSelection"]            = "No events selected",
            ["eam.event.mixedMoveBlocked"]       = "Cannot move: selected events have mixed positioning types",
            ["eam.event.commentNote"]            = "(Comment event)",
            ["eam.event.levelEndNote"]           = "(Level end)",
            ["eam.event.customMethodNote"]       = "(Requires custom method)",
            ["eam.event.tagNote"]                = "(Tag operation)",
            ["eam.track.added"]                  = "Track added, character: {0}",
            ["eam.sprite.added"]                 = "Sprite added, character: {0}",
            ["eam.row.rowType"]                  = "Row Type",
            ["eam.row.player"]                   = "Player",
            ["eam.row.character"]                = "Character",
            ["eam.row.cpuMarker"]                = "CPU Marker",
            ["eam.row.hideAtStart"]              = "Hide at Start",
            ["eam.row.muteBeats"]                = "Mute Beats",
            ["eam.row.muteInSinglePlayer"]       = "Mute in Single Player",
            ["eam.row.beatSound"]                = "Beat Sound",
            ["eam.row.room"]                     = "Room",
            ["eam.room.option"]                  = "Room {0}",
            ["eam.confirm.changeRowType"]        = "Changing row type will delete all {0} events on this track. Continue?",
            ["eam.error.roomFull"]               = "Room {0} is full, cannot move track",
            ["eam.error.helperNotFound"]         = "Cannot start event editor. Please ensure RDEventEditorHelper.exe exists",
            ["eam.cursor.jump.title"]            = "Jump to Position",
            ["eam.cursor.jump.bar"]              = "Bar",
            ["eam.cursor.jump.beat"]             = "Beat",
            ["eam.cursor.jump.success"]          = "Jumped to {0}",
        };

        [HarmonyPrefix]
        public static bool GetPrefix(string key, ref string __result)
        {
            // 性能：非 eam. key 仅多一次 4 字符 StartsWith 检查（< 10ns）
            if (key == null || !key.StartsWith("eam.")) return true;
            var dict = RDString.isChinese ? _zh : _en;
            __result = dict.TryGetValue(key, out string val) ? val : key;
            return false; // 拦截原方法
        }
    }

}