using NUnit.Framework;
using Tichu.Net;
using Tichu.Presentation.Net;

namespace Tichu.Presentation.Tests
{
    public class DeviceIdentityTests
    {
        [Test]
        public void A_new_id_is_created_once_and_then_reused()
        {
            string stored = "";
            string first = DeviceIdentity.GetOrCreate(() => stored, v => stored = v);
            string second = DeviceIdentity.GetOrCreate(() => stored, v => stored = v);

            Assert.That(ProtocolVersion.IsValidDeviceId(first), Is.True);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void A_corrupt_stored_value_is_replaced()
        {
            string stored = "garbage";
            string id = DeviceIdentity.GetOrCreate(() => stored, v => stored = v);
            Assert.That(ProtocolVersion.IsValidDeviceId(id), Is.True);
            Assert.That(stored, Is.EqualTo(id));
        }
    }
}
