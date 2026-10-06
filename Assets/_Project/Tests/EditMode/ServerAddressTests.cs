using System;
using NUnit.Framework;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    public class ServerAddressTests
    {
        [TestCase("192.168.0.5", "ws://192.168.0.5:7777/ws")]
        [TestCase("  192.168.0.5  ", "ws://192.168.0.5:7777/ws")]
        [TestCase("192.168.0.5:9000", "ws://192.168.0.5:9000/ws")]
        [TestCase("ws://192.168.0.5:7777/ws", "ws://192.168.0.5:7777/ws")]
        [TestCase("ws://my-pc.local", "ws://my-pc.local:7777/ws")]
        [TestCase("wss://example.com/ws", "wss://example.com:7777/ws")]
        public void Accepted_forms_normalize(string input, string expected)
        {
            Assert.That(ServerAddress.TryParse(input, out var uri), Is.True);
            Assert.That(uri!.ToString(), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("http://192.168.0.5")]
        [TestCase("a b")]
        [TestCase("ws://")]
        public void Bad_input_is_rejected(string? input)
        {
            Assert.That(ServerAddress.TryParse(input, out var uri), Is.False);
            Assert.That(uri, Is.Null);
        }
    }
}
