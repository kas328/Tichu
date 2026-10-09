using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Net;
using Tichu.Presentation.Net;

namespace Tichu.Presentation.Tests
{
    public class LobbyWaiterTests
    {
        [Test]
        public void Lobby_events_update_seat_and_counts()
        {
            var w = new LobbyWaiter();
            Assert.That(w.StatusText, Is.EqualTo("서버 접속 중…"));

            w.Feed(new[] { ServerEvent.Lobby(2, 1, 2) });

            Assert.That(w.Seat, Is.EqualTo(2));
            Assert.That(w.Started, Is.False);
            Assert.That(w.StatusText, Is.EqualTo("다른 플레이어 기다리는 중 1/2"));
        }

        [Test]
        public void Events_after_start_go_to_backlog_in_order()
        {
            var w = new LobbyWaiter();
            var first = ServerEvent.StateUpdate(new PlayerView { Viewer = 2 }, null);
            var second = ServerEvent.ActionRejected(3, "x");

            // 마지막 Lobby 와 매치 이벤트가 한 Drain 묶음에 섞여 와도 하나도 잃지 않는다.
            w.Feed(new List<ServerEvent> { ServerEvent.Lobby(2, 1, 2), ServerEvent.Lobby(2, 2, 2), first, second });

            Assert.That(w.Started, Is.True);
            Assert.That(w.Seat, Is.EqualTo(2));
            Assert.That(w.Backlog, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void Fault_before_start_is_reported()
        {
            var w = new LobbyWaiter();
            w.Feed(new[] { ServerEvent.SessionFaulted("서버 연결 끊김") });

            Assert.That(w.FaultReason, Is.EqualTo("서버 연결 끊김"));
            Assert.That(w.Started, Is.False);
        }

        [Test]
        public void Queued_shows_a_countdown_from_when_it_arrived()
        {
            double now = 100;
            var w = new LobbyWaiter(() => now);
            w.Feed(new[] { ServerEvent.Queued(2, 18, false, 0) });

            Assert.That(w.StatusText, Is.EqualTo("상대를 찾는 중 · 2명 대기 · 18초 뒤 AI와 시작"));
            now = 105.2;
            Assert.That(w.StatusText, Is.EqualTo("상대를 찾는 중 · 2명 대기 · 13초 뒤 AI와 시작"));
            now = 200;
            Assert.That(w.StatusText, Is.EqualTo("상대를 찾는 중 · 2명 대기 · 0초 뒤 AI와 시작"));
        }

        [Test]
        public void Busy_shows_the_position()
        {
            var w = new LobbyWaiter(() => 0);
            w.Feed(new[] { ServerEvent.Queued(5, null, true, 3) });

            Assert.That(w.Busy, Is.True);
            Assert.That(w.StatusText, Is.EqualTo("서버가 붐빕니다 · 대기 순번 3"));
        }

        [Test]
        public void Queued_does_not_start_the_match()
        {
            var w = new LobbyWaiter(() => 0);
            w.Feed(new[] { ServerEvent.Queued(1, 30, false, 0) });

            Assert.That(w.Started, Is.False);
            Assert.That(w.Backlog, Is.Empty);
        }

        [Test]
        public void Queued_then_lobby_then_state_starts_with_the_seat()
        {
            var w = new LobbyWaiter(() => 0);
            var first = ServerEvent.StateUpdate(new PlayerView { Viewer = 3 }, null);
            w.Feed(new List<ServerEvent> { ServerEvent.Queued(1, 30, false, 0), ServerEvent.Lobby(3, 2, 2), first });

            Assert.That(w.Started, Is.True);
            Assert.That(w.Seat, Is.EqualTo(3));
            Assert.That(w.Backlog, Is.EqualTo(new[] { first }));
        }
    }
}
