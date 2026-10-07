using System;
using System.Threading;
using System.Threading.Tasks;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 매치의 수명. 방이 차면 빈자리를 PIMC 로 채워 MatchSession 을 시작하고, 세션 스레드가
    /// 끝나면(MatchEnded 또는 폴트) 연결을 정상 종료한 뒤 새 대기실을 연다(프로세스는 계속 돈다).
    /// 매치 중 인간이 끊기면 남은 연결에 SessionFaulted 를 보내고 세션을 멈춘다 — MatchSession.Stop()
    /// 은 아무것도 방송하지 않으므로(취소를 조용히 삼킨다) 방송은 여기 몫이다(스펙 §6.1).
    ///
    /// 매치 번호(_matchIndex)가 전송의 매치 세대다. 초기화 때 여기서 먼저 올린 뒤 전송에 넘기므로,
    /// 잠금 밖에서 늦게 도착한 이전 매치의 SeatLeft 는 세대가 달라 무시된다.
    /// </summary>
    public sealed class MatchHost
    {
        private const int SeatCount = 4;

        private readonly ServerOptions _opt;
        private readonly WebSocketServerTransport _transport;
        private readonly Action<string> _log;
        private readonly object _gate = new object();
        private MatchSession? _session;     // Start() 이후에만 채워진다(_gate)
        private int _faultedSeat = -1;      // 먼저 이탈한 좌석(_gate)
        private int _matchIndex;            // 지금 매치 번호 = 전송 세대(_gate)
        private int _ending;

        public MatchHost(ServerOptions opt, WebSocketServerTransport transport, Action<string> log)
        {
            _opt = opt;
            _transport = transport;
            _log = log;
            transport.SeatsFilled += StartMatch;
            transport.SeatLeft += OnSeatLeft;
        }

        private void StartMatch()
        {
            int index;
            lock (_gate) index = _matchIndex;
            // --seed 가 있으면 매치마다 결정적으로 바꾼다(첫 판은 seed 그대로).
            ulong seed = _opt.Seed is ulong s ? unchecked(s + (ulong)index) : unchecked((ulong)DateTime.UtcNow.Ticks);
            var config = PolicyConfig.For(_opt.Difficulty);
            int budgetMs = PimcBudget.For(_opt.Difficulty);

            var agents = new IAgent?[SeatCount];
            for (int seat = 0; seat < SeatCount; seat++)
                if (Array.IndexOf(_opt.Seats, seat) < 0)
                    agents[seat] = new PacedPimcAgent(seed, seat, config, budgetMs, _opt.AiDelayMs);

            var session = new MatchSession(
                agents, _transport, seed,
                targetScore: _opt.Target,
                interRoundPause: TimeSpan.FromMilliseconds(_opt.InterRoundPauseMs));
            session.Start();
            lock (_gate)
            {
                _session = session;
                if (_faultedSeat >= 0) session.Stop();   // 시작 도중 이미 누가 나갔다
            }
            _log($"매치 시작 — seed {seed} · {_opt.Difficulty} · 목표 {_opt.Target}");

            Task.Factory.StartNew(async () =>
            {
                session.Join(Timeout.InfiniteTimeSpan);
                await EndAsync();
            }, TaskCreationOptions.LongRunning);
        }

        private void OnSeatLeft(int seat, int generation)
        {
            if (Volatile.Read(ref _ending) == 1) return;   // 종료 중 우리가 닫은 연결
            lock (_gate)
            {
                if (generation != _matchIndex) return;       // 이전 매치 연결이 늦게 끝났다
                // 매치가 이미 정상 종료됐다면 늦은 끊김은 폴트가 아니다.
                if (_session != null && _session.Join(TimeSpan.Zero)) return;
                if (_faultedSeat < 0) _faultedSeat = seat;
                _session?.Stop();   // 방송은 세션 스레드가 끝난 뒤 EndAsync 가 한다(스펙 §6.1)
            }
        }

        private async Task EndAsync()
        {
            if (Interlocked.Exchange(ref _ending, 1) == 1) return;
            int faulted;
            lock (_gate) faulted = _faultedSeat;
            if (faulted >= 0)
            {
                _log($"매치 중 좌석 {faulted} 이탈 — 매치 중단");
                for (int s = 0; s < SeatCount; s++)
                    _transport.SendTo(s, ServerEvent.SessionFaulted($"좌석 {faulted} 연결 끊김"));
            }
            _log("매치 종료 — 연결을 닫는다");
            await _transport.CloseAllAsync();

            // 새 매치를 받을 초기 상태로. 세대를 여기서 먼저 올린다 — 그 뒤로 도착하는 이전 매치의
            // SeatLeft 는 걸러진다. 전송 잠금은 이 잠금 밖에서 잡는다(잠금 순서 유지).
            int next;
            lock (_gate)
            {
                _session = null;
                _faultedSeat = -1;
                next = ++_matchIndex;
                Volatile.Write(ref _ending, 0);
            }
            _transport.ResetForNextMatch(next);
            _log("다음 매치 대기");
        }
    }
}
