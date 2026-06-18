using NUnit.Framework;
using Tichu.Presentation.Views;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class CardChipPoolTests
    {
        private static CardChipPool MakePool(out GameObject rootGo, out GameObject prefabGo)
        {
            rootGo = new GameObject("root", typeof(RectTransform));
            prefabGo = new GameObject("prefab", typeof(RectTransform));
            var prefab = prefabGo.AddComponent<CardView>();
            return new CardChipPool(rootGo.GetComponent<RectTransform>(), prefab);
        }

        private static void Build(CardChipPool pool, int n)
        {
            pool.Begin();
            for (int i = 0; i < n; i++) pool.Next();
            pool.End();
        }

        [Test]
        public void Reuses_instances_on_shrink_then_regrow_without_new_alloc()
        {
            var pool = MakePool(out var rootGo, out var prefabGo);
            Build(pool, 5);
            Assert.AreEqual(5, pool.CreatedCount);
            Assert.AreEqual(5, pool.ActiveCount);

            Build(pool, 3);
            Assert.AreEqual(5, pool.CreatedCount, "축소는 새 인스턴스를 만들지 않는다");
            Assert.AreEqual(3, pool.ActiveCount);
            Assert.AreEqual(2, pool.FreeCount);

            Build(pool, 5);
            Assert.AreEqual(5, pool.CreatedCount, "재확장은 비활성 인스턴스를 재사용한다");
            Assert.AreEqual(5, pool.ActiveCount);

            Object.DestroyImmediate(rootGo);
            Object.DestroyImmediate(prefabGo);
        }

        [Test]
        public void Released_chips_are_deactivated_under_root()
        {
            var pool = MakePool(out var rootGo, out var prefabGo);
            var root = rootGo.GetComponent<RectTransform>();
            Build(pool, 5);
            Build(pool, 2);

            int active = 0, inactive = 0;
            foreach (Transform c in root)
                if (c.gameObject.activeSelf) active++; else inactive++;

            Assert.AreEqual(2, active, "활성 칩은 현재 패스 개수");
            Assert.AreEqual(3, inactive, "잉여 칩은 비활성(파괴 아님)");
            Assert.AreEqual(2, pool.ActiveCount);
            Assert.AreEqual(3, pool.FreeCount);

            Object.DestroyImmediate(rootGo);
            Object.DestroyImmediate(prefabGo);
        }
    }
}
