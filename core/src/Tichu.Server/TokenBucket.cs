using System;

namespace Tichu.Server
{
    /// <summary>
    /// 연결당 레이트리밋. 용량만큼 버스트를 허용하고 초당 perSecond 개씩 채운다.
    /// 시계를 주입받아 테스트가 시간을 조종한다. 한 연결의 수신 루프에서만 쓰므로 잠금이 없다.
    /// </summary>
    public sealed class TokenBucket
    {
        private readonly int _capacity;
        private readonly double _perSecond;
        private readonly Func<double> _now;
        private double _tokens;
        private double _last;

        public TokenBucket(int capacity, double perSecond, Func<double> nowSeconds)
        {
            _capacity = capacity;
            _perSecond = perSecond;
            _now = nowSeconds;
            _tokens = capacity;
            _last = nowSeconds();
        }

        public bool TryTake()
        {
            double t = _now();
            _tokens = Math.Min(_capacity, _tokens + (t - _last) * _perSecond);
            _last = t;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }
}
