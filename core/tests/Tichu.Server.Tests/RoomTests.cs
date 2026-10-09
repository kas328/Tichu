using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Tests;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    public class RoomTests
    {
        private readonly List<Room> _rooms = new List<Room>();
        private readonly List<FakeLink> _links = new List<FakeLink>();

        private static ServerOptions Fast(int target = 1, int aiDelay = 0) => new ServerOptions
        {
            Difficulty = Difficulty.Easy, Target = target, AiDelayMs = aiDelay, InterRoundPauseMs = 0, Seed = 7
        };

        private (Room room, Func<int> closedCount, List<string> log) Make(ServerOptions opt, int[] seats, params FakeLink[] humans)
        {
            int closed = 0;
            var log = new List<string>();
            var room = new Room(_rooms.Count, humans, seats, opt, m => { lock (log) log.Add(m); }, () => Interlocked.Increment(ref closed));
            _rooms.Add(room);
            _links.AddRange(humans);
            return (room, () => Volatile.Read(ref closed), log);
        }

        [TearDown]
        public async Task TearDown()
        {
            // 남은 세션 스레드를 끝낸다: 아직 열린 사람 연결을 모두 이탈시킨다.
            foreach (var l in _links) l.Owner?.Left(l);
            await Task.Delay(50);
            _rooms.Clear();
            _links.Clear();
        }

        private static async Task Until(Func<bool> condition, double seconds = 10)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail($"{seconds}초 안에 조건이 성립하지 않았다");
                await Task.Delay(5);
            }
        }

        [Test]
        public async Task Lobby_precedes_the_first_state_update_and_names_the_seat()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, _, _) = Make(Fast(target: 1000, aiDelay: 50), new[] { 3, 1 }, a, b);
            Assert.That(a.Owner, Is.SameAs(room), "생성자가 소속을 방으로 바꾼다");

            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            var first = a.Received[0];
            Assert.That(first.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(first.YourSeat, Is.EqualTo(3));
            Assert.That(first.Connected, Is.EqualTo(2));
            Assert.That(first.Required, Is.EqualTo(2));
            Assert.That(b.Received[0].YourSeat, Is.EqualTo(1));
        }

        [Test]
        public async Task A_human_and_three_ai_finish_and_the_room_closes_once()
        {
            var a = new FakeLink();
            var (room, closed, log) = Make(Fast(), new[] { 2 }, a);
            room.Start();
            var client = new ScriptedClient(2, a);

            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);
            await Until(() => closed() == 1);

            Assert.That(client.FaultReason, Is.Null);
            Assert.That(a.ClosedReason, Is.EqualTo("match over"));
            lock (log) Assert.That(log.Exists(m => m.StartsWith("방 0 시작 — 사람 1 · AI 3 · seed 7 ")), Is.True);
            await Task.Delay(100);
            Assert.That(closed(), Is.EqualTo(1), "onClosed 는 한 번만");
        }

        [Test]
        public async Task Normal_end_closes_links_without_fault()
        {
            var a = new FakeLink();
            var (room, closed, log) = Make(Fast(), new[] { 0 }, a);
            room.Start();
            var client = new ScriptedClient(0, a);
            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);
            await Until(() => closed() == 1);

            room.Left(a);                                    // 서버가 닫은 연결의 끊김 통보가 늦게 온다
            client.Pump();

            Assert.That(a.Received.Any(e => e.Kind == ServerEventKind.SessionFaulted), Is.False);
            lock (log) Assert.That(log.Exists(m => m.Contains("이탈")), Is.False);
        }

        [Test]
        public async Task Leaving_mid_match_faults_only_this_room()
        {
            var a1 = new FakeLink(); var a2 = new FakeLink(); var b1 = new FakeLink();
            var (roomA, closedA, logA) = Make(Fast(target: 1000, aiDelay: 50), new[] { 0, 2 }, a1, a2);
            var (roomB, closedB, _) = Make(Fast(target: 1000, aiDelay: 50), new[] { 1 }, b1);
            roomA.Start(); roomB.Start();
            await Until(() => a1.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            roomA.Left(a2);
            await Until(() => closedA() == 1);

            var fault = a1.Received.Single(e => e.Kind == ServerEventKind.SessionFaulted);
            Assert.That(fault.Reason, Is.EqualTo("좌석 2 연결 끊김"));
            Assert.That(a1.ClosedReason, Is.EqualTo("match over"));
            lock (logA) Assert.That(logA.Exists(m => m == "방 0 좌석 2 이탈 — 매치 중단"), Is.True);

            await Task.Delay(300);
            Assert.That(closedB(), Is.EqualTo(0), "다른 방은 계속 돈다");
            Assert.That(b1.ClosedReason, Is.Null);
            Assert.That(b1.Received.Any(e => e.Kind == ServerEventKind.SessionFaulted), Is.False);
        }

        [Test]
        public async Task Leave_before_start_faults_the_room_on_start()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, closed, _) = Make(Fast(target: 1000, aiDelay: 50), new[] { 0, 1 }, a, b);

            room.Left(b);                                    // 묶인 직후, Start 전에 끊겼다
            room.Start();
            await Until(() => closed() == 1);

            Assert.That(a.Received.Exists(e => e.Kind == ServerEventKind.SessionFaulted && e.Reason == "좌석 1 연결 끊김"), Is.True);
            Assert.That(a.ClosedReason, Is.EqualTo("match over"));
        }

        [Test]
        public void Received_action_takes_the_seat_of_its_link()
        {
            var a = new FakeLink();
            var (room, _, _) = Make(Fast(), new[] { 2 }, a);
            ClientAction? got = null;
            room.OnClientAction(x => got = x);

            room.Receive(a, ClientAction.Pass(0, 1));        // 좌석 0 을 사칭

            Assert.That(got!.Seat, Is.EqualTo(2));
        }

        [Test]
        public void SendTo_reaches_only_that_seat_and_skips_ai_seats()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, _, log) = Make(Fast(), new[] { 0, 3 }, a, b);

            room.SendTo(3, ServerEvent.ActionRejected(5, "x"));
            room.SendTo(1, ServerEvent.SessionFaulted("ai-seat"));   // AI 좌석 — 연결 없음

            Assert.That(b.Received.Single().ActionSeq, Is.EqualTo(5));
            Assert.That(a.Received, Is.Empty);
            lock (log)
            {
                Assert.That(log.Exists(m => m.Contains("좌석 3 ActionRejected: x")), Is.True, "거절·폴트는 로그에 남긴다");
                Assert.That(log.Exists(m => m.Contains("좌석 1 SessionFaulted: ai-seat")), Is.True);
            }
        }
    }
}
