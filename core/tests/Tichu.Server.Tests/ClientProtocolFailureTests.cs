using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>서버가 깨진 JSON 을 보내면 클라이언트가 원인이 담긴 폴트를 정확히 한 번 낸다.</summary>
    public class ClientProtocolFailureTests
    {
        [Test]
        public async Task Garbage_frame_yields_single_fault_naming_ProtocolException()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.UseWebSockets();
            app.Map("/ws", async ctx =>
            {
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("not json")),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                try { await ws.ReceiveAsync(new ArraySegment<byte>(new byte[16]), ctx.RequestAborted); }
                catch (Exception) { }
            });
            await app.StartAsync();
            try
            {
                string addr = app.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses.First();
                var uri = new Uri(addr.Replace("http://", "ws://") + "/ws");

                using var client = await WebSocketClientTransport.ConnectAsync(uri, TimeSpan.FromSeconds(5), CancellationToken.None);
                var got = new List<ServerEvent>();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (got.Count == 0 && DateTime.UtcNow < deadline)
                {
                    got.AddRange(client.Drain());
                    await Task.Delay(20);
                }
                await Task.Delay(300);   // 두 번째 폴트가 생기지 않는지 잠시 지켜본다
                got.AddRange(client.Drain());

                Assert.That(got.Count, Is.EqualTo(1));
                Assert.That(got[0].Kind, Is.EqualTo(ServerEventKind.SessionFaulted));
                Assert.That(got[0].Reason, Does.StartWith("서버 연결 끊김"));
                Assert.That(got[0].Reason, Does.Contain("ProtocolException"));
            }
            finally
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await app.StopAsync(cts.Token); } catch (OperationCanceledException) { }
                await app.DisposeAsync();
            }
        }
    }
}
