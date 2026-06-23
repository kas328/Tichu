using System.Collections.Generic;
using UnityEngine;

namespace Tichu.Presentation.Audio
{
    /// <summary>
    /// SfxId → AudioClip 매핑(ScriptableObject). CardSpriteAtlas.Face 패턴 미러:
    /// clips 비었거나 미등록이면 null(소비자가 무음 처리). _map 지연 빌드.
    /// AudioBank.asset 미할당이면 Resources.Load 가 null → 소비자가 NoOp 폴백.
    /// </summary>
    [CreateAssetMenu(menuName = "Tichu/Audio Bank", fileName = "AudioBank")]
    public sealed class AudioBank : ScriptableObject
    {
        [System.Serializable]
        public struct Entry { public SfxId id; public AudioClip clip; }

        [SerializeField] private Entry[] clips = new Entry[0];
        private Dictionary<SfxId, AudioClip> _map;

        public AudioClip Clip(SfxId id)
        {
            if (clips == null || clips.Length == 0) return null;
            if (_map == null)
            {
                _map = new Dictionary<SfxId, AudioClip>(clips.Length);
                foreach (var e in clips) _map[e.id] = e.clip;
            }
            return _map.TryGetValue(id, out var c) ? c : null;
        }
    }
}
