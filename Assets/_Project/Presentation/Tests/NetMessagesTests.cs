using NUnit.Framework;
using Tichu.Net;
using Tichu.Presentation.Net;

namespace Tichu.Presentation.Tests
{
    public class NetMessagesTests
    {
        [TestCase("stale answer")]
        [TestCase("stale action from a previous round")]
        public void Late_answers_say_the_AI_handled_it(string reason)
        {
            Assert.That(NetMessages.ForRejection(reason), Is.EqualTo("시간이 지나 AI가 대신 처리했습니다"));
        }

        [Test]
        public void Already_decided_rejection_is_silent()
        {
            Assert.That(NetMessages.ForRejection("already decided this round"), Is.Null);
        }

        [TestCase("cannot pass now")]
        [TestCase("illegal or ambiguous move")]
        [TestCase("duplicate or out-of-order actionSeq")]
        [TestCase("")]
        [TestCase(null)]
        public void Other_rejections_are_generic(string reason)
        {
            Assert.That(NetMessages.ForRejection(reason), Is.EqualTo("낼 수 없는 동작입니다"));
        }

        [TestCase("서버 연결 끊김")]
        [TestCase("다른 곳에서 접속")]
        [TestCase("진행 중인 판이 끝났습니다")]
        [TestCase("서버에 연결할 수 없습니다")]
        [TestCase("서버와 연결이 끊어졌습니다")]
        public void Known_faults_pass_through(string reason)
        {
            Assert.That(NetMessages.ForFault(reason), Is.EqualTo(reason));
        }

        [Test]
        public void Update_required_fault_passes_through()
        {
            Assert.That(NetMessages.ForFault(ProtocolVersion.UpdateRequiredReason),
                Is.EqualTo(ProtocolVersion.UpdateRequiredReason));
        }

        [TestCase("Object reference not set to an instance of an object.")]
        [TestCase("ai-seat")]
        [TestCase("")]
        public void Unknown_faults_become_a_server_error(string reason)
        {
            Assert.That(NetMessages.ForFault(reason), Is.EqualTo("서버 오류로 판이 중단되었습니다"));
        }

        [Test]
        public void Fault_null_means_user_cancel_and_stays_null()
        {
            Assert.That(NetMessages.ForFault(null), Is.Null);
        }
    }
}
