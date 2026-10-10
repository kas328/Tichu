using System;

namespace Tichu.Presentation.Net
{
    /// <summary>
    /// 연결이 끊겼을 때 다시 접속할 간격: 1·2·4·8·8…초, 첫 시도부터 budget 초 안에서만(M5 §5).
    /// 서버는 사람 0명인 방을 120초 기다리므로 기본 예산도 120초다.
    /// </summary>
    public sealed class ReconnectPolicy
    {
        private readonly Func<double> _now;
        private readonly double _budget;
        private readonly double _start;
        private int _attempt;

        public ReconnectPolicy(Func<double> nowSeconds, double budgetSeconds = 120)
        {
            _now = nowSeconds;
            _budget = budgetSeconds;
            _start = nowSeconds();
        }

        public bool TryNextDelay(out double seconds)
        {
            seconds = Math.Min(8.0, Math.Pow(2, _attempt));
            _attempt++;
            return _now() - _start + seconds <= _budget;
        }
    }
}
