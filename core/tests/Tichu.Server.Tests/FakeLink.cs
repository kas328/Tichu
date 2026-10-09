using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tichu.Net;

namespace Tichu.Server.Tests
{
    /// <summary>메모리 안 연결. 서버 쪽에선 IPlayerLink, 테스트 클라이언트 쪽에선 IClientTransport 다 —
    /// ScriptedClient 를 그대로 붙일 수 있다(Submit = 소유자에게 Receive).</summary>
    internal sealed class FakeLink : IPlayerLink, IClientTransport
    {
        private static int _ids;
        private readonly object _gate = new object();
        private readonly List<ServerEvent> _all = new List<ServerEvent>();
        private readonly List<ServerEvent> _undrained = new List<ServerEvent>();
        private volatile ILinkOwner? _owner;
        private string? _closedReason;

        public int Id { get; } = Interlocked.Increment(ref _ids);
        public ILinkOwner? Owner { get => _owner; set => _owner = value; }

        public string? ClosedReason { get { lock (_gate) return _closedReason; } }

        public List<ServerEvent> Received { get { lock (_gate) return new List<ServerEvent>(_all); } }

        public List<ServerEvent> QueuedEvents => Received.Where(e => e.Kind == ServerEventKind.Queued).ToList();

        public void Send(ServerEvent ev)
        {
            lock (_gate) { _all.Add(ev); _undrained.Add(ev); }
        }

        public void Close(string reason)
        {
            lock (_gate) _closedReason ??= reason;
        }

        public IReadOnlyList<ServerEvent> Drain()
        {
            lock (_gate)
            {
                var r = new List<ServerEvent>(_undrained);
                _undrained.Clear();
                return r;
            }
        }

        public void Submit(ClientAction a) => Owner?.Receive(this, a);
    }

    /// <summary>매치메이커 단위 테스트용 방. 생성 때 Owner 를 자기로 바꾸고, 받은 것을 기록만 한다.</summary>
    internal sealed class FakeRoom : IRoom
    {
        public int Index { get; }
        public IReadOnlyList<IPlayerLink> Humans { get; }
        public bool Started { get; private set; }
        public List<IPlayerLink> LeftLinks { get; } = new List<IPlayerLink>();
        public List<ClientAction> ReceivedActions { get; } = new List<ClientAction>();

        public FakeRoom(int index, IReadOnlyList<IPlayerLink> humans)
        {
            Index = index;
            Humans = humans;
            foreach (var h in humans) h.Owner = this;
        }

        public void Start() => Started = true;
        public void Left(IPlayerLink who) { lock (LeftLinks) LeftLinks.Add(who); }
        public void Receive(IPlayerLink from, ClientAction a) { lock (ReceivedActions) ReceivedActions.Add(a); }
    }
}
