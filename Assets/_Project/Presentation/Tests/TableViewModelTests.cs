using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
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
        public void Prompt_is_set_only_for_my_seat()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);

            var mine = ViewOf(s, 0);
            mine.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 0 };
            vm.Apply(mine);
            Assert.That(vm.Prompt.CurrentValue, Is.Not.Null);

            var theirs = ViewOf(s, 0);
            theirs.Awaiting = new Awaiting { Kind = StepKind.GrandTichu, Seat = 2 };
            vm.Apply(theirs);
            Assert.That(vm.Prompt.CurrentValue, Is.Null, "남의 차례는 프롬프트가 아니다");
        }

        [Test]
        public void Submitting_a_play_emits_a_client_action_with_increasing_seq()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var card = s.Seats[0].Hand[0];
            var view = ViewOf(s, 0);
            view.Awaiting = new Awaiting
            {
                Kind = StepKind.Play,
                Seat = 0,
                CanPass = true,
                // 서버가 실제로 싣는 것과 같은 형태 — 선검사는 이 목록으로만 한다.
                LegalMoves = new List<Combination>
                {
                    CombinationRecognizer.Recognize(new[] { card }, TrickContext.Lead),
                },
            };
            vm.Apply(view);

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.True);
            Assert.That(vm.SubmitPlay(new List<Card> { card }), Is.True);

            Assert.That(sent.Count, Is.EqualTo(2));
            Assert.That(sent[0].Kind, Is.EqualTo(ClientActionKind.Pass));
            Assert.That(sent[0].Seat, Is.EqualTo(0));
            Assert.That(sent[1].Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(sent[1].ActionSeq, Is.GreaterThan(sent[0].ActionSeq),
                "ActionSeq 는 좌석별 단조 증가라야 서버가 재생을 막을 수 있다");
        }

        [Test]
        public void A_play_outside_the_legal_moves_is_refused()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0);
            var view = ViewOf(s, 0);
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
            vm.Apply(ViewOf(s, 0));            // Awaiting == null

            var sent = new List<ClientAction>();
            using var sub = vm.Submitted.Subscribe(a => sent.Add(a));

            Assert.That(vm.SubmitPass(), Is.False);
            Assert.That(vm.SubmitDragonRecipient(1), Is.False);
            Assert.That(sent.Count, Is.EqualTo(0));
        }

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
    }
}
