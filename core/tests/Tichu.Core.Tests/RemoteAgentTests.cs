using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// RemoteAgent — 원격 좌석. 액션이 도착할 때까지 블로킹하고, 묻기 전에 온 액션도
    /// 보관하며(교환 동시 제출), 불법 입력은 상태를 바꾸지 않고 거부한 뒤 다시 기다린다.
    /// </summary>
    public class RemoteAgentTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private sealed class Harness
        {
            public readonly List<ServerEvent> Sent = new List<ServerEvent>();
            public readonly List<Awaiting> Awaits = new List<Awaiting>();
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            private long _arrival;

            public RemoteAgent Create(int seat, TimeSpan? timeout = null) => new RemoteAgent(
                seat,
                (s, ev) => { lock (Sent) Sent.Add(ev); },
                (s, aw) => { lock (Awaits) Awaits.Add(aw); },
                () => Interlocked.Increment(ref _arrival),
                timeout ?? TimeSpan.FromSeconds(5),
                Cts.Token);

            public int RejectCount
            {
                get { lock (Sent) { int n = 0; foreach (var e in Sent) if (e.Kind == ServerEventKind.ActionRejected) n++; return n; } }
            }
        }

        private static DecisionContext LeadContext(out GameState state)
        {
            state = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));
            return GameFlowHelpers.Context(state, 0);
        }

        [Test]
        public void Action_buffered_before_the_question_is_consumed_without_blocking()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var s = GameEngine.NewRound(42UL);

            // 아직 아무것도 묻지 않은 상태에서 큰 티츄 패스가 먼저 도착한다.
            agent.Submit(ClientAction.DeclineGrandTichu(0, 1L));

            var ctx = new DecisionContext(s, 0);
            Assert.That(agent.CallGrandTichu(ctx), Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(0), "미리 온 액션을 거부해선 안 된다");
        }

        [Test]
        public void Wrong_seat_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.DeclineGrandTichu(seat: 2, actionSeq: 1L));

            Assert.That(h.RejectCount, Is.EqualTo(1));
        }

        [Test]
        public void Duplicate_or_backward_action_seq_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.CallTichu(0, 5L));
            agent.Submit(ClientAction.CallTichu(0, 5L)); // 중복
            agent.Submit(ClientAction.CallTichu(0, 3L)); // 역행

            Assert.That(h.RejectCount, Is.EqualTo(2));
        }

        [Test]
        public void Reserved_tichu_is_taken_once()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.CallTichu(0, 1L));

            Assert.That(agent.TakeReservedTichu(), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.False, "예약은 한 번만 소비된다");
        }

        [Test]
        public void DecideTurn_returns_the_submitted_legal_move()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False);
            Assert.That(d.Move, Is.Not.Null);
            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Illegal_move_is_rejected_and_the_agent_keeps_waiting()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            // 손에 없는 카드를 먼저 보내고, 그 다음 합법수를 보낸다.
            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(13, Suit.Star) }, rank: 0, wish: null));
            agent.Submit(ClientAction.Play(0, 2L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(1), "불법 입력 1건만 거부돼야 한다");
        }

        [Test]
        public void Illegal_pass_on_lead_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Pass(0, 1L));                      // 리드에서는 패스 불가
            agent.Submit(ClientAction.Play(0, 2L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(1));
        }

        [Test]
        public void Awaiting_is_announced_with_legal_moves_before_blocking()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));
            agent.DecideTurn(ctx);

            Assert.That(h.Awaits.Count, Is.GreaterThanOrEqualTo(1));
            var aw = h.Awaits[0];
            Assert.That(aw.Kind, Is.EqualTo(StepKind.Play));
            Assert.That(aw.Seat, Is.EqualTo(0));
            Assert.That(aw.LegalMoves, Is.Not.Null.And.Count.GreaterThan(0));
            Assert.That(aw.Redacted().LegalMoves, Is.Null, "축약판엔 합법수가 없어야 한다");
        }

        [Test]
        public void Timeout_throws()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(50));
            var ctx = LeadContext(out _);

            Assert.Throws<TimeoutException>(() => agent.DecideTurn(ctx));
        }

        [Test]
        public void Cancellation_unblocks_the_wait()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(30));
            var ctx = LeadContext(out _);

            var t = new Thread(() =>
            {
                Thread.Sleep(50);
                h.Cts.Cancel();
            }) { IsBackground = true };
            t.Start();

            Assert.Throws<OperationCanceledException>(() => agent.DecideTurn(ctx));
        }
    }
}
