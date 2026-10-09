using System;
using System.Globalization;
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    public class ProtocolVersionTests
    {
        private static readonly string V = ProtocolVersion.Current.ToString(CultureInfo.InvariantCulture);

        [TestCase("wss://play.example.com/ws", "wss://play.example.com/ws?v=")]
        [TestCase("ws://192.168.0.5:7777/ws", "ws://192.168.0.5:7777/ws?v=")]
        [TestCase("wss://play.example.com/ws?room=7", "wss://play.example.com/ws?room=7&v=")]
        public void Attach_adds_the_current_version_as_query(string input, string expectedPrefix)
        {
            Assert.That(ProtocolVersion.Attach(new Uri(input)).ToString(), Is.EqualTo(expectedPrefix + V));
        }

        [Test]
        public void Current_version_is_accepted()
        {
            Assert.That(ProtocolVersion.Accepts(V), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0")]
        [TestCase("abc")]
        [TestCase("1.0")]
        [TestCase(" 1")]
        [TestCase("+1")]
        [TestCase("1,1")]   // ASP.NET 은 중복 쿼리를 쉼표로 잇는다
        public void Anything_else_is_rejected(string? value)
        {
            Assert.That(ProtocolVersion.Accepts(value), Is.False);
        }

        [Test]
        public void Next_version_is_rejected()
        {
            Assert.That(ProtocolVersion.Accepts((ProtocolVersion.Current + 1).ToString(CultureInfo.InvariantCulture)), Is.False);
        }

        [Test]
        public void Matchmaking_protocol_is_version_2()
        {
            // M3 가 Queued 를 더했다 — v1 앱은 관문에서 업데이트 안내를 받아야 한다.
            Assert.That(ProtocolVersion.Current, Is.EqualTo(2));
            Assert.That(ProtocolVersion.Accepts("1"), Is.False);
        }

        [Test]
        public void Update_reason_names_the_server_version()
        {
            Assert.That(ProtocolVersion.UpdateRequiredReason, Is.EqualTo("앱을 업데이트해 주세요 (서버 프로토콜 v" + V + ")"));
        }
    }
}
