using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace QuestBridge
{
    /// <summary>Uses a real browser textarea while a WebGL text field is being edited or copied.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class QuestBridgeBrowserText : MonoBehaviour, IPointerClickHandler
    {
        static QuestBridgeBrowserText active;
        TMP_InputField input;
        TMP_Text label;
        bool savedReadOnly, savedKeyboardCapture, closing, applying;
        string lastValue;
        readonly Vector3[] corners = new Vector3[4];
        readonly List<UnityEngine.UI.ScrollRect> scrollParents = new List<UnityEngine.UI.ScrollRect>();
#if !UNITY_WEBGL || UNITY_EDITOR
        QuestBridgeReadonlyInput editorSelection;
        float originalTextAlpha;
        int selectionOpenedFrame;
#endif

        [Serializable]
        sealed class NativeOptions
        {
            public int id, limit, start, end;
            public bool readOnly, singleLine;
            public string value, placeholder, color, background;
            public float x, y, width, height, clipLeft, clipTop, clipRight, clipBottom, fontSize;
        }

        [Serializable]
        sealed class NativeState
        {
            public int id, start, end;
            public string value;
            public bool closed;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void QBText_Open(string options);
        [DllImport("__Internal")] static extern string QBText_Read(int id, int close);
        [DllImport("__Internal")] static extern void QBText_Position(string options);
        [DllImport("__Internal")] static extern void QBText_SetValue(int id, string value);
#endif

        public static QuestBridgeBrowserText AttachInput(TMP_InputField field)
        {
            if (!field) return null;
            var bridge = field.GetComponent<QuestBridgeBrowserText>();
            if (bridge) return bridge;
            bridge = field.gameObject.AddComponent<QuestBridgeBrowserText>();
            bridge.input = field;
            bridge.label = field.textComponent;
#if UNITY_WEBGL && !UNITY_EDITOR
            field.shouldHideMobileInput = true;
            field.shouldHideSoftKeyboard = true;
            field.onSelect.AddListener(bridge.OnInputSelected);
#endif
            return bridge;
        }

        /// <summary>Attach only to meaningful body text. Labels inside buttons/inputs are left alone.</summary>
        public static QuestBridgeBrowserText AttachSelectable(TMP_Text text)
        {
            if (!text || text.GetComponentInParent<TMP_InputField>() || text.GetComponentInParent<UnityEngine.UI.Selectable>()) return null;
            var bridge = text.GetComponent<QuestBridgeBrowserText>();
            if (bridge) return bridge;
            bridge = text.gameObject.AddComponent<QuestBridgeBrowserText>();
            bridge.label = text;
            text.raycastTarget = true;
            return bridge;
        }

        /// <summary>Flush native edits before submit, reset, role change or opening another overlay.</summary>
        public static void CloseActive()
        {
            if (active) active.Close();
        }

        public static bool IsOpen => active != null;

        void OnInputSelected(string _) { Open(); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            Open();
#else
            if (!input) OpenEditorSelection(eventData);
#endif
        }

        void Open()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (active == this || !isActiveAndEnabled || !label) return;
            if (input && (!input.enabled || !input.IsInteractable())) return;
            CloseActive();
            var options = Options();
            if (options == null) return;
            if (input)
            {
                savedReadOnly = input.readOnly;
                input.DeactivateInputField();
                input.readOnly = true; // TMP must not edit the same text behind the DOM field.
            }
            savedKeyboardCapture = WebGLInput.captureAllKeyboardInput;
            WebGLInput.captureAllKeyboardInput = false;
            active = this;
            lastValue = options.value;
            foreach (var scroll in GetComponentsInParent<UnityEngine.UI.ScrollRect>())
            {
                scrollParents.Add(scroll);
                scroll.onValueChanged.AddListener(OnParentScroll);
            }
            try { QBText_Open(JsonUtility.ToJson(options)); }
            catch { FinishClose(); throw; }
#endif
        }

        void Update()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (active != this) return;
            if (!label || (input && (!input.enabled || !input.IsInteractable()))) { Close(); return; }
            ReadNative(false);
            if (active != this) return;
            string current = input ? input.text : label.GetParsedText();
            if (!applying && current != lastValue)
            {
                lastValue = current;
                QBText_SetValue(GetInstanceID(), current ?? "");
            }
#else
            if (active != this) return;
            if (!label || !editorSelection || !label.gameObject.activeInHierarchy ||
                (Time.frameCount > selectionOpenedFrame + 1 && EventSystem.current &&
                 EventSystem.current.currentSelectedGameObject != editorSelection.gameObject))
            {
                Close();
                return;
            }
            if (lastValue != label.text) Close();
#endif
        }

        void LateUpdate()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (active != this) return;
            var options = Options();
            if (options == null) { Close(); return; }
            QBText_Position(JsonUtility.ToJson(options));
#else
            if (active == this && label) label.canvasRenderer.SetAlpha(0);
#endif
        }

        void OnParentScroll(Vector2 _) { Close(); }
        void OnApplicationFocus(bool focused) { if (!focused) Close(); }
        void OnDisable() { Close(); }
        void OnDestroy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (input) input.onSelect.RemoveListener(OnInputSelected);
