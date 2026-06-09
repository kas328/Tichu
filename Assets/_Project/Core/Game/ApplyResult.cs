#nullable enable
namespace Tichu.Core.Game
{
    public sealed class ApplyResult
    {
        public bool Ok { get; }
        public string? RejectReason { get; }

        private ApplyResult(bool ok, string? rejectReason)
        {
            Ok = ok;
            RejectReason = rejectReason;
        }

        public static readonly ApplyResult Accepted = new ApplyResult(true, null);

        public static ApplyResult Reject(string reason) =>
            new ApplyResult(false, reason);
    }
}
