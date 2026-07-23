using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// Small Tichu 콜 헤드 입력 인코더. 14장(교환 후) 손패 → 고정 길이 피처 벡터(hand-only).
    /// B1 GrandTichuFeatures 와 동일 의미의 16피처를 14장에 맞게 스케일. 순수·결정적.
    /// 데이터생성과 추론이 이 한 함수를 공유해 train/serve 스큐를 차단한다.
    /// </summary>
    public static class SmallTichuFeatures
    {
        public const int FeatureCount = 16;

        public static float[] Encode(IReadOnlyList<Card> hand)
        {
            var rankCount = new int[16]; // 인덱스 = Rank (마작=1, 일반 2..14)
            int aces = 0, kings = 0, queens = 0, jacks = 0, tens = 0, highCount = 0, rankSum = 0;
            bool hasDragon = false, hasPhoenix = false, hasMahjong = false, hasDog = false;

            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                switch (c.Special)
                {
                    case SpecialKind.Dragon:  hasDragon = true; break;
                    case SpecialKind.Phoenix: hasPhoenix = true; break;
                    case SpecialKind.Mahjong: hasMahjong = true; rankCount[1]++; break;
                    case SpecialKind.Dog:     hasDog = true; break;
                    default: // None (일반 카드)
                        int r = c.Rank;
                        if (r >= 1 && r <= 15) rankCount[r]++;
                        rankSum += r;
                        if (r == 14) aces++;
                        else if (r == 13) kings++;
                        else if (r == 12) queens++;
                        else if (r == 11) jacks++;
                        else if (r == 10) tens++;
                        if (r >= 11) highCount++;
                        break;
                }
            }

            int pairs = 0, triples = 0, bombs = 0;
            for (int r = 1; r <= 15; r++)
            {
                if (rankCount[r] >= 2) pairs++;
                if (rankCount[r] >= 3) triples++;
                if (rankCount[r] == 4) bombs++;
            }
            int longest = LongestStraight(rankCount);

            var x = new float[FeatureCount];
            x[0]  = aces   / 4f;
            x[1]  = kings  / 4f;
            x[2]  = queens / 4f;
            x[3]  = jacks  / 4f;
            x[4]  = tens   / 4f;
            x[5]  = hasDragon  ? 1f : 0f;
            x[6]  = hasPhoenix ? 1f : 0f;
            x[7]  = hasMahjong ? 1f : 0f;
            x[8]  = hasDog     ? 1f : 0f;
            x[9]  = pairs   / 7f;   // 14장: 최대 7 페어
            x[10] = triples / 4f;   // 최대 4 트리플
            x[11] = bombs   / 3f;   // 최대 3 폭탄
            x[12] = longest / 14f;
            x[13] = highCount / 14f;
            x[14] = rankSum / 196f; // 최대 14*14
            x[15] = ((hasDragon ? 1 : 0) + (hasPhoenix ? 1 : 0)) / 2f;
            return x;
        }

        // 존재하는 랭크의 최장 연속 길이(마작=1 포함, 봉황 와일드 미적용).
        private static int LongestStraight(int[] rankCount)
        {
            int best = 0, run = 0;
            for (int r = 1; r <= 14; r++)
            {
                if (rankCount[r] > 0) { run++; if (run > best) best = run; }
                else run = 0;
            }
            return best;
        }
    }
}
