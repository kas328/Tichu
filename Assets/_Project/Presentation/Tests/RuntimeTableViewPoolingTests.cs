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
    }
}
