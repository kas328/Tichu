using System;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.Presentation.Audio;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// SfxMap 순수 매핑(UnityEngine 무의존)의 EditMode 전수 검증.
    /// 진실 로그(GameAction)·콜(TichuCall) → SfxId. 폭탄 판정은 RuntimeTableView.FormatAction과 동일 방식.
    /// </summary>
    public class SfxMapTests
    {
        // ── SfxMap.For(GameAction) ───────────────────────────────────────────

        [Test]
        public void For_normal_play_returns_CardPlay()
        {
            var a = GameAction.Play(0, new[] { Card.Normal(7, Suit.Jade) });
            Assert.That(SfxMap.For(a), Is.EqualTo(SfxId.CardPlay));
        }

        [Test]
        public void For_four_of_a_kind_returns_Bomb()
        {
            var a = GameAction.Play(0, new[]
            {
                Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda),
            });
            Assert.That(SfxMap.For(a), Is.EqualTo(SfxId.Bomb));
        }

        [Test]
        public void For_straight_flush_returns_Bomb()
        {
            var a = GameAction.Play(0, new[]
            {
                Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade),
            });
            Assert.That(SfxMap.For(a), Is.EqualTo(SfxId.Bomb));
        }

        [Test]
        public void For_pass_returns_Pass()
        {
            Assert.That(SfxMap.For(GameAction.Pass(0)), Is.EqualTo(SfxId.Pass));
        }

        [Test]
        public void For_give_dragon_returns_GiveDragon()
        {
            Assert.That(SfxMap.For(GameAction.GiveDragon(0, 1)), Is.EqualTo(SfxId.GiveDragon));
        }

        [Test]
        public void For_non_play_action_returns_None()
        {
            Assert.That(SfxMap.For(GameAction.CallGrandTichu(0)), Is.EqualTo(SfxId.None));
            Assert.That(SfxMap.For(GameAction.CallTichu(0)), Is.EqualTo(SfxId.None));
        }

        [Test]
        public void For_play_with_empty_cards_returns_CardPlay()
        {
            // S1: nullable/빈 Cards 가드 — Recognize 진입 전 CardPlay 폴백(NRE 방지).
            var a = GameAction.Play(0, Array.Empty<Card>());
            Assert.That(SfxMap.For(a), Is.EqualTo(SfxId.CardPlay));
        }

        [Test]
        public void For_play_with_null_cards_returns_CardPlay()
        {
            var a = GameAction.Play(0, null);
            Assert.That(SfxMap.For(a), Is.EqualTo(SfxId.CardPlay));
        }

        // ── SfxMap.ForCall(TichuCall) ────────────────────────────────────────

        [Test]
        public void ForCall_none_returns_None()
        {
            Assert.That(SfxMap.ForCall(TichuCall.None), Is.EqualTo(SfxId.None));
        }

        [Test]
        public void ForCall_tichu_returns_TichuCall()
        {
            Assert.That(SfxMap.ForCall(TichuCall.Tichu), Is.EqualTo(SfxId.TichuCall));
        }

        [Test]
        public void ForCall_grand_tichu_returns_GrandTichuCall()
        {
            Assert.That(SfxMap.ForCall(TichuCall.GrandTichu), Is.EqualTo(SfxId.GrandTichuCall));
        }
    }
}
