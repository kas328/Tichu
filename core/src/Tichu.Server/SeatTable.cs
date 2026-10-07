using System.Collections.Generic;

namespace Tichu.Server
{
    /// <summary>
    /// 인간 좌석 배정표. 목록 순서에서 비어 있는 첫 좌석을 준다 — 끊긴 좌석은 다음 접속자가 받는다.
    /// 스레드 안전하지 않다. 호출자(WebSocketServerTransport)가 잠금 안에서 쓴다.
    /// </summary>
    public sealed class SeatTable
    {
        private readonly int[] _order;
        private readonly HashSet<int> _taken = new HashSet<int>();

        public SeatTable(IReadOnlyList<int> seats)
        {
            _order = new int[seats.Count];
            for (int i = 0; i < seats.Count; i++) _order[i] = seats[i];
        }

        public int Required => _order.Length;
        public int Connected => _taken.Count;
        public bool IsFull => _taken.Count == _order.Length;

        public bool TryTake(out int seat)
        {
            foreach (int s in _order)
            {
                if (_taken.Contains(s)) continue;
                _taken.Add(s);
                seat = s;
                return true;
            }
            seat = -1;
            return false;
        }

        public void Release(int seat) => _taken.Remove(seat);

        /// <summary>모든 좌석을 비운다(다음 매치 대기실). 다음 접속자는 목록의 첫 좌석부터 받는다.</summary>
        public void Clear() => _taken.Clear();
    }
}
