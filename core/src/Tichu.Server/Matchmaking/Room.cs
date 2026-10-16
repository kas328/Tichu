using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 매치 하나(M3 §2.1, M5). 빈자리는 PIMC, 사람 좌석은 원격 좌석 — 사람이 끊기면 그 좌석의 대리 AI 가
    /// 둔다(판은 깨지지 않는다). 같은 기기가 다시 오면(Rejoin) 그 좌석에 다시 붙여 로비(+누적 점수)와
    /// 마지막 상태 스냅샷을 보낸다. 방에 연결된 사람이 0명인 채 EmptyRoomSeconds 가 지나면 닫는다.
    /// 링크 표·스냅샷 캐시·송신은 한 잠금(_gate) 안에서 — 재접속 스냅샷과 세션 방송의 순서가 뒤바뀌지 않는다.
    /// 잠금 순서: 방(_gate) → RemoteAgent 잠금. 연결 상태 갱신(SetSeatConnected·ResetSeatSequence)은 _gate 안에서 해야 Left/Rejoin/Start 갱신이 뒤섞이지 않는다.
    /// RemoteAgent 는 자기 잠금을 쥔 채 Room 을 부르면 안 된다(교착).
    /// </summary>
    public sealed class Room : IRoom, ITichuTransport
    {
        private const int SeatCount = 4;

        private readonly int _index;
        private readonly ServerOptions _opt;
        private readonly Func<DateTime> _now;
        private readonly Action<string> _log;
        private readonly Action<Room> _onClosed;
        private readonly int _humans;
        private readonly string?[] _deviceBySeat = new string?[SeatCount];       // 생성 후 불변
        private readonly IPlayerLink?[] _bySeat = new IPlayerLink?[SeatCount];   // _gate
        private readonly ServerEvent?[] _lastState = new ServerEvent?[SeatCount]; // _gate
        private readonly Awaiting?[] _lastAwaiting = new Awaiting?[SeatCount];     // _gate — 보낸 때의 질문(재전송이 view 를 덮어써도 원본 유지)
        private readonly DateTime[] _lastStateAt = new DateTime[SeatCount];        // _gate — 그 스냅샷을 보낸 시각
        private readonly object _gate = new object();
        private Action<ClientAction>? _handler;   // _gate
        private MatchSession? _session;           // _gate, Start 이후
        private DateTime? _emptySince;            // _gate
        private int _ending;
        private int _closing;

        /// <summary>가볍다 — 매치메이커 잠금 안에서 불린다. 좌석 배정과 소속 전환만 한다.</summary>
        public Room(int index, IReadOnlyList<IPlayerLink> humans, int[] seats, ServerOptions opt,
            Func<DateTime> now, Action<string> log, Action<Room> onClosed)
        {
            if (humans.Count != seats.Length) throw new ArgumentException("humans and seats differ in length");
            _index = index;
            _opt = opt;
            _now = now;
            _log = log;
            _onClosed = onClosed;
            _humans = humans.Count;
            for (int i = 0; i < humans.Count; i++)
            {
                _bySeat[seats[i]] = humans[i];
                _deviceBySeat[seats[i]] = humans[i].DeviceId;
                humans[i].Owner = this;
            }
        }

        public IReadOnlyList<string> DeviceIds
        {
            get
            {
                var ids = new List<string>();
                foreach (var d in _deviceBySeat) if (!string.IsNullOrEmpty(d)) ids.Add(d!);
                return ids;
            }
        }

        public void Start()
        {
            ulong seed = _opt.Seed is ulong s ? unchecked(s + (ulong)_index) : unchecked((ulong)DateTime.UtcNow.Ticks + (ulong)_index);
            var config = PolicyConfig.For(_opt.Difficulty);
            int budgetMs = PimcBudget.For(_opt.Difficulty);

            var agents = new IAgent?[SeatCount];
            for (int seat = 0; seat < SeatCount; seat++)
                if (_deviceBySeat[seat] == null)
                    agents[seat] = new PacedPimcAgent(seed, seat, config, budgetMs, _opt.AiDelayMs);

            var session = new MatchSession(
                agents, this, seed,
                targetScore: _opt.Target,
                turnTimeout: TimeSpan.FromSeconds(_opt.TurnSeconds),
                interRoundPause: TimeSpan.FromMilliseconds(_opt.InterRoundPauseMs),
                standInFor: st => new PacedPimcAgent(unchecked(seed + 0x9E3779B97F4A7C15UL + (ulong)st), st, config, budgetMs, _opt.AiDelayMs),
                log: m => _log($"방 {_index} {m}"));

            var gone = new List<int>();
            lock (_gate)
            {
                _session = session;
                // Lobby 가 첫 StateUpdate 보다 먼저 큐에 들어가야 한다(클라이언트 LobbyWaiter) — 세션 시작 전에 보낸다.
                for (int seat = 0; seat < SeatCount; seat++)
                {
                    if (_deviceBySeat[seat] == null) continue;
                    if (_bySeat[seat] == null) gone.Add(seat);
                    else _bySeat[seat]!.Send(ServerEvent.Lobby(seat, _humans, _humans));
                }
                if (gone.Count == _humans) _emptySince = _now();
                foreach (int seat in gone) session.SetSeatConnected(seat, false);   // 시작 전에 끊긴 좌석
            }
            session.Start();
            _log($"방 {_index} 시작 — 사람 {_humans} · AI {SeatCount - _humans} · seed {seed} · {_opt.Difficulty} · 목표 {_opt.Target}");

            Task.Factory.StartNew(() =>
            {
                session.Join(Timeout.InfiniteTimeSpan);
                End();
            }, TaskCreationOptions.LongRunning);
        }

        public void SendTo(int seat, ServerEvent ev)
        {
            if (ev.Kind == ServerEventKind.ActionRejected || ev.Kind == ServerEventKind.SessionFaulted)
                _log($"방 {_index} 좌석 {seat} {ev.Kind}: {ev.Reason}");
            if (seat < 0 || seat >= SeatCount) return;
            lock (_gate)
            {
                if (ev.Kind == ServerEventKind.StateUpdate)
                {
                    _lastState[seat] = ev;                                            // 재접속 스냅샷
                    _lastAwaiting[seat] = ev.View?.Awaiting;
                    _lastStateAt[seat] = _now();
                }
                _bySeat[seat]?.Send(ev);                                              // 끊긴·AI 좌석은 버린다
            }
        }

        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        public void Receive(IPlayerLink from, ClientAction a)
        {
            Action<ClientAction>? h;
            lock (_gate)
            {
                int seat = SeatOfLocked(from);
                if (seat < 0) return;                        // 교체된 옛 연결
                a.Seat = seat;                               // 사칭 방지
                h = _handler;
            }
            h?.Invoke(a);
        }

        /// <summary>
        /// 재접속 스냅샷의 질문 — 남은 초는 보낸 때의 값이라 그 사이 흐른 만큼 뺀다(0 하한). 남의 차례 시계가
        /// 부풀지 않게 한다(M5.1). 원본은 다른 좌석과 공유될 수 있어 복사본을 만든다.
        /// </summary>
        private static Awaiting? Aged(Awaiting? aw, TimeSpan elapsed)
        {
            if (aw?.SecondsLeft == null) return aw;
            return new Awaiting
            {
                Kind = aw.Kind, Seat = aw.Seat, DeadlineUtc = aw.DeadlineUtc, AskId = aw.AskId,
                LegalMoves = aw.LegalMoves, CanPass = aw.CanPass,
                SecondsLeft = Math.Max(0, aw.SecondsLeft.Value - (int)elapsed.TotalSeconds),
            };
        }

        public void Left(IPlayerLink who)
        {
            if (Volatile.Read(ref _ending) == 1) return;     // 종료 중 우리가 닫은 연결
            int seat;
            bool empty;
            MatchSession? session;
            lock (_gate)
            {
                seat = SeatOfLocked(who);
                if (seat < 0) return;                        // 이미 새 연결로 교체됨
                _bySeat[seat] = null;
                empty = ConnectedLocked() == 0;
                if (empty) _emptySince = _now();
                session = _session;
                session?.SetSeatConnected(seat, false);      // null = 시작 전 — Start 가 처리한다
            }
            _log($"방 {_index} 좌석 {seat} 연결 끊김 — AI 대행{(empty ? " · 사람 0명, 멈춤" : "")}");
        }

        public void Rejoin(IPlayerLink link)
        {
            int seat;
            IPlayerLink? old;
            MatchSession? session;
            lock (_gate)
            {
                seat = Array.IndexOf(_deviceBySeat, link.DeviceId);
                if (seat < 0 || Volatile.Read(ref _ending) == 1)
                {
                    link.Send(ServerEvent.SessionFaulted("진행 중인 판이 끝났습니다"));
                    link.Close("match over");
                    return;
                }
                old = _bySeat[seat];
                _bySeat[seat] = link;
                link.Owner = this;
                _emptySince = null;
                session = _session;

                var lobby = ServerEvent.Lobby(seat, _humans, _humans);
                if (session != null) { var sc = session.MatchScore; lobby.TeamA = sc.TeamA; lobby.TeamB = sc.TeamB; }
                link.Send(lobby);
                var last = _lastState[seat];
                if (last?.View != null)
                    link.Send(ServerEvent.StateUpdate(last.View, Aged(_lastAwaiting[seat], _now() - _lastStateAt[seat])));
                if (session != null)
                {
                    session.ResetSeatSequence(seat);          // 새 앱은 ActionSeq 1 부터 — 수신 루프 시작 전에
                    session.SetSeatConnected(seat, true);     // 멈춰 있었으면 다시 묻는다
                }
            }
            if (old != null)
            {
                // 사유를 먼저 보낸다 — 밀려난 연결이 끊김으로 보고 재접속해 자리를 되찾지 않게.
                old.Send(ServerEvent.SessionFaulted("다른 곳에서 접속"));
                old.Close("다른 곳에서 접속");
            }
            _log($"방 {_index} 좌석 {seat} 재접속 (연결 #{link.Id})");
        }

        public void Tick(DateTime now)
        {
            MatchSession? session;
            lock (_gate)
            {
                if (_emptySince == null || now - _emptySince.Value < TimeSpan.FromSeconds(_opt.EmptyRoomSeconds)) return;
                _emptySince = null;
                session = _session;
            }
            if (Interlocked.Exchange(ref _closing, 1) == 1) return;
            _log($"방 {_index} 사람 0명 {_opt.EmptyRoomSeconds}초 — 방 종료");
            session?.Stop();                                  // 세션 스레드가 끝나면 End 가 정리한다
        }

        private int SeatOfLocked(IPlayerLink link)
        {
            for (int s = 0; s < SeatCount; s++)
                if (_bySeat[s] == link) return s;
            return -1;
        }

        private int ConnectedLocked()
        {
            int n = 0;
            for (int s = 0; s < SeatCount; s++) if (_bySeat[s] != null) n++;
            return n;
        }

        private void End()
        {
            if (Interlocked.Exchange(ref _ending, 1) == 1) return;
            _log($"방 {_index} 종료");
            IPlayerLink?[] links;
            lock (_gate) links = (IPlayerLink?[])_bySeat.Clone();
            foreach (var l in links) l?.Close("match over");
            _onClosed(this);
        }
    }
}
