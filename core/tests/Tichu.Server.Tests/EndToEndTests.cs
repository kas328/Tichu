using System;
using System.Collections.Generic;
using System.Net.WebSockets;
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

        /// <summary>서버가 첫 접속자에게 좌석을 배정할 때까지 기다린다. 핸드셰이크 완료가 좌석 배정보다 먼저라
        /// 곧바로 두 번째 클라이언트를 붙이면 좌석 순서가 뒤바뀔 수 있다. 두 번째 매치부터는 이전 판 줄이
        /// 이미 있으므로 from(로그 줄 번호) 이후만 본다.</summary>
        private static async Task WaitForSeatLog(TestServer s, string prefix, int from = 0)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (s.Log) if (s.Log.FindIndex(from, l => l.StartsWith(prefix)) >= 0) return;
                await Task.Delay(5);
            }
            Assert.Fail($"서버 로그에 '{prefix}' 가 5초 안에 나타나지 않았다 — 첫 접속자 좌석 배정 실패");
        }

        /// <summary>두 스크립트 클라이언트로 매치 하나를 MatchEnded 까지 둔다. 폴트가 나면 실패.</summary>
        private static async Task PlayMatch(TestServer server, int from)
        {
            using var t0 = await Connect(server);          // 먼저 접속 → 좌석 0
            await WaitForSeatLog(server, "좌석 0 접속", from);
            using var t2 = await Connect(server);          // 다음 → 좌석 2
            await PlayToEnd(new ScriptedClient(0, t0), new ScriptedClient(2, t2));
        }

        private static async Task PlayToEnd(ScriptedClient a, ScriptedClient b)
        {
            var clients = new[] { a, b };
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTime.UtcNow < deadline && !(a.MatchEnded && b.MatchEnded))
            {
                foreach (var c in clients)
                {
                    c.Pump();
                    if (c.FaultReason != null) Assert.Fail($"좌석 {c.Seat} 폴트: {c.FaultReason}");
                }
                await Task.Delay(2);
            }

            Assert.That(a.MatchEnded, Is.True, $"좌석 {a.Seat} 이 MatchEnded 를 받아야 한다");
            Assert.That(b.MatchEnded, Is.True, $"좌석 {b.Seat} 가 MatchEnded 를 받아야 한다");
        }

        /// <summary>매치가 끝난 서버가 살아 있고 새 대기실을 열었다 — 새 접속자가 첫 좌석부터 1/2 를 받는다.</summary>
        private static async Task AssertFreshLobby(TestServer server)
        {
            await server.WaitForLogAsync("다음 매치 대기");
            Assert.That(server.App.Lifetime.ApplicationStopping.IsCancellationRequested, Is.False, "매치가 끝나도 서버는 계속 돈다");

            using var t = await Connect(server);
            ServerEvent? lobby = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (lobby == null && DateTime.UtcNow < deadline)
            {
                foreach (var e in t.Drain()) if (e.Kind == ServerEventKind.Lobby) { lobby = e; break; }
                await Task.Delay(5);
            }

            Assert.That(lobby, Is.Not.Null, "새 접속자가 Lobby 를 받아야 한다");
            Assert.That(lobby!.YourSeat, Is.EqualTo(0));
            Assert.That(lobby.Connected, Is.EqualTo(1));
            Assert.That(lobby.Required, Is.EqualTo(2));
        }

        [Test]
        public async Task Two_websocket_clients_and_two_server_ai_finish_a_match()
        {
            await using var server = await TestServer.StartAsync(Fast());
            using var t0 = await Connect(server);          // 먼저 접속 → 좌석 0
            await WaitForSeatLog(server, "좌석 0 접속");
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
            await AssertFreshLobby(server);
        }

        [Test]
        public async Task Same_server_plays_two_matches_back_to_back()
        {
            await using var server = await TestServer.StartAsync(Fast(target: 1));
            await PlayMatch(server, from: 0);
            await server.WaitForLogAsync("다음 매치 대기");
            await PlayMatch(server, from: server.LogMark());

            lock (server.Log)
            {
                Assert.That(server.Log.FindAll(m => m.Contains("매치 시작")).Count, Is.EqualTo(2));
                Assert.That(server.Log.Exists(m => m.Contains("매치 시작 — seed 42 ")), Is.True, "첫 판은 --seed 그대로");
                Assert.That(server.Log.Exists(m => m.Contains("매치 시작 — seed 43 ")), Is.True, "다음 판은 seed + 매치 번호");
                Assert.That(server.Log.Exists(m => m.Contains("이탈")), Is.False, "두 판 모두 폴트가 없어야 한다");
            }
        }

        [Test]
        public async Task Late_cleanup_of_an_old_connection_does_not_break_the_next_match()
        {
            await using var server = await TestServer.StartAsync(Fast(target: 1));
            // 1판 좌석 0. 이후 수신을 하지 않으므로 서버의 닫기에 응답하지 않고 열린 채로 남는다.
            var stale = new ClientWebSocket();
            await stale.ConnectAsync(server.WsUri, CancellationToken.None);
            await WaitForSeatLog(server, "좌석 0 접속");
            var leaver = new ClientWebSocket();
            await leaver.ConnectAsync(server.WsUri, CancellationToken.None);
            await WaitForSeatLog(server, "좌석 2 접속");    // 방이 찼다 → 1판 시작
            leaver.Abort();                                  // 1판 중 이탈 → 폴트 → 새 대기실
            leaver.Dispose();
            await server.WaitForLogAsync("다음 매치 대기");
            int mark = server.LogMark();

            using var t0 = await Connect(server);            // 2판 좌석 0 — stale 과 같은 좌석
            await WaitForSeatLog(server, "좌석 0 접속", mark);
            using var t2 = await Connect(server);
            await server.WaitForLogAsync("매치 시작", mark);
            stale.Dispose();                                 // 이전 연결이 새 매치가 시작된 뒤에야 끝난다
            await server.WaitForLogAsync("이전 매치 좌석 0", mark);

            // 이전 연결 정리가 새 좌석 0 을 지우거나 SeatLeft 를 냈다면 새 매치가 폴트로 끝난다.
            await PlayToEnd(new ScriptedClient(0, t0), new ScriptedClient(2, t2));
        }

        [Test]
        public async Task No_fault_is_raised_when_server_closes_connections_after_match_end()
        {
            await using var server = await TestServer.StartAsync(Fast(target: 1));
            using var t0 = await Connect(server);
            await WaitForSeatLog(server, "좌석 0 접속");
            using var t2 = await Connect(server);
            var clients = new[] { new ScriptedClient(0, t0), new ScriptedClient(2, t2) };

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (DateTime.UtcNow < deadline && !(clients[0].MatchEnded && clients[1].MatchEnded))
            {
                foreach (var c in clients) c.Pump();
                await Task.Delay(2);
            }
            await server.WaitForLogAsync("다음 매치 대기");
            await Task.Delay(500);                          // 서버 닫기 프레임이 도착할 시간
            foreach (var c in clients) c.Pump();

            Assert.That(clients[0].FaultReason, Is.Null, "정상 종료 뒤 닫기를 끊김으로 보고하면 결과 화면 대신 메뉴로 튕긴다");
            Assert.That(clients[1].FaultReason, Is.Null);
        }

        [Test]
        public async Task Human_leaving_mid_match_faults_the_other_and_reopens_the_lobby()
        {
            var opt = Fast(target: 1000);
            opt.AiDelayMs = 50;                              // 끊기 전에 매치가 끝나지 않게
            await using var server = await TestServer.StartAsync(opt);
            using var t0 = await Connect(server);
            await WaitForSeatLog(server, "좌석 0 접속");
            var t2 = await Connect(server);
            var c0 = new ScriptedClient(0, t0);

            var started = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < started) { c0.Pump(); t2.Drain(); await Task.Delay(5); if (c0.FaultReason != null) break; }
            t2.Dispose();                                    // 좌석 2 가 사라진다

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline && c0.FaultReason == null) { c0.Pump(); await Task.Delay(5); }

            Assert.That(c0.FaultReason, Does.Contain("좌석 2"), "남은 사람에게 누가 끊겼는지 알려야 한다");
            await AssertFreshLobby(server);
        }

        [Test]
        public async Task Rejected_client_gets_session_faulted()
        {
            var opt = Fast();
            opt.Seats = new[] { 0 };
            await using var server = await TestServer.StartAsync(opt);
            using var t0 = await Connect(server);
            await WaitForSeatLog(server, "좌석 0 접속");
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
