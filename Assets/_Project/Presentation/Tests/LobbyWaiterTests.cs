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
    }
}
