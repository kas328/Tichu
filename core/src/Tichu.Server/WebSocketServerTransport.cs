using System;
using System.Collections.Generic;
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
    /// 권위 세션이 보는 WebSocket 전송(M2 스펙 §5·§6). 연결 하나 = 인간 좌석 하나.
    ///
    /// - 좌석: 접속 순서대로 SeatTable 에서 받는다. 만석·매치 진행 중이면 즉시 닫는다.
    /// - Lobby: 들어오고 나갈 때마다 전원에게 보낸다. 배정·Lobby 큐잉·만석 판정을 한 잠금 안에서
    ///   해서, 마지막 Lobby 가 세션의 첫 StateUpdate 보다 반드시 먼저 큐에 들어간다.
    /// - 송신: 세션 스레드는 SendTo 로 큐에 넣기만 한다(네트워크에서 막히지 않는다). 연결마다
    ///   송신 태스크 하나가 순서대로 보낸다 — WebSocket 은 한 소켓에 동시 전송이 금지다.
    /// - 수신: 16KB 초과·텍스트 아님·깨진 JSON·레이트리밋 초과는 연결을 닫는다.
    ///   받은 액션의 Seat 은 연결 좌석으로 덮어쓴다(사칭 방지).
    /// </summary>
    public sealed class WebSocketServerTransport : ITichuTransport
    {
        public const int MaxMessageBytes = 16 * 1024;

        private readonly SeatTable _seats;
        private readonly Func<TokenBucket> _newBucket;
        private readonly Action<string> _log;
        private readonly object _gate = new object();
        private readonly Dictionary<int, Connection> _bySeat = new Dictionary<int, Connection>();
        private Action<ClientAction>? _handler;
        private bool _matchStarted;
        private bool _filledRaised;

        /// <summary>마지막 인간 좌석이 찬 순간 한 번 발생한다. 호출 스레드 = 그 연결의 처리 스레드.</summary>
        public event Action? SeatsFilled;

        /// <summary>매치 시작 후 인간이 끊겼다. 시작 전 끊김은 좌석 반납으로 처리하고 알리지 않는다.</summary>
        public event Action<int>? SeatLeft;

        public WebSocketServerTransport(SeatTable seats, Func<TokenBucket> newBucket, Action<string> log)
        {
            _seats = seats;
            _newBucket = newBucket;
            _log = log;
        }

        /// <summary>이후의 끊김은 좌석 반납이 아니라 SeatLeft 가 된다. 새 접속은 거절된다.</summary>
        public void MarkMatchStarted()
        {
            lock (_gate) _matchStarted = true;
        }

        public void SendTo(int seat, ServerEvent ev)
        {
            Connection? c;
            lock (_gate) _bySeat.TryGetValue(seat, out c);
            c?.Enqueue(ProtocolJson.Serialize(ev));   // AI 좌석·끊긴 좌석은 연결이 없다 → 버린다
        }

        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        /// <summary>연결 하나를 끝날 때까지 처리한다. ASP.NET 핸들러가 await 한다.</summary>
        public async Task HandleAsync(WebSocket ws, CancellationToken ct)
        {
            var conn = new Connection(ws);
            int seat;
            bool raiseFilled = false;

            lock (_gate)
            {
                if (_matchStarted || !_seats.TryTake(out seat))
                {
                    seat = -1;
                }
                else
                {
                    _bySeat[seat] = conn;
                    EnqueueLobbyLocked();
                    if (_seats.IsFull && !_filledRaised) { _filledRaised = true; raiseFilled = true; }
                }
            }

            if (seat < 0)
            {
                _log("만석 — 접속 거절");
                conn.Complete(WebSocketCloseStatus.PolicyViolation, "room full");
                await conn.WriterTask;
                return;
            }

            _log($"좌석 {seat} 접속 ({_seats.Connected}/{_seats.Required})");
            if (raiseFilled) SeatsFilled?.Invoke();

            try
            {
                await ReceiveLoop(conn, seat, ct);
            }
            catch (WebSocketException) { /* 갑작스러운 끊김 */ }
            catch (OperationCanceledException) { /* 서버 종료 */ }
            finally
            {
                bool started;
                lock (_gate)
                {
                    _bySeat.Remove(seat);
                    started = _matchStarted;
                    if (!started)
                    {
                        _seats.Release(seat);
                        _filledRaised = false;
                        EnqueueLobbyLocked();
                    }
                }
                conn.Complete(WebSocketCloseStatus.NormalClosure, "bye");
                _log($"좌석 {seat} 끊김");
                if (started) SeatLeft?.Invoke(seat);
            }

            await conn.WriterTask;
        }

        /// <summary>남은 메시지를 다 보낸 뒤 모든 연결을 정상 종료한다(매치 종료·폴트 후).</summary>
        public async Task CloseAllAsync()
        {
            List<Connection> all;
            lock (_gate) all = new List<Connection>(_bySeat.Values);
            foreach (var c in all) c.Complete(WebSocketCloseStatus.NormalClosure, "match over");

            var writers = new List<Task>();
            foreach (var c in all) writers.Add(c.WriterTask);
            await Task.WhenAny(Task.WhenAll(writers), Task.Delay(TimeSpan.FromSeconds(2)));
        }

        /// <summary>메시지 하나를 해석해 세션에 넘긴다. 해석 실패면 false(호출자가 연결을 닫는다).</summary>
        internal bool Dispatch(int seat, string json)
        {
            ClientAction a;
            try { a = ProtocolJson.DeserializeClientAction(json); }
            catch (ProtocolException e)
            {
                _log($"좌석 {seat} 잘못된 메시지: {e.Message}");
                return false;
            }

            a.Seat = seat;                    // 사칭 방지(스펙 §5.4) — 세션은 a.Seat 로 우편함을 고른다

            Action<ClientAction>? h;
            lock (_gate) h = _handler;
            h?.Invoke(a);                     // 매치 시작 전(핸들러 없음)에는 버린다
            return true;
        }

        private async Task ReceiveLoop(Connection conn, int seat, CancellationToken ct)
        {
            var bucket = _newBucket();
            var buf = new byte[4096];
            var message = new MemoryStream();

            while (true)
            {
                message.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await conn.Socket.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    if (r.MessageType != WebSocketMessageType.Text)
                    {
                        Abort(conn, seat, WebSocketCloseStatus.InvalidMessageType, "text only");
                        return;
                    }
                    message.Write(buf, 0, r.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        Abort(conn, seat, WebSocketCloseStatus.MessageTooBig, "message too big");
                        return;
                    }
                } while (!r.EndOfMessage);

                if (!bucket.TryTake())
                {
                    Abort(conn, seat, WebSocketCloseStatus.PolicyViolation, "rate limit");
                    return;
                }

                string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                if (!Dispatch(seat, json))
                {
                    Abort(conn, seat, WebSocketCloseStatus.InvalidPayloadData, "bad message");
                    return;
                }
            }
        }

        private void Abort(Connection conn, int seat, WebSocketCloseStatus status, string reason)
        {
            _log($"좌석 {seat} 연결 종료: {reason}");
            conn.Complete(status, reason);
        }

        private void EnqueueLobbyLocked()
        {
            foreach (var kv in _bySeat)
                kv.Value.Enqueue(ProtocolJson.Serialize(ServerEvent.Lobby(kv.Key, _seats.Connected, _seats.Required)));
        }

        /// <summary>소켓 하나 + 송신 큐 + 송신 태스크. 송신이 끝나면 지정된 상태로 닫는다.</summary>
        private sealed class Connection
        {
            private readonly Channel<string> _out =
                Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
            private int _completed;
            private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;
            private string _closeReason = "bye";

            public WebSocket Socket { get; }
            public Task WriterTask { get; }

            public Connection(WebSocket socket)
            {
                Socket = socket;
                WriterTask = Task.Run(WriteLoop);
            }

            public void Enqueue(string json) => _out.Writer.TryWrite(json);

            /// <summary>더 보낼 것이 없다. 남은 큐를 비운 뒤 status 로 닫는다. 처음 호출만 유효.</summary>
            public void Complete(WebSocketCloseStatus status, string reason)
            {
                if (Interlocked.Exchange(ref _completed, 1) == 1) return;
                _closeStatus = status;
                _closeReason = reason;
                _out.Writer.TryComplete();
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
}
