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

        [Test]
        public void Remote_seat_timeout_faults_the_session_and_ends_the_thread()
        {
            // 요구2 일반 핸들러 경로: 아무도 제출하지 않으면 RemoteAgent 가 TimeoutException 을
            // 던지고 세션은 SessionFaulted 를 방송한 뒤 스레드를 끝내야 한다. 라운드 재시도 금지.
            var transport = new LoopbackTransport();
            var seats = new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

            var session = new MatchSession(
                seats, transport, masterSeed: 42UL,
                targetScore: 1000,
                turnTimeout: TimeSpan.FromMilliseconds(200));

            session.Start();
            Assert.That(session.Join(TimeSpan.FromSeconds(10)), Is.True, "세션 스레드가 끝나지 않았다");

            bool faulted = false, askedSeat0 = false;
            foreach (var ev in transport.Inbox(0))
            {
                if (ev.Kind == ServerEventKind.SessionFaulted) faulted = true;
                if (ev.Kind == ServerEventKind.StateUpdate && ev.View!.Awaiting?.Seat == 0) askedSeat0 = true;
            }

            Assert.That(askedSeat0, Is.True, "원격 좌석에 질의 공지가 가지 않았다 — OnAwait 배선 미동작");
            Assert.That(faulted, Is.True, "타임아웃이 SessionFaulted 로 보고되지 않았다");

            for (int seat = 1; seat < 4; seat++)
            {
                bool any = false;
                foreach (var ev in transport.Inbox(seat))
                    if (ev.Kind == ServerEventKind.SessionFaulted) any = true;
                Assert.That(any, Is.True, $"seat {seat} 이 실패 통보를 받지 못했다");
            }
        }

        [Test]
        public void Stop_ends_the_thread_without_faulting()
        {
            // 요구2 OperationCanceledException 경로: 취소는 정상 종료이므로 SessionFaulted 가
            // 나가면 안 된다. 세션 스레드가 대기에 들어갔든 아니든 같은 결론이어야 한다.
            var transport = new LoopbackTransport();
            var seats = new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

            var session = new MatchSession(
                seats, transport, masterSeed: 7UL,
                targetScore: 1000,
                turnTimeout: TimeSpan.FromSeconds(30));

            session.Start();
            Thread.Sleep(150);   // 원격 좌석이 대기에 들어갈 시간을 준다
            session.Stop();

            Assert.That(session.Join(TimeSpan.FromSeconds(10)), Is.True, "취소 후 스레드가 끝나지 않았다");

            for (int seat = 0; seat < 4; seat++)
                foreach (var ev in transport.Inbox(seat))
                    Assert.That(ev.Kind, Is.Not.EqualTo(ServerEventKind.SessionFaulted),
                        $"정상 취소인데 seat {seat} 에 실패 통보가 갔다");
        }

        [Test]
        public void Out_of_range_seat_submission_is_discarded_silently()
        {
            // ActionRejected 는 발신자에게만 가야 하는데 좌석 번호가 범위 밖이면 발신자를
            // 식별할 수 없다 → 아무에게도 보내지 않는다. 무고한 좌석 0 으로 돌려보내던
            // 결함의 회귀 가드이며, 범위 검사를 지워 색인 예외가 나는 회귀도 함께 잡는다.
            var transport = new LoopbackTransport();
            var session = new MatchSession(FourScripted(), transport, 42UL, targetScore: 1);

            Assert.DoesNotThrow(() => session.Submit(ClientAction.Pass(seat: 7, actionSeq: 1L)));
            Assert.DoesNotThrow(() => session.Submit(ClientAction.Pass(seat: -1, actionSeq: 2L)));

            for (int seat = 0; seat < 4; seat++)
                Assert.That(transport.Inbox(seat).Count, Is.EqualTo(0),
                    $"범위 밖 제출에 대해 seat {seat} 이 이벤트를 받았다");
        }
    }
}
