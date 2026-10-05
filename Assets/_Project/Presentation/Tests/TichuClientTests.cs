using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using R3;
using Tichu.Core.Cards;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;
using Tichu.Presentation.Net;
using Tichu.Presentation.Shell;
using Tichu.Presentation.ViewModel;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 전체 경로: MatchSession(권위) → LoopbackTransport → TichuClient → TableViewModel.
    /// 좌석 0 은 원격이고 "사람 대신" 서버가 보낸 합법수 중 첫 수를 자동 제출한다.
    /// </summary>
    [TestFixture]
    public class TichuClientTests
    {
        private const int MySeat = 0;

        /// <summary>
        /// 프롬프트가 내게 오면 합법수 중 첫 수(없으면 패스)를 낸다.
        ///
        /// 같은 질문에 두 번 답하지 않는다. 폴링 루프는 서버가 깨어나기 전에 수백 번
        /// 돌므로, 프롬프트 인스턴스를 기억하지 않으면 한 질문에 수백 개의 액션이
        /// 제출돼 서버 우편함(차례밖 Play 큐)이 쓰레기로 찬다. Awaiting 은 질문마다
        /// 새 인스턴스라 참조 비교로 충분하다(RemoteAgent.BuildAwaiting).
        /// </summary>
        private sealed class AutoResponder
        {
            private readonly TableViewModel _vm;
            private Awaiting _answered;

            public AutoResponder(TableViewModel vm) { _vm = vm; }

            public void Tick()
            {
                var aw = _vm.Prompt.CurrentValue;
                if (aw == null || ReferenceEquals(aw, _answered)) return;
                _answered = aw;

                switch (aw.Kind)
                {
                    case StepKind.GrandTichu:
                        _vm.SubmitGrandTichu(false);
                        break;
                    case StepKind.Exchange:
                        var h = _vm.MyHand.CurrentValue;
                        if (h.Count >= 3)
                            _vm.SubmitExchange(new ExchangeChoice(h[0], h[1], h[2]));
                        break;
                    case StepKind.Play:
                        if (aw.LegalMoves != null && aw.LegalMoves.Count > 0)
                            _vm.SubmitPlay(new List<Card>(aw.LegalMoves[0].Cards));
                        else if (aw.CanPass)
                            _vm.SubmitPass();
                        break;
                    case StepKind.DragonGift:
                        _vm.SubmitDragonRecipient(1);
                        break;
                }
            }
        }

        private static IAgent[] AgentsWithRemoteSeat0() => new IAgent[]
        {
            null,                                   // 좌석 0 = 원격(이 테스트가 대신 낸다)
            new AiAgent(1UL, 1), new AiAgent(1UL, 2), new AiAgent(1UL, 3),
        };

        [Test]
        public void Client_drives_a_full_match_through_the_protocol()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            MatchSummary? summary = null;
            using (var client = new TichuClient(transport, vm, MySeat, s => summary = s))
            {
                var responder = new AutoResponder(vm);
                var session = new MatchSession(
                    AgentsWithRemoteSeat0(), transport, masterSeed: 9090UL, targetScore: 1);
                session.Start();
                try
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                    while (!summary.HasValue && DateTime.UtcNow < deadline)
                    {
                        client.Pump();
                        responder.Tick();
                        Thread.Sleep(2);     // 세션 스레드에 코어를 양보(LoopbackMatchTests 선례)
                    }
                }
                finally
                {
                    session.Stop();
                    session.Join(TimeSpan.FromSeconds(5));
                }
            }

            Assert.That(summary.HasValue, Is.True, "매치가 끝나고 요약이 올라와야 한다");
            Assert.That(vm.CumulativeA.CurrentValue + vm.CumulativeB.CurrentValue, Is.Not.EqualTo(0),
                "누적 점수가 RoundScored 로 채워져야 한다");
        }

        [Test]
        public void View_model_never_sees_another_seats_hand_over_the_whole_match()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            int maxHand = 0;
            bool matchEnded;
            // 폴링으로 재면 한 Pump 가 여러 뷰를 적용할 때 중간 값을 놓친다 — 구독으로
            // 모든 투영을 본다(남의 카드가 한 프레임만 새도 잡히도록).
            using var handWatch = vm.MyHand.Subscribe(h => { if (h.Count > maxHand) maxHand = h.Count; });
            using (var client = new TichuClient(transport, vm, MySeat))
            {
                var responder = new AutoResponder(vm);
                var session = new MatchSession(
                    AgentsWithRemoteSeat0(), transport, masterSeed: 1234UL, targetScore: 1);
                session.Start();
                try
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                    while (!client.MatchEnded && DateTime.UtcNow < deadline)
                    {
                        client.Pump();
                        responder.Tick();
                        Thread.Sleep(2);
                    }
                }
                finally
                {
                    matchEnded = client.MatchEnded;
                    session.Stop();
                    session.Join(TimeSpan.FromSeconds(5));
                }
            }

            Assert.That(matchEnded, Is.True, "매치가 끝나지 않았다 — 손패 상한 단언이 공허해진다");

            // 내 손패는 어떤 순간에도 14장을 넘을 수 없다 — 넘으면 남의 카드가 섞인 것이다.
            Assert.That(maxHand, Is.LessThanOrEqualTo(14), $"내 손패가 {maxHand}장까지 보였다");
            Assert.That(maxHand, Is.GreaterThan(0), "손패를 한 번도 못 받았다 — 경로가 돌지 않았다");
        }
    }
}
