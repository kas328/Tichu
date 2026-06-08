namespace Tichu.Core.Game
{
    public static class Seating
    {
        /// <summary>0 for seats {0,2}; 1 for seats {1,3}.</summary>
        public static int TeamOf(int seat) => seat % 2;

        /// <summary>The seat directly across the table.</summary>
        public static int Partner(int seat) => (seat + 2) % 4;

        /// <summary>Next seat clockwise (increasing index mod 4) that is not IsOut.
        /// At least one active seat must exist.</summary>
        public static int NextActive(PlayerSeat[] seats, int fromSeat)
        {
            int next = (fromSeat + 1) % 4;
            for (int steps = 0; steps < 4; steps++)
            {
                if (!seats[next].IsOut)
                    return next;
                next = (next + 1) % 4;
            }
            throw new System.InvalidOperationException("no active seat");
        }
    }
}
