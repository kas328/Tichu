using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using R3;
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

        /// <summary>
        /// 매치를 구동한다. 셸(GameSessionPresenter)이 Table을 Additive 로드한 뒤 호출한다.
        /// 목표 점수 도달 시 <paramref name="onComplete"/>로 결과를 알리고 루프를 종료한다.
        /// </summary>
        public void Begin(GameLaunchArgs args, Action<MatchSummary> onComplete)
        {
            _args = args;
            _onComplete = onComplete;

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
            var vm = new TableViewModel(args.MySeat);
            ITableView view = new RuntimeTableView(new DoTweenPlayAnimator());
            view.Bind(vm, canvas, this.GetCancellationTokenOnDestroy());

            // 3) 매치 루프 기동(여러 라운드, 누적 점수, 목표 점수에서 종료).
            RunMatchAsync(vm).Forget();
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
            using var client = new TichuClient(transport, vm, _args.MySeat, s => summary = s);

            var session = new MatchSession(
                agents, transport, masterSeed,
                targetScore: _args.TargetScore,
                interRoundPause: TimeSpan.FromSeconds(3.5));   // 결과를 보여 줄 시간
            session.Start();

            try
            {
                while (!summary.HasValue && !ct.IsCancellationRequested)
                {
                    client.Pump();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                if (summary.HasValue) _onComplete?.Invoke(summary.Value);
            }
            catch (OperationCanceledException) { /* 씬 종료: 정상 */ }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                session.Stop();
                session.Join(TimeSpan.FromMilliseconds(250));
            }
        }

        /// <summary>난이도별 인게임 1수 시간예산(ms). 스펙 §5 시작값. 0=탐색 OFF(Easy).</summary>
        internal static int BudgetMsFor(Tichu.GameFlow.Agents.Difficulty d)
        {
            switch (d)
            {
                case Tichu.GameFlow.Agents.Difficulty.Easy:   return 0;
                // P2-G 정합성: 예산은 티어 오름차순 비감소. 탐색은 관전 딜레이(~900ms)와 겹쳐 돌아
                // 900 이내는 체감 지연 0 → Hard/Expert 도 900으로 올려 예산 역전 제거(무지연).
                case Tichu.GameFlow.Agents.Difficulty.Normal: return 900;
                case Tichu.GameFlow.Agents.Difficulty.Hard:   return 900;
                case Tichu.GameFlow.Agents.Difficulty.Expert: return 900;
                default:                                       return 80;
            }
        }

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
