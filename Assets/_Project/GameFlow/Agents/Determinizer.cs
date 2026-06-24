using System;
using System.Collections.Generic;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 미관측 손패를 1개 "세계"로 결정화한다.
    /// 관측 좌석의 손패·공개정보는 절대 변형하지 않으며(치트 방지),
    /// 미관측 풀(56장 − 관측 손패 − 공개된 모든 카드)을 상대 좌석의 공개된 장수에 맞게 재분배한다.
    /// 분배 결과는 56장 폐쇄를 유지하므로 GameEngine.Apply 가 거부하지 않는다.
    /// </summary>
    public static class Determinizer
    {
        /// <summary>
        /// src 의 복제본에서 observerSeat 외 좌석 손패를 미관측 풀로 재분배한 새 GameState 를 반환한다.
        /// src 는 변형하지 않는다. 56장 폐쇄가 맞지 않으면(불완전 상태) InvalidOperationException.
        /// </summary>
        public static GameState Sample(GameState src, int observerSeat, ref Rng rng)
        {
            var clone = src.Clone();

            // 1) 관측자에게 보이는 모든 카드(= 미관측 풀에서 제외할 카드).
            var visible = new HashSet<Card>();
            foreach (var c in clone.Seats[observerSeat].Hand) visible.Add(c);
            for (int i = 0; i < 4; i++)
                foreach (var c in clone.Seats[i].WonCards) visible.Add(c);
            if (clone.CurrentTrick != null)
                foreach (var p in clone.CurrentTrick.History)
                    if (p.Combination != null)
                        foreach (var c in p.Combination.Cards) visible.Add(c);
            foreach (var t in clone.CompletedTricks)
                foreach (var p in t.History)
                    if (p.Combination != null)
                        foreach (var c in p.Combination.Cards) visible.Add(c);

            // 2) 미관측 풀 = 표준 56장 − 보이는 카드.
            var pool = new List<Card>();
            foreach (var c in Deck.CreateStandard())
                if (!visible.Contains(c)) pool.Add(c);

            // 3) 폐쇄 불변식: 풀 크기 == 상대 좌석 손패 장수 합.
            int need = 0;
            for (int i = 0; i < 4; i++)
                if (i != observerSeat) need += clone.Seats[i].Hand.Count;
            if (need != pool.Count)
                throw new InvalidOperationException(
                    $"determinize closure mismatch: pool={pool.Count} need={need} (observer={observerSeat})");

            // 4) 풀 셔플(주입 Rng).
            Deck.Shuffle(pool, ref rng);

            // 5) 상대 좌석에 순차 분배(공개된 장수 정확히 일치).
            int idx = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == observerSeat) continue;
                int count = clone.Seats[i].Hand.Count;
                var newHand = new List<Card>(count);
                for (int k = 0; k < count; k++) newHand.Add(pool[idx++]);
                clone.Seats[i].Hand = newHand;
            }

            return clone;
        }
    }
}
