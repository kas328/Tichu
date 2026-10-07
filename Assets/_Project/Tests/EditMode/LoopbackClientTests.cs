using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    public class LoopbackClientTests
    {
        [Test]
        public void Drain_returns_only_the_bound_seat_events()
        {
            var transport = new LoopbackTransport();
            transport.SendTo(0, ServerEvent.ActionRejected(1, "zero"));
            transport.SendTo(2, ServerEvent.ActionRejected(2, "two"));

            var client = new LoopbackClient(transport, 2);
            var got = client.Drain();

            Assert.That(got.Count, Is.EqualTo(1));
            Assert.That(got[0].Reason, Is.EqualTo("two"));
            Assert.That(client.Drain().Count, Is.EqualTo(0), "한 번 비운 수신함은 다시 비어 있어야 한다");
            Assert.That(transport.Drain(0).Count, Is.EqualTo(1), "다른 좌석 수신함은 건드리지 않는다");
        }

        [Test]
        public void Submit_forwards_to_the_session_handler()
        {
            var transport = new LoopbackTransport();
            ClientAction? received = null;
            transport.OnClientAction(a => received = a);

            new LoopbackClient(transport, 1).Submit(ClientAction.Pass(1, 7));

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.ActionSeq, Is.EqualTo(7));
        }
    }
}
