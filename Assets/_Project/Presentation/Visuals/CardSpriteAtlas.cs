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

        private Dictionary<string, Sprite> _map;

        public Sprite Back => back;

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
