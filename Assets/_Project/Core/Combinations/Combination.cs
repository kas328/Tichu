using System;
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    public enum CombinationType
    {
        Invalid,
        Single,
        Pair,
        Triple,
        FullHouse,
        Straight,
        ConsecutivePairs,
        FourBomb,
        StraightFlushBomb
    }

    /// <summary>판별된 조합. Rank는 ×2 스케일(일반값 v→2v, 봉황 단독 반칸→홀수).</summary>
    public sealed class Combination
    {
        public CombinationType Type { get; }
        public IReadOnlyList<Card> Cards { get; }
        public int Length { get; }
        public int Rank { get; }
        public int PointsInPlay { get; }

        public bool IsBomb =>
            Type == CombinationType.FourBomb || Type == CombinationType.StraightFlushBomb;

        public Combination(CombinationType type, IReadOnlyList<Card> cards, int length, int rank, int pointsInPlay)
        {
            Type = type; Cards = cards; Length = length; Rank = rank; PointsInPlay = pointsInPlay;
        }

        public static readonly Combination Invalid =
            new Combination(CombinationType.Invalid, Array.Empty<Card>(), 0, 0, 0);
    }

    /// <summary>봉황 단독 비교값 산출에 필요한 트릭 문맥.</summary>
    public readonly struct TrickContext
    {
        public readonly bool IsLead;                  // 따라갈 트릭이 없으면 true
        public readonly bool TopIsSingle;             // 현재 Top이 단독인가
        public readonly int CurrentSingleRankScaled;  // TopIsSingle일 때 그 단독의 ×2 Rank

        public TrickContext(bool isLead, bool topIsSingle, int currentSingleRankScaled)
        {
            IsLead = isLead; TopIsSingle = topIsSingle; CurrentSingleRankScaled = currentSingleRankScaled;
        }

        public static readonly TrickContext Lead = new TrickContext(true, false, 0);
    }
}
