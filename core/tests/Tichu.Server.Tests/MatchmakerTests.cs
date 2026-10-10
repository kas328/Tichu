using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    public class MatchmakerTests
    {
        private DateTime _now;
        private List<FakeRoom> _rooms = null!;
        private List<string> _log = null!;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            _rooms = new List<FakeRoom>();
            _log = new List<string>();
        }

        private Matchmaker Make(int fillAfter = 30, int maxRooms = 2) =>
            new Matchmaker(fillAfter, maxRooms, () => _now,
                (index, humans) => { var r = new FakeRoom(index, humans); _rooms.Add(r); return r; },
                m => _log.Add(m));

        private void Advance(double seconds) => _now = _now.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

        private static FakeLink[] Links(int n) => Enumerable.Range(0, n).Select(_ => new FakeLink()).ToArray();

        [Test]
        public void Four_players_open_a_room_at_once()
        {
            var mm = Make();
            var p = Links(4);
            foreach (var l in p) mm.Enqueue(l);

            Assert.That(_rooms.Count, Is.EqualTo(1));
            Assert.That(_rooms[0].Humans, Is.EqualTo(p));
            Assert.That(_rooms[0].Started, Is.True);
            Assert.That(p.All(l => l.Owner == _rooms[0]), Is.True, "묶인 연결은 방 소속이다");
            Assert.That(mm.QueuedCount, Is.EqualTo(0));
            Assert.That(mm.ActiveRooms, Is.EqualTo(1));
        }

        [Test]
        public void Fewer_than_four_wait_until_fill_after_then_open_with_those_present()
        {
            var mm = Make();
            var p = Links(3);
            foreach (var l in p) mm.Enqueue(l);

            Advance(29.9); mm.Tick();
            Assert.That(_rooms, Is.Empty, "30초 전엔 방이 없다");

            Advance(0.1); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(1));
            Assert.That(_rooms[0].Humans, Is.EqualTo(p));
        }

        [Test]
        public void Fill_timer_counts_from_the_oldest_waiter()
        {
            var mm = Make();
            var a = new FakeLink(); var b = new FakeLink();
            mm.Enqueue(a);
            Advance(20); mm.Enqueue(b);
            Advance(10); mm.Tick();

            Assert.That(_rooms.Count, Is.EqualTo(1));
            Assert.That(_rooms[0].Humans, Is.EqualTo(new[] { a, b }), "늦게 온 b 도 같은 방이다");
        }

        [Test]
        public void Fifth_player_keeps_its_own_arrival_time()
        {
            var mm = Make();
            foreach (var l in Links(4)) mm.Enqueue(l);       // 방 0, 즉시
            Advance(10);
            var fifth = new FakeLink();
            mm.Enqueue(fifth);

            Advance(29); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(1), "다섯째의 30초는 그 사람의 입장 시각부터");
            Advance(1); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(2));
            Assert.That(_rooms[1].Humans, Is.EqualTo(new[] { fifth }));
        }

        [Test]
        public void Queued_carries_the_countdown_of_the_oldest_waiter()
        {
            var mm = Make();
            var a = new FakeLink(); var b = new FakeLink();
            mm.Enqueue(a);
            Advance(10); mm.Enqueue(b);

            var q = b.QueuedEvents.Last();
            Assert.That(q.Waiting, Is.EqualTo(2));
            Assert.That(q.FillInSeconds, Is.EqualTo(20));
            Assert.That(q.Busy, Is.False);
            Assert.That(q.Position, Is.EqualTo(0));
            Assert.That(a.QueuedEvents.Last().Waiting, Is.EqualTo(2), "인원이 바뀌면 먼저 온 사람도 다시 받는다");
        }

        [Test]
        public void Queued_is_sent_only_when_it_changes()
        {
            var mm = Make();
            var a = new FakeLink();
            mm.Enqueue(a);
            int before = a.QueuedEvents.Count;

            for (int i = 0; i < 5; i++) { Advance(1); mm.Tick(); }

            Assert.That(before, Is.EqualTo(1));
            Assert.That(a.QueuedEvents.Count, Is.EqualTo(1), "매초 보내지 않는다 — 카운트다운은 클라이언트가 센다");
        }

        [Test]
        public void Leaving_the_queue_renotifies_and_resets_the_countdown_base()
        {
            var mm = Make();
            var a = new FakeLink(); var b = new FakeLink();
            mm.Enqueue(a);
            Advance(10); mm.Enqueue(b);
            Advance(5);
            mm.Left(a);

            var q = b.QueuedEvents.Last();
            Assert.That(q.Waiting, Is.EqualTo(1));
            Assert.That(q.FillInSeconds, Is.EqualTo(25), "이제 가장 오래 기다린 사람은 b(입장 5초 전)");
            Advance(24); mm.Tick();
            Assert.That(_rooms, Is.Empty);
            Advance(1); mm.Tick();
            Assert.That(_rooms.Single().Humans, Is.EqualTo(new[] { b }));
        }

        [Test]
        public void Busy_when_max_rooms_reached_sends_position_without_countdown()
        {
            var mm = Make(maxRooms: 1);
            foreach (var l in Links(4)) mm.Enqueue(l);       // 방 0 → 상한
            var x = new FakeLink(); var y = new FakeLink();
            mm.Enqueue(x); mm.Enqueue(y);

            Assert.That(x.QueuedEvents.Last().Busy, Is.True);
            Assert.That(x.QueuedEvents.Last().Position, Is.EqualTo(1));
            Assert.That(x.QueuedEvents.Last().FillInSeconds, Is.Null);
            Assert.That(y.QueuedEvents.Last().Position, Is.EqualTo(2));

            Advance(60); mm.Tick();
            Assert.That(_rooms.Count, Is.EqualTo(1), "상한에선 30초가 지나도 방을 열지 않는다");
        }

        [Test]
        public void Busy_queue_starts_when_a_room_frees()
        {
            var mm = Make(maxRooms: 1);
            foreach (var l in Links(4)) mm.Enqueue(l);       // 방 0
            var waiting = Links(4);
            foreach (var l in waiting) mm.Enqueue(l);        // 4명이 모였지만 상한
            Assert.That(_rooms.Count, Is.EqualTo(1));

            mm.RoomClosed(_rooms[0]);

            Assert.That(_rooms.Count, Is.EqualTo(2));
            Assert.That(_rooms[1].Humans, Is.EqualTo(waiting));
            Assert.That(mm.ActiveRooms, Is.EqualTo(1));
        }

        [Test]
        public void Game_action_while_queued_closes_and_removes_the_link()
        {
            var mm = Make();
            var a = new FakeLink();
            mm.Enqueue(a);

            a.Submit(ClientAction.Pass(0, 1));

            Assert.That(a.ClosedReason, Is.EqualTo("not in a match"));
            Assert.That(mm.QueuedCount, Is.EqualTo(0));
        }

        [Test]
        public void Leave_after_grouping_is_forwarded_to_the_room()
        {
            // 연결의 수신 루프가 Owner(=대기열) 를 읽은 직후 묶인 경우 — 매치메이커가 방으로 넘긴다.
            var mm = Make();
            var p = Links(4);
            foreach (var l in p) mm.Enqueue(l);

            mm.Left(p[2]);
            mm.Receive(p[1], ClientAction.Pass(0, 1));

            Assert.That(_rooms[0].LeftLinks, Is.EqualTo(new[] { p[2] }));
            Assert.That(_rooms[0].ReceivedActions.Count, Is.EqualTo(1));
            Assert.That(p[1].ClosedReason, Is.Null, "방에 속한 연결은 대기 중 액션 규칙으로 닫지 않는다");
        }
    }
}
