using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Game;
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
            using (var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat, s => summary = s))
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
            using (var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat))
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

        // ── 라운드 경계·거부(세션 없이 이벤트를 직접 넣어 검증) ─────────────────

        [Test]
        public void The_first_state_update_after_scoring_starts_a_new_round()
        {
            // 설계 메모 6 — 라운드 인덱스를 프로토콜에 넣지 않고 RoundScored 다음의 첫
            // StateUpdate 를 새 라운드로 본다. 세션은 targetScore 1 로 끝나 이 경로를
            // 태우지 않으므로 전송에 직접 이벤트를 넣는다.
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            using var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat);

            int cleared = 0;
            using var sub = vm.PlaysCleared.Subscribe(_ => cleared++);

            var first = GameEngine.NewRound(55UL);
            transport.SendTo(MySeat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(first, MySeat), null));
            client.Pump();
            vm.FastForward = true;          // 내가 아웃돼 스킵을 눌렀다

            transport.SendTo(MySeat,
                ServerEvent.RoundScored(new RoundResult { TeamATotal = 60, TeamBTotal = 40 }));
            client.Pump();
            Assert.That(vm.RoundResult.CurrentValue, Is.Not.Null, "정산은 결과 배너를 띄운다");
            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(60), "누적은 클라이언트가 더한다");
            Assert.That(vm.CumulativeB.CurrentValue, Is.EqualTo(40));
            Assert.That(cleared, Is.EqualTo(0), "정산만으로는 비우지 않는다 — 결과를 보여 줘야 한다");

            var second = GameEngine.NewRound(66UL);
            transport.SendTo(MySeat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(second, MySeat), null));
            client.Pump();
            Assert.That(vm.RoundResult.CurrentValue, Is.Null, "새 라운드는 배너를 내린다");
            Assert.That(cleared, Is.EqualTo(1), "로그 비우기가 한 번 발행된다");
            Assert.That(vm.FastForward, Is.False, "스킵은 라운드마다 해제된다");

            transport.SendTo(MySeat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(second, MySeat), null));
            client.Pump();
            Assert.That(cleared, Is.EqualTo(1), "라운드 시작 플래그는 한 번만 소비된다");
        }

        [Test]
        public void Scoring_accumulates_across_rounds()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            using var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat);

            transport.SendTo(MySeat,
                ServerEvent.RoundScored(new RoundResult { TeamATotal = 60, TeamBTotal = 40 }));
            transport.SendTo(MySeat,
                ServerEvent.RoundScored(new RoundResult { TeamATotal = -25, TeamBTotal = 125 }));
            client.Pump();

            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(35));
            Assert.That(vm.CumulativeB.CurrentValue, Is.EqualTo(165));
        }

        [Test]
        public void A_rejection_is_recorded_and_leaves_the_view_alone()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            using var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat);

            var s = GameEngine.NewRound(55UL);
            transport.SendTo(MySeat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(s, MySeat), null));
            client.Pump();
            int handBefore = vm.MyHand.CurrentValue.Count;

            Assert.That(client.LastRejection, Is.Null, "거부 전에는 사유가 없다");
            transport.SendTo(MySeat, ServerEvent.ActionRejected(7L, "cannot pass now"));
            client.Pump();

            Assert.That(client.LastRejection, Is.EqualTo("cannot pass now"));
            Assert.That(vm.MyHand.CurrentValue.Count, Is.EqualTo(handBefore),
                "거부는 상태를 되돌리지 않는다 — 진실은 StateUpdate 다");
        }

        [Test]
        public void A_session_fault_is_recorded()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            using var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat);

            transport.SendTo(MySeat, ServerEvent.SessionFaulted("boom"));
            client.Pump();

            Assert.That(client.LastRejection, Is.EqualTo("boom"));
            Assert.That(client.MatchEnded, Is.False, "실패는 정상 종료가 아니다");
            Assert.That(client.Faulted, Is.True, "폴트는 Faulted 로 드러나야 RoundBootstrap 이 펌프를 멈춘다");
        }

        [Test]
        public void Match_ended_raises_the_summary_the_server_sent()
        {
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            MatchSummary? got = null;
            using var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat, x => got = x);

            transport.SendTo(MySeat, ServerEvent.MatchEnded(1, 410, 1010));
            client.Pump();

            Assert.That(client.MatchEnded, Is.True);
            Assert.That(got.HasValue, Is.True);
            Assert.That(got.Value.WinningTeam, Is.EqualTo(1));
            Assert.That(got.Value.TeamA, Is.EqualTo(410));
            Assert.That(got.Value.TeamB, Is.EqualTo(1010));
        }

        [Test]
        public void Backlog_is_applied_before_new_events()
        {
            // 대기실이 미리 꺼낸 이벤트(라운드 결과)가 먼저 적용돼야 한다 — 순서가 뒤집히면
            // RoundScored 뒤 첫 StateUpdate 에서 비우는 "새 라운드" 판정이 어긋난다.
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(MySeat);
            var round = new RoundResult { TeamATotal = 100, TeamBTotal = 0 };
            var backlog = new List<ServerEvent> { ServerEvent.RoundScored(round) };

            using var client = new TichuClient(new LoopbackClient(transport, MySeat), vm, MySeat, null, backlog);
            client.Pump();

            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(100), "백로그의 RoundScored 가 적용돼야 한다");
            client.Pump();
            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(100), "백로그는 한 번만 적용된다");
        }

        [Test]
        public void Client_seated_at_two_sees_itself_as_local_seat_zero_and_finishes()
        {
            const int serverSeat = 2;
            var transport = new LoopbackTransport();
            var vm = new TableViewModel(0);                 // 뷰모델은 항상 로컬 좌석 0
            MatchSummary? summary = null;
            bool sawMyPrompt = false;
            var agents = new IAgent[] { new AiAgent(1UL, 0), new AiAgent(1UL, 1), null, new AiAgent(1UL, 3) };

            using (var client = new TichuClient(new LoopbackClient(transport, serverSeat), vm, serverSeat, s => summary = s))
            using (vm.Prompt.Subscribe(aw => { if (aw != null && aw.Seat == 0) sawMyPrompt = true; }))
            {
                var responder = new AutoResponder(vm);
                var session = new MatchSession(agents, transport, masterSeed: 4242UL, targetScore: 1);
                session.Start();
                try
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                    while (!summary.HasValue && DateTime.UtcNow < deadline)
                    {
                        client.Pump();
                        responder.Tick();
                        Thread.Sleep(2);
                    }
                }
                finally
                {
                    session.Stop();
                    session.Join(TimeSpan.FromSeconds(5));
                }
            }

            Assert.That(summary.HasValue, Is.True, "좌석 2 클라이언트도 매치를 끝까지 둬야 한다(회전된 액션이 서버에서 받아들여짐)");
            Assert.That(sawMyPrompt, Is.True, "내 차례 프롬프트가 로컬 좌석 0 으로 와야 한다");
        }

        [Test]
        public void Second_human_decides_grand_tichu_without_waiting_for_the_first()
        {
            // 서버는 큰 티츄를 좌석 순서(0→1→2→3)로 묻는다. 좌석 0 사람이 고민하는 동안에도
            // 좌석 2 사람은 이미 답할 수 있어야 한다 — 실기에서 S23 이 에디터를 기다렸던 문제.
            var transport = new LoopbackTransport();
            var agents = new IAgent[] { null, new AiAgent(1UL, 1), null, new AiAgent(1UL, 3) };
            var vm0 = new TableViewModel(0);
            var vm2 = new TableViewModel(0);                    // 뷰모델은 항상 로컬 좌석 0
            MatchSummary? summary = null;
            bool seat2AnsweredGrand = false;
            bool seat0Released = false;

            using (var c0 = new TichuClient(new LoopbackClient(transport, 0), vm0, 0, s => summary = s))
            using (var c2 = new TichuClient(new LoopbackClient(transport, 2), vm2, 2))
            using (vm2.Submitted.Subscribe(a =>
            {
                if (a.Kind == ClientActionKind.CallGrandTichu || a.Kind == ClientActionKind.DeclineGrandTichu)
                    seat2AnsweredGrand = true;
            }))
            {
                var r0 = new AutoResponder(vm0);
                var r2 = new AutoResponder(vm2);
                var session = new MatchSession(agents, transport, masterSeed: 5150UL, targetScore: 1);
                session.Start();
                try
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                    while (!summary.HasValue && DateTime.UtcNow < deadline)
                    {
                        c0.Pump();
                        c2.Pump();
                        r2.Tick();
                        if (seat2AnsweredGrand) seat0Released = true;   // 좌석 0 은 좌석 2 가 답한 뒤에야 움직인다
                        if (seat0Released) r0.Tick();
                        Thread.Sleep(2);
                    }
                }
                finally
                {
                    session.Stop();
                    session.Join(TimeSpan.FromSeconds(5));
                }
            }

            Assert.That(seat2AnsweredGrand, Is.True, "좌석 2 가 좌석 0 보다 먼저 큰 티츄에 답할 수 있어야 한다");
            Assert.That(summary.HasValue, Is.True, "두 사람이 모두 둔 매치가 끝까지 가야 한다");
        }
    }
}
