using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using R3;
using Tichu.GameFlow.Agents;
using Tichu.Net;
using Tichu.Presentation.Net;
using Tichu.Presentation.Shell;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using Tichu.Presentation.Visuals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[assembly: InternalsVisibleTo("Tichu.Presentation.Tests")]   // BudgetMsFor 정합성 테스트 접근용

namespace Tichu.Presentation
{
    /// <summary>
    /// 한 라운드를 끝까지 구동하는 부트스트랩.
    /// 씬에는 이 컴포넌트를 붙인 GameObject 하나만 있으면 된다(Canvas·EventSystem 을 직접 생성).
    /// 좌석 0(South)은 인간, 나머지 셋은 Normal AI.
    /// </summary>
    public sealed class RoundBootstrap : MonoBehaviour
    {
        private GameLaunchArgs _args;
        private Action<MatchSummary> _onComplete;
        private Action<string> _onFault;
        private bool _faulted;   // 폴트 알림은 한 번만 — 취소 뒤 늦은 경로가 UI 를 건드리지 않게 하는 표시도 겸한다

        private void InvokeFault(string reason)
        {
            if (_faulted) return;
            _faulted = true;
            _onFault?.Invoke(reason);
        }

        /// <summary>
        /// 매치를 구동한다. 셸(GameSessionPresenter)이 Table을 Additive 로드한 뒤 호출한다.
        /// 목표 점수 도달 시 <paramref name="onComplete"/>로 결과를 알리고 루프를 종료한다.
        /// 세션 폴트·접속 실패 시 <paramref name="onFault"/>를 사유와 함께 부른다(null = 사용자 취소, 알림 없음).
        /// </summary>
        public void Begin(GameLaunchArgs args, Action<MatchSummary> onComplete, Action<string> onFault)
        {
            _args = args;
            _onComplete = onComplete;
            _onFault = onFault;

            // 가로 60fps 목표(D1). 방향 잠금은 PlayerSettings(가로만 허용).
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            // D6: 디버그 빌드 한정 FPS 오버레이(실기기 60fps 측정). 릴리스 빌드에선 미생성(no-op).
            if (Debug.isDebugBuild) Diagnostics.FpsOverlay.Create();

            // 1) EventSystem 보장(없으면 생성 — 메뉴 셸이 이미 만들었으면 건너뜀).
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // 2) Screen-Space-Overlay 캔버스 + ViewModel + 뷰(한 번만 생성, 매치 내내 유지).
            var canvas = CreateCanvas();
            var vm = new TableViewModel(0);   // 뷰모델은 항상 "나 = 좌석 0" — 회전은 TichuClient 가 한다
            ITableView view = new RuntimeTableView(new DoTweenPlayAnimator());
            view.Bind(vm, canvas, this.GetCancellationTokenOnDestroy());

            // 3) 매치 루프 기동(여러 라운드, 누적 점수, 목표 점수에서 종료).
            if (args.ServerAddress != null) RunNetworkMatchAsync(vm, canvas).Forget();
            else RunMatchAsync(vm).Forget();
        }

        /// <summary>
        /// 권위 세션을 띄우고 매 프레임 수신함을 비운다. 좌석 0 은 원격(= 이 클라이언트),
        /// 1~3 은 세션 스레드에서 도는 동기 PIMC 다. 라운드 루프·누적 점수·종료 판정은
        /// 모두 세션이 쥔다 — 클라이언트가 중복 판정하지 않는다.
        /// </summary>
        private async UniTaskVoid RunMatchAsync(TableViewModel vm)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            ulong masterSeed = _args.RandomDeal ? unchecked((ulong)DateTime.UtcNow.Ticks) : _args.Seed;

            var cfg = Tichu.GameFlow.Agents.PolicyConfig.For(_args.Difficulty);
            int budgetMs = BudgetMsFor(_args.Difficulty);

            // AI 에이전트는 매치 내내 재사용한다. PimcAgent 의 라운드 상태(_passed)는 매
            // 라운드 ChooseExchange 가 덮어쓰고 그 전에 결정화가 없으므로 안전하며, _rng 는
            // 하나의 결정적 스트림으로 이어진다.
            var agents = new Tichu.GameFlow.Agents.IAgent[4];
            for (int seat = 0; seat < 4; seat++)
            {
                if (seat == _args.MySeat) { agents[seat] = null; continue; }   // 원격 = 나
                agents[seat] = new PacedPimcAgent(
                    masterSeed, seat, cfg, budgetMs, _args.AiDelayMs, () => vm.FastForward);
            }

            var transport = new LoopbackTransport();
            MatchSummary? summary = null;
            using var client = new TichuClient(new LoopbackClient(transport, _args.MySeat), vm, _args.MySeat, s => summary = s);

            var session = new MatchSession(
                agents, transport, masterSeed,
                targetScore: _args.TargetScore,
                interRoundPause: TimeSpan.FromSeconds(3.5));   // 결과를 보여 줄 시간
            session.Start();

