using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RDLevelEditor;

namespace RDLevelEditorAccess
{
    /// <summary>
    /// 专门处理输入框 (InputField / TMP_InputField) 的无障碍朗读。
    /// 使用状态比对 (State Diffing) 策略，自动检测文本输入、删除和光标移动。
    /// </summary>
    public class InputFieldReader
    {
        // --- 状态缓存 ---
        private GameObject _lastFocusObj; // 上一帧获得焦点的物体
        private string _lastText = "";    // 上一帧的文本内容
        private int _lastCaret = -1;      // 上一帧的光标位置
        private bool _wasFocused = false; // 上一帧是否处于激活状态

        /// <summary>
        /// 核心检测方法：请在 Update 中每一帧调用此方法。
        /// </summary>
        /// <param name="currentObj">当前 EventSystem.currentSelectedGameObject</param>
        public void UpdateReader(GameObject currentObj)
        {
            //Debug.Log("检测到输入框。");
            // 1. 基础有效性检查：如果没有选中物体，重置状态
            if (currentObj == null)
            {
                ResetState();
                return;
            }

            // 2. 尝试获取输入框组件 (兼容旧版 UI 和新版 TMP)
            InputField input = currentObj.GetComponent<InputField>();
            TMP_InputField tmpInput = currentObj.GetComponent<TMP_InputField>();

            // 如果都不是输入框，重置状态并退出
            if (input == null && tmpInput == null)
            {
                ResetState();
                return;
            }

            // 3. 统一接口数据 (将 input 和 tmpInput 的差异抹平)
            bool isFocused = false;
            string text = "";
            int caret = 0;
            bool isPassword = false;

            if (input != null)
            {
                isFocused = input.isFocused; // 注意：有时 isFocused 会延迟，EventSystem 选中通常意味着聚焦
                text = input.text;
                caret = input.caretPosition;
                isPassword = (input.inputType == InputField.InputType.Password);
            }
            else if (tmpInput != null)
            {
                isFocused = tmpInput.isFocused;
                text = tmpInput.text;
                caret = tmpInput.caretPosition;
                isPassword = (tmpInput.contentType == TMP_InputField.ContentType.Password);
            }

            // 如果组件认为自己没聚焦（防抖动），也不处理
            if (!isFocused)
            {
                ResetState();
                return;
            }

            // 4. 焦点切换检测 (Focus Changed)
            // 如果是从别的控件切过来的，或者是刚激活，只同步状态，不读差异
            if (currentObj != _lastFocusObj || !_wasFocused)
            {
                _lastFocusObj = currentObj;
                _lastText = text;
                _lastCaret = caret;
                _wasFocused = true;
                // 这里不需要朗读，因为外部导航逻辑通常已经读了 "编辑框 [内容]"
                return;
            }

            // =========================================================
            // 5. 状态比对核心逻辑 (Diffing)
            // =========================================================

            // A. 检测文本变化 (Typing / Deleting)
            if (text != _lastText)
            {
                HandleTextChange(text, _lastText, caret, _lastCaret, isPassword);
            }
            // B. 检测光标移动 (Navigation) - 只有文本没变时才读光标
            else if (caret != _lastCaret)
            {
                HandleCaretMove(text, caret, isPassword);
            }

            // 6. 更新状态缓存
            _lastText = text;
            _lastCaret = caret;
        }

        /// <summary>
        /// 重置内部状态，防止切回同一个输入框时状态错乱
        /// </summary>
        private void ResetState()
        {
            _lastFocusObj = null;
            _lastText = "";
            _lastCaret = -1;
            _wasFocused = false;
        }

        /// <summary>
        /// 处理文本增加或减少
        /// </summary>
        private void HandleTextChange(string curr, string prev, int currPos, int prevPos, bool isPassword)
        {
            int delta = curr.Length - prev.Length;

            // --- 情况 1: 输入 (Text Added) ---
            if (delta > 0)
            {
                // 计算新增内容的起始位置
                // 逻辑：当前光标位置减去新增长度，即为新增内容的起点
                int startIndex = currPos - delta;
                if (startIndex >= 0 && startIndex < curr.Length)
                {
                    string addedContent = curr.Substring(startIndex, delta);

                    if (isPassword) Speak("星号");
                    else Speak(addedContent);
                }
            }
            // --- 情况 2: 删除 (Text Deleted) ---
            else if (delta < 0)
            {
                // 需要区分是 Backspace (退格) 还是 Delete (向后删)
                // Backspace: 光标会左移 (prevPos > currPos)
                // Delete: 光标位置不变 (prevPos == currPos)

                string deletedContent = "";
                int absDelta = Mathf.Abs(delta); // 删除的字符数

                // 我们从【旧文本 (prev)】中提取被删掉的部分
                // 无论 Backspace 还是 Delete，被删内容的起始索引在旧文本中通常就是 currPos
                // 例子 Backspace: "abc|" -> "ab|"。 prevPos=3, currPos=2. 删掉的是 prev[2] ('c')
                // 例子 Delete: "|abc" -> "|bc"。 prevPos=0, currPos=0. 删掉的是 prev[0] ('a')

                if (currPos >= 0 && (currPos + absDelta) <= prev.Length)
                {
                    deletedContent = prev.Substring(currPos, absDelta);
                }

                if (!string.IsNullOrEmpty(deletedContent))
                {
                    if (isPassword) Speak("删除 星号");
                    else Speak($"删除 {deletedContent}");
                }
            }
        }

        /// <summary>
        /// 处理光标移动
        /// </summary>
        private void HandleCaretMove(string text, int caret, bool isPassword)
        {
            // 如果是空文本
            if (string.IsNullOrEmpty(text))
            {
                Speak("空");
                return;
            }

            // 如果光标在最后
            if (caret >= text.Length)
            {
                Speak("行尾");
                return;
            }

            // 朗读光标右侧的字符 (符合 Windows 标准编辑框行为)
            char c = text[caret];

            if (isPassword) Speak("星号");
            else Speak(c.ToString());
        }

        /// <summary>
        /// 统一朗读接口
        /// </summary>
        private void Speak(string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            // 遵照要求：始终使用 Navigation 类型
            Narration.Say(content, NarrationCategory.Navigation);
        }
    }
}