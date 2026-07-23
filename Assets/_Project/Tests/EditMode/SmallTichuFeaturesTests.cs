using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>Small Tichu 콜 헤드 인코더(14장) 검증 — 알려진 손패 → 기대 피처·결정성.</summary>
    public class SmallTichuFeaturesTests
    {
        // 14장: A♦ A♠ A♣ K♦ K♠ Q♦ J♦ 10♦ 9♦ 8♦ 7♦ 용 봉황 마작
        private static List<Card> SampleHand() => new List<Card>
        {
            Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Sword), Card.Normal(14, Suit.Pagoda),
            Card.Normal(13, Suit.Jade), Card.Normal(13, Suit.Sword),
            Card.Normal(12, Suit.Jade), Card.Normal(11, Suit.Jade), Card.Normal(10, Suit.Jade),
            Card.Normal(9, Suit.Jade), Card.Normal(8, Suit.Jade), Card.Normal(7, Suit.Jade),
            Card.Dragon, Card.Phoenix, Card.Mahjong,
        };

        [Test]
        public void Encode_length_is_FeatureCount()
        {
            Assert.That(SmallTichuFeatures.Encode(SampleHand()).Length, Is.EqualTo(SmallTichuFeatures.FeatureCount));
        }

        [Test]
        public void Encode_sample_hand_matches_expected_vector()
        {
            var x = SmallTichuFeatures.Encode(SampleHand());
            Assert.That(x[0], Is.EqualTo(0.75f).Within(1e-4));      // #A = 3/4
            Assert.That(x[1], Is.EqualTo(0.5f).Within(1e-4));       // #K = 2/4
            Assert.That(x[2], Is.EqualTo(0.25f).Within(1e-4));     // #Q
            Assert.That(x[3], Is.EqualTo(0.25f).Within(1e-4));     // #J
            Assert.That(x[4], Is.EqualTo(0.25f).Within(1e-4));     // #10
            Assert.That(x[5], Is.EqualTo(1f));                     // 용
            Assert.That(x[6], Is.EqualTo(1f));                     // 봉황
            Assert.That(x[7], Is.EqualTo(1f));                     // 마작
            Assert.That(x[8], Is.EqualTo(0f));                     // 개
            Assert.That(x[9], Is.EqualTo(2f / 7f).Within(1e-4));   // #페어 = 2/7 (K×2, A×3)
            Assert.That(x[10], Is.EqualTo(0.25f).Within(1e-4));    // #트리플 = 1/4 (A×3)
            Assert.That(x[11], Is.EqualTo(0f));                    // #폭탄
            Assert.That(x[12], Is.EqualTo(8f / 14f).Within(1e-4)); // 최장 스트레이트 8/14 (7..14)
            Assert.That(x[13], Is.EqualTo(0.5f).Within(1e-4));     // 랭크>=11 장수 7/14
            Assert.That(x[14], Is.EqualTo(125f / 196f).Within(1e-4)); // 랭크합 125/196
            Assert.That(x[15], Is.EqualTo(1f));                    // 고특수(용+봉황) 2/2
        }

        [Test]
        public void Encode_is_deterministic()
        {
            Assert.That(SmallTichuFeatures.Encode(SampleHand()), Is.EqualTo(SmallTichuFeatures.Encode(SampleHand())));
        }
    }
}