#endif
            Close();
        }

        void Close()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (active != this || closing) return;
            closing = true;
            try { ReadNative(true); }
            finally { FinishClose(); closing = false; }
#else
            if (active != this || closing) return;
            closing = true;
            active = null;
            foreach (var scroll in scrollParents) if (scroll) scroll.onValueChanged.RemoveListener(OnParentScroll);
            scrollParents.Clear();
            var selection = editorSelection;
            editorSelection = null;
            if (selection)
            {
                selection.closeRequested = null;
                selection.gameObject.SetActive(false);
                Destroy(selection.gameObject);
            }
            if (label) label.canvasRenderer.SetAlpha(originalTextAlpha);
            closing = false;
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        void OpenEditorSelection(PointerEventData pointer)
        {
            if (active == this || !isActiveAndEnabled || !label || !label.enabled || input) return;
            if (!EventSystem.current || string.IsNullOrEmpty(label.text)) return;
            CloseActive();
            Canvas.ForceUpdateCanvases();
            if (label.rectTransform.rect.width < 2 || label.rectTransform.rect.height < 2) return;

            // Create only after the first click, when the caller has finished sizing its label.
            // A child rect follows subsequent layout changes without changing the original label.
            var box = new GameObject("Text selection", typeof(RectTransform));
            box.SetActive(false);
            var boxRect = (RectTransform)box.transform;
            boxRect.SetParent(label.transform, false);
            Stretch(boxRect);
            var hitArea = box.AddComponent<UnityEngine.UI.Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true;
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(boxRect, false);
            Stretch(viewport);
            var textObject = new GameObject("Selectable text", typeof(RectTransform));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(viewport, false);
            Stretch(textRect);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = label.font;
            text.fontSharedMaterial = label.fontSharedMaterial;
            text.fontSize = label.fontSize;
            text.fontStyle = label.fontStyle;
            text.fontWeight = label.fontWeight;
            text.color = label.color;
            text.alignment = label.alignment;
            text.margin = label.margin;
            text.characterSpacing = label.characterSpacing;
            text.wordSpacing = label.wordSpacing;
            text.lineSpacing = label.lineSpacing;
            text.paragraphSpacing = label.paragraphSpacing;
            text.textWrappingMode = label.textWrappingMode;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.richText = false;
            text.raycastTarget = false;

            editorSelection = box.AddComponent<QuestBridgeReadonlyInput>();
            editorSelection.textViewport = viewport;
            editorSelection.textComponent = text;
            editorSelection.targetGraphic = hitArea;
            editorSelection.transition = UnityEngine.UI.Selectable.Transition.None;
            editorSelection.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            editorSelection.lineType = TMP_InputField.LineType.MultiLineNewline;
            editorSelection.richText = false;
            editorSelection.readOnly = true;
            editorSelection.onFocusSelectAll = false;
            editorSelection.resetOnDeActivation = false;
            editorSelection.restoreOriginalTextOnEscape = false;
            editorSelection.customCaretColor = true;
            editorSelection.caretColor = new Color32(23, 33, 42, 255);
            editorSelection.caretWidth = 2;
            editorSelection.caretBlinkRate = 1.4f;
            editorSelection.selectionColor = new Color32(65, 145, 230, 105);
            lastValue = label.text;
            editorSelection.SetTextWithoutNotify(label.richText ? label.GetParsedText() : label.text);
            originalTextAlpha = label.canvasRenderer.GetAlpha();
            active = this;
            selectionOpenedFrame = Time.frameCount;
            box.SetActive(true);
            Canvas.ForceUpdateCanvases();
            text.ForceMeshUpdate();
            label.canvasRenderer.SetAlpha(0);
            editorSelection.closeRequested = Close;
            foreach (var scroll in GetComponentsInParent<UnityEngine.UI.ScrollRect>())
            {
                scrollParents.Add(scroll);
                scroll.onValueChanged.AddListener(OnParentScroll);
            }

            // Reuse TMP's actual hit testing and selection implementation at the clicked point.
            editorSelection.OnPointerDown(pointer);
            editorSelection.OnPointerUp(pointer);
            editorSelection.ActivateInputField();
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
#endif

        void ReadNative(bool close)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var payload = QBText_Read(GetInstanceID(), close ? 1 : 0);
            if (string.IsNullOrEmpty(payload)) return;
            var state = JsonUtility.FromJson<NativeState>(payload);
            if (state == null || state.id != GetInstanceID()) return;
            if (input && !savedReadOnly)
            {
                applying = true;
                try
                {
                    if (input.text != state.value) input.text = state.value ?? "";
                    if (input)
                    {
                        lastValue = input.text;
                        input.selectionStringAnchorPosition = Mathf.Clamp(state.start, 0, input.text.Length);
                        input.selectionStringFocusPosition = Mathf.Clamp(state.end, 0, input.text.Length);
                        if (!state.closed && state.value != input.text) QBText_SetValue(GetInstanceID(), input.text);
                    }
                }
                finally { applying = false; }
            }
            if (state.closed && !closing) FinishClose();
#endif
        }

        void FinishClose()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            foreach (var scroll in scrollParents) if (scroll) scroll.onValueChanged.RemoveListener(OnParentScroll);
            scrollParents.Clear();
            if (active != this) return;
            active = null;
            WebGLInput.captureAllKeyboardInput = savedKeyboardCapture;
            if (input)
            {
                input.readOnly = savedReadOnly;
                input.DeactivateInputField();
                input.ForceLabelUpdate();
                input.onEndEdit.Invoke(input.text);
            }
