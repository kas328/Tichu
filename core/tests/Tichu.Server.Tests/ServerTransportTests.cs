using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
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
        public async Task Disconnect_right_after_the_room_fills_stops_the_server_and_faults_the_survivor()
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
            Assert.That(await server.WaitForStopAsync(TimeSpan.FromSeconds(10)), Is.True, "서버가 멈추지 않고 종료해야 한다");
            lock (server.Log)
            {
                Assert.That(server.Log.FindAll(m => m.Contains("매치 시작")).Count, Is.EqualTo(1), "매치는 한 번만 시작된다");
            }
        }

        [Test]
        public async Task Client_connecting_after_the_room_filled_is_rejected_even_if_a_seat_was_left()
        {
            await using var server = await TestServer.StartAsync(Lobby2());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            var b = await Connect(server.WsUri);
            await Receive(b);
            b.Dispose();                                      // 좌석 2 이탈(매치 시작 후)

            using var c = await Connect(server.WsUri);
            Assert.That(await ReadUntilClosed(c), Is.EqualTo(WebSocketCloseStatus.PolicyViolation), "좌석은 반납되지 않는다");
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
    }
}
