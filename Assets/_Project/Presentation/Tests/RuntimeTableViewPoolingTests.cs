using System.Threading;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Tests
{
    public class RuntimeTableViewPoolingTests
    {
        private static Transform FindByName(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static int ActiveCards(Transform t) =>
            t.GetComponentsInChildren<CardView>(false).Length;

        [Test]
        public void HandRoot_is_subcanvas_with_raycaster()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                ITableView view = new RuntimeTableView();
                view.Bind(new TableViewModel(0), canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                var hand = FindByName(canvasGo, "Hand");
                Assert.IsNotNull(hand, "Hand 루트가 있어야 한다");
                Assert.IsNotNull(hand.GetComponent<Canvas>(), "손패는 서브 Canvas");
                Assert.IsNotNull(hand.GetComponent<GraphicRaycaster>(), "손패 클릭에는 GraphicRaycaster 필수");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Hand_renders_pooled_chips_and_reuses_on_rerender()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView();
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                var state = GameEngine.NewRound(12345UL);
                vm.ApplySnapshot(state);

                var hand = FindByName(canvasGo, "Hand");
                int expected = state.Seats[0].Hand.Count;
                Assert.AreEqual(expected, ActiveCards(hand), "활성 칩 수 == 좌석0 손패 수");
                int totalAfterFirst = hand.GetComponentsInChildren<CardView>(true).Length;

                // 같은 크기로 재렌더 → 새 인스턴스 생성 없이 재사용.
                vm.ApplySnapshot(state);
                Assert.AreEqual(expected, ActiveCards(hand));
                Assert.AreEqual(totalAfterFirst, hand.GetComponentsInChildren<CardView>(true).Length,
                    "동일 크기 재렌더는 새 칩을 만들지 않는다(풀 재사용)");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void TrickAndBackRoots_are_subcanvases_without_raycaster()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                ITableView view = new RuntimeTableView();
                view.Bind(new TableViewModel(0), canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                var trick = FindByName(canvasGo, "TrickRow");
                Assert.IsNotNull(trick.GetComponent<Canvas>(), "트릭은 서브 Canvas");
                Assert.IsNull(trick.GetComponent<GraphicRaycaster>(), "트릭은 버튼 없음 → 레이캐스터 불필요");

                foreach (var seat in new[] { 1, 2, 3 })
                {
                    var backs = FindByName(canvasGo, $"Backs{seat}");
                    Assert.IsNotNull(backs, $"Backs{seat} 루트가 있어야 한다");
                    Assert.IsNotNull(backs.GetComponent<Canvas>(), $"Backs{seat} 는 서브 Canvas");
                    Assert.IsNull(backs.GetComponent<GraphicRaycaster>(), $"Backs{seat} 는 레이캐스터 불필요");
                }
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Backs_reuse_pool_on_count_change()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView();
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                vm.ApplySnapshot(GameEngine.NewRound(777UL));
                var backs1 = FindByName(canvasGo, "Backs1");
                int afterDeal = backs1.GetComponentsInChildren<CardView>(true).Length;
                Assert.Greater(afterDeal, 0, "상대 뒷면이 렌더돼야 한다");

                // 같은 스냅샷 재적용 → 인스턴스 수 불변(풀 재사용).
                vm.ApplySnapshot(GameEngine.NewRound(777UL));
                Assert.AreEqual(afterDeal, backs1.GetComponentsInChildren<CardView>(true).Length,
                    "뒷면도 동일 크기 재렌더 시 새 칩을 만들지 않는다");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
    }
}
