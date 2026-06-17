using System.Threading;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// ITableView 구독 계약 테스트(D0). 현행 RuntimeTableView 및 이후 PrefabTableView(D2)가
    /// 동일 계약을 만족하는지 컴파일·런타임 양쪽에서 강제한다.
    /// </summary>
    [TestFixture]
    public class ITableViewContractTests
    {
        [Test]
        public void RuntimeTableView_Implements_ITableView()
        {
            Assert.IsTrue(typeof(ITableView).IsAssignableFrom(typeof(RuntimeTableView)),
                "RuntimeTableView must implement the ITableView contract.");
        }

        [Test]
        public void Bind_ThroughInterface_BuildsHierarchy_AndSurvivesSnapshot()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var canvas = canvasGo.GetComponent<Canvas>();
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView();

                view.Bind(vm, canvas, CancellationToken.None);

                Assert.Greater(canvas.transform.childCount, 0,
                    "Bind must build the UI hierarchy under the canvas.");

                // 진실 스냅샷 투영이 구독자(뷰)를 NRE 없이 통과해야 한다 = 구독 계약 와이어링.
                var state = GameEngine.NewRound(12345UL);
                Assert.DoesNotThrow(() => vm.ApplySnapshot(state),
                    "Projecting a snapshot must not throw through the bound view.");
            }
            finally
            {
                Object.DestroyImmediate(canvasGo);
            }
        }
    }
}
