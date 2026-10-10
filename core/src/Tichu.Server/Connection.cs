using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 소켓 하나(M2 스펙 §5·§6, M3 스펙 §2.1). 송신은 큐에 넣기만 하고 연결마다 송신 태스크 하나가
    /// 순서대로 보낸다(WebSocket 은 한 소켓에 동시 전송 금지). 수신은 16KB 초과·텍스트 아님·레이트리밋
    /// 초과·깨진 JSON 이면 닫는다. 받은 액션은 지금 소속(Owner: 대기열 또는 방)에 넘긴다.
    /// </summary>
    public sealed class Connection : IPlayerLink
    {
        public const int MaxMessageBytes = 16 * 1024;
        private static int _nextId;

        private readonly Channel<string> _out =
            Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly TokenBucket? _bucket;
        private readonly Action<string> _log;
        private volatile ILinkOwner? _owner;
        private int _completed;
        private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;
        private string _closeReason = "bye";

        public Connection(WebSocket socket, TokenBucket? bucket, Action<string> log, string deviceId = "")
        {
            DeviceId = deviceId;
            Socket = socket;
            _bucket = bucket;
            _log = log;
            WriterTask = Task.Run(WriteLoop);
        }

        public int Id { get; } = Interlocked.Increment(ref _nextId);
        public string DeviceId { get; }
        public WebSocket Socket { get; }
        public Task WriterTask { get; }

        public ILinkOwner? Owner
        {
            get => _owner;
            set => _owner = value;
        }

        public void Send(ServerEvent ev) => _out.Writer.TryWrite(ProtocolJson.Serialize(ev));

        public void Close(string reason) => Complete(WebSocketCloseStatus.NormalClosure, reason);

        /// <summary>더 보낼 것이 없다. 남은 큐를 비운 뒤 status 로 닫는다. 처음 호출만 유효.</summary>
        public void Complete(WebSocketCloseStatus status, string reason)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 1) return;
            _closeStatus = status;
            _closeReason = reason;
            _out.Writer.TryComplete();
        }

        /// <summary>
        /// 좌석·대기열 없이 사유 하나(SessionFaulted)만 보낸 뒤 닫는다 — 프로토콜 버전 불일치(M2b §5.3).
        /// 바로 끊으면 클라이언트가 아직 읽지 않은 사유가 버려질 수 있으므로 상대의 닫기 응답을 최대 2초 기다린다.
        /// </summary>
        public static async Task RejectAsync(WebSocket ws, string reason)
        {
            var conn = new Connection(ws, null, _ => { });
            conn.Send(ServerEvent.SessionFaulted(reason));
            conn.Complete(WebSocketCloseStatus.PolicyViolation, "protocol version");
            await conn.WriterTask;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var buf = new byte[256];
            try
            {
                while (ws.State == WebSocketState.CloseSent)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }

        /// <summary>연결이 끝날 때까지 수신한다. 끝나면 소유자에게 Left 를 알리고 송신을 마친다.</summary>
        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await ReceiveLoop(ct);
            }
            catch (WebSocketException) { /* 갑작스러운 끊김 */ }
            catch (OperationCanceledException) { /* 서버 종료 */ }
            finally
            {
                Complete(WebSocketCloseStatus.NormalClosure, "bye");
                _log($"연결 #{Id} 끊김");
                Owner?.Left(this);
            }
            await WriterTask;
        }

        /// <summary>메시지 하나를 해석해 소유자에게 넘긴다. 해석 실패면 false(호출자가 닫는다).</summary>
        internal bool Dispatch(string json)
        {
            ClientAction a;
            try { a = ProtocolJson.DeserializeClientAction(json); }
            catch (ProtocolException e)
            {
                _log($"연결 #{Id} 잘못된 메시지: {e.Message}");
                return false;
            }
            Owner?.Receive(this, a);   // 방이 Seat 을 연결 좌석으로 덮어쓴다(사칭 방지)
            return true;
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[4096];
            var message = new MemoryStream();

            while (true)
            {
                message.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await Socket.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (r.MessageType != WebSocketMessageType.Text)
                    {
                        Abort(WebSocketCloseStatus.InvalidMessageType, "text only");
                        return;
                    }
                    message.Write(buf, 0, r.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        Abort(WebSocketCloseStatus.MessageTooBig, "message too big");
                        return;
                    }
                } while (!r.EndOfMessage);

                if (_bucket != null && !_bucket.TryTake())
                {
                    Abort(WebSocketCloseStatus.PolicyViolation, "rate limit");
                    return;
                }

                string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                if (!Dispatch(json))
                {
                    Abort(WebSocketCloseStatus.InvalidPayloadData, "bad message");
                    return;
                }
            }
        }

        private void Abort(WebSocketCloseStatus status, string reason)
        {
            _log($"연결 #{Id} 연결 종료: {reason}");
            Complete(status, reason);
        }

        private async Task WriteLoop()
        {
            try
            {
                await foreach (var json in _out.Reader.ReadAllAsync())
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                if (Socket.State == WebSocketState.Open || Socket.State == WebSocketState.CloseReceived)
                    await Socket.CloseOutputAsync(_closeStatus, _closeReason, CancellationToken.None);
            }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
