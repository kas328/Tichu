using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

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

        /// <summary>지금까지 쌓인 로그 줄 수. 이후 줄만 보고 기다릴 때 쓴다(두 번째 매치 등).</summary>
        public int LogMark()
        {
            lock (Log) return Log.Count;
        }

        /// <summary>from 번째 줄 이후에 fragment 를 담은 로그가 나올 때까지 기다린다(10초 데드라인).</summary>
        public async Task WaitForLogAsync(string fragment, int from = 0)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (Log)
                    for (int i = from; i < Log.Count; i++)
                        if (Log[i].Contains(fragment)) return;
                await Task.Delay(5);
            }
            Assert.Fail($"서버 로그에 '{fragment}' 가 10초 안에 나타나지 않았다");
        }

        public async ValueTask DisposeAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await App.StopAsync(cts.Token); } catch (OperationCanceledException) { }
            await App.DisposeAsync();
        }
    }
}
