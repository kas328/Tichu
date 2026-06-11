using Cysharp.Threading.Tasks;
using R3;
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

        private const int MySeat = 0;

        private void Start()
        {
            // 1) EventSystem 보장(없으면 생성).
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // 2) Screen-Space-Overlay 캔버스 생성.
            var canvas = CreateCanvas();

            // 3) ViewModel 생성.
            var vm = new TableViewModel(MySeat);

            // 4) 뷰 빌드 + 구독.
            var view = new TableUiView();
            view.Bind(vm, canvas);

            // 5) 에이전트 구성: 좌석 0=인간, 1~3=Normal AI.
            var agents = new IDecisionAgent[]
            {
                new HumanAgent(vm),
                new AiDecisionAgent(Seed, 1),
                new AiDecisionAgent(Seed, 2),
                new AiDecisionAgent(Seed, 3)
            };

            // 6) 초기 렌더 후 라운드 구동. (RandomDeal 이면 매 실행 무작위 시드.)
            if (RandomDeal) Seed = unchecked((ulong)System.DateTime.UtcNow.Ticks);
            var state = GameEngine.NewRound(Seed);
            vm.ApplySnapshot(state);

            RunRoundAsync(agents, state, vm).Forget();
        }

        /// <summary>라운드를 비동기로 구동하고 예외를 콘솔에 노출한다.</summary>
        private async UniTaskVoid RunRoundAsync(IDecisionAgent[] agents, GameState state, TableViewModel vm)
        {
            try
            {
                var outcome = await new AsyncGameDriver(agents)
                    .RunRoundAsync(state, this.GetCancellationTokenOnDestroy());

                // 7) 완료: 최종 결산 표시.
                vm.RoundResult.Value = outcome.Result;
                vm.ApplySnapshot(outcome.State);
            }
            catch (System.OperationCanceledException)
            {
                // 오브젝트 파괴로 인한 취소는 정상 종료.
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
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
