using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 프로토콜 JSON 왕복. 유니티(IL2CPP)와 서버(dotnet)가 같은 코드로 주고받는다.
    /// 미러되는 테스트라 Newtonsoft 타입을 직접 쓰지 않는다(ProtocolJson·ProtocolException 만).
    /// </summary>
    public class ProtocolJsonTests
    {
        private static string Fragment(Card c) =>
            "{\"r\":" + c.Rank + ",\"s\":\"" + c.Suit + "\",\"x\":\"" + c.Special + "\",\"p\":" + c.Points + "}";

        private static ServerEvent RoundTrip(ServerEvent ev) =>
            ProtocolJson.DeserializeServerEvent(ProtocolJson.Serialize(ev));

        private static ClientAction RoundTrip(ClientAction a) =>
            ProtocolJson.DeserializeClientAction(ProtocolJson.Serialize(a));

        [Test]
        public void Every_client_action_kind_round_trips()
        {
            var cards = new List<Card> { Card.Normal(5, Suit.Jade), Card.Phoenix };

            var play = RoundTrip(ClientAction.Play(2, 9, cards, 10, 7));
            Assert.That(play.Kind, Is.EqualTo(ClientActionKind.Play));
            Assert.That(play.Seat, Is.EqualTo(2));
            Assert.That(play.ActionSeq, Is.EqualTo(9));
            Assert.That(play.Cards, Is.EqualTo(cards));
            Assert.That(play.Rank, Is.EqualTo(10));
            Assert.That(play.Wish, Is.EqualTo(7));

            var ex = RoundTrip(ClientAction.Exchange(1, 3, Card.Dog, Card.Mahjong, Card.Dragon));
            Assert.That(ex.ToLeft, Is.EqualTo(Card.Dog));
            Assert.That(ex.ToPartner, Is.EqualTo(Card.Mahjong));
            Assert.That(ex.ToRight, Is.EqualTo(Card.Dragon));

            Assert.That(RoundTrip(ClientAction.GiveDragon(0, 4, 3)).RecipientSeat, Is.EqualTo(3));
            Assert.That(RoundTrip(ClientAction.CallGrandTichu(0, 1)).Kind, Is.EqualTo(ClientActionKind.CallGrandTichu));
            Assert.That(RoundTrip(ClientAction.DeclineGrandTichu(0, 1)).Kind, Is.EqualTo(ClientActionKind.DeclineGrandTichu));
            Assert.That(RoundTrip(ClientAction.CallTichu(0, 1)).Kind, Is.EqualTo(ClientActionKind.CallTichu));
            Assert.That(RoundTrip(ClientAction.Pass(0, 1)).Kind, Is.EqualTo(ClientActionKind.Pass));
        }

        [Test]
        public void Special_cards_keep_rank_suit_kind_and_points()
        {
            foreach (var c in new[] { Card.Mahjong, Card.Dog, Card.Phoenix, Card.Dragon, Card.Normal(13, Suit.Star) })
            {
                var back = RoundTrip(ClientAction.Play(0, 1, new List<Card> { c }, 0, null)).Cards![0];
                Assert.That(back, Is.EqualTo(c));
                Assert.That(back.Points, Is.EqualTo(c.Points), c.ToString());
            }
        }

        [Test]
        public void State_update_with_full_view_round_trips()
        {
            var top = new Combination(CombinationType.Pair,
                new[] { Card.Normal(9, Suit.Sword), Card.Normal(9, Suit.Pagoda) }, 2, 18, 0);
            var view = new PlayerView
            {
                Viewer = 1, Phase = RoundPhase.Play, Turn = 2, Wish = 8, DragonGiftWinner = 3,
                MyHand = new List<Card> { Card.Normal(14, Suit.Star), Card.Dragon },
                HandCounts = new[] { 3, 2, 5, 0 },
                Calls = new[] { TichuCall.None, TichuCall.Tichu, TichuCall.None, TichuCall.GrandTichu },
                IsOut = new[] { false, false, false, true },
                FinishOrder = new[] { 0, 0, 0, 1 },
                WonCards = new[] { new List<Card> { Card.Normal(5, Suit.Jade) }, new List<Card>(), new List<Card>(), new List<Card>() },
                CurrentTrick = new TrickView
                {
                    LeadType = CombinationType.Pair, LeadLength = 2, Top = top, TopOwnerSeat = 0, AccumulatedPoints = 10,
                    History = new List<PlayView> { new PlayView { Seat = 0, Cards = new List<Card>(top.Cards) }, new PlayView { Seat = 1, IsPass = true } },
                },
                CompletedTricks = new List<TrickSummary> { new TrickSummary { TopOwnerSeat = 3, AccumulatedPoints = 25, WonByDragon = true, DragonGiftRecipient = 0 } },
                TeamAScore = 120, TeamBScore = 80, TargetScore = 1000,
            };
            var awaiting = new Awaiting { Kind = StepKind.Play, Seat = 1, CanPass = true, LegalMoves = new List<Combination> { top } };
            var applied = new AppliedAction { Kind = GameActionKind.Play, Seat = 0, Cards = new List<Card>(top.Cards), RecipientSeat = -1 };

            var back = RoundTrip(ServerEvent.StateUpdate(view, awaiting, applied));

            var v = back.View!;
            Assert.That(back.Kind, Is.EqualTo(ServerEventKind.StateUpdate));
            Assert.That(v.Viewer, Is.EqualTo(1));
            Assert.That(v.Phase, Is.EqualTo(RoundPhase.Play));
            Assert.That(v.Wish, Is.EqualTo(8));
            Assert.That(v.DragonGiftWinner, Is.EqualTo(3));
            Assert.That(v.MyHand, Is.EqualTo(view.MyHand));
            Assert.That(v.HandCounts, Is.EqualTo(view.HandCounts));
            Assert.That(v.Calls, Is.EqualTo(view.Calls));
            Assert.That(v.IsOut, Is.EqualTo(view.IsOut));
            Assert.That(v.FinishOrder, Is.EqualTo(view.FinishOrder));
            Assert.That(v.WonCards[0], Is.EqualTo(view.WonCards[0]));
            Assert.That(v.CurrentTrick!.Top!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(v.CurrentTrick.Top.Rank, Is.EqualTo(18));
            Assert.That(v.CurrentTrick.Top.Length, Is.EqualTo(2));
            Assert.That(v.CurrentTrick.Top.Cards, Is.EqualTo(top.Cards));
            Assert.That(v.CurrentTrick.History.Count, Is.EqualTo(2));
            Assert.That(v.CurrentTrick.History[1].IsPass, Is.True);
            Assert.That(v.CompletedTricks[0].DragonGiftRecipient, Is.EqualTo(0));
            Assert.That(v.CompletedTricks[0].WonByDragon, Is.True);
            Assert.That(v.TeamAScore, Is.EqualTo(120));
            Assert.That(v.Awaiting!.LegalMoves!.Count, Is.EqualTo(1));
            Assert.That(v.Awaiting.LegalMoves[0].Cards, Is.EqualTo(top.Cards));
            Assert.That(v.Awaiting.CanPass, Is.True);
            Assert.That(back.Applied!.Cards, Is.EqualTo(applied.Cards));
            Assert.That(back.Applied.RecipientSeat, Is.EqualTo(-1));
        }

        [Test]
        public void Empty_trick_view_keeps_null_top()
        {
            var view = new PlayerView { CurrentTrick = new TrickView { Top = null, TopOwnerSeat = -1 } };
            var v = RoundTrip(ServerEvent.StateUpdate(view, null)).View!;
            Assert.That(v.CurrentTrick!.Top, Is.Null);
            Assert.That(v.Awaiting, Is.Null);
        }

        [Test]
        public void Other_server_events_round_trip()
        {
            var round = RoundTrip(ServerEvent.RoundScored(new RoundResult { TeamACardPoints = 60, TeamBCardPoints = 40, TeamATichuDelta = -100, TeamATotal = -40, TeamBTotal = 40 }));
            Assert.That(round.Round!.TeamATichuDelta, Is.EqualTo(-100));
            Assert.That(round.Round.TeamBTotal, Is.EqualTo(40));

            var end = RoundTrip(ServerEvent.MatchEnded(1, 900, 1020));
            Assert.That(end.WinningTeam, Is.EqualTo(1));
            Assert.That(end.TeamB, Is.EqualTo(1020));

            var rej = RoundTrip(ServerEvent.ActionRejected(12, "카드 없음"));
            Assert.That(rej.ActionSeq, Is.EqualTo(12));
            Assert.That(rej.Reason, Is.EqualTo("카드 없음"));

            Assert.That(RoundTrip(ServerEvent.SessionFaulted("좌석 2 연결 끊김")).Reason, Is.EqualTo("좌석 2 연결 끊김"));

            var lobby = RoundTrip(ServerEvent.Lobby(2, 1, 2));
            Assert.That(lobby.Kind, Is.EqualTo(ServerEventKind.Lobby));
            Assert.That(lobby.YourSeat, Is.EqualTo(2));
            Assert.That(lobby.Connected, Is.EqualTo(1));
            Assert.That(lobby.Required, Is.EqualTo(2));

            var waiting = RoundTrip(ServerEvent.Queued(2, 18, false, 0));
            Assert.That(waiting.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(waiting.Waiting, Is.EqualTo(2));
            Assert.That(waiting.FillInSeconds, Is.EqualTo(18));
            Assert.That(waiting.Busy, Is.False);
            Assert.That(waiting.Position, Is.EqualTo(0));

            var busy = RoundTrip(ServerEvent.Queued(5, null, true, 3));
            Assert.That(busy.FillInSeconds, Is.Null, "Busy 면 카운트다운이 없다");
            Assert.That(busy.Busy, Is.True);
            Assert.That(busy.Position, Is.EqualTo(3));

            var view = new PlayerView { Viewer = 1 };
            view.Away[2] = true;
            view.PhaseSecondsLeft = 17;
            var ask = ServerEvent.StateUpdate(view, new Awaiting { Kind = StepKind.Play, Seat = 1, AskId = 7, SecondsLeft = 30 });
            var ask2 = RoundTrip(ask);
            Assert.That(ask2.View!.Away, Is.EqualTo(new[] { false, false, true, false }));
            Assert.That(ask2.View.PhaseSecondsLeft, Is.EqualTo(17));
            Assert.That(ask2.View.Awaiting!.AskId, Is.EqualTo(7));
            Assert.That(ask2.View.Awaiting.SecondsLeft, Is.EqualTo(30));
        }

        [Test]
        public void Resume_and_ask_id_round_trip()
        {
            var resume = ProtocolJson.DeserializeClientAction(ProtocolJson.Serialize(ClientAction.Resume(2, 9)));
            Assert.That(resume.Kind, Is.EqualTo(ClientActionKind.Resume));
            Assert.That(resume.ActionSeq, Is.EqualTo(9));
            var answered = ClientAction.Pass(0, 3);
            answered.AskId = 41;
            Assert.That(ProtocolJson.DeserializeClientAction(ProtocolJson.Serialize(answered)).AskId, Is.EqualTo(41));
        }

        [TestCase("not json")]
        [TestCase("{}")]
        [TestCase("{\"Kind\":\"Teleport\"}")]
        [TestCase("{\"Kind\":3}")]
        [TestCase("null")]
        [TestCase("{\"Kind\":\"Play\",\"Cards\":[{\"r\":5}]}")]
        public void Malformed_client_messages_throw_protocol_exception(string json)
        {
            Assert.Throws<ProtocolException>(() => ProtocolJson.DeserializeClientAction(json));
        }

        [TestCase("{\"Kind\":\"Explode\"}")]
        [TestCase("[1,2]")]
        public void Malformed_server_messages_throw_protocol_exception(string json)
        {
            Assert.Throws<ProtocolException>(() => ProtocolJson.DeserializeServerEvent(json));
        }

        [Test]
        public void Serialized_view_on_the_wire_contains_no_other_seat_card()
        {
            // 유선 위 마스킹 불변식(기획서 7.5): 갓 나눈 라운드는 공개 카드가 0장이므로,
            // 좌석 0 에게 보내는 JSON 원문에 나타나는 카드는 좌석 0 손패뿐이어야 한다 —
            // 타 좌석 손패와 미배분 덱(큰 티츄 단계의 남은 6장씩) 모두 없어야 한다.
            // 티츄 카드는 (Rank, Suit, Special) 로 유일하므로 조각 검색으로 판정할 수 있다.
            var state = GameEngine.NewRound(2026UL);
            var mine = state.Seats[0].Hand;
            string json = ProtocolJson.Serialize(ServerEvent.StateUpdate(ViewProjector.ProjectFor(state, 0), null));

            foreach (var c in mine)
                Assert.That(json, Does.Contain(Fragment(c)), "양성 대조: 내 카드는 형식대로 실려야 한다 " + c);
            foreach (var c in Deck.CreateStandard())
                if (!mine.Contains(c))
                    Assert.That(json.Contains(Fragment(c)), Is.False, $"내 것이 아닌 카드 {c} 가 유선에 샜다");
        }

        private static string PlayWith(string cardJson) =>
            "{\"Kind\":\"Play\",\"Seat\":0,\"ActionSeq\":1,\"Cards\":[" + cardJson + "]}";

        [Test]
        public void Wire_points_are_ignored_and_the_canonical_card_is_built()
        {
            var king = ProtocolJson.DeserializeClientAction(PlayWith("{\"r\":13,\"s\":\"Star\",\"x\":\"None\",\"p\":999}")).Cards![0];
            Assert.That(king.Points, Is.EqualTo(10));

            var phoenix = ProtocolJson.DeserializeClientAction(PlayWith("{\"r\":0,\"s\":\"Special\",\"x\":\"Phoenix\",\"p\":50}")).Cards![0];
            Assert.That(phoenix.Points, Is.EqualTo(-25));
        }

        [TestCase("{\"r\":1,\"s\":\"Jade\",\"x\":\"None\",\"p\":0}")]
        [TestCase("{\"r\":15,\"s\":\"Jade\",\"x\":\"None\",\"p\":0}")]
        [TestCase("{\"r\":5,\"s\":\"Special\",\"x\":\"None\",\"p\":0}")]
        [TestCase("{\"r\":15,\"s\":\"Jade\",\"x\":\"Dragon\",\"p\":25}")]
        [TestCase("{\"r\":5,\"s\":\"Special\",\"x\":\"Dragon\",\"p\":25}")]
        [TestCase("{\"r\":5,\"s\":\"2\",\"x\":\"None\",\"p\":0}")]
        public void Invalid_card_combinations_throw_protocol_exception(string cardJson)
        {
            Assert.Throws<ProtocolException>(() => ProtocolJson.DeserializeClientAction(PlayWith(cardJson)));
        }

        [Test]
        public void Numeric_enum_strings_are_rejected()
        {
            Assert.Throws<ProtocolException>(() => ProtocolJson.DeserializeClientAction("{\"Kind\":\"3\"}"));
        }
    }
}
