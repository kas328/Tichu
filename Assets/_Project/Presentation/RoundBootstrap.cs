using System;
using Cysharp.Threading.Tasks;
using R3;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.Presentation.Shell;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

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

            // 1) EventSystem 보장(없으면 생성 — 메뉴 셸이 이미 만들었으면 건너뜀).
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // 2) Screen-Space-Overlay 캔버스 + ViewModel + 뷰(한 번만 생성, 매치 내내 유지).
            var canvas = CreateCanvas();
            var vm = new TableViewModel(args.MySeat);
            ITableView view = new RuntimeTableView();
            view.Bind(vm, canvas, this.GetCancellationTokenOnDestroy());

            // 3) 매치 루프 기동(여러 라운드, 누적 점수, 목표 점수에서 종료).
            RunMatchAsync(vm).Forget();
        }

        /// <summary>여러 라운드를 누적 점수와 함께 구동한다(인간 1 + AI 3). 목표 점수 도달 시 종료.</summary>
        private async UniTaskVoid RunMatchAsync(TableViewModel vm)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var human = new HumanAgent(vm);                 // 인간 에이전트는 매치 내내 재사용
            var master = new Rng(_args.RandomDeal ? unchecked((ulong)DateTime.UtcNow.Ticks) : _args.Seed);
            int teamA = 0, teamB = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 고정 시드도 매 라운드 master를 전진시킨다(MatchRunner.RunMatch와 동일) —
                    // 라운드별 distinct·재현 가능하며 동일-라운드 무한반복(무승부 시 비종료)을 막는다.
                    ulong seed = master.NextULong();
                    var state = GameEngine.NewRound(seed);
                    vm.RoundResult.Value = null;            // 새 라운드: 이전 결과·로그 지움
                    vm.ClearPlays();
                    vm.FastForward = false;                 // 새 라운드: 스킵(빠른 진행) 해제
                    vm.ApplySnapshot(state);

                    var agents = new IDecisionAgent[]
                    {
                        human,
                        new DelayedAiDecisionAgent(seed, 1, _args.AiDelayMs, () => vm.FastForward),
                        new DelayedAiDecisionAgent(seed, 2, _args.AiDelayMs, () => vm.FastForward),
                        new DelayedAiDecisionAgent(seed, 3, _args.AiDelayMs, () => vm.FastForward),
                    };

                    // 매 플레이마다 뷰 갱신 + 로그 기록 → 사람이 AI 플레이를 보며 카운팅.
                    var outcome = await new AsyncGameDriver(agents).RunRoundAsync(
                        state, ct, (st, act) => { vm.ApplySnapshot(st); vm.RecordPlay(act); },
                        vm.TakePendingTichuSeat, vm.TakePendingBomb, vm.BombReserved);

                    teamA += outcome.Result.TeamATotal;     // 누적
                    teamB += outcome.Result.TeamBTotal;
                    vm.CumulativeA.Value = teamA;
                    vm.CumulativeB.Value = teamB;
                    vm.RoundResult.Value = outcome.Result;
                    vm.ApplySnapshot(outcome.State);

                    // 목표 점수 종료 판정(이미 단위 테스트된 순수 로직 재사용).
                    var decision = MatchRunner.Decide(teamA, teamB, _args.TargetScore);
                    if (decision != MatchDecision.Continue)
                    {
                        int winner = decision == MatchDecision.TeamAWins ? 0 : 1;
                        _onComplete?.Invoke(new MatchSummary(winner, teamA, teamB));
                        return;                             // 매치 종료
                    }

                    // 결과를 잠시 보여준 뒤 다음 라운드.
                    await UniTask.Delay(TimeSpan.FromSeconds(3.5), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { /* 씬 종료: 정상 */ }
            catch (Exception e) { Debug.LogException(e); }
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
