using NUnit.Framework;
using Tichu.Presentation.Shell;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class OnlineEntryTests
    {
        [Test]
        public void Release_build_with_address_starts_directly_with_the_normalized_uri()
        {
            var d = OnlineEntry.Decide(isDebugBuild: false, releaseAddress: "wss://play.example.com");

            Assert.That(d.Kind, Is.EqualTo(OnlineEntryKind.StartDirect));
            Assert.That(d.Uri.ToString(), Is.EqualTo("wss://play.example.com/ws"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("http://play.example.com")]
        public void Release_build_with_blank_or_invalid_address_is_unavailable(string address)
        {
            var d = OnlineEntry.Decide(isDebugBuild: false, releaseAddress: address);

            Assert.That(d.Kind, Is.EqualTo(OnlineEntryKind.Unavailable));
            Assert.That(d.Uri, Is.Null);
        }

        [TestCase(null)]
        [TestCase("wss://play.example.com")]
        public void Debug_build_always_asks_for_an_address(string address)
        {
            var d = OnlineEntry.Decide(isDebugBuild: true, releaseAddress: address);

            Assert.That(d.Kind, Is.EqualTo(OnlineEntryKind.AskAddress));
            Assert.That(d.Uri, Is.Null);
        }

        [Test]
        public void Initial_address_prefers_the_saved_one()
        {
            Assert.That(OnlineEntry.InitialAddress("192.168.0.5", "wss://play.example.com"), Is.EqualTo("192.168.0.5"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void Initial_address_falls_back_to_the_release_address(string saved)
        {
            Assert.That(OnlineEntry.InitialAddress(saved, "wss://play.example.com"), Is.EqualTo("wss://play.example.com"));
        }

        [Test]
        public void Initial_address_is_empty_when_neither_exists()
        {
            Assert.That(OnlineEntry.InitialAddress(null, null), Is.EqualTo(""));
        }

        [Test]
        public void Release_server_asset_ships_and_is_empty_until_M2b()
        {
            var cfg = Resources.Load<ReleaseServerConfig>("ReleaseServer");

            Assert.That(cfg, Is.Not.Null, "Resources/ReleaseServer.asset 이 있어야 출시 빌드가 주소를 읽는다");
            // M2b 에서 운영 주소를 채우면 이 단언을 그 주소로 바꾼다(의도된 변경).
            Assert.That(ReleaseServerConfig.LoadAddress(), Is.EqualTo(""));
        }
    }
}
