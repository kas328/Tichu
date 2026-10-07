using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>
    /// 서버 전송 규칙을 원시 ClientWebSocket 으로 검증한다(클라이언트 전송 구현과 독립).
    /// 매치가 시작되지 않도록 대부분 인간 2좌석 중 하나만 접속한다.
    /// </summary>
    public class ServerTransportTests
    {
        private static ServerOptions Lobby2() => new ServerOptions
        {
            Seats = new[] { 0, 2 }, Difficulty = Difficulty.Easy, AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 1
        };

        private static async Task<ClientWebSocket> Connect(Uri uri)
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, CancellationToken.None);
            return ws;
        }

        private static async Task SendText(ClientWebSocket ws, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>다음 메시지를 받는다. 닫히면 null(그때 ws.CloseStatus 를 본다).</summary>
        private static async Task<ServerEvent?> Receive(ClientWebSocket ws)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));   // 회귀가 멈춤이 아니라 실패가 되게
            var buf = new byte[64 * 1024];
            int total = 0;
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, total, buf.Length - total), cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                total += r.Count;
            } while (!r.EndOfMessage);
            return ProtocolJson.DeserializeServerEvent(Encoding.UTF8.GetString(buf, 0, total));
        }

        /// <summary>Close 가 올 때까지 메시지를 버리며 읽는다.</summary>
        private static async Task<WebSocketCloseStatus?> ReadUntilClosed(ClientWebSocket ws)
        {
            while (await Receive(ws) != null) { }
            return ws.CloseStatus;
        }

        [Test]
        public async Task First_client_gets_lobby_with_its_seat()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);

            var ev = await Receive(a);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(ev.YourSeat, Is.EqualTo(0));
            Assert.That(ev.Connected, Is.EqualTo(1));
            Assert.That(ev.Required, Is.EqualTo(2));
        }

        [Test]
        public async Task Second_client_fills_the_room_and_lobby_precedes_the_first_state_update()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);                                 // Lobby 1/2
            using var b = await Connect(server.WsUri);

            var bFirst = await Receive(b);
            Assert.That(bFirst!.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(bFirst.YourSeat, Is.EqualTo(2));
            Assert.That(bFirst.Connected, Is.EqualTo(2));

            var aSecond = await Receive(a);
            Assert.That(aSecond!.Kind, Is.EqualTo(ServerEventKind.Lobby), "마지막 Lobby 가 매치 이벤트보다 먼저 와야 한다");
            Assert.That(aSecond.Connected, Is.EqualTo(2));

            var aThird = await Receive(a);
            Assert.That(aThird!.Kind, Is.EqualTo(ServerEventKind.StateUpdate), "방이 차면 매치가 시작된다");
            Assert.That(aThird.View!.Viewer, Is.EqualTo(0));
        }

        [Test]
        public async Task Seat_freed_before_start_goes_to_the_next_client()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            var a = await Connect(server.WsUri);
            await Receive(a);
            await a.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            a.Dispose();

            // 서버가 좌석을 반납할 때까지 기다린다. 반납 전에 b 가 붙으면 좌석 2 를 받아
            // 방이 차고 매치가 시작돼 버린다(재시도로 풀 수 없는 경쟁).
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < until)
            {
                lock (server.Log) if (server.Log.Exists(m => m.Contains("좌석 0 끊김"))) break;
                await Task.Delay(10);
            }

            using var b = await Connect(server.WsUri);
            var ev = await Receive(b);

            Assert.That(ev!.YourSeat, Is.EqualTo(0), "반납된 좌석 0 을 다시 받아야 한다");
            Assert.That(ev.Connected, Is.EqualTo(1));
        }

        [Test]
        public async Task Connection_is_closed_when_room_is_full()
        {
            var opt = Lobby2();
            opt.Seats = new[] { 0 };                          // 한 명으로 즉시 만석·시작
            await using var server = await TestServer.StartAsync(opt);
            using var a = await Connect(server.WsUri);
            await Receive(a);

            using var b = await Connect(server.WsUri);
            Assert.That(await ReadUntilClosed(b), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
        }

        [Test]
        public async Task Oversized_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);

            await SendText(a, new string('x', WebSocketServerTransport.MaxMessageBytes + 1));

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
        }

        [Test]
        public async Task Malformed_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);

            await SendText(a, "not json");

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.InvalidPayloadData));
        }

        [Test]
        public async Task Exceeding_the_rate_limit_closes_the_connection()
        {
            var opt = Lobby2();
            opt.RateCapacity = 5;
            opt.RatePerSecond = 0.001;
            await using var server = await TestServer.StartAsync(opt);
            using var a = await Connect(server.WsUri);
            await Receive(a);

            for (int i = 1; i <= 6; i++)
                await SendText(a, ProtocolJson.Serialize(ClientAction.Pass(0, i)));

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
        }

        [Test]
        public async Task Disconnect_right_after_the_room_fills_faults_the_survivor_and_reopens_the_lobby()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);                                 // Lobby 1/2
            var b = await Connect(server.WsUri);
            await Receive(b);                                 // Lobby 2/2 -> 방이 찼다
            await b.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            b.Dispose();

            ServerEvent? ev;
            do { ev = await Receive(a); }
            while (ev != null && ev.Kind != ServerEventKind.SessionFaulted);

            Assert.That(ev, Is.Not.Null, "생존자는 SessionFaulted 를 받아야 한다");
            Assert.That(ev!.Reason, Does.Contain("좌석 2"));
            await server.WaitForLogAsync("다음 매치 대기");   // 멈추지 않고 정리돼 새 대기실을 연다
            lock (server.Log)
            {
                Assert.That(server.Log.FindAll(m => m.Contains("매치 시작")).Count, Is.EqualTo(1), "매치는 한 번만 시작된다");
            }
        }

        [Test]
        public async Task Client_connecting_after_a_mid_match_leave_never_joins_the_running_match()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            var b = await Connect(server.WsUri);
            await Receive(b);
            b.Dispose();                                      // 좌석 2 이탈(매치 시작 후)

            // 좌석은 진행 중 매치에 반납되지 않는다. 매치를 정리하는 동안이면 거절되고, 정리가 끝났으면
            // 새 대기실의 첫 좌석(1/2)을 받는다 — 어느 쪽이든 a 와 같은 매치(2/2)에 끼지는 않는다.
            using var c = await Connect(server.WsUri);
            var first = await Receive(c);
            if (first == null)
            {
                Assert.That(c.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            }
            else
            {
                Assert.That(first.Kind, Is.EqualTo(ServerEventKind.Lobby));
                Assert.That(first.YourSeat, Is.EqualTo(0));
                Assert.That(first.Connected, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task Late_cleanup_of_a_previous_match_connection_leaves_the_new_match_alone()
        {
            var transport = new WebSocketServerTransport(
                new SeatTable(new[] { 0 }), () => new TokenBucket(100, 100, () => 0), _ => { });
            int filled = 0;
            var left = new ConcurrentQueue<int>();
            transport.SeatsFilled += () => Interlocked.Increment(ref filled);
            transport.SeatLeft += (seat, generation) => left.Enqueue(seat);

            var old = new HeldSocket();                       // 1판 좌석 0 — 닫기에 응답하지 않고 열려 있다
            var oldTask = transport.HandleAsync(old, CancellationToken.None);
            await Until(() => Volatile.Read(ref filled) == 1);

            transport.ResetForNextMatch(1);                   // 매치가 끝나 새 대기실

            var fresh = new HeldSocket();                     // 2판 좌석 0
            var freshTask = transport.HandleAsync(fresh, CancellationToken.None);
            await Until(() => Volatile.Read(ref filled) == 2);
            var actions = new ConcurrentQueue<ClientAction>();
            transport.OnClientAction(actions.Enqueue);        // 새 세션 핸들러

            old.Push(ProtocolJson.Serialize(ClientAction.Pass(0, 1)));   // 이전 연결이 늦게 보낸 액션
            old.PushClose();                                  // 이전 연결의 finally 가 초기화 뒤에 돈다
            await Until(() => oldTask.IsCompleted);

            Assert.That(actions, Is.Empty, "이전 매치 연결의 액션이 새 세션에 닿으면 안 된다");
            Assert.That(left, Is.Empty, "이전 매치 연결의 끊김은 새 매치의 이탈이 아니다");
            Assert.That(filled, Is.EqualTo(2), "SeatsFilled 는 매치당 한 번");

            transport.SendTo(0, ServerEvent.ActionRejected(7, "new-match"));
            await Until(() => fresh.SentContains("new-match"));   // 새 연결이 여전히 좌석 0 이다
            Assert.That(old.SentContains("new-match"), Is.False);

            fresh.PushClose();
            await Until(() => freshTask.IsCompleted);
            Assert.That(left, Is.EqualTo(new[] { 0 }), "새 연결의 끊김은 그대로 SeatLeft 다");
        }

        private static async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("5초 안에 조건이 성립하지 않았다");
                await Task.Delay(5);
            }
        }

        /// <summary>테스트가 수신을 손으로 흘려 주는 가짜 소켓. 보낸 텍스트를 기록한다.</summary>
        private sealed class HeldSocket : WebSocket
        {
            private readonly Channel<string?> _in = Channel.CreateUnbounded<string?>();   // null = 닫기 프레임
            private readonly ConcurrentQueue<string> _sent = new ConcurrentQueue<string>();
            private WebSocketState _state = WebSocketState.Open;

            public void Push(string json) => _in.Writer.TryWrite(json);
            public void PushClose() => _in.Writer.TryWrite(null);
            public bool SentContains(string fragment) => _sent.ToArray().Any(s => s.Contains(fragment));

            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            {
                string? text = await _in.Reader.ReadAsync(ct);
                if (text == null) return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "");
                var bytes = Encoding.UTF8.GetBytes(text);
                bytes.CopyTo(buffer.Array!, buffer.Offset);
                return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken ct)
            {
                _sent.Enqueue(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
            {
                _state = WebSocketState.CloseSent;
                return Task.CompletedTask;
            }

            public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
            {
                _state = WebSocketState.Closed;
                return Task.CompletedTask;
            }

            public override WebSocketCloseStatus? CloseStatus => null;
            public override string? CloseStatusDescription => null;
            public override WebSocketState State => _state;
            public override string? SubProtocol => null;
            public override void Abort() { }
            public override void Dispose() { }
        }

        [Test]
        public void Dispatch_overwrites_the_claimed_seat_with_the_connection_seat()
        {
            var transport = new WebSocketServerTransport(
                new SeatTable(new[] { 0, 2 }), () => new TokenBucket(20, 10, () => 0), _ => { });
            ClientAction? got = null;
            transport.OnClientAction(a => got = a);

            bool ok = transport.Dispatch(2, ProtocolJson.Serialize(ClientAction.Pass(0, 1)));   // 좌석 0 을 사칭

            Assert.That(ok, Is.True);
            Assert.That(got!.Seat, Is.EqualTo(2), "연결에 배정된 좌석으로 덮어써야 한다(스펙 §5.4)");
        }

        [Test]
        public void Dispatch_rejects_malformed_json()
        {
            var transport = new WebSocketServerTransport(
                new SeatTable(new[] { 0 }), () => new TokenBucket(20, 10, () => 0), _ => { });
            transport.OnClientAction(_ => Assert.Fail("깨진 메시지가 세션에 닿으면 안 된다"));

            Assert.That(transport.Dispatch(0, "{\"Kind\":\"Teleport\"}"), Is.False);
        }

        [Test]
        public void Rejections_and_faults_are_logged()
        {
            var log = new System.Collections.Generic.List<string>();
            var transport = new WebSocketServerTransport(
                new SeatTable(new[] { 0 }), () => new TokenBucket(20, 10, () => 0), log.Add);

            transport.SendTo(0, ServerEvent.ActionRejected(1, "x"));
            transport.SendTo(0, ServerEvent.SessionFaulted("boom"));

            Assert.That(log.Exists(l => l.Contains("ActionRejected") && l.Contains("x")), Is.True);
            Assert.That(log.Exists(l => l.Contains("SessionFaulted") && l.Contains("boom")), Is.True);
        }
    }
}
