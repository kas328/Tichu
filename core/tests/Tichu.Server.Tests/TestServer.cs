using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Tichu.Server.Tests
{
    /// <summary>프로세스 안 실제 Kestrel. 임의 포트(0)로 띄우고 실제 주소를 읽는다.</summary>
    internal sealed class TestServer : IAsyncDisposable
    {
        public WebApplication App { get; }
        public Uri WsUri { get; }
        public List<string> Log { get; }

        private TestServer(WebApplication app, Uri wsUri, List<string> log)
        {
            App = app; WsUri = wsUri; Log = log;
        }

        public static async Task<TestServer> StartAsync(ServerOptions opt)
        {
            var log = new List<string>();
            var app = ServerApp.Build(opt, "http://127.0.0.1:0", m => { lock (log) log.Add(m); });
            await app.StartAsync();
            string addr = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First();
            return new TestServer(app, new Uri(addr.Replace("http://", "ws://") + "/ws"), log);
        }

        /// <summary>서버가 스스로 종료를 요청했는가(매치 종료·폴트 후 StopApplication).</summary>
        public async Task<bool> WaitForStopAsync(TimeSpan timeout)
        {
            var stopping = new TaskCompletionSource();
            using var reg = App.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
            return await Task.WhenAny(stopping.Task, Task.Delay(timeout)) == stopping.Task;
        }

        public async ValueTask DisposeAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await App.StopAsync(cts.Token); } catch (OperationCanceledException) { }
            await App.DisposeAsync();
        }
    }
}
