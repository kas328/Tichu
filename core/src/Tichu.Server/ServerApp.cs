using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>서버 앱 조립. Program 과 테스트(TestServer)가 같은 경로를 쓴다.</summary>
    public static class ServerApp
    {
        public static WebApplication Build(ServerOptions opt, string url, Action<string>? log = null)
        {
            log ??= m => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {m}");

            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(url);
            builder.Logging.ClearProviders();
            // 클라이언트가 닫기에 응답하지 않아도 종료가 30초씩 매달리지 않게 한다.
            builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3));
            var app = builder.Build();

            static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            // --seed 가 있으면 좌석 배정도 결정적이다(테스트·재현). SeatPicker 는 매치메이커 잠금 안에서만 불린다.
            var rng = opt.Seed is ulong seed ? new Random(unchecked((int)seed)) : new Random();
            Matchmaker? matchmaker = null;
            matchmaker = new Matchmaker(
                opt.FillAfterSeconds, opt.MaxRooms, () => DateTime.UtcNow,
                (index, humans) => new Room(index, humans, SeatPicker.Pick(humans.Count, rng), opt,
                    () => DateTime.UtcNow, log, r => matchmaker!.RoomClosed(r)),
                log);
            var tick = new Timer(_ =>
            {
                try { matchmaker.Tick(); }
                catch (Exception e) { log($"매치메이커 틱 오류: {e}"); }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            app.Lifetime.ApplicationStopping.Register(() => tick.Dispose());

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15), KeepAliveTimeout = TimeSpan.FromSeconds(30) });
            app.Map("/ws", (RequestDelegate)(async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                string? version = ctx.Request.Query[ProtocolVersion.QueryKey];
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                if (!ProtocolVersion.Accepts(version))
                {
                    log($"프로토콜 버전 불일치 — 거절 (받은 값: {(string.IsNullOrEmpty(version) ? "없음" : version)}, 서버: {ProtocolVersion.Current})");
                    await Connection.RejectAsync(ws, ProtocolVersion.UpdateRequiredReason);
                    return;
                }
                string? deviceId = ctx.Request.Query[ProtocolVersion.DeviceIdKey];
                if (!ProtocolVersion.IsValidDeviceId(deviceId))
                {
                    log("기기 식별자 없음 — 거절");
                    await Connection.RejectAsync(ws, ProtocolVersion.UpdateRequiredReason);
                    return;
                }
                var conn = new Connection(ws, new TokenBucket(opt.RateCapacity, opt.RatePerSecond, NowSeconds), log, deviceId!);
                log($"연결 #{conn.Id} 접속");
                matchmaker.Enqueue(conn);
                await conn.RunAsync(ctx.RequestAborted);
            }));

            return app;
        }
    }
}
