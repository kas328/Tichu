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

        private DateTime _clock = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

        private (Room room, Func<int> closedCount, List<string> log) Make(ServerOptions opt, int[] seats, params FakeLink[] humans)
        {
            int closed = 0;
            var log = new List<string>();
            var room = new Room(_rooms.Count, humans, seats, opt, () => _clock,
                m => { lock (log) log.Add(m); }, _ => Interlocked.Increment(ref closed));
            _rooms.Add(room);
            _links.AddRange(humans);
            return (room, () => Volatile.Read(ref closed), log);
        }

        private FakeLink Track(FakeLink l) { _links.Add(l); return l; }

        [TearDown]
        public async Task TearDown()
        {
            // 남은 세션 스레드를 끝낸다: 아직 열린 사람 연결을 모두 이탈시킨다.
            foreach (var l in _links) l.Owner?.Left(l);
            foreach (var r in _rooms) r.Tick(DateTime.MaxValue.AddDays(-1));
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

        // 방이 닫힌 뒤 늦게 오는 Left 는 _ending 가드가 막는다(좌석 끊김 처리·로그 없음).
        // 실제 종료 경로는 EndToEndTests.No_fault_is_raised_when_server_closes_connections_after_match_end 가 덮는다.
        [Test]
        public async Task Normal_end_closes_links_without_fault()
        {
            var a = new FakeLink();
            var (room, closed, log) = Make(Fast(), new[] { 0 }, a);
            room.Start();
            var client = new ScriptedClient(0, a);
            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);
            await Until(() => closed() == 1);

            room.Left(a);                                    // 방이 이미 닫힌 뒤의 이탈 통보 — 폴트도 재방송도 없다
            client.Pump();

            Assert.That(a.Received.Any(e => e.Kind == ServerEventKind.SessionFaulted), Is.False);
            lock (log) Assert.That(log.Exists(m => m.Contains("연결 끊김")), Is.False);
        }

        [Test]
        public async Task A_leaving_human_is_played_by_the_stand_in_and_the_room_continues()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, closed, log) = Make(Fast(target: 200), new[] { 0, 2 }, a, b);
            room.Start();
            var client = new ScriptedClient(0, a);
            await Until(() => { client.Pump(); return a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate); });

            room.Left(b);
            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);

            Assert.That(client.FaultReason, Is.Null, "한 명이 나가도 판은 깨지지 않는다");
            lock (log) Assert.That(log.Exists(m => m == "방 0 좌석 2 연결 끊김 — AI 대행"), Is.True);
            await Until(() => closed() == 1);
        }

        [Test]
        public async Task A_human_gone_before_start_is_played_by_the_stand_in()
        {
            var a = new FakeLink(); var b = new FakeLink();
            var (room, closed, _) = Make(Fast(target: 200), new[] { 0, 1 }, a, b);
            room.Left(b);
            room.Start();
            var client = new ScriptedClient(0, a);
            await Until(() => { client.Pump(); return client.MatchEnded; }, 60);
            Assert.That(client.FaultReason, Is.Null);
        }

        [Test]
        public async Task Rejoin_restores_the_seat_with_score_and_snapshot()
        {
            var a = new FakeLink();
            var (room, _, log) = Make(Fast(target: 1000, aiDelay: 20), new[] { 1 }, a);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            room.Receive(a, ClientAction.Pass(1, 50));       // 옛 앱이 ActionSeq 를 이미 올려 놨다
            room.Left(a);                                    // 유일한 사람 → 멈춤
            var a2 = Track(new FakeLink { DeviceId = a.DeviceId });
            room.Rejoin(a2);

            var first = a2.Received[0];
            Assert.That(first.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(first.YourSeat, Is.EqualTo(1));
            Assert.That(a2.Received[1].Kind, Is.EqualTo(ServerEventKind.StateUpdate), "로비 다음은 스냅샷");
            Assert.That(a2.Received[1].Applied, Is.Null, "스냅샷은 효과음·로그를 다시 일으키지 않는다");
            Assert.That(a2.Owner, Is.SameAs(room));
            lock (log) Assert.That(log.Exists(m => m.StartsWith("방 0 좌석 1 재접속")), Is.True);

            // 새 앱은 ActionSeq 1 부터 — 판이 이어져야 한다.
            var client = new ScriptedClient(1, a2);
            int before = a2.Received.Count;
            await Until(() => { client.Pump(); return a2.Received.Count > before + 20; }, 30);
            Assert.That(a2.Received.Any(e => e.Kind == ServerEventKind.ActionRejected && e.Reason!.Contains("actionSeq")), Is.False);
        }

        [Test]
        public async Task Duplicate_rejoin_closes_the_old_link_and_ignores_its_late_leave()
        {
            var a = new FakeLink(); var other = new FakeLink();
            var (room, _, log) = Make(Fast(target: 1000, aiDelay: 20), new[] { 0, 2 }, a, other);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));

            var a2 = Track(new FakeLink { DeviceId = a.DeviceId });
            room.Rejoin(a2);
            room.Left(a);                                    // 옛 연결이 늦게 끊긴다

            Assert.That(a.ClosedReason, Is.EqualTo("다른 곳에서 접속"));
            lock (log) Assert.That(log.Exists(m => m.Contains("좌석 0 연결 끊김")), Is.False, "옛 연결의 끊김은 좌석에 닿지 않는다");
        }

        [Test]
        public async Task Empty_room_closes_after_the_grace_period()
        {
            var a = new FakeLink();
            var (room, closed, log) = Make(Fast(target: 1000, aiDelay: 20), new[] { 0 }, a);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));
            room.Left(a);

            room.Tick(_clock.AddSeconds(119));
            await Task.Delay(200);
            Assert.That(closed(), Is.EqualTo(0));

            room.Tick(_clock.AddSeconds(120));
            await Until(() => closed() == 1);
            lock (log) Assert.That(log.Exists(m => m == "방 0 사람 0명 120초 — 방 종료"), Is.True);
        }

        [Test]
        public async Task Returning_within_the_grace_period_keeps_the_room()
        {
            var a = new FakeLink();
            var (room, closed, _) = Make(Fast(target: 1000, aiDelay: 20), new[] { 0 }, a);
            room.Start();
            await Until(() => a.Received.Any(e => e.Kind == ServerEventKind.StateUpdate));
            room.Left(a);
            room.Rejoin(Track(new FakeLink { DeviceId = a.DeviceId }));

            room.Tick(_clock.AddSeconds(500));
            await Task.Delay(300);
            Assert.That(closed(), Is.EqualTo(0));
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
