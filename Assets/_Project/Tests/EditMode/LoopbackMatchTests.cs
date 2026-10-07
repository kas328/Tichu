using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 루프백 통합 — 스크립트 인간 2 + AI 2 로 매치를 끝까지 돌린다.
    /// 적용된 모든 액션이 엔진 검증을 통과했다는 것은 세션이 throw 없이 MatchEnded 까지
    /// 갔다는 사실로 증명된다(드라이버는 불법 액션에 throw 한다).
    /// </summary>
    public class LoopbackMatchTests
    {
        /// <summary>
        /// 클라이언트를 돌리며 매치가 끝날 때까지 기다린다. 클라이언트가 없는 좌석의
        /// 수신함도 비워 테스트 메모리가 무한히 자라지 않게 한다.
        /// </summary>
        private static void RunUntilEnd(
            MatchSession session, LoopbackTransport transport, ScriptedClient[] clients, TimeSpan budget)
        {
            var clientSeats = new HashSet<int>();
            foreach (var c in clients) clientSeats.Add(c.Seat);

            session.Start();
            try
            {
                var deadline = DateTime.UtcNow + budget;
                while (DateTime.UtcNow < deadline)
                {
                    foreach (var c in clients) c.Pump();
                    for (int seat = 0; seat < 4; seat++)
                        if (!clientSeats.Contains(seat)) transport.Drain(seat);

                    foreach (var c in clients)
                    {
                        if (c.FaultReason != null) Assert.Fail($"세션이 실패했다: {c.FaultReason}");
                        if (c.MatchEnded) return;
                    }

                    Thread.Sleep(2);
                }
            }
            finally
            {
                session.Stop();
                session.Join(TimeSpan.FromSeconds(5));
            }
        }

        [Test]
        public void Two_scripted_humans_and_two_ai_finish_a_match()
        {
            var transport = new LoopbackTransport();

            // 좌석 0·2 = 원격(인간), 좌석 1·3 = AI.
            var seats = new IAgent?[] { null, new ScriptedAgent(), null, new ScriptedAgent() };

            var session = new MatchSession(
                seats, transport, masterSeed: 42UL,
                targetScore: 400,
                turnTimeout: TimeSpan.FromSeconds(30));

            var clients = new[] { new ScriptedClient(0, transport), new ScriptedClient(2, transport) };

            RunUntilEnd(session, transport, clients, TimeSpan.FromSeconds(90));

            Assert.That(clients[0].MatchEnded, Is.True, "매치가 목표 점수에 도달하지 못했다");
        }

        [Test]
        public void Illegal_submission_is_rejected_without_stalling_the_match()
        {
            var transport = new LoopbackTransport();
            var seats = new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

            var session = new MatchSession(
                seats, transport, masterSeed: 7UL,
                targetScore: 400,
                turnTimeout: TimeSpan.FromSeconds(30));

            var client = new ScriptedClient(0, transport) { SubmitOneIllegalAction = true };

            RunUntilEnd(session, transport, new[] { client }, TimeSpan.FromSeconds(90));

            Assert.That(client.MatchEnded, Is.True, "불법 제출 뒤에도 매치가 끝까지 가야 한다");
            Assert.That(client.RejectionsSeen, Is.GreaterThanOrEqualTo(1), "거부 통보를 못 받았다");
        }

        [Test]
        public void Tichu_called_while_holding_the_turn_prompt_is_broadcast_before_the_move()
        {
            // 실기 재현: 원격 좌석이 자기 차례 프롬프트를 받은 채 작은 티츄만 누르고 수는
            // 아직 안 낸다. 콜은 그 순간 모두에게 보여야 한다 — 수를 낸 뒤가 아니라.
            var transport = new LoopbackTransport();
            var seats = new IAgent?[] { null, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

            var session = new MatchSession(
                seats, transport, masterSeed: 42UL,
                targetScore: 400,
                turnTimeout: TimeSpan.FromSeconds(30));

            var client = new ScriptedClient(0, transport) { HoldFirstTurnWithTichu = true };

            bool released = false, otherSeatSaw = false;
            session.Start();
            try
            {
                var holdDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
                while (DateTime.UtcNow < deadline)
                {
                    client.Pump();
                    foreach (var ev in transport.Drain(1))
                        if (ev.Kind == ServerEventKind.StateUpdate && ev.View!.Calls[0] == TichuCall.Tichu)
                            otherSeatSaw = true;
                    transport.Drain(2);
                    transport.Drain(3);

                    if (client.FaultReason != null) Assert.Fail($"세션이 실패했다: {client.FaultReason}");

                    if (!released)
                    {
                        bool selfSaw = client.LastCalls != null && client.LastCalls[0] == TichuCall.Tichu;
                        if (client.Holding && selfSaw && otherSeatSaw)
                        {
                            client.ReleaseHold();   // 이제서야 수를 낸다
                            released = true;
                        }
                        else if (DateTime.UtcNow > holdDeadline)
                        {
                            Assert.Fail($"수를 내기 전에 티츄가 방송되지 않았다 (holding={client.Holding}, " +
                                        $"본인 뷰={selfSaw}, 다른 좌석 뷰={otherSeatSaw})");
                        }
                    }
                    else if (client.MatchEnded)
                    {
                        break;
                    }

                    Thread.Sleep(2);
                }
            }
            finally
            {
                session.Stop();
                session.Join(TimeSpan.FromSeconds(5));
            }

            Assert.That(released, Is.True);
            Assert.That(client.MatchEnded, Is.True, "콜 뒤 수를 내면 매치가 정상 진행돼야 한다");
        }
    }
}
