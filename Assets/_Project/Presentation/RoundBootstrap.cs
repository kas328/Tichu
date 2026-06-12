using Cysharp.Threading.Tasks;
using R3;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow;
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
        /// <summary>체크 시 매 실행마다 무작위 분배. 해제 시 아래 Seed 로 고정(재현용).</summary>
        public bool RandomDeal = true;

        /// <summary>라운드 시드(RandomDeal 해제 시 사용).</summary>
        public ulong Seed = 42;

        /// <summary>AI 가 카드를 낼 때의 딜레이(ms). 사람이 따라 보며 카운팅하도록.</summary>
        public int AiDelayMs = 900;

        private const int MySeat = 0;

        private void Start()
        {
            // 1) EventSystem 보장(없으면 생성).
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // 2) Screen-Space-Overlay 캔버스 + ViewModel + 뷰(한 번만 생성, 매치 내내 유지).
            var canvas = CreateCanvas();
            var vm = new TableViewModel(MySeat);
            new TableUiView().Bind(vm, canvas, this.GetCancellationTokenOnDestroy());

            // 3) 매치 루프 기동(여러 라운드, 누적 점수).
            RunMatchAsync(vm).Forget();
        }

        /// <summary>여러 라운드를 누적 점수와 함께 구동한다(인간 1 + AI 3). 라운드 사이 잠시 결과 표시.</summary>
        private async UniTaskVoid RunMatchAsync(TableViewModel vm)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var human = new HumanAgent(vm);                 // 인간 에이전트는 매치 내내 재사용
            var master = new Rng(RandomDeal ? unchecked((ulong)System.DateTime.UtcNow.Ticks) : Seed);
            int teamA = 0, teamB = 0;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    ulong seed = RandomDeal ? master.NextULong() : Seed;
                    var state = GameEngine.NewRound(seed);
                    vm.RoundResult.Value = null;            // 새 라운드: 이전 결과·로그 지움
                    vm.ClearPlays();
                    vm.FastForward = false;                 // 새 라운드: 스킵(빠른 진행) 해제
                    vm.ApplySnapshot(state);

                    var agents = new IDecisionAgent[]
                    {
                        human,
                        new DelayedAiDecisionAgent(seed, 1, AiDelayMs, () => vm.FastForward),
                        new DelayedAiDecisionAgent(seed, 2, AiDelayMs, () => vm.FastForward),
                        new DelayedAiDecisionAgent(seed, 3, AiDelayMs, () => vm.FastForward),
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

                    // 결과를 잠시 보여준 뒤 다음 라운드.
                    await UniTask.Delay(System.TimeSpan.FromSeconds(3.5), cancellationToken: ct);
                }
            }
            catch (System.OperationCanceledException) { /* 씬 종료: 정상 */ }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // 모바일 세로 기준
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }
    }
}
