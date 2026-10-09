using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Tichu.Server.Tests
{
    public class SeatPickerTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void Seats_are_distinct_and_in_range(int humans)
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var seats = SeatPicker.Pick(humans, new Random(seed));
                Assert.That(seats.Length, Is.EqualTo(humans));
                Assert.That(seats.Distinct().Count(), Is.EqualTo(humans));
                Assert.That(seats.All(s => s >= 0 && s <= 3), Is.True);
            }
        }

        [Test]
        public void A_solo_player_can_land_on_every_seat()
        {
            var seen = new HashSet<int>();
            for (int seed = 0; seed < 200; seed++) seen.Add(SeatPicker.Pick(1, new Random(seed))[0]);
            Assert.That(seen.Count, Is.EqualTo(4), "고정 좌석이면 무작위 배정이 아니다");
        }

        [Test]
        public void Two_players_are_sometimes_partners_and_sometimes_opponents()
        {
            bool partners = false, opponents = false;
            for (int seed = 0; seed < 200; seed++)
            {
                var s = SeatPicker.Pick(2, new Random(seed));
                if ((s[0] - s[1]) % 2 == 0) partners = true; else opponents = true;
            }
            Assert.That(partners && opponents, Is.True);
        }

        [TestCase(0)]
        [TestCase(5)]
        public void Out_of_range_counts_throw(int humans)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SeatPicker.Pick(humans, new Random(1)));
        }
    }
}
