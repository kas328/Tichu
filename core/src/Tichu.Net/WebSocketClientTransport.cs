using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tichu.Net
{
    /// <summary>
    /// 실제 서버에 붙는 클라이언트 전송(M2). 수신 태스크가 이벤트를 큐에 쌓고 메인 스레드가
    /// Drain 으로 꺼낸다(루프백 폴링과 같은 모양). Submit 은 송신 큐에 넣고 즉시 돌아온다 —
    /// 송신 태스크 하나가 순서대로 보내므로 동시 전송이 생기지 않는다.
    ///
    /// 연결이 끊기면 수신 큐에 SessionFaulted("서버 연결 끊김")을 직접 넣어, 위층(TichuClient)이
    /// 서버 폴트와 똑같이 처리하게 한다. 단 MatchEnded·서버의 SessionFaulted 를 이미 받았거나
    /// 우리가 Dispose 한 뒤의 닫힘은 정상이므로 만들지 않는다.
    /// </summary>
    public sealed class WebSocketClientTransport : IClientTransport, IDisposable
    {
        private const string LostReason = "서버 연결 끊김";

        private readonly ClientWebSocket _ws;
        private readonly ConcurrentQueue<ServerEvent> _inbox = new ConcurrentQueue<ServerEvent>();
        private readonly ConcurrentQueue<string?> _outbox = new ConcurrentQueue<string?>();   // null = 닫기 응답
        private readonly SemaphoreSlim _outSignal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private int _endedNormally;
        private int _disposed;

        private WebSocketClientTransport(ClientWebSocket ws)
        {
            _ws = ws;
            Task.Run(ReceiveLoop);
            Task.Run(SendLoop);
        }

        /// <summary>접속한다. 시간 초과·거절·주소 오류는 예외로 올라온다.</summary>
        public static async Task<WebSocketClientTransport> ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                linked.CancelAfter(timeout);
                try { await ws.ConnectAsync(uri, linked.Token).ConfigureAwait(false); }
                catch { ws.Dispose(); throw; }
            }
            return new WebSocketClientTransport(ws);
        }

        public IReadOnlyList<ServerEvent> Drain()
        {
            var taken = new List<ServerEvent>();
            while (_inbox.TryDequeue(out var ev)) taken.Add(ev);
            return taken;
        }

        public void Submit(ClientAction a)
        {
            _outbox.Enqueue(ProtocolJson.Serialize(a));
            _outSignal.Release();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _cts.Cancel();
            _ws.Abort();
            _ws.Dispose();
        }

        private async Task ReceiveLoop()
        {
            var buf = new byte[8192];
            var message = new MemoryStream();
            try
            {
                while (true)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            RequestCloseReply();
                            ReportLostIfUnexpected();
                            return;
                        }
                        message.Write(buf, 0, r.Count);
                    } while (!r.EndOfMessage);

                    var ev = ProtocolJson.DeserializeServerEvent(
                        Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    if (ev.Kind == ServerEventKind.MatchEnded || ev.Kind == ServerEventKind.SessionFaulted)
                        Interlocked.Exchange(ref _endedNormally, 1);
                    _inbox.Enqueue(ev);
                }
            }
            catch (Exception)
            {
                ReportLostIfUnexpected();
            }
        }

        private async Task SendLoop()
        {
            try
            {
                while (true)
                {
                    await _outSignal.WaitAsync(_cts.Token).ConfigureAwait(false);
                    if (!_outbox.TryDequeue(out var json)) continue;
                    if (json == null)
                    {
                        await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                            .ConfigureAwait(false);
                        return;
                    }
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception) { /* 끊김은 ReceiveLoop 가 보고한다 */ }
        }

        /// <summary>서버의 닫기에 응답한다 — 송신 태스크를 거쳐 동시 전송을 피한다.</summary>
        private void RequestCloseReply()
        {
            _outbox.Enqueue(null);
            _outSignal.Release();
        }

        private void ReportLostIfUnexpected()
        {
            if (Volatile.Read(ref _disposed) == 1) return;
            if (Interlocked.Exchange(ref _endedNormally, 1) == 1) return;   // 이미 정상 종료(또는 이미 보고)
            _inbox.Enqueue(ServerEvent.SessionFaulted(LostReason));
        }
    }
}
