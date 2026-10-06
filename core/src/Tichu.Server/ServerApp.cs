using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            var transport = new WebSocketServerTransport(
                new SeatTable(opt.Seats),
                () => new TokenBucket(opt.RateCapacity, opt.RatePerSecond, NowSeconds),
                log);
            _ = new MatchHost(opt, transport, () => { app.Lifetime.StopApplication(); return Task.CompletedTask; }, log);

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
            app.Map("/ws", (RequestDelegate)(async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await transport.HandleAsync(ws, ctx.RequestAborted);
            }));

            return app;
        }
    }
}