#endif
        }

        NativeOptions Options()
        {
            if (!label || Screen.width <= 0 || Screen.height <= 0) return null;
            var rect = input && input.textViewport ? input.textViewport : label.rectTransform;
            var canvas = label.canvas;
            if (!canvas || !canvas.isActiveAndEnabled) return null;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect full = ScreenRect(rect, camera);
            Rect visible = Intersect(full, new Rect(0, 0, Screen.width, Screen.height));
            foreach (var mask in GetComponentsInParent<UnityEngine.UI.RectMask2D>())
                if (mask.isActiveAndEnabled) visible = Intersect(visible, ScreenRect(mask.rectTransform, camera));
            foreach (var mask in GetComponentsInParent<UnityEngine.UI.Mask>())
                if (mask.isActiveAndEnabled) visible = Intersect(visible, ScreenRect(mask.rectTransform, camera));
            foreach (var group in GetComponentsInParent<CanvasGroup>())
                if (!group.interactable || !group.blocksRaycasts || group.alpha < .01f) return null;
            if (full.width < 2 || full.height < 2 || visible.width < 2 || visible.height < 2) return null;
            string value = (input ? input.text : label.GetParsedText()) ?? "";
            Color background = Color.white;
            foreach (var image in GetComponentsInParent<UnityEngine.UI.Image>())
                if (image.isActiveAndEnabled && image.color.a > .1f) { background = image.color; break; }
            background.a = 1;
            return new NativeOptions
            {
                id = GetInstanceID(), value = value ?? "",
                readOnly = !input || (active == this ? savedReadOnly : input.readOnly),
                singleLine = input && input.lineType == TMP_InputField.LineType.SingleLine,
                limit = input ? input.characterLimit : 0,
                start = input ? value.Length : 0, end = value.Length,
                placeholder = input && input.placeholder is TMP_Text hint ? hint.text : "",
                color = "#" + ColorUtility.ToHtmlStringRGBA(label.color),
                background = "#" + ColorUtility.ToHtmlStringRGB(background),
                x = full.xMin / Screen.width, y = 1 - full.yMax / Screen.height,
                width = full.width / Screen.width, height = full.height / Screen.height,
                clipLeft = (visible.xMin - full.xMin) / Screen.width,
                clipTop = (full.yMax - visible.yMax) / Screen.height,
                clipRight = (full.xMax - visible.xMax) / Screen.width,
                clipBottom = (visible.yMin - full.yMin) / Screen.height,
                fontSize = label.fontSize * full.height / Mathf.Max(1, rect.rect.height) / Screen.height
            };
        }

        Rect ScreenRect(RectTransform rect, Camera camera)
        {
            rect.GetWorldCorners(corners);
            var first = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 min = first, max = first;
            for (int i = 1; i < 4; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static Rect Intersect(Rect a, Rect b)
        {
            float left = Mathf.Max(a.xMin, b.xMin), bottom = Mathf.Max(a.yMin, b.yMin);
            return new Rect(left, bottom, Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - left), Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - bottom));
        }
    }
}
