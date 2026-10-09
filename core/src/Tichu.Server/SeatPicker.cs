using System;

namespace Tichu.Server
{
    /// <summary>
    /// 방이 만들어질 때 사람 좌석을 고른다. 0~3 을 섞어 앞에서 humans 개 — 사람 둘이 같은 팀일
    /// 수도 상대일 수도 있다(스펙 §4). 클라이언트는 "나 = 아래" 로 회전하므로 어느 좌석이든 같다.
    /// </summary>
    public static class SeatPicker
    {
        public static int[] Pick(int humans, Random rng)
        {
            if (humans < 1 || humans > 4) throw new ArgumentOutOfRangeException(nameof(humans));
            var seats = new[] { 0, 1, 2, 3 };
            for (int i = seats.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (seats[i], seats[j]) = (seats[j], seats[i]);
            }
            var picked = new int[humans];
            Array.Copy(seats, picked, humans);
            return picked;
        }
    }
}
