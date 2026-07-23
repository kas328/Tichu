using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>B1 Grand 콜 헤드 인코더 검증. 알려진 8장 손패 → 기대 피처 벡터·결정성.</summary>
    public class GrandTichuFeaturesTests
    {
        // 손패: A♦ A♠ K♦ Q♦ J♦ 10♦ 용 봉황 (8장)
        private static List<Card> SampleHand() => new List<Card>
        {
            Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Sword),
            Card.Normal(13, Suit.Jade), Card.Normal(12, Suit.Jade),
            Card.Normal(11, Suit.Jade), Card.Normal(10, Suit.Jade),
            Card.Dragon, Card.Phoenix,
        };

        [Test]
        public void Encode_length_is_FeatureCount()
        {
            Assert.That(GrandTichuFeatures.Encode(SampleHand()).Length, Is.EqualTo(GrandTichuFeatures.FeatureCount));
        }

        [Test]
        public void Encode_sample_hand_matches_expected_vector()
        {
            var x = GrandTichuFeatures.Encode(SampleHand());
            Assert.That(x[0], Is.EqualTo(0.5f).Within(1e-4));    // #A = 2/4
            Assert.That(x[1], Is.EqualTo(0.25f).Within(1e-4));   // #K
            Assert.That(x[2], Is.EqualTo(0.25f).Within(1e-4));   // #Q
            Assert.That(x[3], Is.EqualTo(0.25f).Within(1e-4));   // #J
            Assert.That(x[4], Is.EqualTo(0.25f).Within(1e-4));   // #10
            Assert.That(x[5], Is.EqualTo(1f));                   // 용
            Assert.That(x[6], Is.EqualTo(1f));                   // 봉황
            Assert.That(x[7], Is.EqualTo(0f));                   // 마작
            Assert.That(x[8], Is.EqualTo(0f));                   // 개
            Assert.That(x[9], Is.EqualTo(0.25f).Within(1e-4));   // #페어 = 1/4 (랭크 14가 2장)
            Assert.That(x[10], Is.EqualTo(0f));                  // #트리플
            Assert.That(x[11], Is.EqualTo(0f));                  // #폭탄
            Assert.That(x[12], Is.EqualTo(0.625f).Within(1e-4)); // 최장 스트레이트 5/8 (10..14)
            Assert.That(x[13], Is.EqualTo(0.625f).Within(1e-4)); // 랭크>=11 장수 5/8
            Assert.That(x[14], Is.EqualTo(74f / 112f).Within(1e-4)); // 랭크합 74/112
            Assert.That(x[15], Is.EqualTo(1f));                  // 고특수(용+봉황) 2/2
        }

        [Test]
        public void Encode_is_deterministic()
        {
            var a = GrandTichuFeatures.Encode(SampleHand());
            var b = GrandTichuFeatures.Encode(SampleHand());
            Assert.That(a, Is.EqualTo(b));
        }
    }
}
