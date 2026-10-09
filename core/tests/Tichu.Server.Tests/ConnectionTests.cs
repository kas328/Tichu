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
    /// <summary>서버 연결 규칙을 원시 ClientWebSocket 으로 검증한다. 충원 대기를 길게 둬 방이 열리지 않게 한다.</summary>
    public class ConnectionTests
    {
        private static ServerOptions QueueOnly() => new ServerOptions
        {
            Difficulty = Difficulty.Easy, AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 1, FillAfterSeconds = 600
        };

        private static Task<ClientWebSocket> Connect(Uri uri) => ConnectRaw(ProtocolVersion.Attach(uri));

        private static async Task<ClientWebSocket> ConnectRaw(Uri uri)
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

        private static async Task<ServerEvent?> Receive(ClientWebSocket ws)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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

        private static async Task<WebSocketCloseStatus?> ReadUntilClosed(ClientWebSocket ws)
        {
            while (await Receive(ws) != null) { }
            return ws.CloseStatus;
        }

        [Test]
        public async Task First_message_is_the_queue_status()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);

            var ev = await Receive(a);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(ev.Waiting, Is.EqualTo(1));
            Assert.That(ev.Busy, Is.False);
            Assert.That(ev.FillInSeconds, Is.InRange(599, 600));
        }

        [Test]
        public async Task Oversized_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            await SendText(a, new string('x', Connection.MaxMessageBytes + 1));
            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
        }

        [Test]
        public async Task Malformed_message_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            await SendText(a, "not json");
            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.InvalidPayloadData));
            await server.WaitForLogAsync("잘못된 메시지");
        }

        [Test]
        public async Task Exceeding_the_rate_limit_closes_the_connection()
        {
            // 대기 중엔 어떤 게임 액션이든 첫 메시지에서 닫히므로(대기 중 액션 규칙), 버킷을 0 으로 두어
            // 첫 메시지가 해석 전에 레이트리밋에 걸리는지 본다 — 검사 순서(버킷 → 해석)도 함께 고정된다.
            var opt = QueueOnly();
            opt.RateCapacity = 0;
            opt.RatePerSecond = 0.001;
            await using var server = await TestServer.StartAsync(opt);
            using var a = await Connect(server.WsUri);
            await Receive(a);

            await SendText(a, ProtocolJson.Serialize(ClientAction.Pass(0, 1)));

            Assert.That(await ReadUntilClosed(a), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            await server.WaitForLogAsync("연결 종료: rate limit");
        }

        [Test]
        public async Task Game_action_while_queued_closes_the_connection()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            await Receive(a);
            await SendText(a, ProtocolJson.Serialize(ClientAction.Pass(0, 1)));
            await ReadUntilClosed(a);
            await server.WaitForLogAsync("대기 중 게임 액션");
        }

        [Test]
        public async Task Leaving_the_queue_updates_the_others()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var a = await Connect(server.WsUri);
            Assert.That((await Receive(a))!.Waiting, Is.EqualTo(1));
            var b = await Connect(server.WsUri);
            Assert.That((await Receive(a))!.Waiting, Is.EqualTo(2));

            b.Abort(); b.Dispose();

            Assert.That((await Receive(a))!.Waiting, Is.EqualTo(1));
        }

        [TestCase("")]
        [TestCase("?v=1")]
        [TestCase("?v=999")]
        [TestCase("?v=abc")]
        public async Task Missing_wrong_or_garbage_version_gets_the_update_notice_and_is_closed(string query)
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var old = await ConnectRaw(new Uri(server.WsUri + query));

            var ev = await Receive(old);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.SessionFaulted));
            Assert.That(ev.Reason, Is.EqualTo(ProtocolVersion.UpdateRequiredReason));
            Assert.That(await ReadUntilClosed(old), Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
            await server.WaitForLogAsync("프로토콜 버전 불일치");
        }

        [Test]
        public async Task Rejected_connection_does_not_join_the_queue()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using (var old = await ConnectRaw(server.WsUri))
            {
                await Receive(old);
                await ReadUntilClosed(old);
            }

            using var a = await Connect(server.WsUri);
            var ev = await Receive(a);

            Assert.That(ev!.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(ev.Waiting, Is.EqualTo(1), "거절된 구 앱이 대기 인원에 세어지면 안 된다");
        }

        [Test]
        public async Task Client_transport_attaches_the_current_version()
        {
            await using var server = await TestServer.StartAsync(QueueOnly());
            using var client = await WebSocketClientTransport.ConnectAsync(server.WsUri, TimeSpan.FromSeconds(5), CancellationToken.None);

            ServerEvent? first = null;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (first == null && DateTime.UtcNow < deadline)
            {
                var got = client.Drain();
                if (got.Count > 0) first = got[0];
                else await Task.Delay(20);
            }

            Assert.That(first, Is.Not.Null, "5초 안에 아무 이벤트도 오지 않았다");
            Assert.That(first!.Kind, Is.EqualTo(ServerEventKind.Queued), "버전이 안 붙으면 SessionFaulted 가 온다");
        }
    }
}
