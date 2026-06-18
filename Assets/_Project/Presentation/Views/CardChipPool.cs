using System.Collections.Generic;
using UnityEngine;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// CardView 전용 루트별 풀(D3). Destroy+Instantiate 대신 SetActive 토글로 재사용한다.
    /// Begin → Next ×N → End: 커서 이후의 칩을 비활성화한다(파괴하지 않음).
    /// 칩은 생성 순서대로 root 의 자식이 되고 그 순서로 재사용되므로 sibling 순서 == 채움 순서.
    /// </summary>
    public sealed class CardChipPool
    {
        private readonly RectTransform _root;
        private readonly CardView _prefab;
        private readonly List<CardView> _items = new List<CardView>();
        private int _cursor;

        public CardChipPool(RectTransform root, CardView prefab)
        {
            _root = root;
            _prefab = prefab;
        }

        public int CreatedCount => _items.Count;
        public int ActiveCount => _cursor;
        public int FreeCount => _items.Count - _cursor;

        public void Begin() => _cursor = 0;

        public CardView Next()
        {
            CardView cv;
            if (_cursor < _items.Count) cv = _items[_cursor];
            else { cv = Object.Instantiate(_prefab, _root); _items.Add(cv); }
            cv.gameObject.SetActive(true);
            _cursor++;
            return cv;
        }

        public void End()
        {
            for (int i = _cursor; i < _items.Count; i++)
                _items[i].gameObject.SetActive(false);
        }
    }
}
