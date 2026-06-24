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

            // 3) 버려진 개 보정.
            //    개는 플레이되면 어떤 더미에도 들지 않고 버려진다(GameEngine.ApplyDog) — 손패/트릭/획득 어디에도
            //    자취가 없다. 미플레이 개는 정상 미관측 카드지만, 버려진 개는 풀에 유령으로 남는다.
            //    유령 개는 풀을 상대 손패 합보다 정확히 1 크게 만드는 유일한 원인이므로 그 경우에만 제거한다
            //    (개가 상대 손에 있으면 pool==need 이라 제거되지 않고 정상 분배된다).
            int need = 0;
            for (int i = 0; i < 4; i++)
                if (i != observerSeat) need += clone.Seats[i].Hand.Count;

            if (pool.Count == need + 1)
                pool.Remove(Card.Dog);

            // 4) 폐쇄 불변식: 보정 후 풀 크기 == 상대 좌석 손패 장수 합.
            if (need != pool.Count)
                throw new InvalidOperationException(
                    $"determinize closure mismatch: pool={pool.Count} need={need} (observer={observerSeat})");

            // 5) 풀 셔플(주입 Rng).
            Deck.Shuffle(pool, ref rng);

            // 6) 상대 좌석에 순차 분배(공개된 장수 정확히 일치).
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
