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
    // 它的唯一任务就是活着把监听器挂上去，哪怕自己随后被销毁，监听器也是静态的，不会死。
    // ===================================================================================
    [BepInPlugin("com.hzt.rd-editor-access", "RDEditorAccess", "0.2")]
    public class EditorAccess : BaseUnityPlugin
    {
        public void Awake()
        {
            Logger.LogInfo(">>> 加载器启动 (Loader Awake)");

            // 使用静态事件订阅。
            // 即使 EditorAccess 这个实例被销毁，这个静态方法的订阅依然存在于内存中！
            SceneManager.sceneLoaded += StaticOnSceneLoaded;
            var harmoney = new Harmony("com.hzt.rd-editor-access"");
                harmoney.PatchAll();
        }

        public void OnDestroy()
        {
            // 这里千万不要取消订阅 SceneManager.sceneLoaded！
            // 因为游戏初始化时会误杀我们，如果取消订阅，后面就没法复活了。
            Logger.LogWarning(">>> 加载器被销毁 (这很正常，只要静态监听器还在就行)");
        }

        // 静态监听方法：这是真正的“不死鸟”
        private static void StaticOnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 只有当加载的场景不是空的，我们才开始工作
            if (string.IsNullOrEmpty(scene.name)) return;

            Console.WriteLine($"[RDEditorAccess] 场景加载检测: {scene.name}");

            // 检查逻辑核心是否已经存在，避免重复创建
            if (AccessLogic.Instance != null)
            {
                return;
            }

            // 创建一个新的游戏对象来承载逻辑
            GameObject logicObj = new GameObject("RDEditorAccess_Logic");
            AccessLogic logic = logicObj.AddComponent<AccessLogic>();

            // 核心保活：这个新生成的对象，是在场景加载后生成的，属于“这一代”的幸存者
            DontDestroyOnLoad(logicObj);

            Console.WriteLine("[RDEditorAccess] 核心逻辑已注入 (Logic Injected)");
        }
    }

    // ===================================================================================
    // 第二部分：核心逻辑 (Worker)
    // 这里包含你所有的原始功能。它继承自 MonoBehaviour，由上面的静态方法动态创建。
    // ===================================================================================
    public class AccessLogic : MonoBehaviour
    {
        // 单例引用，方便检查是否已存在
        public static AccessLogic Instance { get; private set; }

        // 记录上一次朗读的对象，避免重复朗读
        private GameObject lastSelectedObj;

        // 记录上一次选中的时间轴事件
        private LevelEventControl_Base lastSelectedTimelineEvent;

        private float debugTimer = 0f;

        public void Awake()
        {
            Instance = this;
            Debug.Log("无障碍核心逻辑已启动 (Logic Awake)");
        }

        public void OnDestroy()
        {
            // 只有当游戏彻底关闭，或者我们主动销毁时才会触发
            if (Instance == this) Instance = null;
        }

        public void Update()
        {
            // --- 心跳检测与调试 ---
            debugTimer += Time.unscaledDeltaTime;
            if (debugTimer > 5f)
            {
                // Debug.Log($"Mod 运行中... 场景: {SceneManager.GetActiveScene().name}");
                debugTimer = 0f;
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                LogDiagnostics();
            }

            // --- 安全检查 ---
            // 如果编辑器核心实例还没准备好，暂停执行
            if (scnEditor.instance == null) return;

            // --- 业务逻辑 ---

            // 快捷键测试
            if (Input.GetKeyDown(KeyCode.F10))
            {
                scnEditor.instance.MenuButtonClick();
            }

            // 1. 免责声明弹窗导航
            if (scnEditor.instance.copyrightPopup != null && scnEditor.instance.copyrightPopup.activeInHierarchy)
            {
                HandleGeneralUINavigation(scnEditor.instance.copyrightPopup, "免责声明");
                return; // 如果弹窗存在，阻止后续逻辑（模态对话框逻辑）
            }

            // 2. 设置菜单导航
            if (scnEditor.instance.settingsMenu != null && scnEditor.instance.settingsMenu.gameObject.activeInHierarchy)
            {
                HandleGeneralUINavigation(scnEditor.instance.settingsMenu.gameObject, "设置菜单");
                return;
            }

            // 3. 顶部下拉菜单导航
            if (scnEditor.instance.mainMenu != null && scnEditor.instance.mainMenu.activeInHierarchy)
            {
                HandleGeneralUINavigation(scnEditor.instance.mainMenu, "下拉菜单");
                return;
            }

            // 4. 时间轴导航 (当没有菜单打开时)
            HandleTimelineNavigation();
        }

        // ===================================================================================
        // 核心功能区域：通用 UI 导航逻辑
        // ===================================================================================

        /// <summary>
        /// 处理通用的 UI 导航逻辑
        /// </summary>
        /// <param name="rootObject">要搜索 UI 控件的根父物体（例如整个菜单面板）</param>
        /// <param name="menuName">当前菜单名称（用于调试或提示）</param>
        private void HandleGeneralUINavigation(GameObject rootObject, string menuName)
        {
            if (rootObject == null) return;

            // 1. 查找所有可见的 UI 元素 (Graphic 是 Text, Image 的基类)
            //    我们不使用 Selectable，因为我们也想读取纯文本标签
            var allControls = rootObject.GetComponentsInChildren<Graphic>()
                .Where(g => g.gameObject.activeInHierarchy) // 只找看得见的
                .ToList();

            if (allControls.Count == 0) return;

            // 2. 对控件进行视觉排序 (从上到下，从左到右)
            //    这对无障碍非常重要，保证导航顺序符合视觉逻辑
            allControls.Sort((a, b) =>
            {
                var posA = a.transform.position;
                var posB = b.transform.position;

                // 先比较 Y 轴 (注意：屏幕上方 Y 值大，所以 B compare A 才是从上到下)
                int yComparison = posB.y.CompareTo(posA.y);
                if (yComparison != 0) return yComparison;

                // 如果 Y 轴相同（在同一行），则比较 X 轴（从左到右）
                return posA.x.CompareTo(posB.x);
            });

            // 3. 获取当前的事件系统
            var targetEventSystem = scnEditor.instance.eventSystem;
            // 容错：如果游戏自己的 EventSystem 没激活，尝试获取全局当前的
            if (targetEventSystem == null || !targetEventSystem.gameObject.activeInHierarchy)
                targetEventSystem = EventSystem.current;

            if (targetEventSystem == null) return;

            // 4. 确定当前焦点在列表中的位置
            var currentObj = targetEventSystem.currentSelectedGameObject;
            int currentIndex = -1;

            if (currentObj != null)
            {
                // 查找当前选中的物体是否在我们的列表里
                currentIndex = allControls.FindIndex(s => s.gameObject == currentObj);

                // 如果没找到（可能焦点在列表外的某个角落），但我们之前记录过“假焦点”（lastSelectedObj），尝试对齐
                if (currentIndex == -1 && lastSelectedObj != null)
                {
                    currentIndex = allControls.FindIndex(s => s.gameObject == lastSelectedObj);
                }
            }

            // 如果当前没有任何选中项，默认选中第一个
            if (currentIndex == -1)
            {
                if (allControls.Count > 0) SelectUIElement(allControls[0], targetEventSystem);
                return;
            }

            // 5. 处理输入逻辑
            int direction = 0;
            bool isTab = false; // 标记是否是 Tab 键模式

            // 方向键：逐个遍历（包括纯文本）
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow)) direction = -1;
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow)) direction = 1;
            // Tab 键：智能跳转（只跳到可交互的控件）
            else if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.LeftShift)) direction = -1; // Shift+Tab 反向
                else direction = 1;
                isTab = true;
            }

            // 6. 导航核心算法
            if (direction != 0)
            {
                int newIndex = currentIndex;

                if (!isTab)
                {
                    // === 普通模式 (方向键) ===
                    // 简单地移动索引，无论是文本还是按钮都停下来读
                    newIndex += direction;
                }
                else
                {
                    // === 智能模式 (Tab) ===
                    // 需要跳过中间那些不能交互的纯文本 (Graphic without Selectable)
                    int targetIndex = newIndex;
                    while (true)
                    {
                        targetIndex += direction; // 探路指针前进一步

                        // 边界检查：防止探路指针跑出数组范围
                        if (targetIndex >= 0 && targetIndex < allControls.Count)
                        {
                            // 检查该位置是否有 Selectable 组件 (是否可交互)
                            if (allControls[targetIndex].GetComponent<Selectable>() != null)
                            {
                                // 找到了！这是一个按钮或输入框，这是我们新的落脚点
                                newIndex = targetIndex;
                                break;
                            }
                            // 如果是纯文本，循环继续，继续往下找...
                        }
                        else
                        {
                            // 撞墙了！已经搜寻到列表尽头还是没找到
                            // 强制退出循环，避免死锁
                            break;
                        }
                    }
                }

                // 循环列表处理 (Looping)
                // 如果超出末尾，回到开头；如果小于0，去到末尾
                if (newIndex >= allControls.Count) newIndex = 0;
                if (newIndex < 0) newIndex = allControls.Count - 1;

                // 执行选中逻辑
                SelectUIElement(allControls[newIndex], targetEventSystem);
            }

            // 7. 确认键交互逻辑
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                // 获取当前位置的 Selectable 组件
                var currentGraphic = allControls[currentIndex];
                var item = currentGraphic.GetComponent<Selectable>();

                if (item != null && item.interactable)
                {
                    // 模式匹配：根据组件的具体类型执行不同操作
                    if (item is Button btn)
                    {
                        btn.onClick.Invoke(); // 点击按钮
                    }
                    else if (item is Toggle tgl)
                    {
                        tgl.isOn = !tgl.isOn; // 切换开关
                        // 切换后最好立刻朗读一下新状态
                        Narration.Say(tgl.isOn ? "已开启" : "已关闭", NarrationCategory.Notification);
                    }
                    else if (item is InputField input)
                    {
                        input.ActivateInputField(); // 激活输入框
                        Narration.Say("输入框已激活", NarrationCategory.Notification);
                    }
                }
            }
        }

        /// <summary>
        /// 执行选中某个 UI 元素的动作（设置焦点 + 朗读）
        /// </summary>
        private void SelectUIElement(Graphic element, EventSystem es)
        {
            if (element == null) return;

            // 尝试获取可交互组件
            var selectableComponent = element.GetComponent<Selectable>();

            // 1. 设置 Unity 系统焦点
            // 只有当它真的是一个按钮/输入框时，我们才告诉 EventSystem 去选中它。
            // 纯文本 (Graphic) 没有 Select 方法，强行调用会报错或没反应。
            if (selectableComponent != null && es != null)
            {
                selectableComponent.Select();
                es.SetSelectedGameObject(selectableComponent.gameObject);
            }
            else
            {
                // 如果是纯文本，为了配合方向键浏览，我们不设置 EventSystem 的焦点（因为那是给交互用的），
                // 但我们要假装它被选中了，以便下一次按方向键时知道从哪里开始。
                // (注意：这里我们通过 HandleGeneralUINavigation 里的 currentIndex 逻辑来隐式处理位置，
                // 但 EventSystem.currentSelectedGameObject 可能会丢失，所以依赖 lastSelectedObj 很重要)
            }

            // 2. 朗读逻辑 (TTS)
            if (lastSelectedObj != element.gameObject)
            {
                lastSelectedObj = element.gameObject;
                string textToSay = "";

                // 尝试提取文本内容

                var tmComp = element.GetComponentInChildren<TMP_Text>();
                if (tmComp != null) textToSay = tmComp.text;

                // 优先找 Text 子组件
                var textComp = element.GetComponentInChildren<Text>();
                // 如果自己就是 Text，那更好
                if (element is Text selfText) textComp = selfText;

                if (textComp != null) textToSay = textComp.text;

                // 针对特殊控件修饰朗读内容
                if (selectableComponent is InputField inputField)
                {
                    // 如果是输入框，读出占位符或当前内容
                    textToSay = $"编辑框 {inputField.text}";
                    if (string.IsNullOrEmpty(inputField.text) && inputField.placeholder is Text ph)
                    {
                        textToSay = $"编辑框 {ph.text}";
                    }
                }

                if ( selectableComponent is Toggle toggle)
                {
                    textToSay = $"{textToSay} " + (toggle.isOn ? "已选中" : "未选中");
                }

                // 兜底：如果实在没文字，读物体名字
                if (string.IsNullOrEmpty(textToSay)) textToSay = element.name;

                Debug.Log($"[朗读] {textToSay}");

                // 发送给朗读模块
                Narration.Say(textToSay, NarrationCategory.Notification);
            }
        }

        // ===================================================================================
        // 辅助与诊断区域
        // ===================================================================================

        private void LogDiagnostics()
        {
            Debug.Log("=== 诊断信息 ===");
            var editor = scnEditor.instance;
            if (editor == null)
            {
                Debug.Log("scnEditor.instance is NULL");
                return;
            }
            Debug.Log($"SettingsMenu Active: {editor.settingsMenu?.gameObject.activeInHierarchy}");
            Debug.Log($"Current EventSystem: {EventSystem.current?.name}");
        }

        private void HandleTimelineNavigation()
        {
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) NavigateTimelineEvents(-1);
                else if (Input.GetKeyDown(KeyCode.RightArrow)) NavigateTimelineEvents(1);
            }
        }

        private void NavigateTimelineEvents(int direction)
        {
            var allEvents = scnEditor.instance.eventControls;
            if (allEvents == null || allEvents.Count == 0) return;

            var currentSelected = scnEditor.instance.selectedControl;
            int currentIndex = allEvents.IndexOf(currentSelected);
            int newIndex = currentIndex + direction;

            if (currentIndex == -1)
            {
                if (direction > 0) newIndex = 0;
                else newIndex = allEvents.Count - 1;
            }
            else
            {
                if (newIndex < 0) newIndex = 0;
                if (newIndex >= allEvents.Count) newIndex = allEvents.Count - 1;
            }

            if (newIndex == currentIndex && currentIndex != -1) return;

            var targetEvent = allEvents[newIndex];
            scnEditor.instance.SelectEventControl(targetEvent, true);

            if (lastSelectedTimelineEvent != targetEvent)
            {
                string eventName = targetEvent.levelEvent.name;
                Narration.Say($"{eventName} 小结 {targetEvent.bar}， 拍 {targetEvent.beat}", NarrationCategory.Notification);
                lastSelectedTimelineEvent = targetEvent;
            }
        }
    }
}