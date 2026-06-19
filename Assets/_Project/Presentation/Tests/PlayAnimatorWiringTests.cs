using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Tests
{
    public class PlayAnimatorWiringTests
    {
        private sealed class RecordingAnimator : IPlayAnimator
        {
            public int Turn;
            public void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward) { }
            public void TurnChanged(Text activeSeatLabel) { Turn++; }
            public void ResultShown(RectTransform banner) { }
        }

        [Test]
        public void TurnChange_routes_to_injected_animator()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var rec = new RecordingAnimator();
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView(rec);
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                int before = rec.Turn;        // 초기 -1 구독은 호출 안 됨(차례 없음)
                vm.CurrentTurn.Value = 2;      // 파트너 차례
                Assert.AreEqual(before + 1, rec.Turn, "차례 변경이 주입된 애니메이터로 라우팅돼야 한다");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Default_ctor_uses_noop_and_does_not_throw()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView(); // 기본 = NoOp
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);
                Assert.DoesNotThrow(() => vm.CurrentTurn.Value = 1);
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
    }
}
