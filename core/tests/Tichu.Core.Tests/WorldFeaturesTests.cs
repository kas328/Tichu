using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>D4 Fork A 리프 인코더 검증 — 결정화 세계 → 관측좌석 상대 집계 피처.</summary>
    public class WorldFeaturesTests
    {
        private static List<Card> H(params Card[] c) => new List<Card>(c);
        private static Card N(int r, Suit s) => Card.Normal(r, s);

        // seat0: 용 A K Q 5 6(6장) · seat1: 봉황 + 7네장(폭탄) + 2(6장) · seat2: 3 4 5(3장) · seat3: 8 9 10 J(4장)
        private static GameState World()
        {
            return GameFlowHelpers.PlayState(0,
                H(Card.Dragon, N(14, Suit.Jade), N(13, Suit.Jade), N(12, Suit.Jade), N(5, Suit.Jade), N(6, Suit.Jade)),
                H(Card.Phoenix, N(7, Suit.Jade), N(7, Suit.Sword), N(7, Suit.Pagoda), N(7, Suit.Star), N(2, Suit.Jade)),
                H(N(3, Suit.Sword), N(4, Suit.Sword), N(5, Suit.Sword)),
                H(N(8, Suit.Star), N(9, Suit.Star), N(10, Suit.Star), N(11, Suit.Star)));
        }

        [Test]
        public void Encode_length_is_FeatureCount()
        {
            Assert.That(WorldFeatures.Encode(World(), 0).Length, Is.EqualTo(WorldFeatures.FeatureCount));
        }

        [Test]
        public void Encode_per_seat_and_global_features()
        {
            var x = WorldFeatures.Encode(World(), 0);
            // seat0 (rel0, base 0): 6장·nHigh 3(A,K,Q)·용·폭탄X
            Assert.That(x[0], Is.EqualTo(6f / 14f).Within(1e-4));
            Assert.That(x[1], Is.EqualTo(3f / 8f).Within(1e-4));
            Assert.That(x[2], Is.EqualTo(1f)); // 용
            Assert.That(x[3], Is.EqualTo(0f)); // 봉황X
            Assert.That(x[4], Is.EqualTo(0f)); // 폭탄X
            // seat1 (rel1, base 8): 6장·nHigh 0·봉황·폭탄(7네장)
            Assert.That(x[8], Is.EqualTo(6f / 14f).Within(1e-4));
            Assert.That(x[9], Is.EqualTo(0f));
            Assert.That(x[11], Is.EqualTo(1f)); // 봉황
            Assert.That(x[12], Is.EqualTo(1f)); // 폭탄(7×4)
            // seat3 (rel3, base 24): 4장·nHigh 1(J)
            Assert.That(x[24], Is.EqualTo(4f / 14f).Within(1e-4));
            Assert.That(x[25], Is.EqualTo(1f / 8f).Within(1e-4));
            // 글로벌: turn=0=observer → rel0 원핫
            Assert.That(x[32], Is.EqualTo(1f)); // turn self
            Assert.That(x[33], Is.EqualTo(0f));
            Assert.That(x[36], Is.EqualTo(0f)); // 트릭 없음
            Assert.That(x[41], Is.EqualTo(19f / 56f).Within(1e-4)); // 손패합 (6+6+3+4)/56
        }

        [Test]
        public void Encode_is_observer_relative()
        {
            // 같은 세계를 관측좌석 2(파트너)에서 인코딩하면 seat2가 rel0(자기)로 회전.
            var x2 = WorldFeatures.Encode(World(), 2);
            Assert.That(x2[0], Is.EqualTo(3f / 14f).Within(1e-4)); // rel0 = seat2 (3장)
            Assert.That(x2[34], Is.EqualTo(1f)); // turn=0=seat0 = rel2(파트너) 원핫
        }

        [Test]
        public void Encode_is_deterministic()
        {
            Assert.That(WorldFeatures.Encode(World(), 0), Is.EqualTo(WorldFeatures.Encode(World(), 0)));
        }
    }
}
