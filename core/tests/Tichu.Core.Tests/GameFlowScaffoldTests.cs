using NUnit.Framework;

namespace Tichu.Core.Tests
{
    [TestFixture]
    public class GameFlowScaffoldTests
    {
        [Test]
        public void Assembly_marker_is_wired()
        {
            Assert.That(Tichu.GameFlow.AssemblyMarker.Name, Is.EqualTo("Tichu.GameFlow"));
        }
    }
}
