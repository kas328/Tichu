using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;
using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>
    /// Card→Sprite 매핑(미할당/미발견=null → CardView 텍스트 폴백). 아트 도입 시 채움.
    /// </summary>
    [CreateAssetMenu(menuName = "Tichu/Card Sprite Atlas", fileName = "CardSpriteAtlas")]
    public sealed class CardSpriteAtlas : ScriptableObject
    {
        [System.Serializable]
        public struct Entry { public string key; public Sprite sprite; }

        [SerializeField] private Sprite back;
        [SerializeField] private Entry[] faces = new Entry[0];
        [SerializeField] private bool generateArt; // true면 미할당 면/뒷면을 CardArtFactory로 생성

        private Dictionary<string, Sprite> _map;
        private CardArtFactory _factory;
        private CardArtFactory Factory => _factory ?? (_factory = new CardArtFactory());

        public Sprite Back => back != null ? back : (generateArt ? Factory.Back : null);

        /// <summary>면 배경 프레임(생성 아트). PNG 면(Face)이 있으면 CardView가 그쪽을 우선한다.</summary>
        public Sprite Frame(Card card) =>
            generateArt ? Factory.Frame(CardArtFactory.StyleFor(card)) : null;

        public Sprite Face(Card card)
        {
            if (faces == null || faces.Length == 0) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, Sprite>(faces.Length);
                foreach (var e in faces)
                    if (!string.IsNullOrEmpty(e.key)) _map[e.key] = e.sprite;
            }
            return _map.TryGetValue(CardFormat.Key(card), out var s) ? s : null;
        }
    }
}
