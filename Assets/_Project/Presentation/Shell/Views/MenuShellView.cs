using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 메뉴 셸의 런타임 UI 빌더. 영속 Canvas + EventSystem + 메뉴 화면별 패널
    /// (CanvasGroup + 제목 + 버튼 컨테이너)을 코드로 생성한다(플레이스홀더 — 실제 비주얼은 P1-D).
    /// 화면 흐름/이벤트는 모른다 — <see cref="MenuShellPresenter"/>가 버튼을 배선하고 패널을 토글한다.
    /// </summary>
    public sealed class MenuShellView
    {
        // 메뉴 네비 화면. InGame/Result는 메뉴 셸 밖(C4) — 그 상태에선 프레젠터가 전 패널을 숨긴다.
        public static readonly ScreenState[] MenuStates =
        {
            ScreenState.Intro, ScreenState.MainHub, ScreenState.ModeSelect, ScreenState.HowTo, ScreenState.Settings,
        };

        readonly Dictionary<ScreenState, CanvasGroup> _panels = new();
        readonly Dictionary<ScreenState, RectTransform> _buttonRoots = new();
        CanvasGroup _toast;
        Text _toastText;

        public MenuShellView()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas();
            foreach (var s in MenuStates)
                BuildPanel(s, canvas.transform);
            BuildToast(canvas.transform);   // 패널 뒤에 추가 → 같은 캔버스에서 위에 렌더
        }

        public IReadOnlyDictionary<ScreenState, CanvasGroup> Panels => _panels;

        /// <summary>토스트(전 화면 위 잠깐 뜨는 알림). 프레젠터가 페이드로 구동.</summary>
        public CanvasGroup ToastGroup => _toast;
        public Text ToastText => _toastText;

        /// <summary>패널에 네비 버튼을 추가하고 클릭을 배선한다.</summary>
        public void AddButton(ScreenState panel, string label, Action onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_buttonRoots[panel], false);
            go.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.34f, 1f);
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 460; le.preferredHeight = 100;
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
            AddCenteredLabel(go.transform, label, 36);
        }

        void BuildPanel(ScreenState s, Transform parent)
        {
            var go = new GameObject($"Panel_{s}", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            go.transform.SetParent(parent, false);
            StretchFull((RectTransform)go.transform);
            go.GetComponent<Image>().color = PanelColor(s);

            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;
            go.SetActive(false);
            _panels[s] = cg;

            // 제목 + 버튼을 하나의 중앙 세로 레이아웃에 쌓는다 → 가로/세로 어느 종횡비에서도 겹치지 않는다.
            // (이전엔 제목=상단앵커·버튼=중앙앵커라 가로 화면(1080 높이)에서 겹쳤음.)
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(go.transform, false);
            var rt = (RectTransform)content.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            var v = content.GetComponent<VerticalLayoutGroup>();
            v.spacing = 28; v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var title = NewText($"Title_{s}", content.transform, PanelTitle(s), 72);
            var tle = title.gameObject.AddComponent<LayoutElement>();
            tle.preferredWidth = 640f; tle.preferredHeight = 150f;

            _buttonRoots[s] = rt;   // 버튼은 제목 뒤로 content에 추가된다
        }

        void BuildToast(Transform parent)
        {
            var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(800f, 120f); rt.anchoredPosition = new Vector2(0f, 280f);
            go.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.92f);
            _toast = go.GetComponent<CanvasGroup>();
            _toast.alpha = 0f; _toast.interactable = false; _toast.blocksRaycasts = false;  // 입력 막지 않음
            _toastText = NewAnchoredText("ToastText", rt, "", 38,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 110f));
        }

        // ── 생성 헬퍼(TableUiView 관례와 동일) ───────────────────────────────────

        static void EnsureEventSystem()
        {
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        static Canvas CreateCanvas()
        {
            var go = new GameObject("MenuShellCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // 모바일 세로 기준(가로 전환은 D1)
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static Color PanelColor(ScreenState s) => s switch
        {
            ScreenState.Intro      => new Color(0.07f, 0.09f, 0.16f, 1f),
            ScreenState.MainHub    => new Color(0.06f, 0.13f, 0.10f, 1f),
            ScreenState.ModeSelect => new Color(0.12f, 0.08f, 0.16f, 1f),
            ScreenState.HowTo      => new Color(0.06f, 0.12f, 0.14f, 1f),
            ScreenState.Settings   => new Color(0.11f, 0.11f, 0.13f, 1f),
            _                      => new Color(0.08f, 0.08f, 0.08f, 1f),
        };

        static string PanelTitle(ScreenState s) => s switch
        {
            ScreenState.Intro      => "TICHU",
            ScreenState.MainHub    => "메인 허브",
            ScreenState.ModeSelect => "모드 선택",
            ScreenState.HowTo      => "게임 방법",
            ScreenState.Settings   => "설정",
            _                      => s.ToString(),
        };

        static void AddCenteredLabel(Transform parent, string text, int size)
        {
            var t = NewText("L", parent, text, size);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            StretchFull(t.rectTransform);
        }

        static Text NewAnchoredText(string name, RectTransform parent, string content, int fontSize,
            Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var t = NewText(name, parent, content, fontSize);
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor; rt.sizeDelta = size; rt.anchoredPosition = pos;
            return t;
        }

        static Text NewText(string name, Transform parent, string content, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont(); t.fontSize = fontSize; t.color = new Color(0.92f, 0.94f, 0.98f);
            t.text = content; t.alignment = TextAnchor.MiddleCenter;
            return t;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        static Font DefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}
