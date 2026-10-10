using System;
using System.Collections.Generic;
using Tichu.Net;

namespace Tichu.Server
{
    /// <summary>
    /// 랜덤 매칭 대기열(M3 스펙 §2). 4명이 모이면 즉시, 아니면 가장 오래 기다린 사람이 fillAfter 를
    /// 채우는 순간 그때 있는 사람으로 방을 연다(빈자리는 방이 AI 로 채운다). 방이 maxRooms 개면 대기열에
    /// 남기고 Busy·순번을 알린다.
    ///
    /// 동시성: 대기열 판단·방 생성(openRoom)·Queued 송신은 한 잠금 안에서 한다 — 묶인 연결은 같은
    /// 잠금 안에서 대기열을 떠나 방 소속(Owner)이 되므로 양쪽에 동시에 있는 연결이 없다. Queued 를 잠금
    /// 안에서 큐에 넣으므로 같은 연결의 Lobby(방 Start, 잠금 밖)보다 반드시 앞선다. Start 는 잠금 밖.
    /// </summary>
    public sealed class Matchmaker : ILinkOwner
    {
        public const int SeatCount = 4;

        private readonly TimeSpan _fillAfter;
        private readonly int _maxRooms;
        private readonly Func<DateTime> _now;
        private readonly Func<int, IReadOnlyList<IPlayerLink>, IRoom> _openRoom;
        private readonly Action<string> _log;
        private readonly object _gate = new object();
        private readonly List<Entry> _queue = new List<Entry>();
        private int _activeRooms;
        private int _nextRoom;
        private bool _busyLogged;

        private sealed class Entry
        {
            public IPlayerLink Link = null!;
            public DateTime Since;
            public (int Waiting, bool Busy, int Position)? LastSent;
        }

        public Matchmaker(int fillAfterSeconds, int maxRooms, Func<DateTime> utcNow,
            Func<int, IReadOnlyList<IPlayerLink>, IRoom> openRoom, Action<string> log)
        {
            _fillAfter = TimeSpan.FromSeconds(fillAfterSeconds);
            _maxRooms = maxRooms;
            _now = utcNow;
            _openRoom = openRoom;
            _log = log;
        }

        public int QueuedCount { get { lock (_gate) return _queue.Count; } }
        public int ActiveRooms { get { lock (_gate) return _activeRooms; } }

        public void Enqueue(IPlayerLink link)
        {
            int count;
            lock (_gate)
            {
                link.Owner = this;
                _queue.Add(new Entry { Link = link, Since = _now() });
                count = _queue.Count;
            }
            _log($"연결 #{link.Id} 대기열 입장 ({count}명)");
            Pump();
        }

        /// <summary>1초마다 서버 타이머가 부른다 — 충원 시각이 된 묶음을 연다.</summary>
        public void Tick() => Pump();

        /// <summary>방 하나가 끝났다(정상·폴트). 자리가 났으니 대기열을 다시 본다.</summary>
        public void RoomClosed(IRoom room)
        {
            lock (_gate) _activeRooms--;
            Pump();
        }

        public void Left(IPlayerLink who)
        {
            if (Remove(who))
            {
                _log($"연결 #{who.Id} 대기열 이탈");
                Pump();
                return;
            }
            // 수신 루프가 Owner 를 읽은 뒤 방으로 묶였다 — 방이 처리한다.
            var owner = who.Owner;
            if (owner != null && owner != this) owner.Left(who);
        }

        public void Receive(IPlayerLink from, ClientAction a)
        {
            if (Remove(from))
            {
                // 정상 클라이언트는 Lobby 전에 게임 액션을 보내지 않는다.
                _log($"연결 #{from.Id} 대기 중 게임 액션 — 연결 종료");
                from.Close("not in a match");
                Pump();
                return;
            }
            var owner = from.Owner;
            if (owner != null && owner != this) owner.Receive(from, a);
        }

        private bool Remove(IPlayerLink link)
        {
            lock (_gate) return _queue.RemoveAll(e => e.Link == link) > 0;
        }

        private void Pump()
        {
            var rooms = new List<IRoom>();
            lock (_gate)
            {
                DateTime now = _now();
                while (_queue.Count > 0 && _activeRooms < _maxRooms)
                {
                    int take;
                    if (_queue.Count >= SeatCount) take = SeatCount;
                    else if (now - _queue[0].Since >= _fillAfter) take = _queue.Count;
                    else break;

                    var humans = new List<IPlayerLink>(take);
                    for (int i = 0; i < take; i++) humans.Add(_queue[i].Link);
                    _queue.RemoveRange(0, take);
                    int index = _nextRoom++;
                    _activeRooms++;
                    rooms.Add(_openRoom(index, humans));   // 반환 전에 humans 의 Owner 가 방으로 바뀐다
                    _log($"방 {index} 배정 — 사람 {take}명");
                }

                bool busy = _activeRooms >= _maxRooms;
                if (busy && _queue.Count > 0 && !_busyLogged) { _busyLogged = true; _log($"방 상한 {_maxRooms} 도달 — 대기 {_queue.Count}명"); }
                if (!busy) _busyLogged = false;
                for (int i = 0; i < _queue.Count; i++)
                {
                    var e = _queue[i];
                    var key = (_queue.Count, busy, busy ? i + 1 : 0);
                    if (e.LastSent == key) continue;
                    e.LastSent = key;
                    int? fill = busy ? (int?)null : SecondsLeft(now, _queue[0].Since);
                    e.Link.Send(ServerEvent.Queued(key.Item1, fill, busy, key.Item3));
                }
            }
            foreach (var room in rooms) room.Start();
        }

        private int SecondsLeft(DateTime now, DateTime oldest) =>
            Math.Max(0, (int)Math.Ceiling((_fillAfter - (now - oldest)).TotalSeconds));
    }
}
