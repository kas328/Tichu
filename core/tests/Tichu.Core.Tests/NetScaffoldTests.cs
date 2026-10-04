using NUnit.Framework;

namespace Tichu.Core.Tests
{
    [TestFixture]
    public class NetScaffoldTests
    {
        [Test]
        public void Assembly_marker_is_wired()
        {
            Assert.That(Tichu.Net.AssemblyMarker.Name, Is.EqualTo("Tichu.Net"));
        }
    }
}
