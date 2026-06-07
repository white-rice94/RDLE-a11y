using System;
using System.IO;
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
    [BepInPlugin("com.hzt.rd-editor-access", "RDEditorAccess", "1.8")]
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

        // SoundDataStruct.used 字段兼容性检测（稳定版无此字段）
        private static readonly bool _hasUsedField =
            typeof(SoundDataStruct).GetField("used") != null;
        private static readonly System.Reflection.ConstructorInfo _soundDataCtor6 =
            typeof(SoundDataStruct).GetConstructor(new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) });

        // ===================================================================================
        // 虚拟菜单状态
        // ===================================================================================
        private enum VirtualMenuState
        {
            None,
            CharacterSelect,      // 角色选择（添加轨道/精灵）
            EventTypeSelect,      // 事件类型选择
            LinkSelect,           // 链接选择
            EventChainSelect,     // 事件链选择
            ConditionalSelect,    // 条件选择
            GridSelect,            // 网格精度选择
            VirtualSelectionOptions  // 虚拟选择选项
        }

        private VirtualMenuState virtualMenuState = VirtualMenuState.None;
        private int virtualMenuIndex = 0;
        private string virtualMenuPurpose = "";  // "row", "sprite", "event"
        private LevelEventType selectedEventType;

        // 紧急回退：Helper 卡死时快速按 Esc 5 次强制取消
        private int _emergencyEscCount = 0;
        private float _emergencyEscLastTime = 0f;
        private const float EscWindowSeconds = 2f;   // 2 秒内按够 5 次

        // 虚拟菜单激活时为 true，用于屏蔽游戏原生快捷键
        internal static bool IsVirtualMenuActive = false;

        // 条件选择菜单相关字段
        private struct ConditionalEntry
        {
            public int     localId;   // 本地条件 ID（全局条件为 -1）
            public string? globalId;  // 全局条件 GID（本地条件为 null）
            public string  description; // 条件描述文本
        }
        private List<ConditionalEntry> _conditionalEntries = new List<ConditionalEntry>();
        private LevelEvent_Base? _conditionalTargetEvent = null;
        private int _pendingDeleteConditionalId = -1;  // 待确认删除的条件 ID，-1 表示无待删除

        // 链接相关字段
        private List<ModUtils.LinkInfo> currentElementLinks = new List<ModUtils.LinkInfo>();  // 当前元素的链接列表
        private GameObject? currentElementWithLinks = null;  // 当前包含链接的元素

        // 事件链相关字段
        private List<string> eventChainNames = new List<string>();  // 可用事件链名称列表
        internal string? _pendingChainData;                         // 暂存序列化数据等待命名
        private static readonly float[] ChainSpeedOptions = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f };
        private int _chainSpeedIndex = 3;                           // 默认 1x

        // 编辑光标：时间轴上的虚拟锚点，用于精确控制事件插入/粘贴位置
        internal BarAndBeat _editCursor = new BarAndBeat(1, 1f);

        // SoundData 偏移保护：延迟到下一帧恢复
        private LevelEvent_Base? _pendingSoundRestoreEvent;
        private Dictionary<string, object>? _pendingSoundRestoreSnapshot;

        // [调试用] SoundData 偏移监听
        private LevelEvent_Base? _debugWatchedEvent;
        private Dictionary<string, int> _debugLastOffsets = new Dictionary<string, int>();

        // ===================================================================================
        // 虚拟选区状态
        // ===================================================================================
        private HashSet<LevelEventControl_Base> virtualSelection = new HashSet<LevelEventControl_Base>();
        private int virtualSelectionBrowseIndex = -1;

        private enum BrowseDirection { Previous, Next, First, Last }

        // ===================================================================================
        // 属性快速调节状态
        // ===================================================================================
        private int currentPropertyIndex = -1;  // 当前选中的属性索引（-1 表示未选择）
        private List<BasePropertyInfo>? adjustableProperties = null;  // 当前事件的可调节属性列表
        private LevelEvent_Base? lastSelectedEvent = null;  // 上次选中的事件

        // 光标导航修复：记录期望的导航目标，下帧检查游戏是否成功切换
        private LevelEventControl_Base _lastFrameSelectedControl = null;

        // 标签模式状态监听：记录上一次 tagFieldMode 状态，用于检测变化并朗读
        private bool _lastTagFieldMode = false;

        public void Awake()
        {
            Instance = this;
            inputFieldReader = new InputFieldReader();
            AccessibilityBridge.Initialize(gameObject);
            AccessibilityBridge.SetConditionalSavedCallback(OnConditionalSaved);
            Debug.Log("无障碍核心逻辑已启动 (Logic Awake)");
        }

        private void OnConditionalSaved(int id)
        {
            if (virtualMenuState != VirtualMenuState.ConditionalSelect || _conditionalTargetEvent == null) return;
            BuildConditionalList(_conditionalTargetEvent);
            int idx = _conditionalEntries.FindIndex(e => e.localId == id);
            virtualMenuIndex = idx >= 0 ? idx : Mathf.Clamp(virtualMenuIndex, 0, _conditionalEntries.Count - 1);
            if (_conditionalEntries.Count > 0)
                AnnounceCurrentConditional();
        }

        public void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Update()
        {
            // --- SoundData 偏移保护：在下一帧开头恢复 ---
            if (_pendingSoundRestoreEvent != null && _pendingSoundRestoreSnapshot != null)
            {
                Debug.Log($"[SoundDataGuard] 下一帧开始，执行延迟恢复检查 (事件类型: {_pendingSoundRestoreEvent.type})");
                RestoreSoundDataValues(_pendingSoundRestoreEvent, _pendingSoundRestoreSnapshot);
                _pendingSoundRestoreEvent = null;
                _pendingSoundRestoreSnapshot = null;
            }

            // --- 心跳检测 ---
            debugTimer += Time.unscaledDeltaTime;
            if (debugTimer > 10f)
            {
                debugTimer = 0f;
            }

            // --- [调试用] SoundData 偏移监听 ---
            DebugWatchSoundDataOffsets();

            // --- FileIPC 轮询 ---
            AccessibilityBridge.Update();

            // Helper 运行时不处理任何快捷键，但检测紧急 Esc×5 回退
            if (AccessibilityBridge.IsEditing)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (Time.realtimeSinceStartup - _emergencyEscLastTime > EscWindowSeconds)
                        _emergencyEscCount = 0;
                    _emergencyEscLastTime = Time.realtimeSinceStartup;
                    _emergencyEscCount++;
                    if (_emergencyEscCount >= 5)
                    {
                        _emergencyEscCount = 0;
                        AccessibilityBridge.ForceCancel();
                        Narration.Say(RDString.Get("eam.helper.forceCancelled"), NarrationCategory.Notification);
                    }
                }
                return;
            }

            if (scnEditor.instance == null) return;

            // --- 标签模式状态监听 ---
            bool currentTagFieldMode = scnEditor.instance.tagFieldMode;
            if (currentTagFieldMode != _lastTagFieldMode)
            {
                _lastTagFieldMode = currentTagFieldMode;
                string modeKey = currentTagFieldMode ? "eam.tagMode.enabled" : "eam.tagMode.disabled";
                Narration.Say(RDString.Get(modeKey), NarrationCategory.Notification);
            }

            // --- 虚拟菜单优先处理（最高优先级）---
            if (virtualMenuState != VirtualMenuState.None)
            {
                HandleVirtualMenu();
                return;
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                Debug.Log($"编辑状态： {scnEditor.instance.userIsEditingAnInputField}， 当前菜单： {currentMenu}");
            }

            if (Input.GetKeyDown(KeyCode.F10))
            {
                // 只在没有其他菜单打开时才触发主菜单
                bool hasOtherMenuOpen =
                    (scnEditor.instance.copyrightPopup != null && scnEditor.instance.copyrightPopup.activeInHierarchy) ||
                    (scnEditor.instance.insertUrlContainer != null && scnEditor.instance.insertUrlContainer.activeInHierarchy) ||
                    (scnEditor.instance.dialog != null && scnEditor.instance.dialog.gameObject.activeInHierarchy) ||
                    (scnEditor.instance.colorPickerPopup != null && scnEditor.instance.colorPickerPopup.gameObject.activeInHierarchy) ||
                    (scnEditor.instance.characterPickerPopup != null && scnEditor.instance.characterPickerPopup.gameObject.activeInHierarchy) ||
                    (scnEditor.instance.publishPopup != null && scnEditor.instance.publishPopup.gameObject.activeInHierarchy) ||
                    (scnEditor.instance.settingsMenu != null && scnEditor.instance.settingsMenu.gameObject.activeInHierarchy);

                if (!hasOtherMenuOpen)
                {
                    scnEditor.instance.MenuButtonClick();
                }
            }

            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                scnEditor.instance.TwoPlayerButtonClick();
                string modeKey = GC.twoPlayerMode ? "eam.playerMode.two" : "eam.playerMode.one";
                Narration.Say(RDString.Get(modeKey), NarrationCategory.Notification);
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

            // 6. 设置菜单（已有原生无障碍支持，不拦截导航，但需要阻止 Update 继续执行）
            if (scnEditor.instance.settingsMenu != null && scnEditor.instance.settingsMenu.gameObject.activeInHierarchy)
            {
                // 设置菜单打开时，直接返回，不执行后续的主菜单和时间轴导航
                return;
            }

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

        /// <summary>
        /// 检查 UI 元素是否有有意义的文本内容
        /// </summary>
        private static bool HasMeaningfulText(Graphic graphic)
        {
            // 检查子元素中的 TextMeshPro 文本组件
            var tmpText = graphic.GetComponentInChildren<TMP_Text>();
            if (tmpText != null && !string.IsNullOrWhiteSpace(tmpText.text))
                return true;

            // 检查子元素中的 Unity Text 组件
            var text = graphic.GetComponentInChildren<Text>();
            if (text != null && !string.IsNullOrWhiteSpace(text.text))
                return true;

            // 如果自己就是文本组件
            if (graphic is Text selfText && !string.IsNullOrWhiteSpace(selfText.text))
                return true;

            if (graphic is TMP_Text selfTmpText && !string.IsNullOrWhiteSpace(selfTmpText.text))
                return true;

            return false;
        }

        // ===================================================================================
        // 核心功能区域：通用 UI 导航逻辑 (已优化)
        // ===================================================================================

        private void HandleGeneralUINavigation(GameObject rootObject, string menuName)
        {
            if (rootObject == null) return;

            // 如果虚拟菜单激活，不处理 UI 导航
            if (virtualMenuState != VirtualMenuState.None) return;

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
            bool isSpaceKey = Input.GetKeyDown(KeyCode.Space);

            // Space 键：如果当前元素有链接，打开链接选择菜单
            if (isSpaceKey && !scnEditor.instance.userIsEditingAnInputField)
            {
                if (currentElementLinks != null && currentElementLinks.Count > 0)
                {
                    StartLinkSelect();
                    return;
                }
            }

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
                        var selectable = g.GetComponent<Selectable>();

                        // A. 如果是可交互的 (Selectable)
                        if (selectable != null)
                        {
                            // 过滤掉 Scrollbar 和 Slider (通常没有文本，不适合键盘导航)
                            if (selectable is Scrollbar || selectable is Slider)
                            {
                                // 例外：如果它确实有文本标签，保留它（罕见但可能）
                                if (HasMeaningfulText(g))
                                    return true;

                                // 否则过滤掉
                                return false;
                            }

                            // 其他 Selectable (Button, Toggle, InputField, Dropdown) 保留
                            return true;
                        }

                        // B. 如果是纯文本 (Text/TMP)，保留 (用于朗读标签)，但排除 Selectable 的子文本（避免重复）
                        if ((g is Text || g is TMPro.TMP_Text) && g.GetComponentInParent<Selectable>() == null)
                            return true;

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

                if (string.IsNullOrEmpty(textToSay))
                {
                    // 如果是 Scrollbar/Slider 且没有文本，跳过朗读
                    if (selectableComponent is Scrollbar || selectableComponent is Slider)
                    {
                        Debug.LogWarning($"[SelectUIElement] 跳过无文本的 {selectableComponent.GetType().Name}: {element.name}");
                        return; // 不朗读，直接返回
                    }

                    textToSay = element.name; // 其他情况使用 GameObject 名称作为后备
                }

                // 处理富文本标签
                List<ModUtils.LinkInfo> links;
                textToSay = ModUtils.ProcessRichText(textToSay, out links);

                // 存储链接信息
                if (links.Count > 0)
                {
                    currentElementLinks = links;
                    currentElementWithLinks = element.gameObject;
                }
                else
                {
                    currentElementLinks.Clear();
                    currentElementWithLinks = null;
                }

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
            if (editor == null)
            {
                if (virtualSelection.Count > 0)
                {
                    virtualSelection.Clear();
                    virtualSelectionBrowseIndex = -1;
                }
                return;
            }

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
                bool isLeftArrow = Input.GetKeyDown(KeyCode.LeftArrow);

                // 如果已经选中了事件
                if (editor.selectedControl != null)
                {
                    // 比较上一帧末尾的 selectedControl 与当前帧的，判断游戏原生导航是否已经生效
                    bool nativeNavWorked = (_lastFrameSelectedControl != null
                        && editor.selectedControl != _lastFrameSelectedControl);

                    if (!nativeNavWorked)
                    {
                        // 游戏没有处理导航，由 mod 主动切换
                        var nextControl = isLeftArrow
                            ? editor.GetControlToTheLeft(editor.selectedControl)
                            : editor.GetControlToTheRight(editor.selectedControl);

                        if (nextControl == null)
                        {
                            // 已经在边界，重新朗读当前事件信息
                            ModUtils.AnnounceEventSelection(editor.selectedControl.levelEvent);
                            return; // 不需要继续处理
                        }

                        editor.SelectEventControl(nextControl, sound: true);
                        editor.timeline.UnfollowPlayhead();
                        editor.timeline.CenterOnPosition(
                            (nextControl.rt.anchoredPosition.x + nextControl.rightPosition) / 2f, 0.3f);
                        editor.timeline.CenterOnVertPosition(
                            (0f - (nextControl.rt.anchoredPosition.y + nextControl.bottomPosition)) / 2f, 0.3f);
                    }
                    // 原生导航已生效，无需额外操作（Harmony postfix 会处理朗读）
                }
                // 如果没有选中事件，或者选中的事件不属于当前 Tab，则重新选择
                else if (editor.selectedControls.Count <= 0 || !IsSelectedEventInCurrentTab(editor))
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

            // 大键盘 0：打开关卡元数据编辑器（仅当没有按 Shift）
            if (Input.GetKeyDown(KeyCode.Alpha0) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
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
                    var levelEvent = editor.selectedControl.levelEvent;
                    int eventBar = levelEvent.bar;

                    // 同时设置编辑光标到事件位置
                    _editCursor = levelEvent.barAndBeat;

                    editor.ScrubToBar(eventBar, playAfterScrubbing: true);
                    Narration.Say(string.Format(RDString.Get("eam.event.jumpAndPlay"), $"{RDString.Get("editor.bar")} {eventBar}"), NarrationCategory.Notification);
                }
            }

            // ===================================================================================
            // 属性快速调节快捷键
            // ===================================================================================

            // E 键：循环选择属性（Shift+E 反向）
            if (Input.GetKeyDown(KeyCode.E))
            {
                CycleProperty(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                return;
            }

            // R 键：减少属性值
            if (Input.GetKeyDown(KeyCode.R))
            {
                AdjustProperty(-1, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                                   Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
                return;
            }

            // T 键：增加属性值
            if (Input.GetKeyDown(KeyCode.T))
            {
                AdjustProperty(1, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                                  Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
                return;
            }

            // Alt+G: 打开网格精度选择菜单
            if (Input.GetKeyDown(KeyCode.G) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) &&
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                StartGridSelect();
                return;
            }

            // Insert 或 F2: 添加事件
            if ((Input.GetKeyDown(KeyCode.Insert) || Input.GetKeyDown(KeyCode.F2)) &&
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
            {
                StartEventTypeSelect();
            }

            // Ctrl+Insert 或 Ctrl+F2: 添加轨道/精灵（取决于当前 Tab）
            if ((Input.GetKeyDown(KeyCode.Insert) || Input.GetKeyDown(KeyCode.F2)) &&
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
                float snapBeat = tl.cellWidth * (1f / editor.denominator);
                float snappedX = Mathf.Max(0f, Mathf.Round(cursorX / snapBeat) * snapBeat);
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

            // Ctrl+分号：保存虚拟选区为事件链
            if (Input.GetKeyDown(KeyCode.Semicolon) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                StartSaveEventChain();
                return;
            }

            // 分号（无修饰键）：加载事件链
            if (Input.GetKeyDown(KeyCode.Semicolon) &&
                !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl) &&
                !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                StartLoadEventChain();
                return;
            }

            // 逗号：编辑光标后退（Alt+Shift: 1小节，Alt: 0.01拍，Shift: 0.1拍，无修饰: 1拍）
            if (Input.GetKeyDown(KeyCode.Comma))
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (alt && shift)
                {
                    MoveEditCursorByBar(-1);
                }
                else
                {
                    MoveEditCursor(alt ? -0.01f : shift ? -0.1f : -1f / scnEditor.instance.denominator);
                }
            }

            // 句号：编辑光标前进（Alt+Shift: 1小节，Alt: 0.01拍，Shift: 0.1拍，无修饰: 1拍）
            if (Input.GetKeyDown(KeyCode.Period))
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (alt && shift)
                {
                    MoveEditCursorByBar(1);
                }
                else
                {
                    MoveEditCursor(alt ? 0.01f : shift ? 0.1f : 1f / scnEditor.instance.denominator);
                }
            }

            // ===================================================================================
            // 快速移动事件（Z/C/X 键）
            // ===================================================================================

            // 检查是否按下了 Ctrl 键（避免与 Ctrl+X/Ctrl+C 冲突）
            bool ctrlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // Z键：选中事件后退（Alt+Shift: 1小节，Beat模式: Alt 0.01拍/Shift 0.1拍/无修饰 1拍；BarOnly模式: 1小节）
            if (Input.GetKeyDown(KeyCode.Z) && !ctrlPressed)
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (alt && shift)
                {
                    // Alt+Shift: 强制按小节移动
                    MoveSelectedEventsByBar(-1);
                }
                else
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
                        MoveSelectedEvents(alt ? -0.01f : shift ? -0.1f : -1f / scnEditor.instance.denominator);
                    }
                }
            }

            // X键：选中事件前进（Alt+Shift: 1小节，Beat模式: Alt 0.01拍/Shift 0.1拍/无修饰 1拍；BarOnly模式: 1小节）
            if (Input.GetKeyDown(KeyCode.X) && !ctrlPressed)
            {
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (alt && shift)
                {
                    // Alt+Shift: 强制按小节移动
                    MoveSelectedEventsByBar(1);
                }
                else
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
                        MoveSelectedEvents(alt ? 0.01f : shift ? 0.1f : 1f / scnEditor.instance.denominator);
                    }
                }
            }

            // C键：选中事件吸附到最近的正拍或半拍（Alt+C 保留给条件菜单）
            if (Input.GetKeyDown(KeyCode.C) && !ctrlPressed &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt))
            {
                var moveMode = GetSelectedEventsMoveMode();
                if (moveMode == EventMoveMode.Mixed)
                    Narration.Say(RDString.Get("eam.event.mixedMoveBlocked"), NarrationCategory.Navigation);
                else if (moveMode != EventMoveMode.BarOnly)
                    SnapSelectedEventsToGrid();
            }

            // Alt+C：打开当前选中事件的条件选择菜单
            if (Input.GetKeyDown(KeyCode.C) && !ctrlPressed &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            {
                var evt = editor.selectedControl?.levelEvent;
                if (evt == null)
                {
                    Narration.Say(RDString.Get("eam.event.noSelection"), NarrationCategory.Navigation);
                }
                else
                {
                    BuildConditionalList(evt);
                    virtualMenuIndex = 0;
                    virtualMenuState = VirtualMenuState.ConditionalSelect;
                    SetFakeInputField();
                    Narration.Say(string.Format(RDString.Get("eam.conditional.menuHeader"),
                        ModUtils.eventNameI18n(evt)), NarrationCategory.Navigation);
                    Narration.Say(RDString.Get("editor.Conditionals"), NarrationCategory.Instruction);
                }
            }
            // 虚拟选区快捷键
            // ===================================================================================

            // Ctrl+Alt+Space：打开虚拟选区选项菜单
            bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (Input.GetKeyDown(KeyCode.Space) && ctrlPressed && altPressed)
            {
                StartVirtualSelectionOptions();
                return;
            }

            bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Ctrl+Shift+Space：清空虚拟选区
            if (Input.GetKeyDown(KeyCode.Space) && shiftPressed && ctrlPressed)
            {
                virtualSelection.Clear();
                virtualSelectionBrowseIndex = -1;
                Narration.Say(RDString.Get("eam.vsel.cleared"), NarrationCategory.Navigation);
            }
            // Shift+Space（不含 Ctrl）：切换当前选中事件的虚拟选区状态
            else if (Input.GetKeyDown(KeyCode.Space) && shiftPressed && !ctrlPressed)
            {
                if (editor.selectedControl != null && editor.selectedControl.levelEvent != null)
                {
                    var control = editor.selectedControl;
                    if (virtualSelection.Contains(control))
                    {
                        virtualSelection.Remove(control);
                        Narration.Say(string.Format(RDString.Get("eam.vsel.removed"),
                            ModUtils.eventNameI18n(control.levelEvent)), NarrationCategory.Navigation);
                    }
                    else
                    {
                        virtualSelection.Add(control);
                        Narration.Say(string.Format(RDString.Get("eam.vsel.added"),
                            ModUtils.eventNameI18n(control.levelEvent)), NarrationCategory.Navigation);
                    }
                    virtualSelectionBrowseIndex = -1;
                }
            }
            // 减号：浏览虚拟选区（上一个 / Shift：第一个）
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                BrowseVirtualSelection(shiftPressed ? BrowseDirection.First : BrowseDirection.Previous);
            }

            // 等号：浏览虚拟选区（下一个 / Shift：最后一个）
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                BrowseVirtualSelection(shiftPressed ? BrowseDirection.Last : BrowseDirection.Next);
            }

            // 记录本帧末尾的选中状态，供下帧即时检测使用
            _lastFrameSelectedControl = editor.selectedControl;
        }

        // ===================================================================================
        // 属性快速调节方法
        // ===================================================================================

        /// <summary>
        /// 重置属性选择状态
        /// </summary>
        public void ResetPropertySelection()
        {
            currentPropertyIndex = -1;
            adjustableProperties = null;
            lastSelectedEvent = null;
        }

        /// <summary>
        /// 获取事件的可调节属性列表
        /// </summary>
        private List<BasePropertyInfo> GetAdjustableProperties(LevelEvent_Base evt)
        {
            if (evt?.info == null) return new List<BasePropertyInfo>();

            // 过滤可调节的属性
            // 注意：evt.info.propertiesInfo 已经自动排除了基础属性（使用 BindingFlags.DeclaredOnly）
            var result = new List<BasePropertyInfo>();
            foreach (var prop in evt.info.propertiesInfo)
            {
                // 跳过 UI-only 属性
                if (prop.onlyUI) continue;

                // 检查动态可见性
                if (prop.enableIf != null && !prop.enableIf(evt)) continue;

                // 只支持特定类型
                if (prop is IntPropertyInfo || prop is FloatPropertyInfo ||
                    prop is EnumPropertyInfo || prop is BoolPropertyInfo ||
                    prop is NullablePropertyInfo nullable &&
                    (nullable.underlyingPropertyInfo is IntPropertyInfo ||
                     nullable.underlyingPropertyInfo is FloatPropertyInfo ||
                     nullable.underlyingPropertyInfo is EnumPropertyInfo ||
                     nullable.underlyingPropertyInfo is BoolPropertyInfo))
                {
                    result.Add(prop);
                }
            }
            return result;
        }

        /// <summary>
        /// 循环选择属性
        /// </summary>
        private void CycleProperty(bool reverse)
        {
            var editor = scnEditor.instance;
            if (editor?.selectedControl?.levelEvent == null)
            {
                Narration.Say(RDString.Get("eam.event.noSelection"), NarrationCategory.Navigation);
                return;
            }

            var evt = editor.selectedControl.levelEvent;

            // 检测事件是否变化
            if (evt != lastSelectedEvent)
            {
                lastSelectedEvent = evt;
                adjustableProperties = GetAdjustableProperties(evt);
                currentPropertyIndex = -1;
            }

            if (adjustableProperties == null || adjustableProperties.Count == 0)
            {
                Narration.Say(RDString.Get("eam.quickAdjust.notAdjustable"), NarrationCategory.Navigation);
                return;
            }

            // 循环索引
            if (reverse)
            {
                currentPropertyIndex--;
                if (currentPropertyIndex < 0) currentPropertyIndex = adjustableProperties.Count - 1;
            }
            else
            {
                currentPropertyIndex++;
                if (currentPropertyIndex >= adjustableProperties.Count) currentPropertyIndex = 0;
            }

            // 朗读属性名和当前值
            var prop = adjustableProperties[currentPropertyIndex];
            string propName = GetLocalizedPropertyName(prop, evt);
            string valueStr = FormatPropertyValue(prop, evt);
            Narration.Say($"{propName}: {valueStr}", NarrationCategory.Navigation);
        }

        /// <summary>
        /// 调节属性值
        /// </summary>
        private void AdjustProperty(int direction, bool shift, bool alt)
        {
            var editor = scnEditor.instance;
            if (editor?.selectedControl?.levelEvent == null)
            {
                Narration.Say(RDString.Get("eam.event.noSelection"), NarrationCategory.Navigation);
                return;
            }

            if (currentPropertyIndex < 0 || adjustableProperties == null ||
                currentPropertyIndex >= adjustableProperties.Count)
            {
                Narration.Say(RDString.Get("eam.quickAdjust.noProperty"), NarrationCategory.Navigation);
                return;
            }

            var evt = editor.selectedControl.levelEvent;
            var prop = adjustableProperties[currentPropertyIndex];

            using (new SaveStateScope())
            {
                bool success = false;
                object? newValue = null;

                // 根据属性类型调节
                if (prop is IntPropertyInfo intProp)
                {
                    int current = (int)prop.propertyInfo.GetValue(evt);
                    newValue = Mathf.Clamp(current + direction, intProp.min, intProp.max);
                    success = true;
                }
                else if (prop is FloatPropertyInfo floatProp)
                {
                    float step = (alt && shift) ? 0.0001f : (alt ? 0.001f : (shift ? 0.01f : 0.1f));
                    float current = (float)prop.propertyInfo.GetValue(evt);
                    newValue = (float)Math.Round(Mathf.Clamp(current + direction * step, floatProp.min, floatProp.max), 4);
                    success = true;
                }
                else if (prop is EnumPropertyInfo enumProp)
                {
                    object current = prop.propertyInfo.GetValue(evt);
                    Array values = Enum.GetValues(enumProp.enumType);
                    int currentIndex = Array.IndexOf(values, current);
                    int nextIndex = (currentIndex + direction + values.Length) % values.Length;
                    newValue = values.GetValue(nextIndex);
                    success = true;
                }
                else if (prop is BoolPropertyInfo)
                {
                    bool current = (bool)prop.propertyInfo.GetValue(evt);
                    newValue = !current;
                    success = true;
                }
                else if (prop is NullablePropertyInfo nullableProp)
                {
                    // 处理可空类型
                    object? current = prop.propertyInfo.GetValue(evt);
                    if (current == null)
                    {
                        // 设置为默认值
                        if (nullableProp.underlyingPropertyInfo is IntPropertyInfo intP)
                            newValue = intP.min;
                        else if (nullableProp.underlyingPropertyInfo is FloatPropertyInfo floatP)
                            newValue = floatP.min;
                        success = true;
                    }
                    else
                    {
                        // 按底层类型调节（简化：直接设为 null）
                        newValue = null;
                        success = true;
                    }
                }

                if (success)
                {
                    prop.propertyInfo.SetValue(evt, newValue);
                }
            }

            // 更新 UI
            editor.selectedControl.UpdateUI();
            editor.inspectorPanelManager.GetCurrent()?.UpdateUI(evt);

            // 重新获取可调节属性列表（因为属性值变化可能影响其他属性的 enableIf 条件）
            var oldProp = prop;
            adjustableProperties = GetAdjustableProperties(evt);

            // 尝试保持当前属性的选中状态
            currentPropertyIndex = adjustableProperties.IndexOf(oldProp);
            if (currentPropertyIndex < 0 && adjustableProperties.Count > 0)
            {
                // 如果当前属性不再可用，选择第一个属性
                currentPropertyIndex = 0;
            }

            // 朗读新值
            string valueStr = FormatPropertyValue(oldProp, evt);
            Narration.Say(valueStr, NarrationCategory.Navigation);
        }

        /// <summary>
        /// 获取属性的本地化名称
        /// </summary>
        private string GetLocalizedPropertyName(BasePropertyInfo prop, LevelEvent_Base evt)
        {
            string propertyName = prop.name;

            // 如果有自定义本地化键，直接使用
            if (!string.IsNullOrEmpty(prop.customLocalizationKey))
            {
                return RDString.Get(prop.customLocalizationKey);
            }

            // 尝试特定于事件类型的键: editor.{eventType}.{propertyName}
            string specificKey = $"editor.{evt.type}.{propertyName}";
            string localized = RDString.GetWithCheck(specificKey, out bool exists);
            if (exists)
                return localized;

            // 尝试通用键: editor.{propertyName}
            string genericKey = $"editor.{propertyName}";
            localized = RDString.GetWithCheck(genericKey, out exists);
            if (exists)
                return localized;

            // 如果都没有找到，返回原始属性名
            return propertyName;
        }

        /// <summary>
        /// 格式化属性值
        /// </summary>
        private string FormatPropertyValue(BasePropertyInfo prop, LevelEvent_Base evt)
        {
            object? value = prop.propertyInfo.GetValue(evt);

            if (value == null)
                return "null";

            if (prop is BoolPropertyInfo)
            {
                bool boolValue = (bool)value;
                return RDString.Get(boolValue ? "eam.bool.enabled" : "eam.bool.disabled");
            }

            if (prop is EnumPropertyInfo enumProp)
            {
                // 尝试获取枚举值的本地化
                string enumKey = $"enum.{enumProp.enumType.Name}.{value}";
                string localized = RDString.GetWithCheck(enumKey, out bool exists);
                return exists ? localized : value.ToString() ?? "";
            }

            if (prop is FloatPropertyInfo)
                return ((float)value).ToString();

            return value.ToString() ?? "";
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
                case VirtualMenuState.LinkSelect:
                    HandleLinkSelectMenu();
                    break;
                case VirtualMenuState.EventChainSelect:
                    HandleEventChainSelectMenu();
                    break;
                case VirtualMenuState.ConditionalSelect:
                    HandleConditionalSelect();
                    break;
                case VirtualMenuState.GridSelect:
                    HandleGridSelectMenu();
                    break;
                case VirtualMenuState.VirtualSelectionOptions:
                    HandleVirtualSelectionOptionsMenu();
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
            SetFakeInputField();
            
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
            // Shift+Enter: 添加轨道后自动打开 Helper 编辑（仅对 row 有效）
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                bool openHelper = virtualMenuPurpose == "row" &&
                                  (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                Character selectedChar = characters[virtualMenuIndex];

                if (virtualMenuPurpose == "row")
                {
                    AddNewRow(selectedChar, openHelper);
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
            SetFakeInputField();

            Narration.Say(GetEventTypeNameWithShortcut(editor.currentTab, 0, eventTypes[0]), NarrationCategory.Navigation);
            Narration.Say(RDString.Get("eam.event.selectPrompt"), NarrationCategory.Instruction);
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
                Narration.Say(GetEventTypeNameWithShortcut(editor.currentTab, virtualMenuIndex, eventTypes[virtualMenuIndex]), NarrationCategory.Navigation);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                virtualMenuIndex = (virtualMenuIndex + 1) % eventTypes.Count;
                Narration.Say(GetEventTypeNameWithShortcut(editor.currentTab, virtualMenuIndex, eventTypes[virtualMenuIndex]), NarrationCategory.Navigation);
            }

            // 字母快捷键跳转（仅移动光标，不创建事件）
            if (RDEditorConstants.eventKeyCodes.TryGetValue(editor.currentTab, out var keyCodes))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                for (int i = 0; i < eventTypes.Count && i < keyCodes.Count; i++)
                {
                    var kc = keyCodes[i];
                    if (kc.key != KeyCode.None && kc.shift == shift && Input.GetKeyDown(kc.key))
                    {
                        virtualMenuIndex = i;
                        Narration.Say(GetEventTypeNameWithShortcut(editor.currentTab, virtualMenuIndex, eventTypes[virtualMenuIndex]), NarrationCategory.Navigation);
                        break;
                    }
                }
            }

            // 回车确认 - 直接创建事件（使用默认值）
            // Ctrl+Enter: 创建后自动打开 Helper 编辑
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                bool openHelper = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                selectedEventType = eventTypes[virtualMenuIndex];
                CloseVirtualMenu();

                // 使用编辑光标位置创建事件
                var barAndBeat = _editCursor;
                int bar = barAndBeat.bar;
                float beat = barAndBeat.beat;
                int row = editor.selectedRowIndex >= 0 ? editor.selectedRowIndex : 0;

                CreateEventAndEdit(selectedEventType, bar, beat, row, openHelper);
            }
            
            // Escape 取消
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                CloseVirtualMenu();
            }
        }

        /// <summary>
        /// 开始链接选择菜单
        /// </summary>
        private void StartLinkSelect()
        {
            if (currentElementLinks == null || currentElementLinks.Count == 0) return;

            virtualMenuState = VirtualMenuState.LinkSelect;
            virtualMenuIndex = 0;
            SetFakeInputField();

            string title = RDString.Get("eam.link.menu.title");
            string count = string.Format(RDString.Get("eam.link.menu.count"), currentElementLinks.Count);
            string firstLink = GetLinkDescription(currentElementLinks[0]);
            Narration.Say($"{title}，{count}。{firstLink}", NarrationCategory.Navigation);
        }

        /// <summary>
        /// 获取链接描述（只包含链接名称和"链接"后缀）
        /// </summary>
        private string GetLinkDescription(ModUtils.LinkInfo link)
        {
            // 只朗读链接名称和"链接"后缀
            string linkSuffix = RDString.Get("eam.link.suffix");
            return $"{link.text}{linkSuffix}";
        }

        /// <summary>
        /// 处理链接选择菜单
        /// </summary>
        private void HandleLinkSelectMenu()
        {
            if (currentElementLinks == null || currentElementLinks.Count == 0)
            {
                CloseVirtualMenu();
                return;
            }

            int linkCount = currentElementLinks.Count;

            // 上箭头：上一个链接
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                virtualMenuIndex = (virtualMenuIndex - 1 + linkCount) % linkCount;
                string linkDesc = GetLinkDescription(currentElementLinks[virtualMenuIndex]);
                Narration.Say(linkDesc, NarrationCategory.Navigation);
            }
            // 下箭头：下一个链接
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                virtualMenuIndex = (virtualMenuIndex + 1) % linkCount;
                string linkDesc = GetLinkDescription(currentElementLinks[virtualMenuIndex]);
                Narration.Say(linkDesc, NarrationCategory.Navigation);
            }
            // Enter：打开选中的链接
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                var selectedLink = currentElementLinks[virtualMenuIndex];
                Application.OpenURL(selectedLink.url);
                string message = string.Format(RDString.Get("eam.link.opening"), selectedLink.text);
                Narration.Say(message, NarrationCategory.Notification);
                CloseVirtualMenu();
            }
            // Escape：取消
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                Narration.Say(RDString.Get("eam.link.cancelled"), NarrationCategory.Notification);
                CloseVirtualMenu();
            }
        }

        // ===================================================================================
        // 条件选择菜单
        // ===================================================================================

        /// <summary>
        /// 构建条件列表（全局条件优先，再加本地条件）
        /// </summary>
        private void BuildConditionalList(LevelEvent_Base levelEvent)
        {
            _conditionalEntries = new List<ConditionalEntry>();
            _conditionalTargetEvent = levelEvent;
            _pendingDeleteConditionalId = -1;

            foreach (var c in Conditionals.GetGlobalConditionals())
            {
                _conditionalEntries.Add(new ConditionalEntry
                {
                    localId = -1,
                    globalId = c.gid,
                    description = c.description
                });
            }

            var editor = scnEditor.instance;
            if (editor?.conditionals != null)
            {
                foreach (var c in editor.conditionals)
                {
                    _conditionalEntries.Add(new ConditionalEntry
                    {
                        localId = c.id,
                        globalId = null,
                        description = string.IsNullOrEmpty(c.description) ? c.tag : c.description
                    });
                }
            }
        }

        /// <summary>
        /// 获取条件在目标事件上的附加状态（null=未附加，false=激活，true=取反）
        /// </summary>
        private bool? GetConditionalState(ConditionalEntry entry)
        {
            if (_conditionalTargetEvent == null) return null;
            if (entry.globalId != null)
                return _conditionalTargetEvent.HasGlobalConditional(entry.globalId);
            return _conditionalTargetEvent.HasConditional(entry.localId);
        }

        /// <summary>
        /// 循环切换状态：无→激活→取反→无
        /// </summary>
        private static bool? CycleConditionalState(bool? current)
        {
            if (!current.HasValue) return false; // 无 → 激活
            if (!current.Value) return true;      // 激活 → 取反
            return null;                           // 取反 → 无
        }

        /// <summary>
        /// 宣读当前条目的状态和名称
        /// </summary>
        private void AnnounceCurrentConditional()
        {
            var entry = _conditionalEntries[virtualMenuIndex];
            bool? state = GetConditionalState(entry);
            string stateKey = state == null ? "eam.conditional.stateNone"
                            : state.Value   ? "eam.conditional.stateNegated"
                                            : "eam.conditional.stateActive";
            Narration.Say(RDString.Get(stateKey) + "，" + entry.description, NarrationCategory.Navigation);
        }

        /// <summary>
        /// 条件选择菜单的导航处理
        /// </summary>
        private void HandleConditionalSelect()
        {
            if (_conditionalTargetEvent == null)
            {
                Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                CloseVirtualMenu();
                return;
            }

            // 列表为空时：只允许 N 新建或 Escape 退出
            if (_conditionalEntries.Count == 0)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                    CloseVirtualMenu();
                }
                else if (Input.GetKeyDown(KeyCode.N))
                {
                    Narration.Say(RDString.Get("eam.conditional.openCreate"), NarrationCategory.Navigation);
                    AccessibilityBridge.CreateCondition(_conditionalTargetEvent);
                }
                return;
            }

            int count = _conditionalEntries.Count;

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _pendingDeleteConditionalId = -1;
                virtualMenuIndex = (virtualMenuIndex - 1 + count) % count;
                AnnounceCurrentConditional();
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _pendingDeleteConditionalId = -1;
                virtualMenuIndex = (virtualMenuIndex + 1) % count;
                AnnounceCurrentConditional();
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                _pendingDeleteConditionalId = -1;
                var entry = _conditionalEntries[virtualMenuIndex];
                bool? newState = CycleConditionalState(GetConditionalState(entry));

                using (new SaveStateScope())
                {
                    if (entry.globalId != null)
                        _conditionalTargetEvent.SetConditional(0, entry.globalId, newState);
                    else
                        _conditionalTargetEvent.SetConditional(entry.localId, null, newState);
                }

                string stateKey = newState == null ? "eam.conditional.removed"
                                : newState.Value   ? "eam.conditional.negated"
                                                   : "eam.conditional.activated";
                Narration.Say(string.Format(RDString.Get(stateKey), entry.description), NarrationCategory.Navigation);
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_pendingDeleteConditionalId != -1)
                {
                    // 取消待删除确认，重新朗读当前条件
                    _pendingDeleteConditionalId = -1;
                    Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                    AnnounceCurrentConditional();
                }
                else
                {
                    Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                    CloseVirtualMenu();
                }
            }
            else if (Input.GetKeyDown(KeyCode.N))
            {
                _pendingDeleteConditionalId = -1;
                Narration.Say(RDString.Get("eam.conditional.openCreate"), NarrationCategory.Navigation);
                AccessibilityBridge.CreateCondition(_conditionalTargetEvent);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                _pendingDeleteConditionalId = -1;
                var entry = _conditionalEntries[virtualMenuIndex];
                if (entry.localId == -1)
                {
                    Narration.Say(RDString.Get("eam.conditional.cannotEditGlobal"), NarrationCategory.Navigation);
                }
                else
                {
                    Narration.Say(RDString.Get("eam.conditional.openEdit"), NarrationCategory.Navigation);
                    AccessibilityBridge.EditCondition(entry.localId);
                }
            }
            else if (Input.GetKeyDown(KeyCode.D))
            {
                var entry = _conditionalEntries[virtualMenuIndex];
                if (entry.localId == -1)
                {
                    _pendingDeleteConditionalId = -1;
                    Narration.Say(RDString.Get("eam.conditional.cannotDeleteGlobal"), NarrationCategory.Navigation);
                }
                else if (_pendingDeleteConditionalId == entry.localId)
                {
                    // 第二次按 D：执行删除（复现 Conditionals.Delete 的核心逻辑）
                    _pendingDeleteConditionalId = -1;
                    var editor = scnEditor.instance;
                    if (editor != null)
                    {
                        int delId = entry.localId;
                        var affected = new List<LevelEventControl_Base>();
                        using (new SaveStateScope())
                        {
                            editor.conditionals.RemoveAll(c => c.id == delId);
                            if (editor.eventControls != null)
                            {
                                foreach (var ctrl in editor.eventControls)
                                {
                                    if (ctrl?.levelEvent?.HasConditional(delId).HasValue == true)
                                    {
                                        ctrl.levelEvent.SetConditional(delId, null, null);
                                        affected.Add(ctrl);
                                    }
                                }
                            }
                        }
                        foreach (var ctrl in affected)
                            ctrl.UpdateUIInternal();
                        Narration.Say(string.Format(RDString.Get("eam.conditional.deleted"), entry.description), NarrationCategory.Navigation);
                    }
                    CloseVirtualMenu();
                }
                else
                {
                    // 第一次按 D：统计使用该条件的事件数，朗读确认提示
                    _pendingDeleteConditionalId = entry.localId;
                    int usageCount = 0;
                    var editor = scnEditor.instance;
                    if (editor?.eventControls != null)
                    {
                        foreach (var ctrl in editor.eventControls)
                        {
                            if (ctrl?.levelEvent?.HasConditional(entry.localId).HasValue == true)
                                usageCount++;
                        }
                    }
                    string confirmMsg;
                    if (usageCount == 0)
                        confirmMsg = string.Format(RDString.Get("eam.conditional.deleteConfirmNoUsage"), entry.description);
                    else if (usageCount == 1)
                        confirmMsg = string.Format(RDString.Get("eam.conditional.deleteConfirmSingle"), entry.description);
                    else
                        confirmMsg = string.Format(RDString.Get("eam.conditional.deleteConfirm"), entry.description, usageCount);
                    Narration.Say(confirmMsg, NarrationCategory.Navigation);
                }
            }
        }

        // 反射缓存：denominatorIndex / denominatorValues 均为 private
        private static readonly System.Reflection.FieldInfo _denominatorIndexField =
            typeof(scnEditor).GetField("denominatorIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo _denominatorValuesField =
            typeof(scnEditor).GetField("denominatorValues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private int GetDenominatorIndex(scnEditor editor) =>
            _denominatorIndexField != null ? (int)_denominatorIndexField.GetValue(editor) : 3;

        private int[] GetDenominatorValues(scnEditor editor) =>
            _denominatorValuesField != null ? (int[])_denominatorValuesField.GetValue(editor) : new int[] { 1, 2, 3, 4, 6, 8, 12, 16, 100 };

        /// <summary>
        /// 开始网格精度选择菜单（Alt+G）
        /// </summary>
        private void StartGridSelect()
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            var values = GetDenominatorValues(editor);
            int curIndex = GetDenominatorIndex(editor);
            virtualMenuState = VirtualMenuState.GridSelect;
            virtualMenuIndex = curIndex;
            SetFakeInputField();

            string label = virtualMenuIndex < values.Length - 1
                ? string.Format(RDString.Get("eam.grid.item"), values[virtualMenuIndex])
                : string.Format(RDString.Get("eam.grid.custom"), values[values.Length - 1]);
            Narration.Say(label, NarrationCategory.Navigation);
            Narration.Say(RDString.Get("eam.grid.selectPrompt"), NarrationCategory.Instruction);
        }

        /// <summary>
        /// 开始虚拟选区选项菜单
        /// </summary>
        private void StartVirtualSelectionOptions()
        {
            virtualMenuState = VirtualMenuState.VirtualSelectionOptions;
            virtualMenuIndex = 0;
            SetFakeInputField();
            Narration.Say(RDString.Get("eam.vsel.options"), NarrationCategory.Navigation);
        }

        /// <summary>
        /// 处理网格精度选择菜单
        /// </summary>
        private void HandleGridSelectMenu()
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            var values = GetDenominatorValues(editor);
            int count = values.Length;

            void AnnounceCurrentItem()
            {
                string label = virtualMenuIndex < count - 1
                    ? string.Format(RDString.Get("eam.grid.item"), values[virtualMenuIndex])
                    : string.Format(RDString.Get("eam.grid.custom"), values[count - 1]);
                Narration.Say(label, NarrationCategory.Navigation);
            }

            void ConfirmSelection()
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                // 切换 denominator index 到目标值
                int curIndex = GetDenominatorIndex(editor);
                int diff = virtualMenuIndex - curIndex;
                if (diff != 0)
                {
                    int dir = diff > 0 ? 1 : -1;
                    for (int i = 0; i < Mathf.Abs(diff); i++)
                        editor.CycleSnapValues(dir);
                }

                // 自定义项：Ctrl+Enter 弹 Helper，Enter 直接应用当前自定义值
                if (virtualMenuIndex == count - 1 && ctrl)
                {
                    CloseVirtualMenu();
                    AccessibilityBridge.GridCustomInput(values[count - 1]);
                    return;
                }

                AnnounceCurrentItem();
                CloseVirtualMenu();
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                virtualMenuIndex = (virtualMenuIndex - 1 + count) % count;
                AnnounceCurrentItem();
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                virtualMenuIndex = (virtualMenuIndex + 1) % count;
                AnnounceCurrentItem();
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                ConfirmSelection();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                CloseVirtualMenu();
            }
            else
            {
                // 数字键 1~8 直接跳到对应 index（1→0 ... 8→7），9 选自定义（index count-1）
                for (int i = 1; i <= 9; i++)
                {
                    KeyCode kc = KeyCode.Alpha0 + i;
                    if (Input.GetKeyDown(kc))
                    {
                        virtualMenuIndex = i <= count ? i - 1 : count - 1;
                        AnnounceCurrentItem();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 处理虚拟选区选项菜单（选择/添加）
        /// </summary>
        private void HandleVirtualSelectionOptionsMenu()
        {
            const int itemCount = 2;
            var editor = scnEditor.instance;
            if (editor == null) return;

            void AnnounceCurrentItem()
            {
                string label = virtualMenuIndex switch
                {
                    0 => RDString.Get("eam.vsel.selectEvents"),
                    1 => RDString.Get("eam.vsel.addSelected"),
                    _ => ""
                };
                Narration.Say(label, NarrationCategory.Navigation);
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                virtualMenuIndex = (virtualMenuIndex - 1 + itemCount) % itemCount;
                AnnounceCurrentItem();
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                virtualMenuIndex = (virtualMenuIndex + 1) % itemCount;
                AnnounceCurrentItem();
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                switch (virtualMenuIndex)
                {
                    case 0:
                        // 选中虚拟选区中的事件
                        var sorted = GetSortedVirtualSelection();
                        if (sorted.Count > 0)
                        {
                            editor.SelectEventControls(sorted);
                            Narration.Say(string.Format(RDString.Get("eam.vsel.selectedCount"), sorted.Count), NarrationCategory.Navigation);
                        }
                        break;
                    case 1:
                        // 将当前选区添加到虚拟选区
                        if (editor.selectedControls != null && editor.selectedControls.Count > 0)
                        {
                            int addedCount = editor.selectedControls.Count;
                            foreach (var control in editor.selectedControls)
                            {
                                virtualSelection.Add(control);
                            }
                            virtualSelectionBrowseIndex = -1;
                            Narration.Say(string.Format(RDString.Get("eam.vsel.addedCount"), addedCount), NarrationCategory.Navigation);
                        }
                        break;
                }
                CloseVirtualMenu();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
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
            _pendingDeleteConditionalId = -1;
            IsVirtualMenuActive = false;
        }

        /// <summary>
        /// 虚拟菜单打开时设置标志，屏蔽游戏原生快捷键。
        /// </summary>
        private void SetFakeInputField()
        {
            IsVirtualMenuActive = true;
        }

        // ===================================================================================
        // 事件链功能
        // ===================================================================================

        /// <summary>
        /// 获取当前关卡的事件链存储目录
        /// </summary>
        private string? GetEventChainDirectory()
        {
            string filePath = scnEditor.instance?.openedFilePath;
            if (string.IsNullOrEmpty(filePath)) return null;
            string levelDir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(levelDir) || !Directory.Exists(levelDir)) return null;
            return Path.Combine(levelDir, ".RDLEAccess", "EventChains");
        }

        /// <summary>
        /// Ctrl+分号：将虚拟选区中的事件序列化并启动命名对话框
        /// </summary>
        private void StartSaveEventChain()
        {
            var sorted = GetSortedVirtualSelection();
            if (sorted.Count == 0)
            {
                Narration.Say(RDString.Get("eam.vsel.empty"), NarrationCategory.Navigation);
                return;
            }

            string chainDir = GetEventChainDirectory();
            if (chainDir == null)
            {
                Narration.Say(RDString.Get("eam.chain.noLevel"), NarrationCategory.Navigation);
                return;
            }

            // 序列化每个事件（Encode 使用共享 StringBuilder，需逐个调用）
            var encodedEvents = new List<string>();
            foreach (var control in sorted)
            {
                if (control?.levelEvent == null) continue;
                string encoded = "{ " + control.levelEvent.Encode() + " }";
                encodedEvents.Add(encoded);
            }

            // 构建事件链 JSON
            var chainDict = new Dictionary<string, object>
            {
                ["version"] = 1,
                ["events"] = encodedEvents
            };
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            _pendingChainData = System.Text.Json.JsonSerializer.Serialize(chainDict, opts);

            AccessibilityBridge.SaveEventChain();
        }

        /// <summary>
        /// 分号：列出可用事件链并进入选择菜单
        /// </summary>
        private void StartLoadEventChain()
        {
            string chainDir = GetEventChainDirectory();
            if (chainDir == null)
            {
                Narration.Say(RDString.Get("eam.chain.noLevel"), NarrationCategory.Navigation);
                return;
            }

            if (!Directory.Exists(chainDir))
            {
                Narration.Say(RDString.Get("eam.chain.noChains"), NarrationCategory.Navigation);
                return;
            }

            var files = Directory.GetFiles(chainDir, "*.json");
            if (files.Length == 0)
            {
                Narration.Say(RDString.Get("eam.chain.noChains"), NarrationCategory.Navigation);
                return;
            }

            eventChainNames.Clear();
            foreach (var f in files)
            {
                eventChainNames.Add(Path.GetFileNameWithoutExtension(f));
            }

            virtualMenuState = VirtualMenuState.EventChainSelect;
            virtualMenuIndex = 0;
            _chainSpeedIndex = 3;
            SetFakeInputField();
            Narration.Say(eventChainNames[0], NarrationCategory.Navigation);
            Narration.Say(RDString.Get("eam.chain.selectPrompt"), NarrationCategory.Instruction);
        }

        /// <summary>
        /// 事件链选择菜单的导航处理
        /// </summary>
        private void HandleEventChainSelectMenu()
        {
            if (eventChainNames.Count == 0)
            {
                CloseVirtualMenu();
                return;
            }

            int count = eventChainNames.Count;

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                virtualMenuIndex = (virtualMenuIndex - 1 + count) % count;
                Narration.Say(eventChainNames[virtualMenuIndex], NarrationCategory.Navigation);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                virtualMenuIndex = (virtualMenuIndex + 1) % count;
                Narration.Say(eventChainNames[virtualMenuIndex], NarrationCategory.Navigation);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (_chainSpeedIndex > 0)
                {
                    _chainSpeedIndex--;
                    Narration.Say(string.Format(RDString.Get("eam.chain.speed"), FormatChainSpeed(ChainSpeedOptions[_chainSpeedIndex])), NarrationCategory.Navigation);
                }
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                if (_chainSpeedIndex < ChainSpeedOptions.Length - 1)
                {
                    _chainSpeedIndex++;
                    Narration.Say(string.Format(RDString.Get("eam.chain.speed"), FormatChainSpeed(ChainSpeedOptions[_chainSpeedIndex])), NarrationCategory.Navigation);
                }
            }
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                string selectedName = eventChainNames[virtualMenuIndex];
                CloseVirtualMenu();
                LoadAndInsertEventChain(selectedName);
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                Narration.Say(RDString.Get("eam.action.cancelled"), NarrationCategory.Navigation);
                CloseVirtualMenu();
            }
        }

        /// <summary>
        /// 从事件链文件加载事件并插入到编辑光标位置
        /// </summary>
        private void LoadAndInsertEventChain(string chainName)
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            string chainDir = GetEventChainDirectory();
            if (chainDir == null) return;

            string filePath = Path.Combine(chainDir, chainName + ".json");
            if (!File.Exists(filePath))
            {
                Narration.Say(string.Format(RDString.Get("eam.chain.loadFailed"), chainName), NarrationCategory.Navigation);
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var chainData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

                if (!chainData.TryGetProperty("events", out var eventsArray))
                {
                    Narration.Say(string.Format(RDString.Get("eam.chain.loadFailed"), chainName), NarrationCategory.Navigation);
                    return;
                }

                // 反序列化所有事件
                var events = new List<LevelEvent_Base>();
                int skipped = 0;

                foreach (var eventElement in eventsArray.EnumerateArray())
                {
                    string eventStr = eventElement.GetString();
                    if (string.IsNullOrEmpty(eventStr)) { skipped++; continue; }

                    var dict = ParseEventJson(eventStr);
                    if (dict == null || !dict.ContainsKey("type")) { skipped++; continue; }

                    string typeName = dict["type"] as string;
                    if (string.IsNullOrEmpty(typeName)) { skipped++; continue; }
                    string fullTypeName = "RDLevelEditor.LevelEvent_" + typeName;
                    Type eventType = Type.GetType(fullTypeName);
                    if (eventType == null)
                    {
                        fullTypeName += ", Assembly-CSharp";
                        eventType = Type.GetType(fullTypeName);
                    }
                    if (eventType == null || !eventType.IsSubclassOf(typeof(LevelEvent_Base)))
                    {
                        skipped++;
                        continue;
                    }

                    var levelEvent = (LevelEvent_Base)Activator.CreateInstance(eventType);
                    levelEvent.Decode(dict);
                    events.Add(levelEvent);
                }

                if (events.Count == 0)
                {
                    Narration.Say(string.Format(RDString.Get("eam.chain.loadFailed"), chainName), NarrationCategory.Navigation);
                    return;
                }

                // 找到第一个事件（sortOrder 最小）作为锚点
                LevelEvent_Base firstEvent = events[0];
                foreach (var evt in events)
                {
                    if (evt.sortOrder < firstEvent.sortOrder)
                        firstEvent = evt;
                }

                // 使用像素空间计算位置偏移，并应用倍速缩放
                var tl = editor.timeline;
                float firstEventX = tl.GetPosXFromBarAndBeat(firstEvent.barAndBeat);
                float cursorX = tl.GetPosXFromBarAndBeat(_editCursor);
                float speedMultiplier = ChainSpeedOptions[_chainSpeedIndex];

                // 创建事件控件并插入
                var controls = new List<LevelEventControl_Base>();
                using (new SaveStateScope())
                {
                    foreach (var evt in events)
                    {
                        // 相对锚点的像素距离除以倍速，实现间距缩放
                        float relativeX = tl.GetPosXFromBarAndBeat(evt.barAndBeat) - firstEventX;
                        float newX = Mathf.Max(0f, cursorX + relativeX / speedMultiplier);
                        var newPos = tl.GetBarAndBeatWithPosX(newX);
                        evt.bar = newPos.bar;
                        evt.beat = newPos.beat;

                        // 生成新 UID 并创建控件
                        evt.GenerateNewUID();
                        var control = editor.CreateEventControl(evt, evt.defaultTab);
                        control.UpdateUI();
                        controls.Add(control);
                    }
                }

                // 选中所有插入的事件
                if (controls.Count > 0)
                {
                    editor.SelectEventControls(controls);
                }

                // 朗读结果
                Narration.Say(string.Format(RDString.Get("eam.chain.inserted"), chainName, events.Count), NarrationCategory.Navigation);

                if (skipped > 0)
                {
                    Narration.Say(string.Format(RDString.Get("eam.chain.skippedEvents"), skipped), NarrationCategory.Navigation);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EventChain] 加载事件链失败: {ex.Message}");
                Debug.LogException(ex);
                Narration.Say(string.Format(RDString.Get("eam.chain.loadFailed"), chainName), NarrationCategory.Navigation);
            }
        }

        /// <summary>
        /// 将事件 JSON 字符串解析为 Dictionary&lt;string, object&gt;，
        /// 值类型与游戏 MiniJSON 兼容（int/double/bool/string/List/Dict）
        /// </summary>
        private static Dictionary<string, object> ParseEventJson(string jsonStr)
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                return JsonElementToDict(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> JsonElementToDict(System.Text.Json.JsonElement element)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = JsonElementToObject(prop.Value);
            }
            return dict;
        }

        private static object JsonElementToObject(System.Text.Json.JsonElement element)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    return element.GetString();
                case System.Text.Json.JsonValueKind.True:
                    return true;
                case System.Text.Json.JsonValueKind.False:
                    return false;
                case System.Text.Json.JsonValueKind.Null:
                    return null;
                case System.Text.Json.JsonValueKind.Number:
                    // MiniJSON 兼容：整数返回 int，小数返回 double
                    if (element.TryGetInt32(out int intVal))
                        return intVal;
                    if (element.TryGetInt64(out long longVal))
                        return longVal;
                    return element.GetDouble();
                case System.Text.Json.JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                        list.Add(JsonElementToObject(item));
                    return list;
                case System.Text.Json.JsonValueKind.Object:
                    return JsonElementToDict(element);
                default:
                    return element.ToString();
            }
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
            float newX = (float)Math.Round(Mathf.Max(0f, cursorX + deltaBeat * tl.cellWidth), 4);
            _editCursor = tl.GetBarAndBeatWithPosX(newX);

            string announcement = _editCursor.bar != oldBar
                ? FormatBarAndBeat(_editCursor)
                : FormatBeatOnly(_editCursor.beat);
            Narration.Say(announcement, NarrationCategory.Navigation);
        }

        /// <summary>
        /// 将编辑光标按小节移动（正数向右，负数向左）。
        /// 自动处理变速小节（SetCrotchetsPerBar）。
        /// </summary>
        private void MoveEditCursorByBar(int deltaBar)
        {
            var editor = scnEditor.instance;
            if (editor?.timeline == null) return;

            int oldBar = _editCursor.bar;
            int newBar = Mathf.Max(1, _editCursor.bar + deltaBar);
            _editCursor.bar = newBar;

            string announcement = FormatBarAndBeat(_editCursor);
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
                    float newX = (float)Math.Round(Mathf.Max(0f, posX + deltaBeat * tl.cellWidth), 4);
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
        /// 将所有选中事件吸附到当前网格精度（由原生编辑器 denominator 决定）。
        /// 使用像素空间运算以自动处理变速小节。
        /// </summary>
        private void SnapSelectedEventsToGrid()
        {
            var editor = scnEditor.instance;
            if (editor?.timeline == null) return;
            if (editor.selectedControls == null || editor.selectedControls.Count == 0)
            {
                Narration.Say(RDString.Get("eam.event.noSelection"), NarrationCategory.Navigation);
                return;
            }

            var tl = editor.timeline;
            float snapBeat = tl.cellWidth * (1f / editor.denominator);

            using (new SaveStateScope())
            {
                foreach (var control in editor.selectedControls)
                {
                    float posX = tl.GetPosXFromBarAndBeat(control.levelEvent.barAndBeat);
                    float snappedX = Mathf.Max(0f, Mathf.Round(posX / snapBeat) * snapBeat);
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
        /// 将事件链倍速格式化为字符串（去掉末尾无意义的零，如 1.0 → "1"，0.25 → "0.25"）。
        /// </summary>
        private static string FormatChainSpeed(float speed)
        {
            // G4 格式会去掉末尾零，并在必要时使用科学计数法，但这里数值范围固定不会触发
            return speed.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
        }

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
        private void AddNewRow(Character character, bool openHelper = false)
        {
            var editor = scnEditor.instance;
            if (editor == null) return;

            int roomIndex = editor.selectedRowsTabPageIndex;
            
            var rowData = new LevelEvent_MakeRow();
            rowData.rooms = new int[1] { roomIndex };
            rowData.character = character;
            
            editor.AddNewRow(rowData);
            editor.tabSection_rows.UpdateUI();

            if (openHelper)
            {
                int newRowIndex = editor.rowsData.IndexOf(rowData);
                Narration.Say(string.Format(RDString.Get("eam.track.addedAndOpening"), GetCharacterName(character)), NarrationCategory.Navigation);
                AccessibilityBridge.EditRow(newRowIndex);
            }
            else
            {
                Narration.Say(string.Format(RDString.Get("eam.track.added"), GetCharacterName(character)), NarrationCategory.Navigation);
            }
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
        /// 创建事件
        /// </summary>
        private void CreateEventAndEdit(LevelEventType eventType, int bar, float beat, int row, bool openHelper = false)
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

            if (openHelper)
            {
                Narration.Say(string.Format(RDString.Get("eam.event.createdAndOpening"), GetEventTypeName(eventType)), NarrationCategory.Navigation);
                AccessibilityBridge.EditEvent(levelEvent);
            }
            else
            {
                Narration.Say(string.Format(RDString.Get("eam.event.created"), GetEventTypeName(eventType)), NarrationCategory.Navigation);
            }
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
            string str = RDString.GetWithCheck($"editor.{eventType}", out bool exists);
            return exists ? str : eventType.ToString();
        }

        private string GetEventTypeNameWithShortcut(Tab tab, int index, LevelEventType eventType)
        {
            string name = GetEventTypeName(eventType);
            if (RDEditorConstants.eventKeyCodes.TryGetValue(tab, out var keyCodes) && index < keyCodes.Count)
            {
                var kc = keyCodes[index];
                if (kc.key != KeyCode.None)
                {
                    string keyStr = kc.shift ? $"Shift+{kc.key.ToString().ToLower()}" : kc.key.ToString().ToLower();
                    name = $"{name} ({keyStr})";
                }
            }
            return name;
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

            // 快照 SoundData 属性（防止选择时偏移跑偏）
            var soundSnapshot = SnapshotSoundDataValues(toSelect.levelEvent);

            editor.SelectEventControl(toSelect, true);

            // 延迟到下一帧恢复被意外修改的 SoundData 属性
            if (soundSnapshot.Count > 0)
            {
                _pendingSoundRestoreEvent = toSelect.levelEvent;
                _pendingSoundRestoreSnapshot = soundSnapshot;
                Debug.Log($"[SoundDataGuard] 已排队延迟恢复，将在下一帧执行");
            }
        }

        /// <summary>
        /// 快照事件上所有 SoundData/SoundDataArray 属性的值（用于防止选择时偏移跑偏）
        /// </summary>
        private Dictionary<string, object> SnapshotSoundDataValues(LevelEvent_Base evt)
        {
            var snapshot = new Dictionary<string, object>();
            if (evt?.info == null) return snapshot;

            foreach (var prop in evt.info.propertiesInfo)
            {
                bool isSoundData = prop is SoundDataPropertyInfo;
                bool isNullableSoundData = prop is NullablePropertyInfo np
                    && np.underlyingPropertyInfo is SoundDataPropertyInfo;
                bool isSoundDataArray = prop.propertyInfo.PropertyType == typeof(SoundDataStruct[]);

                if (isSoundData || isNullableSoundData || isSoundDataArray)
                {
                    var value = prop.propertyInfo.GetValue(evt);
                    string propName = prop.propertyInfo.Name;

                    if (isSoundDataArray && value is SoundDataStruct[] arr)
                    {
                        snapshot[propName] = (SoundDataStruct[])arr.Clone();
                        for (int i = 0; i < arr.Length; i++)
                            Debug.Log($"[SoundDataGuard] 快照 {evt.type}.{propName}[{i}]: filename={arr[i].filename}, offset={arr[i].offset}, volume={arr[i].volume}, pitch={arr[i].pitch}, pan={arr[i].pan}");
                    }
                    else if (value is SoundDataStruct sd)
                    {
                        snapshot[propName] = value;
                        Debug.Log($"[SoundDataGuard] 快照 {evt.type}.{propName}: filename={sd.filename}, offset={sd.offset}, volume={sd.volume}, pitch={sd.pitch}, pan={sd.pan}");
                    }
                    else
                    {
                        snapshot[propName] = value;
                        Debug.Log($"[SoundDataGuard] 快照 {evt.type}.{propName}: value={value ?? "null"} (nullable)");
                    }
                }
            }

            Debug.Log($"[SoundDataGuard] 共快照 {snapshot.Count} 个 SoundData 属性 (事件类型: {evt.type})");
            return snapshot;
        }

        /// <summary>
        /// 对比并恢复被意外修改的 SoundData 属性值
        /// </summary>
        private void RestoreSoundDataValues(LevelEvent_Base evt, Dictionary<string, object> snapshot)
        {
            if (evt?.info == null || snapshot.Count == 0) return;

            foreach (var prop in evt.info.propertiesInfo)
            {
                if (!snapshot.TryGetValue(prop.propertyInfo.Name, out var original)) continue;

                var current = prop.propertyInfo.GetValue(evt);
                string propName = prop.propertyInfo.Name;

                bool changed;
                if (current is SoundDataStruct[] currentArr && original is SoundDataStruct[] originalArr)
                {
                    changed = !currentArr.SequenceEqual(originalArr);
                    if (changed)
                    {
                        Debug.LogWarning($"[SoundDataGuard] ★ 数组属性 {evt.type}.{propName} 被意外修改！");
                        for (int i = 0; i < Math.Max(currentArr.Length, originalArr.Length); i++)
                        {
                            string before = i < originalArr.Length ? $"filename={originalArr[i].filename}, offset={originalArr[i].offset}, volume={originalArr[i].volume}, pitch={originalArr[i].pitch}, pan={originalArr[i].pan}" : "(不存在)";
                            string after = i < currentArr.Length ? $"filename={currentArr[i].filename}, offset={currentArr[i].offset}, volume={currentArr[i].volume}, pitch={currentArr[i].pitch}, pan={currentArr[i].pan}" : "(不存在)";
                            if (i < originalArr.Length && i < currentArr.Length && !Equals(originalArr[i], currentArr[i]))
                                Debug.LogWarning($"[SoundDataGuard]   [{i}] 变更前: {before}");
                            if (i < originalArr.Length && i < currentArr.Length && !Equals(originalArr[i], currentArr[i]))
                                Debug.LogWarning($"[SoundDataGuard]   [{i}] 变更后: {after}");
                        }
                    }
                }
                else if (current is SoundDataStruct currentSd && original is SoundDataStruct originalSd)
                {
                    changed = !Equals(current, original);
                    if (changed)
                    {
                        Debug.LogWarning($"[SoundDataGuard] ★ 属性 {evt.type}.{propName} 被意外修改！");
                        Debug.LogWarning($"[SoundDataGuard]   变更前: filename={originalSd.filename}, offset={originalSd.offset}, volume={originalSd.volume}, pitch={originalSd.pitch}, pan={originalSd.pan}");
                        Debug.LogWarning($"[SoundDataGuard]   变更后: filename={currentSd.filename}, offset={currentSd.offset}, volume={currentSd.volume}, pitch={currentSd.pitch}, pan={currentSd.pan}");
                    }
                }
                else
                {
                    changed = !Equals(current, original);
                    if (changed)
                    {
                        Debug.LogWarning($"[SoundDataGuard] ★ Nullable 属性 {evt.type}.{propName} 被意外修改！");
                        Debug.LogWarning($"[SoundDataGuard]   变更前: {original ?? "null"}");
                        Debug.LogWarning($"[SoundDataGuard]   变更后: {current ?? "null"}");
                    }
                }

                if (changed)
                {
                    Debug.LogWarning($"[SoundDataGuard] 正在恢复 {evt.type}.{propName} 到快照值");
                    prop.propertyInfo.SetValue(evt, original);
                    Debug.Log($"[SoundDataGuard] 恢复完成: {evt.type}.{propName}");
                }
                else
                {
                    Debug.Log($"[SoundDataGuard] 属性 {evt.type}.{propName} 未变化，无需恢复");
                }
            }
        }

        // ===================================================================================
        // 虚拟选区方法
        // ===================================================================================

        /// <summary>
        /// 获取虚拟选区按时间轴顺序排序的列表（自动清除无效引用）
        /// </summary>
        internal List<LevelEventControl_Base> GetSortedVirtualSelection()
        {
            virtualSelection.RemoveWhere(c => c == null || c.levelEvent == null);
            return virtualSelection
                .OrderBy(c => c.levelEvent.sortOrder)
                .ThenBy(c => c.levelEvent.y)
                .ToList();
        }

        /// <summary>
        /// 浏览虚拟选区中的事件
        /// </summary>
        private void BrowseVirtualSelection(BrowseDirection direction)
        {
            var sorted = GetSortedVirtualSelection();
            if (sorted.Count == 0)
            {
                Narration.Say(RDString.Get("eam.vsel.empty"), NarrationCategory.Navigation);
                return;
            }

            int targetIndex;
            switch (direction)
            {
                case BrowseDirection.First:
                    targetIndex = 0;
                    break;
                case BrowseDirection.Last:
                    targetIndex = sorted.Count - 1;
                    break;
                case BrowseDirection.Previous:
                    targetIndex = virtualSelectionBrowseIndex <= 0 ? 0 : virtualSelectionBrowseIndex - 1;
                    break;
                case BrowseDirection.Next:
                default:
                    targetIndex = virtualSelectionBrowseIndex < 0 ? 0
                        : virtualSelectionBrowseIndex >= sorted.Count - 1 ? sorted.Count - 1
                        : virtualSelectionBrowseIndex + 1;
                    break;
            }

            virtualSelectionBrowseIndex = targetIndex;
            NavigateToEventControl(sorted[targetIndex]);
        }

        /// <summary>
        /// 导航到指定事件控件：切换 Tab、选择 Row/Sprite、选中事件并居中显示
        /// </summary>
        private void NavigateToEventControl(LevelEventControl_Base control)
        {
            var editor = scnEditor.instance;
            if (editor == null || control == null || control.levelEvent == null) return;

            var levelEvent = control.levelEvent;
            Tab targetTab = control.tab;

            // 1. 切换 Tab
            if (editor.currentTab != targetTab)
            {
                editor.ShowTabSection(targetTab);
                currentTab = targetTab;
            }

            // 2. 切换 Row/Sprite
            if (targetTab == Tab.Rows && levelEvent.row >= 0)
            {
                if (editor.selectedRowIndex != levelEvent.row)
                {
                    RowHeader.ShowPanel(levelEvent.row);
                }
            }
            else if (targetTab == Tab.Sprites && levelEvent.row >= 0
                     && levelEvent.row < editor.spritesData.Count)
            {
                string spriteId = editor.spritesData[levelEvent.row].spriteId;
                if (editor.selectedSprite != spriteId)
                {
                    SpriteHeader.ShowPanel(spriteId);
                }
            }

            // 3. 选中事件（带 SoundData 保护）
            var soundSnapshot = SnapshotSoundDataValues(levelEvent);
            editor.SelectEventControl(control, true);
            if (soundSnapshot.Count > 0)
            {
                _pendingSoundRestoreEvent = levelEvent;
                _pendingSoundRestoreSnapshot = soundSnapshot;
            }

            // 4. 居中显示
            editor.timeline.UnfollowPlayhead();
            editor.timeline.CenterOnPosition(
                (control.rt.anchoredPosition.x + control.rightPosition) / 2f, 0.3f);
            editor.timeline.CenterOnVertPosition(
                (0f - (control.rt.anchoredPosition.y + control.bottomPosition)) / 2f, 0.3f);
        }

        /// <summary>
        /// 兼容创建 SoundDataStruct（稳定版无 used 字段时回退到 5 参数构造）
        /// </summary>
        private static SoundDataStruct CreateSoundDataStructCompat(
            string filename, int volume, int pitch, int pan, int offset, bool usedFallback = true)
        {
            if (_hasUsedField && _soundDataCtor6 != null)
            {
                try
                {
                    return (SoundDataStruct)_soundDataCtor6.Invoke(
                        new object[] { filename, volume, pitch, pan, offset, usedFallback });
                }
                catch { }
            }
            return new SoundDataStruct(filename, volume, pitch, pan, offset);
        }

        /// <summary>
        /// 兼容读取 SoundDataStruct.used（稳定版无此字段时返回 true）
        /// </summary>
        private static bool GetSoundDataUsed(SoundDataStruct sd)
        {
            if (_hasUsedField)
            {
                try { return (bool)typeof(SoundDataStruct).GetField("used").GetValue(sd); }
                catch { }
            }
            return true;
        }

        /// <summary>
        /// [调试用] 每帧监听当前选中事件的 SoundData offset，变化时立即修正
        /// </summary>
        private void DebugWatchSoundDataOffsets()
        {
          try
          {
            var editor = scnEditor.instance;
            var evt = editor?.selectedControl?.levelEvent;

            // 选中事件变化时，重置监听
            if (evt != _debugWatchedEvent)
            {
                _debugWatchedEvent = evt;
                _debugLastOffsets.Clear();

                if (evt?.info != null)
                {
                    foreach (var prop in evt.info.propertiesInfo)
                    {
                        bool isSoundData = prop is SoundDataPropertyInfo
                            || (prop is NullablePropertyInfo np && np.underlyingPropertyInfo is SoundDataPropertyInfo);
                        bool isSoundDataArray = prop.propertyInfo.PropertyType == typeof(SoundDataStruct[]);

                        if (!isSoundData && !isSoundDataArray) continue;

                        var value = prop.propertyInfo.GetValue(evt);
                        if (value is SoundDataStruct sd)
                            _debugLastOffsets[prop.propertyInfo.Name] = sd.offset;
                        else if (value is SoundDataStruct[] arr)
                            for (int i = 0; i < arr.Length; i++)
                                _debugLastOffsets[$"{prop.propertyInfo.Name}[{i}]"] = arr[i].offset;
                    }
                }
                return;
            }

            if (evt?.info == null) return;

            foreach (var prop in evt.info.propertiesInfo)
            {
                bool isSoundData = prop is SoundDataPropertyInfo
                    || (prop is NullablePropertyInfo np && np.underlyingPropertyInfo is SoundDataPropertyInfo);
                bool isSoundDataArray = prop.propertyInfo.PropertyType == typeof(SoundDataStruct[]);

                if (!isSoundData && !isSoundDataArray) continue;

                var value = prop.propertyInfo.GetValue(evt);
                if (value is SoundDataStruct sd)
                {
                    string key = prop.propertyInfo.Name;
                    if (_debugLastOffsets.TryGetValue(key, out int last) && last != sd.offset)
                    {
                        Debug.LogWarning($"[SoundDataWatch] {evt.type}.{key} offset 跑偏: {last} -> {sd.offset}，正在修正");
                        prop.propertyInfo.SetValue(evt, CreateSoundDataStructCompat(sd.filename, sd.volume, sd.pitch, sd.pan, last, GetSoundDataUsed(sd)));
                    }
                    // 不更新 _debugLastOffsets，始终以初始值为准
                }
                else if (value is SoundDataStruct[] arr)
                {
                    bool anyFixed = false;
                    var fixedArr = (SoundDataStruct[])arr.Clone();
                    for (int i = 0; i < arr.Length; i++)
                    {
                        string key = $"{prop.propertyInfo.Name}[{i}]";
                        if (_debugLastOffsets.TryGetValue(key, out int last) && last != arr[i].offset)
                        {
                            Debug.LogWarning($"[SoundDataWatch] {evt.type}.{key} offset 跑偏: {last} -> {arr[i].offset}，正在修正");
                            fixedArr[i] = CreateSoundDataStructCompat(arr[i].filename, arr[i].volume, arr[i].pitch, arr[i].pan, last, GetSoundDataUsed(arr[i]));
                            anyFixed = true;
                        }
                    }
                    if (anyFixed)
                        prop.propertyInfo.SetValue(evt, fixedArr);
                }
            }
          }
          catch (Exception ex)
          {
            Debug.LogWarning($"[SoundDataWatch] DebugWatchSoundDataOffsets 异常，已跳过: {ex.Message}");
          }
        }

        /// <summary>
        /// 用户通过 IPC 编辑了 SoundData 属性后，刷新偏移保护的基准值，
        /// 避免 Guard/Watch 将用户的合法编辑误判为跑偏并回滚。
        /// </summary>
        internal void RefreshSoundDataBaseline(LevelEvent_Base evt)
        {
            // 1. 取消待执行的 Guard 恢复（如果目标是同一个事件）
            if (_pendingSoundRestoreEvent == evt)
            {
                Debug.Log("[SoundDataGuard] 用户已编辑 SoundData，取消待恢复快照");
                _pendingSoundRestoreEvent = null;
                _pendingSoundRestoreSnapshot = null;
            }

            // 2. 刷新 DebugWatch 的基准偏移值
            if (_debugWatchedEvent == evt && evt?.info != null)
            {
                _debugLastOffsets.Clear();
                foreach (var prop in evt.info.propertiesInfo)
                {
                    bool isSoundData = prop is SoundDataPropertyInfo
                        || (prop is NullablePropertyInfo np && np.underlyingPropertyInfo is SoundDataPropertyInfo);
                    bool isSoundDataArray = prop.propertyInfo.PropertyType == typeof(SoundDataStruct[]);

                    if (!isSoundData && !isSoundDataArray) continue;

                    var value = prop.propertyInfo.GetValue(evt);
                    if (value is SoundDataStruct sd)
                        _debugLastOffsets[prop.propertyInfo.Name] = sd.offset;
                    else if (value is SoundDataStruct[] arr)
                        for (int i = 0; i < arr.Length; i++)
                            _debugLastOffsets[$"{prop.propertyInfo.Name}[{i}]"] = arr[i].offset;
                }
                Debug.Log($"[SoundDataWatch] 用户编辑后已刷新基准偏移 ({_debugLastOffsets.Count} 项)");
            }
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

        /// <summary>
        /// 获取事件属性摘要（复用游戏内置 tooltip 文本）
        /// </summary>
        public static string GetEventSummary(LevelEvent_Base levelEvent)
        {
            string tooltip = levelEvent.GetTooltipText();
            if (string.IsNullOrEmpty(tooltip)) return string.Empty;
            // 将换行替换为顿号，完整朗读所有内容
            return string.Join("，", tooltip.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)));
        }

        /// <summary>
        /// 朗读事件选择信息（事件名称、位置和属性摘要）
        /// </summary>
        public static void AnnounceEventSelection(LevelEvent_Base levelEvent)
        {
            if (levelEvent == null) return;

            var editor = scnEditor.instance;
            bool tagMode = editor != null && editor.tagFieldMode;

            var bb = new BarAndBeat(levelEvent.bar, levelEvent.beat);
            string summary = GetEventSummary(levelEvent);

            string positionOrTag;
            if (tagMode)
            {
                // 标签模式：朗读标签名
                positionOrTag = string.IsNullOrEmpty(levelEvent.tag)
                    ? RDString.Get("eam.tag.noTag")
                    : levelEvent.tag;
            }
            else
            {
                // 普通模式：朗读位置
                positionOrTag = FormatBarAndBeat(bb);
            }

            string announcement = string.IsNullOrEmpty(summary)
                ? $"{eventSelectI18n(levelEvent)}，{positionOrTag}"
                : $"{eventSelectI18n(levelEvent)}，{positionOrTag}，{summary}";
            Narration.Say(announcement, NarrationCategory.Navigation);
            if (editor != null)
            {
                var comments = GetCommentsAtPosition(editor, levelEvent.bar, levelEvent.beat, levelEvent);
                if (comments.Count > 0)
                {
                    editor.LevelEditorPlaySound("sndButtonRadio", "LevelEditorActive");
                }
                foreach (var (tabName, comment) in comments)
                {
                    Narration.Say(
                        string.Format(RDString.Get("eam.event.commentAt"), tabName, comment.text),
                        NarrationCategory.Navigation,
                        flipCategoryQueueBehaviour: true);
                }
            }
        }

        private static List<(string tabName, LevelEvent_Comment comment)> GetCommentsAtPosition(
            scnEditor editor, int bar, float beat, LevelEvent_Base exclude)
        {
            var result = new List<(string, LevelEvent_Comment)>();
            void Scan(IEnumerable<LevelEventControl_Base> controls, string tabName)
            {
                foreach (var ctrl in controls)
                    if (ctrl?.levelEvent is LevelEvent_Comment c
                        && c.bar == bar && c.beat == beat
                        && !string.IsNullOrWhiteSpace(c.text)
                        && !ReferenceEquals(c, exclude))
                        result.Add((tabName, c));
            }
            Scan(editor.eventControls_sounds,  RDString.Get("editor.sounds"));
            Scan(editor.eventControls_actions, RDString.Get("editor.actions"));
            Scan(editor.eventControls_rooms,   RDString.Get("editor.rooms"));
            Scan(editor.eventControls_windows, RDString.Get("editor.windows"));
            foreach (var row in editor.eventControls_rows)
                if (row != null) Scan(row, RDString.Get("editor.rows"));
            foreach (var spr in editor.eventControls_sprites)
                if (spr != null) Scan(spr, RDString.Get("editor.sprites"));
            return result;
        }

        /// <summary>
        /// 链接信息类
        /// </summary>
        public class LinkInfo
        {
            public string url = "";
            public string text = "";
        }

        /// <summary>
        /// 处理富文本标签，返回处理后的文本和提取的链接
        /// </summary>
        public static string ProcessRichText(string rawText, out List<LinkInfo> links)
        {
            links = new List<LinkInfo>();
            if (string.IsNullOrEmpty(rawText)) return rawText;

            // 显示前300个字符用于调试
            string sample = rawText.Length > 300 ? rawText.Substring(0, 300) : rawText;
            Debug.Log($"[ProcessRichText] 原始文本长度: {rawText.Length}");
            Debug.Log($"[ProcessRichText] 文本样本: {sample}");

            string processed = rawText;

            // 1. 提取链接信息 - 使用更宽松的模式，支持单引号和双引号
            var linkPattern = @"<link\s*=\s*[""']([^""']+)[""']\s*>(.+?)</link>";
            var linkMatches = System.Text.RegularExpressions.Regex.Matches(
                processed,
                linkPattern,
                System.Text.RegularExpressions.RegexOptions.Singleline
            );

            Debug.Log($"[ProcessRichText] 找到 {linkMatches.Count} 个链接");

            foreach (System.Text.RegularExpressions.Match match in linkMatches)
            {
                Debug.Log($"[ProcessRichText] 找到链接: url={match.Groups[1].Value}, text={match.Groups[2].Value}");
                links.Add(new LinkInfo
                {
                    url = match.Groups[1].Value,
                    text = match.Groups[2].Value
                });
            }

            // 2. 替换链接标签为可读文本（带停顿标记）
            // 使用省略号作为停顿符，使用本地化的"链接"后缀
            string linkSuffix = RDString.Get("eam.link.suffix");
            processed = System.Text.RegularExpressions.Regex.Replace(
                processed,
                linkPattern,
                $"…$2{linkSuffix}…"
            );

            Debug.Log($"[ProcessRichText] 处理后文本长度: {processed.Length}");

            // 3. 移除其他富文本标签
            // 移除 <color> 标签（包括十六进制和命名颜色）
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"<color\s*=\s*[^>]+>", "");
            processed = System.Text.RegularExpressions.Regex.Replace(processed, @"</color>", "");

            // 移除常见格式标签
            var tagsToRemove = new[] { "b", "i", "u", "s", "size", "material", "quad", "sprite" };
            foreach (var tag in tagsToRemove)
            {
                processed = System.Text.RegularExpressions.Regex.Replace(
                    processed,
                    $@"<{tag}[^>]*>",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                processed = System.Text.RegularExpressions.Regex.Replace(
                    processed,
                    $@"</{tag}>",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
            }

            return processed;
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

            // 重置属性索引
            if (AccessLogic.Instance != null)
            {
                AccessLogic.Instance.ResetPropertySelection();
            }

            // 使用新的工具方法朗读事件信息
            ModUtils.AnnounceEventSelection(newControl.levelEvent);
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
    // 虚拟菜单激活时屏蔽游戏原生快捷键
    // ===================================================================================
    [HarmonyPatch(typeof(scnEditor), "get_userIsEditingAnInputField")]
    public static class VirtualMenuInputBlockPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (AccessLogic.IsVirtualMenuActive || AccessibilityBridge.IsEditing)
                __result = true;
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
    // 标签页切换时取消事件选择
    // ===================================================================================
    /// <summary>
    /// 标签页切换时取消所有事件选择
    /// </summary>
    [HarmonyPatch(typeof(scnEditor))]
    public static class TabSwitchPatch
    {
        [HarmonyPatch("ShowTabSection")]
        [HarmonyPostfix]
        public static void ShowTabSectionPostfix(scnEditor __instance)
        {
            if (__instance == null) return;

            // 切换标签页时取消所有事件选择
            __instance.DeselectAllEventControls(updateInspectorUI: false, sound: false);
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
    // 复制时支持虚拟选区替换
    // ===================================================================================
    // Ctrl+Shift+C 时，先用虚拟选区替换游戏内选区，再让游戏执行复制

    [HarmonyPatch(typeof(scnEditor), "Copy")]
    public static class CopyVirtualSelectionPatch
    {
        [HarmonyPrefix]
        public static void CopyPrefix(scnEditor __instance)
        {
            if (AccessLogic.Instance == null || __instance == null) return;

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shiftHeld) return;

            var sorted = AccessLogic.Instance.GetSortedVirtualSelection();
            if (sorted.Count == 0) return;

            // 用虚拟选区替换当前游戏内选区
            __instance.SelectEventControls(sorted);
        }
    }

    // ===================================================================================
    // 剪切时支持虚拟选区替换
    // ===================================================================================
    // Ctrl+Shift+X 时，先用虚拟选区替换游戏内选区，再让游戏执行剪切

    [HarmonyPatch(typeof(scnEditor), "Cut")]
    public static class CutVirtualSelectionPatch
    {
        [HarmonyPrefix]
        public static void CutPrefix(scnEditor __instance)
        {
            if (AccessLogic.Instance == null || __instance == null) return;

            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shiftHeld) return;

            var sorted = AccessLogic.Instance.GetSortedVirtualSelection();
            if (sorted.Count == 0) return;

            // 用虚拟选区替换当前游戏内选区
            __instance.SelectEventControls(sorted);
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
        [HarmonyPrefix]
        public static void PastePrefix(scnEditor __instance, bool onNextBar)
        {
            // 仅在 onNextBar=false 时取消选择
            if (onNextBar) return;
            if (__instance == null) return;

            // 取消选择所有事件
            __instance.DeselectAllEventControls(updateInspectorUI: false, sound: false);
        }

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

            // 更新 inspector 面板以持久化更改（防止取消选择时回退）
            if (firstControl != null)
            {
                __instance.inspectorPanelManager.GetCurrent()?.UpdateUI(firstControl.levelEvent);
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
            // 字段全名别名（供反射读取时使用）
            ["eam.settings.artistPermissionFileName"] = "艺术家授权文件",
            ["eam.settings.previewImageName"]         = "预览图",
            ["eam.settings.syringeIconName"]          = "注射器图标",
            ["eam.settings.previewSongName"]          = "预览歌曲",
            ["eam.settings.separate2PLevelFilename"]  = "独立双人关卡",
            ["eam.settings.rankMaxMistakes"]          = "评级最大失误数",
            ["eam.settings.rankDescription"]          = "评级描述",
            ["eam.editor.openRowEditor"]         = "正在打开轨道 {0} 属性编辑器",
            ["eam.sprite.editNotSupported"]      = "精灵编辑暂不支持",
            ["eam.event.jumpAndPlay"]            = "跳转到 {0} 并开始播放",
            ["eam.action.addRowOrSprite"]        = "请在 Rows 或 Sprites Tab 中添加轨道或精灵",
            ["eam.char.selectPrompt"]            = "选择角色，使用上下箭头导航，回车确认，Escape取消",
            ["eam.event.noTypesAvailable"]       = "当前 Tab 没有可用的事件类型",
            ["eam.event.selectPrompt"]           = "选择事件类型，使用上下箭头导航，回车确认，Escape取消",
            ["eam.grid.selectPrompt"]            = "选择网格精度，使用上下箭头导航，数字键快速选择，回车确认，自定义项可用Ctrl+回车编辑，Escape取消",
            ["eam.grid.item"]                    = "1/{0} 网格",
            ["eam.grid.custom"]                  = "1/{0} 自定义网格",
            ["eam.grid.custom.label"]            = "分母",
            ["eam.grid.custom.invalid"]          = "无效分母，请输入正整数",
            ["eam.event.createFailed"]           = "无法创建事件类型 {0}",
            ["eam.event.createError"]            = "创建事件失败",
            ["eam.event.created"]                = "已创建事件 {0}",
            ["eam.event.createdAndOpening"]      = "已创建事件 {0}，正在打开编辑器",
            ["eam.track.noAvailable"]            = "无轨道",
            ["eam.sprite.noAvailable"]           = "无精灵",
            ["eam.track.info"]                   = "轨道 {0} {1} {2}事件",
            ["eam.sprite.info"]                  = "精灵 {0} {1} {2}事件",
            ["eam.event.noAvailable"]            = "无事件",
            ["eam.event.noSelection"]            = "未选中任何事件",
            ["eam.event.mixedMoveBlocked"]       = "无法移动：选中的事件类型不一致",
            ["eam.event.commentNote"]            = "（注释事件）",
            ["eam.event.commentAt"]              = "{0} 的注释：{1}",
            ["eam.event.levelEndNote"]           = "（结束关卡）",
            ["eam.event.customMethodNote"]       = "（需要配置自定义方法）",
            ["eam.event.tagNote"]                = "（标签操作）",
            ["eam.track.added"]                  = "已添加轨道，角色 {0}",
            ["eam.track.addedAndOpening"]        = "已添加轨道，角色 {0}，正在打开编辑器",
            ["eam.sprite.added"]                 = "已添加精灵，角色 {0}",
            ["eam.confirm.changeRowType"]        = "切换轨道类型将删除轨道上的所有事件（{0}个），是否继续？",
            ["eam.error.roomFull"]               = "房间 {0} 已满，无法移动轨道",
            ["eam.error.helperNotFound"]         = "无法启动事件编辑器，请确保 RDEventEditorHelper.exe 存在",
            ["eam.helper.forceCancelled"]        = "已强制退出事件编辑器",
            ["eam.cursor.jump.title"]            = "跳转到位置",
            ["eam.cursor.jump.bar"]              = "小节",
            ["eam.cursor.jump.beat"]             = "拍",
            ["eam.cursor.jump.success"]          = "已跳转到 {0}",

            // 音效本地化（使用游戏的 enum.SoundEffect 格式）
            ["enum.SoundEffect.Shaker"]          = "摇铃",
            ["enum.SoundEffect.Kick"]            = "底鼓",
            ["enum.SoundEffect.Snare"]           = "军鼓",
            ["enum.SoundEffect.Hat"]             = "踩镲",
            ["enum.SoundEffect.Sizzle"]          = "吊镲",
            ["enum.SoundEffect.Cowbell"]         = "牛铃",
            ["enum.SoundEffect.Clap"]            = "拍手",
            ["enum.SoundEffect.Stick"]           = "鼓棒",

            // 属性快速调节
            ["eam.quickAdjust.noProperty"]       = "未选择属性",
            ["eam.quickAdjust.notAdjustable"]    = "当前事件没有可调节属性",

            // 布尔值
            ["eam.bool.enabled"]                 = "启用",
            ["eam.bool.disabled"]                = "禁用",

            // 富文本和链接相关
            ["eam.link.suffix"]                  = " 链接",
            ["eam.link.menu.title"]              = "链接选择菜单",
            ["eam.link.menu.count"]              = "共 {0} 个链接",
            ["eam.link.opening"]                 = "正在打开链接：{0}",
            ["eam.link.cancelled"]               = "已取消",
            // 虚拟选区
            ["eam.vsel.added"]                   = "已选择{0}",
            ["eam.vsel.removed"]                 = "未选择{0}",
            ["eam.vsel.cleared"]                 = "清空虚拟选区",
            ["eam.vsel.empty"]                   = "虚拟选区为空",
            ["eam.vsel.options"]                = "虚拟选区选项",
            ["eam.vsel.selectEvents"]           = "选中虚拟选区中的事件",
            ["eam.vsel.addSelected"]            = "将当前选区添加到虚拟选区",
            ["eam.vsel.selectedCount"]          = "已选中{0}个事件",
            ["eam.vsel.addedCount"]             = "已添加{0}个事件到虚拟选区",
            // 事件链
            ["eam.chain.noChains"]               = "无可用事件链",
            ["eam.chain.selectPrompt"]           = "选择事件链，上下箭头导航，左右箭头调节倍速，回车确认，Escape取消",
            ["eam.chain.inserted"]               = "已插入事件链 {0}，共 {1} 个事件",
            ["eam.chain.saved"]                  = "已保存事件链：{0}",
            ["eam.chain.saveFailed"]             = "保存事件链失败：{0}",
            ["eam.chain.loadFailed"]             = "加载事件链失败：{0}",
            ["eam.chain.invalidName"]            = "无效的事件链名称",
            ["eam.chain.nameLabel"]              = "事件链名称",
            ["eam.chain.noLevel"]                = "请先打开一个关卡",
            ["eam.chain.skippedEvents"]          = "{0} 个事件因类型不存在被跳过",
            ["eam.chain.speed"]                  = "{0}x 倍速",
            ["eam.bpmcalc.hint"]                 = "请跟着音乐的节拍敲击空格键16次",
            // 条件系统
            ["eam.conditional.menuHeader"]       = "{0} 的条件",
            ["eam.conditional.noConditionals"]   = "暂无条件",
            ["eam.conditional.stateNone"]        = "未设置",
            ["eam.conditional.stateActive"]      = "已激活",
            ["eam.conditional.stateNegated"]     = "已取反",
            ["eam.conditional.activated"]        = "已激活 {0}",
            ["eam.conditional.negated"]          = "已取反 {0}",
            ["eam.conditional.removed"]          = "已移除 {0}",
            ["eam.conditional.noCondEmpty"]      = "暂无条件，按 N 新建",
            ["eam.conditional.openCreate"]       = "正在打开新建条件编辑器",
            ["eam.conditional.openEdit"]         = "正在打开条件编辑器",
            ["eam.conditional.created"]          = "已新建条件 {0}",
            ["eam.conditional.edited"]           = "已修改条件 {0}",
            ["eam.conditional.deleted"]          = "已删除条件 {0}",
            ["eam.conditional.cannotEditGlobal"] = "全局条件不可编辑",
            ["eam.conditional.cannotDeleteGlobal"]   = "全局条件不可删除",
            ["eam.conditional.deleteConfirmNoUsage"] = "确认删除条件 {0}？再按 D 确认，Escape 取消",
            ["eam.conditional.deleteConfirmSingle"]  = "确认删除条件 {0}？已有 1 个事件使用了该条件。再按 D 确认，Escape 取消",
            ["eam.conditional.deleteConfirm"]        = "确认删除条件 {0}？已有 {1} 个事件使用了该条件。再按 D 确认，Escape 取消",
            ["eam.conditional.tagLabel"]         = "标签",
            ["eam.conditional.descriptionLabel"] = "描述",
            ["eam.playerMode.one"]               = "单人模式",
            ["eam.playerMode.two"]               = "双人模式",
            ["eam.tagMode.enabled"]              = "标签模式已开启",
            ["eam.tagMode.disabled"]             = "标签模式已关闭",
            ["eam.tag.noTag"]                    = "无标签",
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
            // 字段全名别名（供反射读取时使用）
            ["eam.settings.artistPermissionFileName"] = "Artist Permission File",
            ["eam.settings.previewImageName"]         = "Preview Image",
            ["eam.settings.syringeIconName"]          = "Syringe Icon",
            ["eam.settings.previewSongName"]          = "Preview Song",
            ["eam.settings.separate2PLevelFilename"]  = "Separate 2P Level",
            ["eam.settings.rankMaxMistakes"]          = "Rank Max Mistakes",
            ["eam.settings.rankDescription"]          = "Rank Descriptions",
            ["eam.editor.openRowEditor"]         = "Opening property editor for track {0}",
            ["eam.sprite.editNotSupported"]      = "Sprite editing not yet supported",
            ["eam.event.jumpAndPlay"]            = "Jump to {0} and play",
            ["eam.action.addRowOrSprite"]        = "Switch to Rows or Sprites tab to add a track or sprite",
            ["eam.char.selectPrompt"]            = "Select character, arrow keys to navigate, Enter to confirm, Escape to cancel",
            ["eam.event.noTypesAvailable"]       = "No event types available in current tab",
            ["eam.event.selectPrompt"]           = "Select event type, arrow keys to navigate, Enter to confirm, Escape to cancel",
            ["eam.grid.selectPrompt"]            = "Select grid size, arrow keys to navigate, number keys to quick-select, Enter to confirm, Ctrl+Enter to edit custom value, Escape to cancel",
            ["eam.grid.item"]                    = "1/{0} grid",
            ["eam.grid.custom"]                  = "1/{0} custom grid",
            ["eam.grid.custom.label"]            = "Denominator",
            ["eam.grid.custom.invalid"]          = "Invalid denominator, please enter a positive integer",
            ["eam.event.createFailed"]           = "Cannot create event type {0}",
            ["eam.event.createError"]            = "Event creation failed",
            ["eam.event.created"]                = "Event {0} created",
            ["eam.event.createdAndOpening"]      = "Event {0} created, opening editor",
            ["eam.track.noAvailable"]            = "No tracks available",
            ["eam.sprite.noAvailable"]           = "No sprites available",
            ["eam.track.info"]                   = "Track {0} {1} {2} events",
            ["eam.sprite.info"]                  = "Sprite {0} {1} {2} events",
            ["eam.event.noAvailable"]            = "No events available",
            ["eam.event.noSelection"]            = "No events selected",
            ["eam.event.mixedMoveBlocked"]       = "Cannot move: selected events have mixed positioning types",
            ["eam.event.commentNote"]            = "(Comment event)",
            ["eam.event.commentAt"]              = "Comment in {0}: {1}",
            ["eam.event.levelEndNote"]           = "(Level end)",
            ["eam.event.customMethodNote"]       = "(Requires custom method)",
            ["eam.event.tagNote"]                = "(Tag operation)",
            ["eam.track.added"]                  = "Track added, character: {0}",
            ["eam.track.addedAndOpening"]        = "Track added, character: {0}, opening editor",
            ["eam.sprite.added"]                 = "Sprite added, character: {0}",
            ["eam.confirm.changeRowType"]        = "Changing row type will delete all {0} events on this track. Continue?",
            ["eam.error.roomFull"]               = "Room {0} is full, cannot move track",
            ["eam.error.helperNotFound"]         = "Cannot start event editor. Please ensure RDEventEditorHelper.exe exists",
            ["eam.helper.forceCancelled"]        = "Event editor forcefully closed",
            ["eam.cursor.jump.title"]            = "Jump to Position",
            ["eam.cursor.jump.bar"]              = "Bar",
            ["eam.cursor.jump.beat"]             = "Beat",
            ["eam.cursor.jump.success"]          = "Jumped to {0}",

            // 音效本地化（使用游戏的 enum.SoundEffect 格式）
            ["enum.SoundEffect.Shaker"]          = "Shaker",
            ["enum.SoundEffect.Kick"]            = "Kick",
            ["enum.SoundEffect.Snare"]           = "Snare",
            ["enum.SoundEffect.Hat"]             = "Hat",
            ["enum.SoundEffect.Sizzle"]          = "Sizzle",
            ["enum.SoundEffect.Cowbell"]         = "Cowbell",
            ["enum.SoundEffect.Clap"]            = "Clap",
            ["enum.SoundEffect.Stick"]           = "Stick",

            // 属性快速调节
            ["eam.quickAdjust.noProperty"]       = "No property selected",
            ["eam.quickAdjust.notAdjustable"]    = "Current event has no adjustable properties",

            // 布尔值
            ["eam.bool.enabled"]                 = "Enabled",
            ["eam.bool.disabled"]                = "Disabled",

            // Rich text and link related
            ["eam.link.suffix"]                  = " Link",
            ["eam.link.menu.title"]              = "Link Selection Menu",
            ["eam.link.menu.count"]              = "{0} links in total",
            ["eam.link.opening"]                 = "Opening link: {0}",
            ["eam.link.cancelled"]               = "Cancelled",
            // Virtual selection
            ["eam.vsel.added"]                   = "Selected {0}",
            ["eam.vsel.removed"]                 = "Deselected {0}",
            ["eam.vsel.cleared"]                 = "Virtual selection cleared",
            ["eam.vsel.empty"]                   = "Virtual selection is empty",
            ["eam.vsel.options"]                = "Virtual Selection Options",
            ["eam.vsel.selectEvents"]           = "Select events in virtual selection",
            ["eam.vsel.addSelected"]            = "Add current selection to virtual selection",
            ["eam.vsel.selectedCount"]          = "Selected {0} events",
            ["eam.vsel.addedCount"]             = "Added {0} events to virtual selection",
            // Event Chains
            ["eam.chain.noChains"]               = "No event chains available",
            ["eam.chain.selectPrompt"]           = "Select event chain, Up/Down to navigate, Left/Right to adjust speed, Enter to confirm, Escape to cancel",
            ["eam.chain.inserted"]               = "Inserted event chain {0}, {1} events",
            ["eam.chain.saved"]                  = "Event chain saved: {0}",
            ["eam.chain.saveFailed"]             = "Failed to save event chain: {0}",
            ["eam.chain.loadFailed"]             = "Failed to load event chain: {0}",
            ["eam.chain.invalidName"]            = "Invalid event chain name",
            ["eam.chain.nameLabel"]              = "Event Chain Name",
            ["eam.chain.noLevel"]                = "Please open a level first",
            ["eam.chain.skippedEvents"]          = "{0} events skipped due to missing type",
            ["eam.chain.speed"]                  = "{0}x speed",
            ["eam.bpmcalc.hint"]                 = "Tap the spacebar 16 times to the beat of the music",
            // Conditional system
            ["eam.conditional.menuHeader"]       = "{0}'s conditions",
            ["eam.conditional.noConditionals"]   = "No conditionals",
            ["eam.conditional.stateNone"]        = "not set",
            ["eam.conditional.stateActive"]      = "active",
            ["eam.conditional.stateNegated"]     = "negated",
            ["eam.conditional.activated"]        = "Activated {0}",
            ["eam.conditional.negated"]          = "Negated {0}",
            ["eam.conditional.removed"]          = "Removed {0}",
            ["eam.conditional.noCondEmpty"]      = "No conditions. Press N to create one",
            ["eam.conditional.openCreate"]       = "Opening condition creator",
            ["eam.conditional.openEdit"]         = "Opening condition editor",
            ["eam.conditional.created"]          = "Created condition {0}",
            ["eam.conditional.edited"]           = "Edited condition {0}",
            ["eam.conditional.deleted"]          = "Deleted condition {0}",
            ["eam.conditional.cannotEditGlobal"] = "Global conditions cannot be edited",
            ["eam.conditional.cannotDeleteGlobal"]   = "Global conditions cannot be deleted",
            ["eam.conditional.deleteConfirmNoUsage"] = "Delete condition {0}? Press D again to confirm, Escape to cancel",
            ["eam.conditional.deleteConfirmSingle"]  = "Delete condition {0}? 1 event uses this condition. Press D again to confirm, Escape to cancel",
            ["eam.conditional.deleteConfirm"]        = "Delete condition {0}? {1} events use this condition. Press D again to confirm, Escape to cancel",
            ["eam.conditional.tagLabel"]         = "Tag",
            ["eam.conditional.descriptionLabel"] = "Description",
            ["eam.playerMode.one"]               = "Single player mode",
            ["eam.playerMode.two"]               = "Two player mode",
            ["eam.tagMode.enabled"]              = "Tag mode enabled",
            ["eam.tagMode.disabled"]             = "Tag mode disabled",
            ["eam.tag.noTag"]                    = "No tag",
        };

        private static readonly Dictionary<string, string> _fr = new Dictionary<string, string>
        {
            ["eam.barbeat.format"]              = "Mesure {0} Temps {1}",
            ["eam.barbeat.beatOnly"]             = "Temps {0}",
            ["eam.barbeat.barOnly"]              = "Mesure {0}",
            ["eam.cursor.suffix"]                = " Curseur d'édition",
            ["eam.cursor.snapPrefix"]            = "Accroché à ",
            ["eam.action.cancelled"]             = "Annulé",
            ["eam.check.checked"]                = "Coché",
            ["eam.check.unchecked"]              = "Non coché",
            ["eam.input.activated"]              = "Champ de saisie activé",
            ["eam.editor.openPropEditor"]        = "Ouverture de l'éditeur de propriétés",
            ["eam.editor.openTrackEditor"]       = "Ouverture de l'éditeur de piste",
            ["eam.editor.openEventEditor"]       = "Ouverture de l'éditeur de propriétés pour {0}",
            ["eam.editor.openSettingsEditor"]    = "Ouverture de l'éditeur de paramètres du niveau",
            ["eam.settings.song"]                = "Titre",
            ["eam.settings.artist"]              = "Artiste",
            ["eam.settings.author"]              = "Auteur",
            ["eam.settings.description"]         = "Description",
            ["eam.settings.tags"]                = "Étiquettes",
            ["eam.settings.difficulty"]          = "Difficulté",
            ["eam.settings.seizureWarning"]      = "Avertissement épilepsie",
            ["eam.settings.canBePlayedOn"]       = "Modes de jeu",
            ["eam.settings.specialArtistType"]   = "Type d'artiste spécial",
            ["eam.settings.artistPermission"]    = "Fichier d'autorisation artiste",
            ["eam.settings.artistLinks"]         = "Liens artiste",
            ["eam.settings.previewImage"]        = "Image de prévisualisation",
            ["eam.settings.syringeIcon"]         = "Icône seringue",
            ["eam.settings.previewSong"]         = "Chanson de prévisualisation",
            ["eam.settings.previewSongStartTime"]= "Début de la prévisualisation",
            ["eam.settings.previewSongDuration"] = "Durée de la prévisualisation",
            ["eam.settings.songLabelHue"]        = "Teinte de l'étiquette",
            ["eam.settings.songLabelGrayscale"]  = "Niveaux de gris de l'étiquette",
            ["eam.settings.levelVolume"]         = "Volume du niveau",
            ["eam.settings.firstBeatBehavior"]   = "Comportement au premier temps",
            ["eam.settings.multiplayerAppearance"]= "Apparence multijoueur",
            ["eam.settings.separate2PLevel"]     = "Niveau 2J séparé",
            // Alias de noms de champs complets (utilisés par réflexion)
            ["eam.settings.artistPermissionFileName"] = "Fichier d'autorisation artiste",
            ["eam.settings.previewImageName"]         = "Image de prévisualisation",
            ["eam.settings.syringeIconName"]          = "Icône seringue",
            ["eam.settings.previewSongName"]          = "Chanson de prévisualisation",
            ["eam.settings.separate2PLevelFilename"]  = "Niveau 2J séparé",
            ["eam.settings.rankMaxMistakes"]          = "Nb max d'erreurs (classement)",
            ["eam.settings.rankDescription"]          = "Descriptions du classement",
            ["eam.editor.openRowEditor"]         = "Ouverture de l'éditeur de propriétés pour la piste {0}",
            ["eam.sprite.editNotSupported"]      = "Édition de sprite non prise en charge",
            ["eam.event.jumpAndPlay"]            = "Aller à {0} et lire",
            ["eam.action.addRowOrSprite"]        = "Allez dans l'onglet Pistes ou Sprites pour ajouter une piste ou un sprite",
            ["eam.char.selectPrompt"]            = "Sélectionnez un personnage — flèches pour naviguer, Entrée pour confirmer, Échap pour annuler",
            ["eam.event.noTypesAvailable"]       = "Aucun type d'événement disponible dans l'onglet actuel",
            ["eam.event.selectPrompt"]           = "Sélectionnez un type d'événement — flèches pour naviguer, Entrée pour confirmer, Échap pour annuler",
            ["eam.grid.selectPrompt"]            = "Sélectionnez la taille de grille — flèches pour naviguer, chiffres pour sélection rapide, Entrée pour confirmer, Ctrl+Entrée pour valeur personnalisée, Échap pour annuler",
            ["eam.grid.item"]                    = "Grille 1/{0}",
            ["eam.grid.custom"]                  = "Grille personnalisée 1/{0}",
            ["eam.grid.custom.label"]            = "Dénominateur",
            ["eam.grid.custom.invalid"]          = "Dénominateur invalide, veuillez saisir un entier positif",
            ["eam.event.createFailed"]           = "Impossible de créer le type d'événement {0}",
            ["eam.event.createError"]            = "Échec de la création d'événement",
            ["eam.event.created"]                = "Événement {0} créé",
            ["eam.event.createdAndOpening"]      = "Événement {0} créé, ouverture de l'éditeur",
            ["eam.track.noAvailable"]            = "Aucune piste disponible",
            ["eam.sprite.noAvailable"]           = "Aucun sprite disponible",
            ["eam.track.info"]                   = "Piste {0} {1} — {2} événements",
            ["eam.sprite.info"]                  = "Sprite {0} {1} — {2} événements",
            ["eam.event.noAvailable"]            = "Aucun événement disponible",
            ["eam.event.noSelection"]            = "Aucun événement sélectionné",
            ["eam.event.mixedMoveBlocked"]       = "Déplacement impossible : types de positionnement mixtes",
            ["eam.event.commentNote"]            = "(Événement commentaire)",
            ["eam.event.commentAt"]              = "Commentaire dans {0} : {1}",
            ["eam.event.levelEndNote"]           = "(Fin du niveau)",
            ["eam.event.customMethodNote"]       = "(Nécessite une méthode personnalisée)",
            ["eam.event.tagNote"]                = "(Opération d'étiquette)",
            ["eam.track.added"]                  = "Piste ajoutée, personnage : {0}",
            ["eam.track.addedAndOpening"]        = "Piste ajoutée, personnage : {0}, ouverture de l'éditeur",
            ["eam.sprite.added"]                 = "Sprite ajouté, personnage : {0}",
            ["eam.confirm.changeRowType"]        = "Changer le type de piste supprimera les {0} événements de cette piste. Continuer ?",
            ["eam.error.roomFull"]               = "La salle {0} est pleine, impossible de déplacer la piste",
            ["eam.error.helperNotFound"]         = "Impossible de démarrer l'éditeur d'événements. Vérifiez que RDEventEditorHelper.exe est présent",
            ["eam.helper.forceCancelled"]        = "Éditeur d'événements fermé de force",
            ["eam.cursor.jump.title"]            = "Aller à la position",
            ["eam.cursor.jump.bar"]              = "Mesure",
            ["eam.cursor.jump.beat"]             = "Temps",
            ["eam.cursor.jump.success"]          = "Saut vers {0}",

            // Effets sonores
            ["enum.SoundEffect.Shaker"]          = "Shaker",
            ["enum.SoundEffect.Kick"]            = "Grosse caisse",
            ["enum.SoundEffect.Snare"]           = "Caisse claire",
            ["enum.SoundEffect.Hat"]             = "Charleston",
            ["enum.SoundEffect.Sizzle"]          = "Cymbale ride",
            ["enum.SoundEffect.Cowbell"]         = "Cowbell",
            ["enum.SoundEffect.Clap"]            = "Clap",
            ["enum.SoundEffect.Stick"]           = "Baguette",

            // Ajustement rapide de propriété
            ["eam.quickAdjust.noProperty"]       = "Aucune propriété sélectionnée",
            ["eam.quickAdjust.notAdjustable"]    = "Cet événement n'a pas de propriétés ajustables",

            // Valeurs booléennes
            ["eam.bool.enabled"]                 = "Activé",
            ["eam.bool.disabled"]                = "Désactivé",

            // Texte enrichi et liens
            ["eam.link.suffix"]                  = " Lien",
            ["eam.link.menu.title"]              = "Menu de sélection de liens",
            ["eam.link.menu.count"]              = "{0} liens au total",
            ["eam.link.opening"]                 = "Ouverture du lien : {0}",
            ["eam.link.cancelled"]               = "Annulé",
            // Sélection virtuelle
            ["eam.vsel.added"]                   = "{0} sélectionné",
            ["eam.vsel.removed"]                 = "{0} désélectionné",
            ["eam.vsel.cleared"]                 = "Sélection virtuelle effacée",
            ["eam.vsel.empty"]                   = "La sélection virtuelle est vide",
            ["eam.vsel.options"]                = "Options de sélection virtuelle",
            ["eam.vsel.selectEvents"]           = "Sélectionner les événements de la sélection virtuelle",
            ["eam.vsel.addSelected"]            = "Ajouter la sélection actuelle à la sélection virtuelle",
            ["eam.vsel.selectedCount"]          = "{0} événements sélectionnés",
            ["eam.vsel.addedCount"]             = "{0} événements ajoutés à la sélection virtuelle",
            // Chaînes d'événements
            ["eam.chain.noChains"]               = "Aucune chaîne d'événements disponible",
            ["eam.chain.selectPrompt"]           = "Sélectionnez une chaîne d'événements — Haut/Bas pour naviguer, Gauche/Droite pour régler la vitesse, Entrée pour confirmer, Échap pour annuler",
            ["eam.chain.inserted"]               = "Chaîne d'événements {0} insérée, {1} événements",
            ["eam.chain.saved"]                  = "Chaîne d'événements sauvegardée : {0}",
            ["eam.chain.saveFailed"]             = "Échec de la sauvegarde de la chaîne : {0}",
            ["eam.chain.loadFailed"]             = "Échec du chargement de la chaîne : {0}",
            ["eam.chain.invalidName"]            = "Nom de chaîne d'événements invalide",
            ["eam.chain.nameLabel"]              = "Nom de la chaîne",
            ["eam.chain.noLevel"]                = "Veuillez d'abord ouvrir un niveau",
            ["eam.chain.skippedEvents"]          = "{0} événements ignorés (type manquant)",
            ["eam.chain.speed"]                  = "Vitesse {0}x",
            ["eam.bpmcalc.hint"]                 = "Appuyez 16 fois sur la barre d'espace en rythme avec la musique",
            // Système de conditions
            ["eam.conditional.menuHeader"]       = "Conditions de {0}",
            ["eam.conditional.noConditionals"]   = "Aucune condition",
            ["eam.conditional.stateNone"]        = "non définie",
            ["eam.conditional.stateActive"]      = "active",
            ["eam.conditional.stateNegated"]     = "inversée",
            ["eam.conditional.activated"]        = "{0} activée",
            ["eam.conditional.negated"]          = "{0} inversée",
            ["eam.conditional.removed"]          = "{0} supprimée",
            ["eam.conditional.noCondEmpty"]      = "Aucune condition. Appuyez sur N pour en créer une",
            ["eam.conditional.openCreate"]       = "Ouverture du créateur de condition",
            ["eam.conditional.openEdit"]         = "Ouverture de l'éditeur de condition",
            ["eam.conditional.created"]          = "Condition {0} créée",
            ["eam.conditional.edited"]           = "Condition {0} modifiée",
            ["eam.conditional.deleted"]          = "Condition {0} supprimée",
            ["eam.conditional.cannotEditGlobal"] = "Les conditions globales ne peuvent pas être modifiées",
            ["eam.conditional.cannotDeleteGlobal"]   = "Les conditions globales ne peuvent pas être supprimées",
            ["eam.conditional.deleteConfirmNoUsage"] = "Supprimer la condition {0} ? Appuyez à nouveau sur D pour confirmer, Échap pour annuler",
            ["eam.conditional.deleteConfirmSingle"]  = "Supprimer la condition {0} ? 1 événement utilise cette condition. Appuyez à nouveau sur D pour confirmer, Échap pour annuler",
            ["eam.conditional.deleteConfirm"]        = "Supprimer la condition {0} ? {1} événements utilisent cette condition. Appuyez à nouveau sur D pour confirmer, Échap pour annuler",
            ["eam.conditional.tagLabel"]         = "Étiquette",
            ["eam.conditional.descriptionLabel"] = "Description",
            ["eam.playerMode.one"]               = "Mode un joueur",
            ["eam.playerMode.two"]               = "Mode deux joueurs",
            ["eam.tagMode.enabled"]              = "Mode étiquettes activé",
            ["eam.tagMode.disabled"]             = "Mode étiquettes désactivé",
            ["eam.tag.noTag"]                    = "Sans étiquette",
        };

        // Détecte si le jeu tourne en français en interrogeant le moteur de localisation du jeu
        // lui-même (RDString.Get), ce qui garantit la cohérence avec le choix de langue in-game
        // et non avec la locale du système d'exploitation.
        private static bool? _isFrench;
        private static bool IsFrench
        {
            get
            {
                if (_isFrench == null)
                    _isFrench = RDString.Get("editor.continue") == "Continuer";
                return _isFrench.Value;
            }
        }

        [HarmonyPrefix]
        public static bool GetPrefix(string key, ref string __result)
        {
            // 性能：非 eam./sound. key 仅多一次 StartsWith 检查（< 10ns）
            if (key == null || (!key.StartsWith("eam.") && !key.StartsWith("sound."))) return true;
            var dict = RDString.isChinese ? _zh : (IsFrench ? _fr : _en);
            __result = dict.TryGetValue(key, out string val) ? val : key;
            return false; // 拦截原方法
        }
    }

}