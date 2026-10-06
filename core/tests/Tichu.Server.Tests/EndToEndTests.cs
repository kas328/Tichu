using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tichu.Core.Tests;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>
    /// 실제 Kestrel + 실제 WebSocketClientTransport + 스크립트 클라이언트 2 + 서버 PIMC 2.
    /// 빠르게 돌도록 AI 는 Easy(탐색 OFF)·지연 0, 라운드 사이 멈춤 0.
    /// </summary>
    public class EndToEndTests
    {
        private static ServerOptions Fast(int target = 400) => new ServerOptions
        {
            Seats = new[] { 0, 2 }, Difficulty = Difficulty.Easy, Target = target,
            AiDelayMs = 0, InterRoundPauseMs = 0, Seed = 42,
            RateCapacity = 100000, RatePerSecond = 100000   // 지연 0 스크립트 클라이언트는 사람보다 훨씬 빠르다
        };

        private static Task<WebSocketClientTransport> Connect(TestServer s) =>
            WebSocketClientTransport.ConnectAsync(s.WsUri, TimeSpan.FromSeconds(5), CancellationToken.None);

        [Test]
        public async Task Two_websocket_clients_and_two_server_ai_finish_a_match()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t0 = await Connect(server);          // 먼저 접속 → 좌석 0
            using var t2 = await Connect(server);          // 다음 → 좌석 2
            var clients = new[] { new ScriptedClient(0, t0), new ScriptedClient(2, t2) };

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
            while (DateTime.UtcNow < deadline && !(clients[0].MatchEnded && clients[1].MatchEnded))
            {
                foreach (var c in clients)
                {
                    c.Pump();
                    if (c.FaultReason != null) Assert.Fail($"좌석 {c.Seat} 폴트: {c.FaultReason}");
                }
                await Task.Delay(2);
            }

            Assert.That(clients[0].MatchEnded, Is.True, "좌석 0 이 MatchEnded 를 받아야 한다");
            Assert.That(clients[1].MatchEnded, Is.True, "좌석 2 가 MatchEnded 를 받아야 한다");
            Assert.That(await server.WaitForStopAsync(TimeSpan.FromSeconds(10)), Is.True, "매치가 끝나면 서버가 종료돼야 한다");
        }

        [Test]
        public async Task No_fault_is_raised_when_server_closes_after_match_end()
        {
            await using var server = await TestServer.StartAsync(Fast(target: 1));
            using var t0 = await Connect(server);
            using var t2 = await Connect(server);
            var clients = new[] { new ScriptedClient(0, t0), new ScriptedClient(2, t2) };

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTime.UtcNow < deadline && !(clients[0].MatchEnded && clients[1].MatchEnded))
            {
                foreach (var c in clients) c.Pump();
                await Task.Delay(2);
            }
            Assert.That(await server.WaitForStopAsync(TimeSpan.FromSeconds(10)), Is.True);
            await Task.Delay(500);                           // 서버 닫기 프레임이 도착할 시간
            foreach (var c in clients) c.Pump();

            Assert.That(clients[0].FaultReason, Is.Null, "정상 종료 뒤 닫기를 끊김으로 보고하면 결과 화면 대신 메뉴로 튕긴다");
            Assert.That(clients[1].FaultReason, Is.Null);
        }

        [Test]
        public async Task Human_leaving_mid_match_faults_the_other_and_stops_the_server()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 50;                              // 끊기 전에 매치가 끝나지 않게
            await using var server = await TestServer.StartAsync(opt);
            using var t0 = await Connect(server);
            var t2 = await Connect(server);
            var c0 = new ScriptedClient(0, t0);

            var started = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < started) { c0.Pump(); t2.Drain(); await Task.Delay(5); if (c0.FaultReason != null) break; }
            t2.Dispose();                                    // 좌석 2 가 사라진다

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline && c0.FaultReason == null) { c0.Pump(); await Task.Delay(5); }

            Assert.That(c0.FaultReason, Does.Contain("좌석 2"), "남은 사람에게 누가 끊겼는지 알려야 한다");
            Assert.That(await server.WaitForStopAsync(TimeSpan.FromSeconds(10)), Is.True);
        }

        [Test]
        public async Task Rejected_client_gets_session_faulted()
        {
            var opt = Fast();
            opt.Seats = new[] { 0 };
            await using var server = await TestServer.StartAsync(opt);
            using var t0 = await Connect(server);
            using var late = await Connect(server);          // 만석 — 서버가 바로 닫는다

            var got = new List<ServerEvent>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && got.TrueForAll(e => e.Kind != ServerEventKind.SessionFaulted))
            {
                got.AddRange(late.Drain());
                await Task.Delay(10);
            }

            Assert.That(got.Exists(e => e.Kind == ServerEventKind.SessionFaulted), Is.True,
                "거절당한 클라이언트가 '기다리는 중'에 갇히지 않으려면 폴트를 받아야 한다");
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
