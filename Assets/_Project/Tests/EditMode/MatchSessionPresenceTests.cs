using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>M5: 끊긴 좌석은 대리 AI 가 두고, 아무도 없으면 멈추며, 돌아오면 이어진다.</summary>
    public class MatchSessionPresenceTests
    {
        private static MatchSession Session(LoopbackTransport t, IAgent?[] seats, int target = 200) =>
            new MatchSession(seats, t, masterSeed: 42UL, targetScore: target,
                turnTimeout: TimeSpan.FromSeconds(30),
                standInFor: s => new ScriptedAgent());

        private static void PumpUntil(Func<bool> done, int ms, params ScriptedClient[] clients)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(ms);
            while (!done())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("시간 안에 끝나지 않았다");
                foreach (var c in clients) c.Pump();
                Thread.Sleep(1);
            }
        }

        [Test]
        public void Disconnected_seat_is_played_by_its_stand_in_and_the_match_finishes()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), null, new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            try
            {
                session.SetSeatConnected(2, false);               // 좌석 2 의 사람은 끝내 오지 않는다
                session.Start();

                PumpUntil(() => c0.MatchEnded, 30000, c0);

                Assert.That(c0.FaultReason, Is.Null);
                Assert.That(session.Join(TimeSpan.FromSeconds(5)), Is.True);
            }
            finally { session.Stop(); }
        }

        [Test]
        public void Away_flags_reach_every_view()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), null, new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            try
            {
                session.SetSeatConnected(2, false);
                session.Start();
                PumpUntil(() => c0.MatchEnded, 30000, c0);

                Assert.That(session.IsSeatAway(2), Is.True);
                Assert.That(session.IsSeatAway(0), Is.False);
                Assert.That(c0.LastAway, Is.Not.Null);
                Assert.That(c0.LastAway![2], Is.True, "다른 좌석의 화면에 AI 대행이 보여야 한다");
            }
            finally { session.Stop(); }
        }

        [Test]
        public void Views_carry_the_shared_phase_countdown_only_during_grand_tichu_and_exchange()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            try
            {
                session.Start();
                PumpUntil(() => c0.MatchEnded, 30000, c0);

                var grand = c0.PhaseSeconds.Where(p => p.Key == Tichu.Core.Game.RoundPhase.GrandTichuDecision).ToList();
                Assert.That(grand, Is.Not.Empty);
                Assert.That(grand.All(p => p.Value.HasValue && p.Value.Value >= 0 && p.Value.Value <= 30), Is.True);
                var play = c0.PhaseSeconds.Where(p => p.Key == Tichu.Core.Game.RoundPhase.Play).ToList();
                Assert.That(play, Is.Not.Empty);
                Assert.That(play.All(p => !p.Value.HasValue), Is.True, "차례 단계엔 단계 카운트다운이 없다");
            }
            finally { session.Stop(); }
        }

        [Test]
        public void Match_score_is_readable_and_matches_the_end()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            try
            {
                session.Start();
                PumpUntil(() => c0.MatchEnded, 30000, c0);

                var (a, b) = session.MatchScore;
                Assert.That(a + b, Is.Not.EqualTo(0));
                Assert.That(Math.Max(a, b), Is.GreaterThanOrEqualTo(200));
            }
            finally { session.Stop(); }
        }

        [Test]
        public void Session_pauses_with_no_connected_seat_and_resumes_on_return()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() });
            var c0 = new ScriptedClient(0, t);
            try
            {
                session.SetSeatConnected(0, false);               // 유일한 사람이 없다 → 멈춤
                session.Start();

                Thread.Sleep(500);
                var drained = t.Inbox(0);   // 소비하지 않고 본다
                Assert.That(session.Join(TimeSpan.Zero), Is.False, "아무도 없으면 판이 AI 끼리 끝나면 안 된다");
                Assert.That(drained.Count, Is.LessThan(10), "멈춘 동안 진행이 거의 없어야 한다(첫 질문 직전까지)");

                session.SetSeatConnected(0, true);
                PumpUntil(() => c0.MatchEnded, 30000, c0);
                Assert.That(c0.FaultReason, Is.Null);
            }
            finally { session.Stop(); }
        }

        [Test]
        public void Session_paused_by_two_seats_resumes_when_a_different_seat_returns()
        {
            var t = new LoopbackTransport();
            var session = Session(t, new IAgent?[] { null, new ScriptedAgent(), null, new ScriptedAgent() });
            try
            {
                session.SetSeatConnected(0, false);
                session.SetSeatConnected(2, false);               // 사람 둘 다 없다 → 멈춤
                session.Start();

                Thread.Sleep(300);
                Assert.That(session.Join(TimeSpan.Zero), Is.False, "아무도 없으면 멈춰 있어야 한다");

                session.SetSeatConnected(2, true);                // 다른 좌석의 사람이 먼저 돌아온다
                var c2 = new ScriptedClient(2, t);
                PumpUntil(() => c2.MatchEnded, 30000, c2);
                Assert.That(c2.FaultReason, Is.Null);
            }
            finally { session.Stop(); }
        }
    }
}
