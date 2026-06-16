using System;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer.Unity;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 게임 세션 조율자(부트 엔트리포인트). InGame 상태에서 Table.unity를 Additive 로드해
    /// <see cref="RoundBootstrap.Begin"/>을 호출하고, 매치 종료 시 <see cref="AppFlowEvent.MatchEnded"/>를 발화한다.
    /// Result 상태에서 결과 패널(런타임 플레이스홀더)을 표시하고 Table을 언로드한다. 메뉴 패널은 MenuShellPresenter 담당.
    /// </summary>
    public sealed class GameSessionPresenter : IStartable, IDisposable
    {
        const string TableScene = "Table";

        readonly AppFlowMachine _flow;
        IDisposable _sub;

        CanvasGroup _resultPanel;
        Text _resultText;
        MatchSummary _lastSummary;
        bool _tableLoaded;

        public GameSessionPresenter(AppFlowMachine flow) => _flow = flow;

        public void Start()
        {
            BuildResultPanel();
            _sub = _flow.State.Subscribe(OnState);
        }

        void OnState(ScreenState s)
        {
            switch (s)
            {
                case ScreenState.InGame:
                    _resultPanel.gameObject.SetActive(false);
                    LoadTableAsync().Forget();
                    break;
                case ScreenState.Result:
                    UnloadTableAsync().Forget();
                    ShowResult();
                    break;
                default:                       // 메뉴 화면들: 게임 산출물 정리
                    _resultPanel.gameObject.SetActive(false);
                    UnloadTableAsync().Forget();
                    break;
            }
        }

        async UniTaskVoid LoadTableAsync()
        {
            if (_tableLoaded) return;
            _tableLoaded = true;
            await SceneManager.LoadSceneAsync(TableScene, LoadSceneMode.Additive);
            var rb = UnityEngine.Object.FindFirstObjectByType<RoundBootstrap>();
            if (rb == null) { Debug.LogError("[GameSession] Table 씬에 RoundBootstrap이 없습니다"); return; }
            rb.Begin(new GameLaunchArgs(), OnMatchEnded);   // 기본 args(목표 1000) — 난이도 선택은 추후
        }

        void OnMatchEnded(MatchSummary summary)
        {
            _lastSummary = summary;
            _flow.Send(AppFlowEvent.MatchEnded);            // → Result
        }

        async UniTaskVoid UnloadTableAsync()
        {
            if (!_tableLoaded) return;
            _tableLoaded = false;
            await SceneManager.UnloadSceneAsync(TableScene);
        }

        void ShowResult()
        {
            string who = _lastSummary.WinningTeam == 0 ? "우리 팀 승리!" : "상대 팀 승리";
            _resultText.text = $"{who}\n\n우리 {_lastSummary.TeamA}  :  {_lastSummary.TeamB} 상대";
            _resultPanel.gameObject.SetActive(true);
        }

        // ── Result 패널 런타임 빌드(플레이스홀더 — 실제 비주얼은 P1-D) ──────────────

        void BuildResultPanel()
        {
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = new GameObject("ResultCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;                       // 메뉴/테이블 위에 표시
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var panelGo = new GameObject("Panel_Result", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            StretchFull((RectTransform)panelGo.transform);
            panelGo.GetComponent<Image>().color = new Color(0.04f, 0.10f, 0.06f, 0.96f);
            _resultPanel = panelGo.GetComponent<CanvasGroup>();
            panelGo.SetActive(false);

            NewText("Title", panelGo.transform, "매치 결과", 72, new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(960f, 130f));
            _resultText = NewText("Summary", panelGo.transform, "", 52, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(960f, 360f));

            var btnGo = new GameObject("Btn_메인으로", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(panelGo.transform, false);
            var brt = (RectTransform)btnGo.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(460f, 100f); brt.anchoredPosition = new Vector2(0f, -320f);
            btnGo.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.34f, 1f);
            btnGo.GetComponent<Button>().onClick.AddListener(() => _flow.Send(AppFlowEvent.ReturnToHub));
            NewText("L", btnGo.transform, "메인으로", 36, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 100f));
        }

        static Text NewText(string name, Transform parent, string content, int fontSize, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont(); t.fontSize = fontSize; t.color = new Color(0.92f, 0.94f, 0.98f);
            t.text = content; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor; rt.sizeDelta = size; rt.anchoredPosition = pos;
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

        public void Dispose() => _sub?.Dispose();
    }
}
