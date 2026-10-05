using System;
using R3;
using Tichu.Net;
using Tichu.Presentation.Shell;
using Tichu.Presentation.ViewModel;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 전송 수신함과 뷰모델 사이의 펌프. 매 프레임 Pump() 가 호출되며, 서버 이벤트를
    /// 뷰모델에 밀어넣고 뷰모델이 낸 ClientAction 을 전송에 올린다.
    ///
    /// 유니티 타입에 의존하지 않는다 — 프레임 구동은 호출자(RoundBootstrap)의 몫이고
    /// 여기서는 순서만 책임진다. 그래서 헤드리스로 전 경로를 테스트할 수 있다.
    /// </summary>
    public sealed class TichuClient : IDisposable
    {
        private readonly LoopbackTransport _transport;
        private readonly TableViewModel _vm;
        private readonly int _mySeat;
        private readonly Action<MatchSummary> _onMatchEnded;
        private readonly IDisposable _outbound;

        // RoundScored 다음에 오는 첫 StateUpdate 가 새 라운드의 시작이다
        // (세션 순서: RoundScored → Decide → 멈춤 → NewRound → Broadcast).
        private bool _roundScored;

        /// <summary>마지막 거부 사유(뷰 표시·디버깅용). 없으면 null.</summary>
        public string LastRejection { get; private set; }

        /// <summary>매치 종료 이벤트를 받았는가.</summary>
        public bool MatchEnded { get; private set; }

        /// <summary>세션 스레드가 폴트로 죽었는가. true면 Pump() 소유자가 펌프를 멈춰야 한다.</summary>
        public bool Faulted { get; private set; }

        public TichuClient(LoopbackTransport transport, TableViewModel vm, int mySeat,
            Action<MatchSummary> onMatchEnded = null)
        {
            _transport = transport;
            _vm = vm;
            _mySeat = mySeat;
            _onMatchEnded = onMatchEnded;
            _outbound = _vm.Submitted.Subscribe(_transport.Submit);
        }

        /// <summary>수신함을 비우고 순서대로 적용한다. 메인스레드에서 호출한다.</summary>
        public void Pump()
        {
            var events = _transport.Drain(_mySeat);
            for (int i = 0; i < events.Count; i++) Handle(events[i]);
        }

        private void Handle(ServerEvent ev)
        {
            switch (ev.Kind)
            {
                case ServerEventKind.StateUpdate:
                    if (_roundScored)
                    {
                        _roundScored = false;
                        _vm.ClearPlays();           // 새 라운드: 결과·로그·로컬 예약 비우기
                    }
                    if (ev.View != null) _vm.Apply(ev.View);
                    if (ev.Applied != null) _vm.Record(ev.Applied);
                    break;

                case ServerEventKind.RoundScored:
                    if (ev.Round != null) _vm.OnRoundScored(ev.Round);
                    _roundScored = true;
                    break;

                case ServerEventKind.MatchEnded:
                    MatchEnded = true;
                    if (_onMatchEnded != null)
                        _onMatchEnded(new MatchSummary(ev.WinningTeam, ev.TeamA, ev.TeamB));
                    break;

                case ServerEventKind.ActionRejected:
                    // 서버가 내 액션을 되돌렸다. 상태는 StateUpdate 가 진실이라 되돌릴 것이
                    // 없고, 사유만 남긴다(사용자 표면화는 M5 — 거부된 예약 관측 가능화와 함께).
                    LastRejection = ev.Reason;
                    break;

                case ServerEventKind.SessionFaulted:
                    LastRejection = ev.Reason;
                    Faulted = true;
                    break;
            }
        }

        public void Dispose() { _outbound.Dispose(); }
    }
}
