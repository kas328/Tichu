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
        public void Reconnect_protocol_is_version_3()
        {
            // M5 가 질문 번호·자리 비움·Resume·기기 식별자를 더했다 — v2 앱은 업데이트 안내를 받아야 한다.
            Assert.That(ProtocolVersion.Current, Is.EqualTo(3));
            Assert.That(ProtocolVersion.Accepts("2"), Is.False);
        }

        [Test]
        public void Attach_with_device_id_adds_both_queries()
        {
            var id = "0123456789abcdef0123456789abcdef";
            var u = ProtocolVersion.Attach(new Uri("ws://192.168.0.5:7777/ws"), id);
            Assert.That(u.ToString(), Is.EqualTo("ws://192.168.0.5:7777/ws?v=3&id=" + id));
        }

        [TestCase("0123456789abcdef0123456789abcdef", true)]
        [TestCase("0123456789ABCDEF0123456789ABCDEF", true)]
        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("0123456789abcdef", false)]
        [TestCase("01234567-89ab-cdef-0123-456789abcdef", false)]   // "N" 형식만
        [TestCase("zz23456789abcdef0123456789abcdef", false)]
        public void Device_id_must_be_a_32_hex_guid(string? id, bool ok)
        {
            Assert.That(ProtocolVersion.IsValidDeviceId(id), Is.EqualTo(ok));
        }

        [Test]
        public void Update_reason_names_the_server_version()
        {
            Assert.That(ProtocolVersion.UpdateRequiredReason, Is.EqualTo("앱을 업데이트해 주세요 (서버 프로토콜 v" + V + ")"));
        }
    }
}
