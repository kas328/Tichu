using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.GameFlow;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 테스트용 얇은 클라이언트. PlayerView 와 Awaiting 만 보고 행동한다 —
    /// 서버가 대기 좌석에게 합법수를 실어 주므로 GameState 를 복원할 필요가 없다.
    ///
    /// 전략: 큰 티츄 패스 · 교환은 손패 앞 3장 · 차례가 오면 합법수 첫 번째(없으면 패스) ·
    /// 용 양도는 왼쪽 상대. 강한 플레이가 목적이 아니라 합법적으로 매치를 완주하는 것이 목적이다.
    /// </summary>
    internal sealed class ScriptedClient
    {
        private readonly int _seat;
        private readonly LoopbackTransport _transport;

        private long _seq;
        private bool _illegalSent;

        /// <summary>true 면 첫 차례에 불법 액션을 한 번 보내 거부 경로를 태운다.</summary>
        public bool SubmitOneIllegalAction { get; set; }

        public int RejectionsSeen { get; private set; }

        /// <summary>MatchEnded 를 받았는가.</summary>
        public bool MatchEnded { get; private set; }

        /// <summary>SessionFaulted 사유. 정상이면 null.</summary>
        public string? FaultReason { get; private set; }

        /// <summary>이 클라이언트가 담당하는 좌석.</summary>
        public int Seat => _seat;

        public ScriptedClient(int seat, LoopbackTransport transport)
        {
            _seat = seat;
            _transport = transport;
        }

        /// <summary>아직 처리하지 않은 이벤트를 꺼내 처리한다.</summary>
        public void Pump()
        {
            foreach (var ev in _transport.Drain(_seat))
            {
                switch (ev.Kind)
                {
                    case ServerEventKind.ActionRejected: RejectionsSeen++; continue;
                    case ServerEventKind.MatchEnded:     MatchEnded = true; continue;
                    case ServerEventKind.SessionFaulted: FaultReason = ev.Reason; continue;
                    case ServerEventKind.StateUpdate:    break;
                    default:                             continue;
                }

                var view = ev.View!;
                var awaiting = view.Awaiting;
                if (awaiting == null || awaiting.Seat != _seat) continue;

                Respond(view, awaiting);
            }
        }

        private void Respond(PlayerView view, Awaiting awaiting)
        {
            switch (awaiting.Kind)
            {
                case StepKind.GrandTichu:
                    _transport.Submit(ClientAction.DeclineGrandTichu(_seat, ++_seq));
                    break;

                case StepKind.Exchange:
                    var h = view.MyHand;
                    _transport.Submit(ClientAction.Exchange(_seat, ++_seq, h[0], h[1], h[2]));
                    break;

                case StepKind.Play:
                    SubmitPlay(awaiting);
                    break;

                case StepKind.DragonGift:
                    _transport.Submit(ClientAction.GiveDragon(_seat, ++_seq, (_seat + 1) % 4));
                    break;
            }
        }

        private void SubmitPlay(Awaiting awaiting)
        {
            var moves = awaiting.LegalMoves;

            if (SubmitOneIllegalAction && !_illegalSent && moves != null && moves.Count > 0)
            {
                _illegalSent = true;
                // 합법수의 카드를 그대로 쓰고 Rank 만 존재할 수 없는 값으로 보낸다 → 반드시 불법.
                // (손에 없을 것 같은 카드를 보내는 방식은 실제로 들고 있으면 합법이 되어 버린다.)
                _transport.Submit(ClientAction.Play(_seat, ++_seq,
                    new List<Card>(moves[0].Cards), rank: int.MaxValue, wish: null));
                // 여기서 끝낸다. 거부되면 에이전트가 다시 대기에 들어가며 새 Awaiting 을
                // 방송하므로 합법수는 다음 Pump 에서 보낸다 — 거부 통보를 삼키지 않는다.
                return;
            }

            if (moves != null && moves.Count > 0)
            {
                var m = moves[0];
                // 마작 리드 시 소원을 걸 수 있지만 단순화를 위해 걸지 않는다.
                _transport.Submit(ClientAction.Play(_seat, ++_seq, new List<Card>(m.Cards), m.Rank, wish: null));
                return;
            }

            _transport.Submit(ClientAction.Pass(_seat, ++_seq));
        }
    }
}
