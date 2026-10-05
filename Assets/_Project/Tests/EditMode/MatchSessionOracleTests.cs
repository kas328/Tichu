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
        public void Broadcast_views_are_routed_to_their_own_seat()
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

            for (int seat = 0; seat < 4; seat++)
                foreach (var ev in transport.Inbox(seat))
                    if (ev.Kind == ServerEventKind.StateUpdate && ev.View!.Awaiting?.LegalMoves != null)
                        Assert.That(ev.View.Awaiting!.Seat, Is.EqualTo(ev.View.Viewer),
                            "대기 좌석이 아닌 곳에 합법수가 실려 갔다 — 은닉 정보 누출");
        }

        [Test]
        public void Broadcast_to_other_seats_never_carries_legal_moves_for_a_play_await()
        {
            // Finding 1 재현 — Awaiting.LegalMoves 는 대기 좌석의 손패에서 계산되므로 다른
            // 좌석에 그대로 보내면 그 손패가 거의 복원된다. MatchSession.OnAwait 의 유일한
            // 가드는 `i == seat ? awaiting : redacted` 삼항식이다. 이걸 검증하려면 Play 종류
            // 대기에 실제로 도달해야 한다 — 큰 티츄·교환 대기는 RemoteAgent.BuildAwaiting 이
            // LegalMoves 를 채우지 않아(StepKind.Play 일 때만 채운다) 가드가 애초에 빈 값을
            // 가리는 셈이고, 그래서는 가드를 지워도 테스트가 실패하지 않는다(공허한 통과).
            //
            // 그래서 이 테스트는 좌석 0 만 담당하는 인라인 클라이언트로 큰 티츄 패스 ·
            // 교환(앞 3장)을 응답해 라운드를 Play 단계로 밀어넣는다. 좌석 0 에 대한 Play 종류
            // Awaiting(LegalMoves 포함)을 **처음** 보는 순간 그 사실만 기록하고 더는 응답하지
            // 않는다 — 그러면 좌석 0 이 타임아웃해 세션이 fault 하며 스레드가 끝나고, 좌석
            // 1~3 은 그 사이 쌓인 방송을 그대로 들고 있다. 그게 검증 대상이다.
            var transport = new LoopbackTransport();
            var seats = new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

            var turnTimeout = TimeSpan.FromSeconds(2);
            var session = new MatchSession(
                seats, transport, masterSeed: 42UL,
                targetScore: 1000,
                turnTimeout: turnTimeout);

            session.Start();

            bool reachedPlayAwaitWithLegalMoves = false;
            long seq = 0;

            // 이 루프 자체의 안전장치 — 라운드가 끝내 Play 단계에 도달하지 않으면 걸려
            // 있지 말고 실패해야 한다. 큰 티츄·교환은 이 루프가 즉시 응답하므로 turnTimeout
            // 2초보다 훨씬 짧게 끝난다 — 10초면 그 두 질문과 폴링 지연을 넉넉히 덮는다.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline && !reachedPlayAwaitWithLegalMoves)
            {
                foreach (var ev in transport.Drain(0))
                {
                    if (reachedPlayAwaitWithLegalMoves) break;
                    if (ev.Kind != ServerEventKind.StateUpdate) continue;

                    var awaiting = ev.View!.Awaiting;
                    if (awaiting == null || awaiting.Seat != 0) continue;

                    if (awaiting.Kind == StepKind.Play)
                    {
                        if (awaiting.LegalMoves != null) reachedPlayAwaitWithLegalMoves = true;
                        continue; // 응답하지 않는다 — 여기서 멈춰야 타임아웃이 걸린다.
                    }

                    if (awaiting.Kind == StepKind.GrandTichu)
                    {
                        transport.Submit(ClientAction.DeclineGrandTichu(0, ++seq));
                    }
                    else if (awaiting.Kind == StepKind.Exchange)
                    {
                        var h = ev.View.MyHand;
                        transport.Submit(ClientAction.Exchange(0, ++seq, h[0], h[1], h[2]));
                    }
                }
                if (!reachedPlayAwaitWithLegalMoves) Thread.Sleep(2);
            }

            Assert.That(reachedPlayAwaitWithLegalMoves, Is.True,
                "라운드가 Play 종류 Awaiting(합법수 포함)에 도달하지 못했다 — 이 테스트가 공허해졌다");

            Assert.That(session.Join(turnTimeout + TimeSpan.FromSeconds(10)), Is.True,
                "좌석 0 타임아웃 뒤에도 세션 스레드가 끝나지 않았다");

            for (int seat = 1; seat < 4; seat++)
                foreach (var ev in transport.Inbox(seat))
                {
                    if (ev.Kind != ServerEventKind.StateUpdate || ev.View!.Awaiting == null) continue;
                    Assert.That(ev.View.Awaiting.LegalMoves, Is.Null,
                        $"seat {seat} 이 다른 좌석(Play 대기)의 합법수를 받았다 — 은닉 정보 누출");
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
