using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;
using Tichu.Presentation.ViewModel;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 뷰모델은 서버가 보낸 PlayerView 만 먹고, 사람의 입력을 ClientAction 으로 낸다.
    /// 권위 상태(GameState)를 들고 있지 않다.
    /// </summary>
    [TestFixture]
    public class TableViewModelTests
    {
        private static PlayerView ViewOf(GameState s, int seat) => ViewProjector.ProjectFor(s, seat);

        /// <summary>내 차례 Play 프롬프트를 세운다 — 합법수는 지정 카드 한 장, 패스 허용.</summary>
        private static TableViewModel VmAwaitingPlay(GameState s, Card legal)
        {
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting
            {
                Kind = StepKind.Play,
                Seat = 0,
                CanPass = true,
                // 서버가 실제로 싣는 것과 같은 형태 — 선검사는 이 목록으로만 한다.
                LegalMoves = new List<Combination>
                {
                    CombinationRecognizer.Recognize(new[] { legal }, TrickContext.Lead),
                },
            };
            vm.Apply(view);
            return vm;
        }

        [Test]
        public void Apply_projects_player_view()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.Phase.CurrentValue, Is.EqualTo(s.Phase));
            Assert.That(vm.MyHand.CurrentValue.Count, Is.EqualTo(s.Seats[0].Hand.Count));
            for (int i = 0; i < 4; i++)
                Assert.That(vm.HandCount(i).CurrentValue, Is.EqualTo(s.Seats[i].Hand.Count));
        }

        [Test]
        public void Apply_never_exposes_another_seats_hand()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            vm.Apply(ViewOf(s, 0));

            // 좌석 1 의 손패 카드가 내 손패 투영에 섞이면 안 된다.
            foreach (var card in s.Seats[1].Hand)
                Assert.That(vm.MyHand.CurrentValue.Contains(card), Is.False,
                    $"좌석 1 의 {card} 가 내 손패에 보인다");
        }

        [Test]
        public void Apply_notifies_the_hand_before_the_prompt()
        {
            // 프롬프트 렌더는 RefreshActions 를 타므로 최신 손패 위에서 돌아야 한다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            int handCountWhenPrompted = -1;

            using var sub = vm.Prompt.Subscribe(
                aw => { if (aw != null) handCountWhenPrompted = vm.MyHand.CurrentValue.Count; });

            var view = ViewOf(s, 0);
            view.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(view);

            Assert.That(handCountWhenPrompted, Is.EqualTo(s.Seats[0].Hand.Count),
                "프롬프트 통지 시점에 손패가 이미 갱신돼 있어야 한다");
        }

        [Test]
        public void Prompt_is_set_only_for_my_seat()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            // 큰 티츄·교환은 단계 진입 시 먼저 묻는다(아래 Early_* 테스트) — 순서 질문 규칙은 Play 로 본다.
            var mine = ViewOf(s, 0);
            mine.Phase = RoundPhase.Play;
            mine.Awaiting = new Awaiting { Kind = StepKind.Play, Seat = 0 };
            vm.Apply(mine);
            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null);

            var theirs = ViewOf(s, 0);
            theirs.Phase = RoundPhase.Play;
            theirs.Awaiting = new Awaiting { Kind = StepKind.Play, Seat = 2 };
            vm.Apply(theirs);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "남의 차례는 프롬프트가 아니다");
        }

        [Test]
        public void Submitting_a_play_emits_a_client_action()
        {
            var s = GameEngine.NewRound(777UL);
            var card = s.Seats[0].Hand[0];
            var vm = VmAwaitingPlay(s, card);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPlay(new List<Card> { card }), Is.True);

            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(sent[0].Seat, Is.EqualTo(0));
            Assert.That(sent[0].Cards.Count, Is.EqualTo(1));
        }

        [Test]
        public void Action_seq_increases_across_submissions()
        {
            // 좌석별 단조 증가라야 서버가 재생(리플레이)을 막을 수 있다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            var first = ViewOf(s, 0);
            first.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(first);
            Assert.That(vm.SubmitGrandTichu(false), Is.True);

            // 거부 뒤 재질의처럼 **새** Awaiting 이 오면 다시 답할 수 있다.
            vm.OnActionRejected();
            var second = ViewOf(s, 0);
            second.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(second);
            Assert.That(vm.SubmitGrandTichu(true), Is.True);

            Assert.That(sent.Count, Is.EqualTo(2));
            Assert.That(sent[1].ActionSeq, Is.GreaterThan(sent[0].ActionSeq));
        }

        // ── 한 질문에 답은 하나 ────────────────────────────────────────────────

        [Test]
        public void One_prompt_accepts_only_one_pass()
        {
            // 뷰는 버튼을 Object.Destroy 로 치우므로 제출 뒤에도 프레임 끝까지 클릭이
            // 살아 있다. 두 번째 패스가 나가면 서버 우편함에 남아 다음 차례에 원치 않은
            // 패스로 서빙된다 — 그래서 뷰모델이 답한 프롬프트를 즉시 내려야 한다.
            var s = GameEngine.NewRound(777UL);
            var vm = VmAwaitingPlay(s, s.Seats[0].Hand[0]);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.True);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "답한 질문은 즉시 내려간다");
            Assert.That(vm.SubmitPass(), Is.False, "같은 질문에 두 번 답할 수 없다");
            Assert.That(sent.Count, Is.EqualTo(1));
        }

        [Test]
        public void One_prompt_accepts_only_one_play()
        {
            var s = GameEngine.NewRound(777UL);
            // 폭탄이 아닌 한 장 — 두 번째 제출이 차례밖 폭탄 입구로도 빠지지 못한다.
            var card = s.Seats[0].Hand.First(c => !c.IsSpecial);
            var vm = VmAwaitingPlay(s, card);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPlay(new List<Card> { card }), Is.True);
            Assert.That(vm.Prompt.CurrentValue, Is.Null);
            Assert.That(vm.SubmitPlay(new List<Card> { card }), Is.False);
            Assert.That(sent.Count, Is.EqualTo(1));
        }

        [Test]
        public void One_prompt_accepts_only_one_grand_tichu_answer()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitGrandTichu(false), Is.True);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "답한 질문은 즉시 내려간다");
            Assert.That(vm.SubmitGrandTichu(true), Is.False, "같은 질문에 두 번 답할 수 없다");
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.DeclineGrandTichu),
                "먼저 보낸 답이 유효하다 — 두 번째가 덮어쓰면 안 된다");
        }

        [Test]
        public void One_prompt_accepts_only_one_exchange()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Exchange;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting { Kind = StepKind.Exchange, Seat = 0 };
            vm.Apply(view);
            var h = vm.MyHand.CurrentValue;

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitExchange(new ExchangeChoice(h[0], h[1], h[2])), Is.True);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "답한 질문은 즉시 내려간다");
            Assert.That(vm.SubmitExchange(new ExchangeChoice(h[3], h[4], h[5])), Is.False,
                "같은 질문에 두 번 답할 수 없다");
            Assert.That(sent.Count, Is.EqualTo(1));
        }

        [Test]
        public void One_prompt_accepts_only_one_dragon_recipient()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting { Kind = StepKind.DragonGift, Seat = 0 };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitDragonRecipient(1), Is.True);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "답한 질문은 즉시 내려간다");
            Assert.That(vm.SubmitDragonRecipient(3), Is.False, "같은 질문에 두 번 답할 수 없다");
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].RecipientSeat, Is.EqualTo(1));
        }

        [Test]
        public void An_off_turn_bomb_does_not_consume_a_prompt()
        {
            // 차례밖 폭탄은 질문에 대한 답이 아니다 — 프롬프트를 건드려선 안 된다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(view);

            var bomb = new List<Card>
            {
                Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda),
            };
            Assert.That(vm.SubmitPlay(bomb), Is.True);
            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null,
                "큰 티츄 질문이 폭탄 전송으로 사라지면 그 질문에 답할 수 없게 된다");
        }

        // ── 거부 관문 ──────────────────────────────────────────────────────────

        [Test]
        public void A_play_outside_the_legal_moves_is_refused()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting
            {
                Kind = StepKind.Play, Seat = 0, CanPass = true,
                LegalMoves = new List<Combination>(),   // 서버가 아무 수도 허용하지 않았다
            };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPlay(new List<Card> { s.Seats[0].Hand[0] }), Is.False);
            Assert.That(sent.Count, Is.EqualTo(0));
        }

        [Test]
        public void Pass_is_refused_when_the_server_says_it_cannot_pass()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting { Kind = StepKind.Play, Seat = 0, CanPass = false };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.False, "서버가 금지한 패스는 내보내지 않는다");
            Assert.That(sent.Count, Is.EqualTo(0));
        }

        [Test]
        public void Nothing_is_submitted_when_no_prompt_is_mine()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);           // Awaiting == null
            view.Phase = RoundPhase.Play;      // 큰 티츄·교환 단계는 질문 없이도 먼저 묻는다
            vm.Apply(view);
            var h = vm.MyHand.CurrentValue;

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.False);
            Assert.That(vm.SubmitDragonRecipient(1), Is.False);
            Assert.That(vm.SubmitGrandTichu(true), Is.False, "큰 티츄는 질문을 받아야 보낸다");
            Assert.That(vm.SubmitExchange(new ExchangeChoice(h[0], h[1], h[2])), Is.False,
                "교환은 질문을 받아야 보낸다");
            Assert.That(sent.Count, Is.EqualTo(0));
        }

        [Test]
        public void Exchange_requires_three_distinct_cards_from_my_hand()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Exchange;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting { Kind = StepKind.Exchange, Seat = 0 };
            vm.Apply(view);
            var h = vm.MyHand.CurrentValue;
            var notMine = s.Seats[1].Hand[0];       // 손패는 좌석 간 서로소다

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitExchange(new ExchangeChoice(h[0], h[0], h[1])), Is.False, "같은 카드 중복");
            Assert.That(vm.SubmitExchange(new ExchangeChoice(h[0], h[1], notMine)), Is.False, "손패에 없는 카드");
            Assert.That(sent.Count, Is.EqualTo(0));

            Assert.That(vm.SubmitExchange(new ExchangeChoice(h[0], h[1], h[2])), Is.True);
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.Exchange));
        }

        [Test]
        public void Dragon_recipient_must_be_an_opponent()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting { Kind = StepKind.DragonGift, Seat = 0 };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitDragonRecipient(2), Is.False, "파트너에게는 못 준다");
            Assert.That(vm.SubmitDragonRecipient(1), Is.True);
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].RecipientSeat, Is.EqualTo(1));
        }

        // ── 작은 티츄 ──────────────────────────────────────────────────────────

        [Test]
        public void Small_tichu_is_fire_and_forget()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;                                  // 첫 패 전 Play
            for (int i = 0; i < 4; i++) view.HandCounts[i] = 14;
            vm.Apply(view);            // 프롬프트 없음 — 그래도 보낼 수 있어야 한다
            Assert.That(vm.TichuAvailable.CurrentValue, Is.True,
                "Play·손패 14장·미콜이면 상시 버튼이 켜진다");

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.DeclareSmallTichu(), Is.True,
                "작은 티츄는 서버가 묻지 않는다 — 언제든 예약으로 보낸다");
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.CallTichu));
            Assert.That(vm.TichuAvailable.CurrentValue, Is.False, "보낸 뒤엔 버튼을 숨긴다");
        }

        [Test]
        public void The_optimistic_tichu_hide_does_not_latch_past_my_next_play()
        {
            // 낙관적 숨김은 제출→적용 창에서만 유효해야 한다. 늦은 콜을 서버가 조용히
            // 버리는 경로가 있으므로(수정은 세션 몫), 플래그가 걸쇠가 되면 버튼이 영구히
            // 사라진다. 내 패가 나간 뒤로는 권위 값만 버튼을 결정해야 한다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;
            for (int i = 0; i < 4; i++) view.HandCounts[i] = 14;
            vm.Apply(view);

            Assert.That(vm.DeclareSmallTichu(), Is.True);
            Assert.That(vm.TichuAvailable.CurrentValue, Is.False);

            vm.Record(AppliedAction.From(GameAction.Play(0, new[] { Card.Normal(7, Suit.Jade) })));
            vm.Apply(view);

            Assert.That(vm.TichuAvailable.CurrentValue, Is.True,
                "권위 뷰가 미콜·14장이라고 하면 버튼은 돌아와야 한다(걸쇠 금지)");
        }

        // ── 차례밖 폭탄 ────────────────────────────────────────────────────────

        [Test]
        public void Off_turn_only_bombs_are_sent_and_marked_pending()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));            // 프롬프트 없음 = 차례밖

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPlay(new List<Card> { Card.Normal(7, Suit.Jade) }), Is.False,
                "차례밖에 일반 수는 보낼 수 없다");
            Assert.That(vm.HasPendingBomb, Is.False);

            var bomb = new List<Card>
            {
                Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda),
            };
            Assert.That(vm.SubmitPlay(bomb), Is.True, "차례밖 폭탄은 예약으로 보낸다");
            Assert.That(vm.HasPendingBomb, Is.True, "뷰가 '발동!' 을 띄울 수 있어야 한다");
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.Play));
        }

        [Test]
        public void A_pending_off_turn_bomb_refuses_a_second_reservation()
        {
            // 차례밖 폭탄은 질문의 답이 아니라 프롬프트로 막을 수 없다. 같은 프레임의
            // 두 번째 탭을 흘려보내면, 둘째 예약은 이미 손패를 떠날 카드로 만들어져
            // 서버에서 영구히 해결되지 않고 _offTurnPlays 머리에 남는다 —
            // TakePendingBomb 가 머리만 들여다보므로 그 라운드의 이후 폭탄이 전부 막힌다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));            // 프롬프트 없음 = 차례밖

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            var bomb = new List<Card>
            {
                Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda),
            };
            Assert.That(vm.SubmitPlay(bomb), Is.True);
            Assert.That(vm.SubmitPlay(bomb), Is.False, "소비되지 않은 예약이 있으면 또 보내지 않는다");
            Assert.That(sent.Count, Is.EqualTo(1));

            // 내 패가 적용되면 예약이 소비됐다는 뜻이므로 다시 보낼 수 있다.
            vm.Record(AppliedAction.From(GameAction.Play(0, bomb)));
            Assert.That(vm.HasPendingBomb, Is.False);
            Assert.That(vm.SubmitPlay(bomb), Is.True, "적용 뒤에는 새 예약을 받는다");
            Assert.That(sent.Count, Is.EqualTo(2));
        }

        [Test]
        public void A_phoenix_single_is_ranked_with_the_current_trick_not_as_a_lead()
        {
            // 봉황 단독의 Rank 는 트릭 문맥에 따라 달라지는 유일한 조합이다. 선검사를
            // Lead 로 인식하면 서버가 보낸 합법수(Top+1)와 어긋나 사람이 봉황을 못 낸다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            var top = CombinationRecognizer.Recognize(
                new[] { Card.Normal(10, Suit.Jade) }, TrickContext.Lead);
            var follow = new TrickContext(false, true, top.Rank);
            var phoenixMove = CombinationRecognizer.Recognize(new[] { Card.Phoenix }, follow);

            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;
            view.MyHand = new List<Card> { Card.Phoenix };
            view.CurrentTrick = new TrickView
            {
                LeadType = CombinationType.Single, LeadLength = 1, Top = top, TopOwnerSeat = 1,
            };
            view.Phase = RoundPhase.Play;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting
            {
                Kind = StepKind.Play, Seat = 0, CanPass = true,
                LegalMoves = new List<Combination> { phoenixMove },
            };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPlay(new List<Card> { Card.Phoenix }), Is.True,
                "현재 트릭 문맥으로 인식해야 서버 합법수와 Rank 가 맞는다");
            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Rank, Is.EqualTo(phoenixMove.Rank),
                "서버 MoveResolver 가 의도 확인에 쓰는 Rank 가 합법수와 같아야 한다");
        }

        [Test]
        public void Same_top_rank_different_length_straight_is_refused_locally()
        {
            // 서버 합법수는 6-7-8-9-10(길이 5, Top=10)뿐이다. 플레이어가 같은 Top(10)의
            // 더 긴 스트레이트(5-6-7-8-9-10, 길이 6)를 선택하면, Length 를 안 보던 구 코드는
            // (Type, Rank) 만 같으면 로컬에서 통과시켜 서버가 거부할 때까지 버튼이 눌려도
            // 아무 반응이 없었다(FIX 3).
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            var legalStraight = CombinationRecognizer.Recognize(
                new[]
                {
                    Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade), Card.Normal(8, Suit.Jade),
                    Card.Normal(9, Suit.Jade), Card.Normal(10, Suit.Jade),
                }, TrickContext.Lead);

            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;   // 질문 종류에 맞는 실제 단계
            view.Awaiting = new Awaiting
            {
                Kind = StepKind.Play, Seat = 0, CanPass = true,
                LegalMoves = new List<Combination> { legalStraight },
            };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            var longerStraight = new List<Card>
            {
                Card.Normal(5, Suit.Sword), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade), Card.Normal(10, Suit.Jade),
            };

            Assert.That(vm.SubmitPlay(longerStraight), Is.False,
                "같은 top rank(10)라도 길이가 다르면(5 vs 6) 로컬에서 거부돼야 한다");
            Assert.That(sent.Count, Is.EqualTo(0));
        }

        // ── 큰 티츄·교환은 내 차례를 기다리지 않는다 ──────────────────────────────
        // 서버는 좌석 순서로 묻지만 답은 미리 받아 둔다(RemoteAgent 선행 큐). 사람이 둘 이상인
        // 네트워크 판에서 앞 좌석이 끝날 때까지 기다리지 않도록, 단계에 들어서면 곧바로 묻는다.

        [Test]
        public void Early_grand_tichu_prompt_appears_when_the_phase_starts_without_server_question()
        {
            var s = GameEngine.NewRound(777UL);          // 큰 티츄 단계, Awaiting 없음
            var vm = new TableViewModel(0);

            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null, "큰 티츄 단계면 곧바로 물어야 한다");
            Assert.That(vm.Prompt.CurrentValue.Kind, Is.EqualTo(StepKind.GrandTichu));
            Assert.That(vm.Prompt.CurrentValue.Seat, Is.EqualTo(0));
        }

        [Test]
        public void Early_prompt_is_the_same_instance_across_updates_in_the_same_phase()
        {
            // 남의 결정마다 StateUpdate 가 온다. 프롬프트가 매번 새 인스턴스면 뷰가 버튼을 다시 그린다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));
            var first = vm.Prompt.CurrentValue;

            var other = ViewOf(s, 0);
            other.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 1 };   // 남의 차례 알림
            vm.Apply(other);

            Assert.That(vm.Prompt.CurrentValue, Is.SameAs(first));
        }

        [Test]
        public void After_answering_early_the_servers_ordered_question_is_ignored()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.SubmitGrandTichu(false), Is.True);

            // 서버가 순서대로 내 좌석에 같은 질문을 보낸다 — 답은 이미 서버 선행 큐에 있다.
            var asked = ViewOf(s, 0);
            asked.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(asked);

            Assert.That(vm.Prompt.CurrentValue, Is.Null, "이미 답한 질문을 다시 띄우면 두 번 답하게 된다");
            Assert.That(vm.SubmitGrandTichu(true), Is.False);
            Assert.That(sent.Count, Is.EqualTo(1));
        }

        [Test]
        public void Early_exchange_prompt_appears_in_the_exchange_phase()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Exchange;
            vm.Apply(view);

            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null);
            Assert.That(vm.Prompt.CurrentValue.Kind, Is.EqualTo(StepKind.Exchange));
            var h = vm.MyHand.CurrentValue;
            Assert.That(vm.SubmitExchange(new ExchangeChoice(h[0], h[1], h[2])), Is.True);

            vm.Apply(view);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "교환을 낸 뒤엔 같은 단계 갱신에서 다시 묻지 않는다");
        }

        [Test]
        public void A_rejection_lets_the_early_question_be_asked_again()
        {
            // 서버가 답을 거부하면 다시 묻는다(RemoteAgent while(true)). 답함 표시를 풀지 않으면 갇힌다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));
            vm.SubmitGrandTichu(false);

            vm.OnActionRejected();
            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null);
            Assert.That(vm.Prompt.CurrentValue.Kind, Is.EqualTo(StepKind.GrandTichu));
        }

        [Test]
        public void A_new_round_asks_again_with_a_fresh_prompt()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));
            var round1 = vm.Prompt.CurrentValue;
            vm.SubmitGrandTichu(false);

            vm.ClearPlays();                             // 새 라운드
            vm.Apply(ViewOf(GameEngine.NewRound(778UL), 0));

            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null);
            Assert.That(vm.Prompt.CurrentValue, Is.Not.SameAs(round1), "라운드마다 새 질문이다");
        }

        // ── 차례 중 작은 티츄 ──────────────────────────────────────────────────
        // 콜이 적용되면 서버는 질문 없는 갱신을 보낸 뒤 같은 질문을 다시 묻는다(GameDriver Yield).

        /// <summary>내 차례 Play 단계 뷰 — awaiting 이 null 이면 "질문 없는" 갱신.</summary>
        private static PlayerView MyTurnView(GameState s, int turn, Awaiting awaiting)
        {
            var view = ViewOf(s, 0);
            view.Phase = RoundPhase.Play;
            view.Turn = turn;
            view.Awaiting = awaiting;
            return view;
        }

        private static Awaiting AskPlay(Card legal, long askId = 1, int? secs = 30) => new Awaiting
        {
            Kind = StepKind.Play,
            Seat = 0,
            AskId = askId,
            SecondsLeft = secs,
            CanPass = true,
            LegalMoves = new List<Combination> { CombinationRecognizer.Recognize(new[] { legal }, TrickContext.Lead) },
        };

        [Test]
        public void A_tichu_call_during_my_turn_keeps_the_same_prompt()
        {
            // 프롬프트가 내려갔다 새 인스턴스로 다시 오면 뷰가 다시 그린다 — 차례 창이 깜빡이고
            // 고르던 마작 소원이 풀린다.
            var s = GameEngine.NewRound(777UL);
            var legal = s.Seats[0].Hand[0];
            var vm = new TableViewModel(0);
            vm.Apply(MyTurnView(s, 0, AskPlay(legal)));
            var asked = vm.Prompt.CurrentValue;
            int notified = 0;
            using var sub = vm.Prompt.Skip(1).Subscribe(_ => notified++);

            vm.Apply(MyTurnView(s, 0, null));            // 콜 적용 방송
            Assert.That(vm.Prompt.CurrentValue, Is.SameAs(asked), "차례는 여전히 내게 있다");

            vm.Apply(MyTurnView(s, 0, AskPlay(legal)));  // 같은 질문 재질의
            Assert.That(vm.Prompt.CurrentValue, Is.SameAs(asked));
            Assert.That(notified, Is.EqualTo(0), "뷰가 다시 그려지면 안 된다");
            Assert.That(vm.SubmitPass(), Is.True, "유지된 프롬프트로 그대로 답할 수 있다");
        }

        [Test]
        public void The_turn_prompt_drops_when_the_turn_moves_away()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            vm.Apply(MyTurnView(s, 0, AskPlay(s.Seats[0].Hand[0])));

            vm.Apply(MyTurnView(s, 1, null));            // 차례밖 폭탄 등으로 차례가 넘어갔다

            Assert.That(vm.Prompt.CurrentValue, Is.Null);
        }

        [Test]
        public void Answers_carry_the_prompt_ask_id_but_off_turn_calls_do_not()
        {
            var s = GameEngine.NewRound(777UL);
            var legal = s.Seats[0].Hand[0];
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 0, AskPlay(legal, askId: 17)));

            vm.SubmitPass();

            Assert.That(sent.Last().AskId, Is.EqualTo(17));
        }

        [Test]
        public void An_in_turn_play_carries_the_prompt_ask_id()
        {
            var s = GameEngine.NewRound(777UL);
            var legal = s.Seats[0].Hand[0];
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 0, AskPlay(legal, askId: 23)));

            Assert.That(vm.SubmitPlay(new List<Card> { legal }), Is.True);

            Assert.That(sent.Last().Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(sent.Last().AskId, Is.EqualTo(23));
        }

        [Test]
        public void A_small_tichu_call_has_no_ask_id_even_while_a_prompt_shows()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 0, AskPlay(s.Seats[0].Hand[0], askId: 23)));

            Assert.That(vm.DeclareSmallTichu(), Is.True);

            Assert.That(sent.Last().Kind, Is.EqualTo(ClientActionKind.CallTichu));
            Assert.That(sent.Last().AskId, Is.EqualTo(0));
        }

        [Test]
        public void An_off_turn_bomb_has_no_ask_id()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 1, null));   // 프롬프트 없음 = 차례밖

            var bomb = new List<Card>
            {
                Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda),
            };
            Assert.That(vm.SubmitPlay(bomb), Is.True);

            Assert.That(sent.Last().AskId, Is.EqualTo(0));
        }

        [Test]
        public void A_dragon_gift_carries_the_prompt_ask_id()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 0, new Awaiting { Kind = StepKind.DragonGift, Seat = 0, AskId = 31 }));

            Assert.That(vm.SubmitDragonRecipient(1), Is.True);

            Assert.That(sent.Last().AskId, Is.EqualTo(31));
        }

        [Test]
        public void An_early_grand_tichu_answer_has_no_ask_id()
        {
            // 스펙 §3.1: 큰 티츄·교환은 서버가 묻기 전에 미리 답한다 — 번호 0, 중복은 서버가 막는다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.SubmitGrandTichu(false), Is.True);

            Assert.That(sent.Last().AskId, Is.EqualTo(0));
        }

        [Test]
        public void A_held_turn_prompt_takes_the_new_ask_id()
        {
            // 작은 티츄 Yield 재질의: 깜빡임 방지로 인스턴스는 유지하지만 번호는 새 질문의 것이어야 한다.
            var s = GameEngine.NewRound(777UL);
            var legal = s.Seats[0].Hand[0];
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            vm.Apply(MyTurnView(s, 0, AskPlay(legal, askId: 5)));
            var held = vm.Prompt.CurrentValue;
            vm.Apply(MyTurnView(s, 0, null));
            vm.Apply(MyTurnView(s, 0, AskPlay(legal, askId: 6)));

            Assert.That(vm.Prompt.CurrentValue, Is.SameAs(held));
            vm.SubmitPass();
            Assert.That(sent.Last().AskId, Is.EqualTo(6));
        }

        [Test]
        public void Turn_countdown_runs_from_the_time_the_question_arrived()
        {
            var s = GameEngine.NewRound(777UL);
            double now = 100;
            var vm = new TableViewModel(0, () => now);
            vm.Apply(MyTurnView(s, 0, AskPlay(s.Seats[0].Hand[0], askId: 1, secs: 30)));

            Assert.That(vm.TurnSecondsLeft, Is.EqualTo(30));
            now = 112.4;
            Assert.That(vm.TurnSecondsLeft, Is.EqualTo(18));
            vm.SubmitPass();
            Assert.That(vm.TurnSecondsLeft, Is.Null, "답하면 타이머가 사라진다");
        }

        [Test]
        public void Away_flags_and_resume()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));
            var v = MyTurnView(s, 1, null);
            v.Away[0] = true; v.Away[2] = true;
            vm.Apply(v);

            Assert.That(vm.MyAway.CurrentValue, Is.True);
            Assert.That(vm.SeatAway(2).CurrentValue, Is.True);
            Assert.That(vm.SubmitResume(), Is.True);
            Assert.That(sent.Last().Kind, Is.EqualTo(ClientActionKind.Resume));
        }

        [Test]
        public void Reconnect_resets_local_answers_and_sets_the_score()
        {
            var s = GameEngine.NewRound(777UL);           // 큰 티츄 단계
            var vm = new TableViewModel(0);
            vm.Apply(ViewOf(s, 0));
            vm.SubmitGrandTichu(false);
            vm.Apply(ViewOf(s, 0));
            Assert.That(vm.Prompt.CurrentValue, Is.Null);

            vm.ResetForReconnect();
            vm.SetMatchScore(450, 300);
            vm.Apply(ViewOf(s, 0));

            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null, "재접속하면 서버 상태로 다시 묻는다");
            Assert.That(vm.CumulativeA.CurrentValue, Is.EqualTo(450));
            Assert.That(vm.CumulativeB.CurrentValue, Is.EqualTo(300));
        }
    }
}
