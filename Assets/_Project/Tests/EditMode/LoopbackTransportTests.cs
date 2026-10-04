using System;
using System.Collections.Generic;
using System.Threading;
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

        [Test]
        public void Concurrent_sends_and_drains_lose_no_events()
        {
            // SendTo 는 세션 스레드에서, Drain 은 클라이언트 스레드에서 온다 —
            // _gate 가 보호해야 하는 바로 그 조합이다. 유실도 중복도 없어야 한다.
            const int total = 2000;
            var transport = new LoopbackTransport();
            var received = new List<ServerEvent>();
            Exception? producerFailure = null;

            var producer = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < total; i++)
                        transport.SendTo(0, ServerEvent.ActionRejected(i, "probe"));
                }
                catch (Exception e) { producerFailure = e; }
            }) { IsBackground = true };

            producer.Start();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (received.Count < total && DateTime.UtcNow < deadline)
                received.AddRange(transport.Drain(0));

            producer.Join(TimeSpan.FromSeconds(10));
            received.AddRange(transport.Drain(0));

            Assert.That(producerFailure, Is.Null, "SendTo 가 예외를 던졌다");
            Assert.That(received.Count, Is.EqualTo(total), "이벤트가 유실되거나 중복됐다");

            var seen = new HashSet<long>();
            foreach (var ev in received)
                Assert.That(seen.Add(ev.ActionSeq), Is.True, $"ActionSeq {ev.ActionSeq} 가 중복 전달됐다");

            for (int i = 0; i < total; i++)
                Assert.That(seen.Contains(i), Is.True, $"ActionSeq {i} 가 유실됐다");
        }
    }
}
