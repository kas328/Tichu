using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// LoopbackTransport — 같은 프로세스 전송. 좌석별 수신함이 섞이지 않는 것과
    /// 클라이언트 제출이 세션 핸들러에 닿는 것을 검증한다.
    /// </summary>
    public class LoopbackTransportTests
    {
        [Test]
        public void SendTo_delivers_only_to_the_target_seat()
        {
            var t = new LoopbackTransport();
            t.SendTo(2, ServerEvent.ActionRejected(7L, "nope"));

            Assert.That(t.Inbox(2).Count, Is.EqualTo(1));
            Assert.That(t.Inbox(2)[0].Kind, Is.EqualTo(ServerEventKind.ActionRejected));
            Assert.That(t.Inbox(2)[0].Reason, Is.EqualTo("nope"));
            Assert.That(t.Inbox(2)[0].ActionSeq, Is.EqualTo(7L));

            foreach (int other in new[] { 0, 1, 3 })
                Assert.That(t.Inbox(other).Count, Is.EqualTo(0), $"seat {other} 수신함이 오염됐다");
        }

        [Test]
        public void Submit_reaches_the_registered_handler()
        {
            var t = new LoopbackTransport();
            var seen = new List<ClientAction>();
            t.OnClientAction(a => seen.Add(a));

            t.Submit(ClientAction.Pass(seat: 1, actionSeq: 1L));

            Assert.That(seen.Count, Is.EqualTo(1));
            Assert.That(seen[0].Kind, Is.EqualTo(ClientActionKind.Pass));
            Assert.That(seen[0].Seat, Is.EqualTo(1));
            Assert.That(seen[0].ActionSeq, Is.EqualTo(1L));
        }

        [Test]
        public void Drain_takes_events_once_and_empties_the_inbox()
        {
            var t = new LoopbackTransport();
            t.SendTo(0, ServerEvent.ActionRejected(1L, "a"));
            t.SendTo(0, ServerEvent.ActionRejected(2L, "b"));

            Assert.That(t.Drain(0).Count, Is.EqualTo(2));
            Assert.That(t.Drain(0).Count, Is.EqualTo(0), "두 번째 Drain 은 비어야 한다");
            Assert.That(t.Inbox(0).Count, Is.EqualTo(0));
        }

        [Test]
        public void Submit_without_handler_is_a_no_op()
        {
            var t = new LoopbackTransport();
            Assert.DoesNotThrow(() => t.Submit(ClientAction.Pass(seat: 0, actionSeq: 1L)));
        }

        [Test]
        public void Play_action_carries_cards_and_rank()
        {
            var cards = new List<Card> { Card.Normal(5, Suit.Jade) };
            var a = ClientAction.Play(seat: 3, actionSeq: 4L, cards: cards, rank: 10, wish: null);

            Assert.That(a.Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(a.Cards, Is.EquivalentTo(cards));
            Assert.That(a.Rank, Is.EqualTo(10));
            Assert.That(a.Wish, Is.Null);
        }
    }
}
