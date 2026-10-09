using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 매치 하나(M3 스펙 §2.1). 빈자리를 PIMC 로 채워 MatchSession 을 돌리고, 세션이 끝나면(정상·폴트)
    /// 자기 연결만 닫고 onClosed 로 매치메이커에 자리를 돌려준다. 매치 중 사람이 끊기면 그 방에만
    /// SessionFaulted 를 보낸다 — MatchSession.Stop() 은 아무것도 방송하지 않으므로 방송은 여기 몫이다.
    /// 연결은 이 방에만 속하므로 이전 매치 연결을 거르는 세대 번호가 필요 없다.
    /// </summary>
    public sealed class Room : IRoom, ITichuTransport
    {
        private const int SeatCount = 4;

        private readonly int _index;
        private readonly ServerOptions _opt;
        private readonly Action<string> _log;
        private readonly Action _onClosed;
        private readonly IPlayerLink?[] _bySeat = new IPlayerLink?[SeatCount];
        private readonly object _gate = new object();
        private Action<ClientAction>? _handler;   // _gate
        private MatchSession? _session;           // _gate, Start 이후
        private int _faultedSeat = -1;            // _gate
        private int _ending;

        /// <summary>가볍다 — 매치메이커 잠금 안에서 불린다. 좌석 배정과 소속 전환만 한다.</summary>
        public Room(int index, IReadOnlyList<IPlayerLink> humans, int[] seats, ServerOptions opt,
            Action<string> log, Action onClosed)
        {
            if (humans.Count != seats.Length) throw new ArgumentException("humans and seats differ in length");
            _index = index;
            _opt = opt;
            _log = log;
            _onClosed = onClosed;
            for (int i = 0; i < humans.Count; i++)
            {
                _bySeat[seats[i]] = humans[i];
                humans[i].Owner = this;
            }
        }

        public void Start()
        {
            ulong seed = _opt.Seed is ulong s ? unchecked(s + (ulong)_index) : unchecked((ulong)DateTime.UtcNow.Ticks);
            var config = PolicyConfig.For(_opt.Difficulty);
            int budgetMs = PimcBudget.For(_opt.Difficulty);

            var agents = new IAgent?[SeatCount];
            int humans = 0;
            for (int seat = 0; seat < SeatCount; seat++)
            {
                if (_bySeat[seat] != null) { humans++; continue; }
                agents[seat] = new PacedPimcAgent(seed, seat, config, budgetMs, _opt.AiDelayMs);
            }

            // Lobby 가 첫 StateUpdate 보다 먼저 큐에 들어가야 한다(클라이언트 LobbyWaiter) — 세션 시작 전에 보낸다.
            for (int seat = 0; seat < SeatCount; seat++)
                _bySeat[seat]?.Send(ServerEvent.Lobby(seat, humans, humans));

            var session = new MatchSession(
                agents, this, seed,
                targetScore: _opt.Target,
                interRoundPause: TimeSpan.FromMilliseconds(_opt.InterRoundPauseMs));
            session.Start();
            lock (_gate)
            {
                _session = session;
                if (_faultedSeat >= 0) session.Stop();   // 시작 전에 이미 누가 나갔다
            }
            _log($"방 {_index} 시작 — 사람 {humans} · AI {SeatCount - humans} · seed {seed} · {_opt.Difficulty} · 목표 {_opt.Target}");

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
            _bySeat[seat]?.Send(ev);                    // AI 좌석은 연결이 없다 → 버린다
        }

        public void OnClientAction(Action<ClientAction> handler)
        {
            lock (_gate) _handler = handler;
        }

        public void Receive(IPlayerLink from, ClientAction a)
        {
            int seat = SeatOf(from);
            if (seat < 0) return;
            a.Seat = seat;                              // 사칭 방지 — 세션은 a.Seat 로 우편함을 고른다
            Action<ClientAction>? h;
            lock (_gate) h = _handler;
            h?.Invoke(a);
        }

        public void Left(IPlayerLink who)
        {
            if (Volatile.Read(ref _ending) == 1) return;   // 종료 중 우리가 닫은 연결
            int seat = SeatOf(who);
            if (seat < 0) return;
            lock (_gate)
            {
                if (_session != null && _session.Join(TimeSpan.Zero)) return;   // 이미 정상 종료 — 폴트 아님
                if (_faultedSeat < 0) _faultedSeat = seat;
                _session?.Stop();   // 방송은 세션 스레드가 끝난 뒤 End 가 한다
            }
        }

        private int SeatOf(IPlayerLink link)
        {
            for (int s = 0; s < SeatCount; s++)
                if (_bySeat[s] == link) return s;
            return -1;
        }

        private void End()
        {
            if (Interlocked.Exchange(ref _ending, 1) == 1) return;
            int faulted;
            lock (_gate) faulted = _faultedSeat;
            if (faulted >= 0)
            {
                _log($"방 {_index} 좌석 {faulted} 이탈 — 매치 중단");
                for (int s = 0; s < SeatCount; s++)
                    SendTo(s, ServerEvent.SessionFaulted($"좌석 {faulted} 연결 끊김"));
            }
            _log($"방 {_index} 종료");
            for (int s = 0; s < SeatCount; s++) _bySeat[s]?.Close("match over");
            _onClosed();
        }
    }
}
