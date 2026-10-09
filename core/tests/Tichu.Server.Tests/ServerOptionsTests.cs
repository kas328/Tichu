using System;
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Server.Tests
{
    public class ServerOptionsTests
    {
        [Test]
        public void Defaults_match_the_spec()
        {
            var o = ServerOptions.Parse(Array.Empty<string>());
            Assert.That(o.Difficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(o.Target, Is.EqualTo(1000));
            Assert.That(o.Port, Is.EqualTo(7777));
            Assert.That(o.Host, Is.EqualTo("0.0.0.0"));
            Assert.That(o.AiDelayMs, Is.EqualTo(900));
            Assert.That(o.InterRoundPauseMs, Is.EqualTo(3500));
            Assert.That(o.Seed, Is.Null);
            Assert.That(o.RateCapacity, Is.EqualTo(20));
            Assert.That(o.RatePerSecond, Is.EqualTo(10));
            Assert.That(o.FillAfterSeconds, Is.EqualTo(30));
            Assert.That(o.MaxRooms, Is.EqualTo(2));
        }

        [Test]
        public void All_flags_parse()
        {
            var o = ServerOptions.Parse(new[]
            {
                "--difficulty", "expert", "--target", "200",
                "--port", "9000", "--ai-delay", "0", "--seed", "42", "--host", "127.0.0.1",
                "--fill-after", "5", "--max-rooms", "3"
            });
            Assert.That(o.Difficulty, Is.EqualTo(Difficulty.Expert));
            Assert.That(o.Target, Is.EqualTo(200));
            Assert.That(o.Port, Is.EqualTo(9000));
            Assert.That(o.AiDelayMs, Is.EqualTo(0));
            Assert.That(o.Seed, Is.EqualTo(42UL));
            Assert.That(o.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(o.FillAfterSeconds, Is.EqualTo(5));
            Assert.That(o.MaxRooms, Is.EqualTo(3));
        }

        [TestCase("--seats", "0,2")]   // M3 에서 삭제된 인자 — 알 수 없는 인자로 거절
        [TestCase("--difficulty", "Godlike")]
        [TestCase("--difficulty", "7")]
        [TestCase("--target", "abc")]
        [TestCase("--bogus", "1")]
        [TestCase("--fill-after", "-1")]
        [TestCase("--fill-after", "x")]
        [TestCase("--max-rooms", "0")]
        [TestCase("--max-rooms", "x")]
        public void Bad_arguments_throw(string key, string value)
        {
            Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { key, value }));
        }

        [Test]
        public void Missing_value_throws()
        {
            Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { "--port" }));
        }
    }
}
