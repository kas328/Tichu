using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// MatchSession 오라클 — AI 4좌석을 세션 경로로 돌린 라운드가 GameDriver 직접
    /// 실행과 상태 해시·액션 로그까지 동일해야 한다. 세션이 게임 규칙에 끼어들지
    /// 않는다는 증명이다.
    /// </summary>
    public class MatchSessionOracleTests
    {
        private static IAgent?[] FourScripted() => new IAgent?[]
        {
            new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent()
        };

        private static RoundOutcome RunFirstRoundThroughSession(ulong masterSeed)
        {
            var transport = new LoopbackTransport();
            RoundOutcome? captured = null;
            using var done = new ManualResetEventSlim(false);

            var session = new MatchSession(
                FourScripted(), transport, masterSeed,
                targetScore: 1,
                onRoundCompleted: o => { if (captured == null) { captured = o; done.Set(); } });

            session.Start();
            Assert.That(done.Wait(TimeSpan.FromSeconds(20)), Is.True, "세션이 첫 라운드를 끝내지 못했다");
            session.Stop();
            session.Join(TimeSpan.FromSeconds(5));

            Assert.That(captured, Is.Not.Null);
            return captured!;
        }

        [TestCase(1UL)]
        [TestCase(42UL)]
        [TestCase(12345UL)]
        public void Session_round_is_bit_identical_to_direct_driver(ulong masterSeed)
        {
            var master = new Rng(masterSeed);
            ulong roundSeed = master.NextULong();

            var direct = new GameDriver(new IAgent[]
            {
                new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent()
            }).RunRound(GameEngine.NewRound(roundSeed));

            var viaSession = RunFirstRoundThroughSession(masterSeed);

            Assert.That(viaSession.State.ComputeHash(), Is.EqualTo(direct.State.ComputeHash()),
                "세션 경로의 상태 해시가 직접 실행과 다르다");
            Assert.That(viaSession.Log.Count, Is.EqualTo(direct.Log.Count), "로그 길이가 다르다");
            for (int i = 0; i < direct.Log.Count; i++)
            {
                Assert.That(viaSession.Log[i].Kind, Is.EqualTo(direct.Log[i].Kind), $"로그 {i} 종류");
                Assert.That(viaSession.Log[i].Seat, Is.EqualTo(direct.Log[i].Seat), $"로그 {i} 좌석");
            }
        }

        [Test]
        public void Every_seat_receives_a_state_update_per_applied_action()
        {
            var transport = new LoopbackTransport();
            RoundOutcome? captured = null;
            using var done = new ManualResetEventSlim(false);

            var session = new MatchSession(
                FourScripted(), transport, 42UL,
                targetScore: 1,
                onRoundCompleted: o => { if (captured == null) { captured = o; done.Set(); } });

            session.Start();
            Assert.That(done.Wait(TimeSpan.FromSeconds(20)), Is.True);
            session.Stop();
            session.Join(TimeSpan.FromSeconds(5));

            for (int seat = 0; seat < 4; seat++)
            {
                int updates = 0;
                foreach (var ev in transport.Inbox(seat))
                    if (ev.Kind == ServerEventKind.StateUpdate) updates++;

                Assert.That(updates, Is.GreaterThanOrEqualTo(captured!.Log.Count),
                    $"seat {seat} 가 받은 StateUpdate 가 적용 액션 수보다 적다");
            }
        }

        [Test]
        public void Masking_holds_in_every_broadcast_state_update()
        {
            var transport = new LoopbackTransport();
            using var done = new ManualResetEventSlim(false);

            var session = new MatchSession(
                FourScripted(), transport, 7UL,
                targetScore: 1,
                onRoundCompleted: _ => done.Set());

            session.Start();
            Assert.That(done.Wait(TimeSpan.FromSeconds(20)), Is.True);
            session.Stop();
            session.Join(TimeSpan.FromSeconds(5));

            for (int seat = 0; seat < 4; seat++)
                foreach (var ev in transport.Inbox(seat))
                {
                    if (ev.Kind != ServerEventKind.StateUpdate) continue;
                    Assert.That(ev.View!.Viewer, Is.EqualTo(seat), "뷰가 다른 좌석에게 갔다");
                    Assert.That(ev.View.MyHand.Count, Is.EqualTo(ev.View.HandCounts[seat]),
                        "자기 손패 장수가 공개 장수와 다르다");
                }
        }

        [Test]
        public void Submit_to_an_ai_seat_is_rejected()
        {
            var transport = new LoopbackTransport();
            var session = new MatchSession(FourScripted(), transport, 42UL, targetScore: 1);

            session.Submit(ClientAction.Pass(seat: 1, actionSeq: 1L));

            var inbox = transport.Inbox(1);
            Assert.That(inbox.Count, Is.EqualTo(1));
            Assert.That(inbox[0].Kind, Is.EqualTo(ServerEventKind.ActionRejected));
        }
    }
}
