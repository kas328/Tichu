using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Presentation.Net;

namespace Tichu.Presentation.Tests
{
    public class ReconnectPolicyTests
    {
        [Test]
        public void Delays_double_up_to_eight_seconds()
        {
            double now = 0;
            var p = new ReconnectPolicy(() => now, 120);
            var got = new List<double>();
            for (int i = 0; i < 6; i++) { Assert.That(p.TryNextDelay(out double d), Is.True); got.Add(d); now += d; }
            Assert.That(got, Is.EqualTo(new[] { 1.0, 2.0, 4.0, 8.0, 8.0, 8.0 }));
        }

        [Test]
        public void Gives_up_when_the_budget_runs_out()
        {
            double now = 0;
            var p = new ReconnectPolicy(() => now, 10);
            Assert.That(p.TryNextDelay(out double d1), Is.True); now += d1;   // 1
            Assert.That(p.TryNextDelay(out double d2), Is.True); now += d2;   // 3
            Assert.That(p.TryNextDelay(out double d3), Is.True); now += d3;   // 7
            Assert.That(p.TryNextDelay(out _), Is.False, "7 + 8 > 10");
        }
    }
}
