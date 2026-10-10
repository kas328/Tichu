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
    /// <summary>
    /// 실제 Kestrel + 실제 WebSocketClientTransport + 스크립트 클라이언트 + 서버 AI. 빠르게 돌도록
    /// AI 는 Easy·지연 0, 라운드 사이 멈춤 0, 충원 1초(틱이 1초라 방은 1~2초 뒤 열린다).
    /// </summary>
    public class EndToEndTests
    {
        private static ServerOptions Fast(int target = 1, int maxRooms = 2) => new ServerOptions
        {
            Difficulty = Difficulty.Easy, Target = target, AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 42,
            FillAfterSeconds = 1, MaxRooms = maxRooms,
            RateCapacity = 100000, RatePerSecond = 100000   // 지연 0 스크립트 클라이언트는 사람보다 훨씬 빠르다
        };

        private static Task<WebSocketClientTransport> Connect(TestServer s) =>
            WebSocketClientTransport.ConnectAsync(s.WsUri, TimeSpan.FromSeconds(5), CancellationToken.None);

        private static Task<WebSocketClientTransport> ConnectAs(TestServer s, string id) =>
            WebSocketClientTransport.ConnectAsync(s.WsUri, id, TimeSpan.FromSeconds(5), CancellationToken.None);

        private static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>Lobby 까지 받은 이벤트를 쥐고 있다가 ScriptedClient 에 다시 흘려 주는 전송.</summary>
        private sealed class Joined : IClientTransport
        {
            private readonly WebSocketClientTransport _inner;
            private List<ServerEvent>? _backlog;
            public int Seat { get; }
            public List<ServerEvent> BeforeLobby { get; }

            public Joined(WebSocketClientTransport inner, int seat, List<ServerEvent> beforeLobby, List<ServerEvent> backlog)
            {
                _inner = inner; Seat = seat; BeforeLobby = beforeLobby; _backlog = backlog;
            }

            public IReadOnlyList<ServerEvent> Drain()
            {
                var got = _inner.Drain();
                if (_backlog == null) return got;
                var all = _backlog; _backlog = null;
                all.AddRange(got);
                return all;
            }

            public void Submit(ClientAction a) => _inner.Submit(a);
        }

        /// <summary>Lobby 를 받을 때까지 기다려 좌석을 알아낸다. Lobby 이후 이벤트는 잃지 않는다.
        /// already = 호출자가 이미 Drain 해 버린 이벤트(먼저 처리한다).</summary>
        private static async Task<Joined> AwaitLobby(WebSocketClientTransport t, double seconds = 10, List<ServerEvent>? already = null)
        {
            var before = new List<ServerEvent>();
            var pending = already ?? new List<ServerEvent>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                var got = new List<ServerEvent>(pending);
                pending.Clear();
                got.AddRange(t.Drain());
                for (int i = 0; i < got.Count; i++)
                {
                    if (got[i].Kind == ServerEventKind.Lobby)
                        return new Joined(t, got[i].YourSeat, before, got.Skip(i + 1).ToList());
                    if (got[i].Kind == ServerEventKind.SessionFaulted) Assert.Fail($"대기 중 폴트: {got[i].Reason}");
                    before.Add(got[i]);
                }
                await Task.Delay(5);
            }
            Assert.Fail($"{seconds}초 안에 Lobby 가 오지 않았다");
            return null!;
        }

        private static async Task PlayToEnd(params ScriptedClient[] clients)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
            while (DateTime.UtcNow < deadline && !clients.All(c => c.MatchEnded))
            {
                foreach (var c in clients)
                {
                    c.Pump();
                    if (c.FaultReason != null) Assert.Fail($"좌석 {c.Seat} 폴트: {c.FaultReason}");
                }
                await Task.Delay(2);
            }
            foreach (var c in clients) Assert.That(c.MatchEnded, Is.True, $"좌석 {c.Seat} 이 MatchEnded 를 받아야 한다");
        }

        private static int Count(TestServer s, string fragment)
        {
            lock (s.Log) return s.Log.Count(m => m.Contains(fragment));
        }

        [Test]
        public async Task Two_clients_share_a_room_with_two_ai_and_finish()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t1 = await Connect(server);
            using var t2 = await Connect(server);
            var j1 = await AwaitLobby(t1);
            var j2 = await AwaitLobby(t2);

            Assert.That(j1.Seat, Is.Not.EqualTo(j2.Seat));
            Assert.That(j1.BeforeLobby.Any(e => e.Kind == ServerEventKind.Queued), Is.True, "대기 중엔 Queued 를 받는다");
            await PlayToEnd(new ScriptedClient(j1.Seat, j1), new ScriptedClient(j2.Seat, j2));
            await server.WaitForLogAsync("방 0 시작 — 사람 2 · AI 2");
        }

        [Test]
        public async Task Solo_client_plays_three_ai_after_the_fill_wait()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t = await Connect(server);
            var j = await AwaitLobby(t);

            await PlayToEnd(new ScriptedClient(j.Seat, j));
            await server.WaitForLogAsync("방 0 시작 — 사람 1 · AI 3");
        }

        [Test]
        public async Task Five_clients_split_into_two_rooms()
        {
            await using var server = await TestServer.StartAsync(Fast());
            var ts = new List<WebSocketClientTransport>();
            for (int i = 0; i < 5; i++) ts.Add(await Connect(server));
            try
            {
                var joined = new List<Joined>();
                foreach (var t in ts) joined.Add(await AwaitLobby(t));

                await PlayToEnd(joined.Select(j => new ScriptedClient(j.Seat, j)).ToArray());
                await server.WaitForLogAsync("사람 4 · AI 0");
                await server.WaitForLogAsync("사람 1 · AI 3");
                Assert.That(Count(server, "시작 — 사람"), Is.EqualTo(2));
            }
            finally { foreach (var t in ts) t.Dispose(); }
        }

        [Test]
        public async Task Busy_queue_waits_for_a_free_room()
        {
            var opt = Fast(maxRooms: 1);
            opt.AiDelayMs = 20;                               // 첫 방이 B 접속 전에 끝나지 않게
            await using var server = await TestServer.StartAsync(opt);
            using var ta = await Connect(server);
            var ja = await AwaitLobby(ta);
            await server.WaitForLogAsync("방 0 시작");

            using var tb = await Connect(server);
            ServerEvent? busy = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            var early = new List<ServerEvent>();
            while (busy == null && DateTime.UtcNow < deadline)
            {
                foreach (var e in tb.Drain()) { early.Add(e); if (e.Kind == ServerEventKind.Queued && e.Busy) busy = e; }
                await Task.Delay(5);
            }
            Assert.That(busy, Is.Not.Null, "상한에선 Busy 를 받아야 한다");
            Assert.That(busy!.Position, Is.EqualTo(1));
            Assert.That(busy.FillInSeconds, Is.Null);

            var a = new ScriptedClient(ja.Seat, ja);
            await PlayToEnd(a);                               // 방 0 이 끝나면 B 가 들어간다
            var jb = await AwaitLobby(tb, 30, early);         // Busy 확인 중 먹은 이벤트부터 다시 본다
            await PlayToEnd(new ScriptedClient(jb.Seat, jb));
            await server.WaitForLogAsync("방 1 시작 — 사람 1 · AI 3");
        }

        [Test]
        public async Task No_fault_is_raised_when_server_closes_connections_after_match_end()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t = await Connect(server);
            var j = await AwaitLobby(t);
            var c = new ScriptedClient(j.Seat, j);
            await PlayToEnd(c);
            await server.WaitForLogAsync("방 0 종료");
            await Task.Delay(500);                            // 서버 닫기 프레임이 도착할 시간
            c.Pump();

            Assert.That(t.ConnectionLost, Is.False, "MatchEnded 뒤 서버의 정상 종료는 끊김이 아니다");
            Assert.That(c.FaultReason, Is.Null, "정상 종료 뒤 닫기를 끊김으로 보고하면 결과 화면 대신 메뉴로 튕긴다");
        }

        [Test]
        public async Task A_dropped_solo_player_rejoins_the_same_seat_and_finishes()
        {
            var opt = Fast(target: 200);
            opt.AiDelayMs = 20;
            await using var server = await TestServer.StartAsync(opt);
            string id = NewId();
            var t1 = await ConnectAs(server, id);
            var j1 = await AwaitLobby(t1);
            var c1 = new ScriptedClient(j1.Seat, j1);
            var until = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < until) { c1.Pump(); await Task.Delay(5); }
            t1.Dispose();                                     // 판 도중 끊김(혼자 → 멈춤)
            await server.WaitForLogAsync("사람 0명, 멈춤");

            using var t2 = await ConnectAs(server, id);
            var j2 = await AwaitLobby(t2);

            Assert.That(j2.Seat, Is.EqualTo(j1.Seat), "같은 자리로 돌아온다");
            await server.WaitForLogAsync($"좌석 {j2.Seat} 재접속");
            await PlayToEnd(new ScriptedClient(j2.Seat, j2));
            Assert.That(Count(server, "시간 초과 2회"), Is.EqualTo(0), "재접속 뒤 액션이 받아들여져야 한다(거부되면 시간 초과로만 진행된다)");
        }

        [Test]
        public async Task Remaining_player_finishes_when_the_other_never_returns()
        {
            var opt = Fast(target: 200);
            opt.AiDelayMs = 20;
            await using var server = await TestServer.StartAsync(opt);
            using var t1 = await Connect(server);
            var t2 = await Connect(server);
            var j1 = await AwaitLobby(t1);
            await AwaitLobby(t2);
            t2.Dispose();

            await PlayToEnd(new ScriptedClient(j1.Seat, j1));
            await server.WaitForLogAsync("연결 끊김 — AI 대행");
        }

        [Test]
        public async Task Idle_player_times_out_and_becomes_away()
        {
            var opt = Fast(target: 200);
            opt.TurnSeconds = 1;
            await using var server = await TestServer.StartAsync(opt);
            using var tActive = await Connect(server);
            using var tIdle = await Connect(server);          // 아무것도 하지 않는다
            var j = await AwaitLobby(tActive);
            await AwaitLobby(tIdle);

            var active = new ScriptedClient(j.Seat, j);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline && !active.MatchEnded)
            {
                active.Pump();
                tIdle.Drain();
                await Task.Delay(2);
            }
            Assert.That(active.MatchEnded, Is.True);
            await server.WaitForLogAsync("자리 비움, 대리 AI");
        }

        [Test]
        public async Task Empty_room_closes_after_the_grace_period()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 20;
            opt.EmptyRoomSeconds = 1;
            await using var server = await TestServer.StartAsync(opt);
            var t = await Connect(server);
            await AwaitLobby(t);
            t.Dispose();

            await server.WaitForLogAsync("사람 0명 1초 — 방 종료");
            await server.WaitForLogAsync("방 0 종료");
        }

        [Test]
        public async Task Second_connection_from_the_same_device_takes_the_seat()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 20;
            await using var server = await TestServer.StartAsync(opt);
            string id = NewId();
            using var first = await ConnectAs(server, id);
            var j1 = await AwaitLobby(first);

            using var second = await ConnectAs(server, id);
            var j2 = await AwaitLobby(second);

            Assert.That(j2.Seat, Is.EqualTo(j1.Seat));
            // 옛 연결은 사유(SessionFaulted)를 받고 닫힌다 — 끊김이 아니므로 재접속하지 않는다.
            string? reason = null;
            var lost = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < lost && reason == null)
            {
                foreach (var e in first.Drain()) if (e.Kind == ServerEventKind.SessionFaulted) reason = e.Reason;
                await Task.Delay(10);
            }
            Assert.That(reason, Is.EqualTo("다른 곳에서 접속"));
            Assert.That(first.ConnectionLost, Is.False, "서버가 사유를 보내고 닫은 것은 끊김이 아니다");
        }

        [Test]
        public async Task Old_v2_app_gets_the_update_notice()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var ws = new System.Net.WebSockets.ClientWebSocket();
            await ws.ConnectAsync(new Uri(server.WsUri + "?v=2"), CancellationToken.None);
            var buf = new byte[4096];
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
            var ev = ProtocolJson.DeserializeServerEvent(System.Text.Encoding.UTF8.GetString(buf, 0, r.Count));
            Assert.That(ev.Reason, Is.EqualTo(ProtocolVersion.UpdateRequiredReason));
        }

        [Test]
        public void Connecting_to_a_dead_port_fails_within_the_timeout()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.ThrowsAsync(Is.InstanceOf<Exception>(), async () =>
                await WebSocketClientTransport.ConnectAsync(new Uri("ws://127.0.0.1:1/ws"), TimeSpan.FromSeconds(2), CancellationToken.None));
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        }
    }
}