            try
            {
                await PumpUntilEndAsync(client, () => summary, ct);
            }
            catch (OperationCanceledException) { /* 씬 종료: 정상 */ }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                session.Stop();
                session.Join(TimeSpan.FromMilliseconds(250));
            }
        }

        /// <summary>매치가 끝나거나 세션이 폴트날 때까지 펌프하고 결과를 셸에 알린다.</summary>
        private async UniTask PumpUntilEndAsync(TichuClient client, Func<MatchSummary?> summary, System.Threading.CancellationToken ct)
        {
            while (!summary().HasValue && !client.Faulted && !ct.IsCancellationRequested)
            {
                client.Pump();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (client.Faulted)
            {
                Debug.LogError($"[RoundBootstrap] 세션 폴트로 매치를 중단한다: {client.LastRejection}");
                InvokeFault(client.LastRejection);
            }
            else if (summary().HasValue)
                _onComplete?.Invoke(summary().Value);
        }

        /// <summary>
        /// 서버에 접속해 대기실에서 좌석을 받은 뒤 같은 펌프로 매치를 돈다. 로컬 세션·AI 는 없다 —
        /// 권위와 AI 페이싱은 서버가 쥔다(빨리감기 무효는 의도된 동작).
        /// </summary>
        private async UniTaskVoid RunNetworkMatchAsync(TableViewModel vm, Canvas canvas)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var overlay = CreateLobbyOverlay(canvas, out var status, out var cancel);
            cancel.onClick.AddListener(() => InvokeFault(null));   // 사용자 취소 → 알림 없이 메뉴로

            WebSocketClientTransport transport;
            try
            {
                transport = await WebSocketClientTransport.ConnectAsync(_args.ServerAddress, TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (ct.IsCancellationRequested) return;   // 씬 종료 뒤 Mono 가 OCE 가 아닌 예외를 던질 수 있다
                Debug.LogWarning($"[RoundBootstrap] 접속 실패 {_args.ServerAddress}: {e.Message}");
                InvokeFault("서버에 연결할 수 없습니다");
                return;
            }

            using (transport)
            {
                try
                {
                    var lobby = new LobbyWaiter();
                    while (!lobby.Started && lobby.FaultReason == null)
                    {
                        lobby.Feed(transport.Drain());
                        if (_faulted) return;   // 사용자 취소 — 파괴될 UI 를 건드리지 않는다
                        status.text = lobby.StatusText;
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                    if (lobby.FaultReason != null) { InvokeFault(lobby.FaultReason); return; }

                    Destroy(overlay);
                    MatchSummary? summary = null;
                    using var client = new TichuClient(transport, vm, lobby.Seat, s => summary = s, lobby.Backlog);
                    await PumpUntilEndAsync(client, () => summary, ct);
                }
                catch (OperationCanceledException) { /* 씬 종료: 정상 */ }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>대기실 오버레이(상태 문구 + 취소). 테이블 위 반투명 판.</summary>
        private static GameObject CreateLobbyOverlay(Canvas canvas, out Text status, out Button cancel)
        {
            var root = new GameObject("LobbyOverlay", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = new Color(0.04f, 0.10f, 0.06f, 0.92f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var textGo = new GameObject("Status", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(root.transform, false);
            status = textGo.GetComponent<Text>();
            status.font = font; status.fontSize = 48; status.alignment = TextAnchor.MiddleCenter;
            status.color = new Color(0.92f, 0.94f, 0.98f); status.text = "서버 접속 중…";
            var trt = status.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(1200f, 120f); trt.anchoredPosition = new Vector2(0f, 60f);

            var btnGo = new GameObject("Btn_취소", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(root.transform, false);
            var brt = (RectTransform)btnGo.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(360f, 96f); brt.anchoredPosition = new Vector2(0f, -100f);
            btnGo.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.34f, 1f);
            cancel = btnGo.GetComponent<Button>();

            var lblGo = new GameObject("L", typeof(RectTransform), typeof(Text));
            lblGo.transform.SetParent(btnGo.transform, false);
            var lbl = lblGo.GetComponent<Text>();
            lbl.font = font; lbl.fontSize = 36; lbl.alignment = TextAnchor.MiddleCenter;
            lbl.color = new Color(0.92f, 0.94f, 0.98f); lbl.text = "취소";
            var lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

            return root;
        }

        /// <summary>난이도별 인게임 1수 시간예산(ms). 정본은 core 의 PimcBudget — 기존 테스트 호환용 위임.</summary>
        internal static int BudgetMsFor(Tichu.GameFlow.Agents.Difficulty d) => PimcBudget.For(d);

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // 모바일 가로 기준
            scaler.matchWidthOrHeight = 1f;                       // 높이 기준(C-1: 앵커 레이아웃과 동시 전환)

            return canvas;
        }
    }
}
