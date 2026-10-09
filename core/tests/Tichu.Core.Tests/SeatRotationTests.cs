using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 테이블 UI 는 "나 = 좌석 0" 을 가정한다. 클라이언트 경계에서 서버 좌표를 로컬로 돌리고
    /// 보낼 때 되돌린다. 홀수 좌석이면 팀 A/B 가 뒤집힌다(로컬 팀 A = 나·파트너).
    /// </summary>
    public class SeatRotationTests
    {
        private static PlayerView View(int viewer) => new PlayerView
        {
            Viewer = viewer,
            Turn = 3,
            DragonGiftWinner = 1,
            HandCounts = new[] { 10, 11, 12, 13 },
            Calls = new[] { TichuCall.None, TichuCall.Tichu, TichuCall.None, TichuCall.GrandTichu },
            IsOut = new[] { false, true, false, false },
            FinishOrder = new[] { 0, 1, 0, 0 },
            WonCards = new[] { new List<Card>(), new List<Card> { Card.Dragon }, new List<Card>(), new List<Card>() },
            CurrentTrick = new TrickView
            {
                TopOwnerSeat = 2,
                History = new List<PlayView> { new PlayView { Seat = 2 }, new PlayView { Seat = 3, IsPass = true } }
            },
            CompletedTricks = new List<TrickSummary> { new TrickSummary { TopOwnerSeat = 1, DragonGiftRecipient = 2 } },
            TeamAScore = 300,
            TeamBScore = 150,
            TargetScore = 1000,
            Awaiting = new Awaiting { Kind = StepKind.Play, Seat = 2, CanPass = true },
        };

        [TestCase(0, 2)] [TestCase(1, 1)] [TestCase(2, 0)] [TestCase(3, 3)]
        public void Seat_two_is_rotated_relative_to_my_seat(int mySeat, int expectedLocal)
        {
            Assert.That(SeatRotation.ToLocalSeat(2, mySeat), Is.EqualTo(expectedLocal));
        }

        [Test]
        public void Seat_round_trips_for_every_pair_and_negative_is_preserved()
        {
            for (int my = 0; my < 4; my++)
            {
                for (int abs = 0; abs < 4; abs++)
                    Assert.That(SeatRotation.ToServerSeat(SeatRotation.ToLocalSeat(abs, my), my), Is.EqualTo(abs));
                Assert.That(SeatRotation.ToLocalSeat(-1, my), Is.EqualTo(-1));
                Assert.That(SeatRotation.ToServerSeat(-1, my), Is.EqualTo(-1));
            }
            Assert.That(SeatRotation.ToLocalSeat(0, 0), Is.EqualTo(0));
        }

        [Test]
        public void My_seat_zero_is_identity()
        {
            var ev = ServerEvent.StateUpdate(View(0), null);
            Assert.That(SeatRotation.ToLocal(ev, 0), Is.SameAs(ev));
            var a = ClientAction.Pass(0, 1);
            Assert.That(SeatRotation.ToServer(a, 0), Is.SameAs(a));
        }

        [Test]
        public void Even_seat_rotates_seats_without_flipping_teams()
        {
            var awaiting = new Awaiting { Kind = StepKind.Play, Seat = 2, CanPass = true };
            var ev = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(2), awaiting), 2);
            var v = ev.View!;

            Assert.That(v.Viewer, Is.EqualTo(0));
            Assert.That(v.Turn, Is.EqualTo(1));
            Assert.That(v.DragonGiftWinner, Is.EqualTo(3));
            Assert.That(v.HandCounts, Is.EqualTo(new[] { 12, 13, 10, 11 }));
            Assert.That(v.Calls[3], Is.EqualTo(TichuCall.Tichu));
            Assert.That(v.IsOut[3], Is.True);
            Assert.That(v.WonCards[3].Count, Is.EqualTo(1));
            Assert.That(v.CurrentTrick!.TopOwnerSeat, Is.EqualTo(0));
            Assert.That(v.CurrentTrick.History[0].Seat, Is.EqualTo(0));
            Assert.That(v.CurrentTrick.History[1].Seat, Is.EqualTo(1));
            Assert.That(v.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(3));
            Assert.That(v.CompletedTricks[0].DragonGiftRecipient, Is.EqualTo(0));
            Assert.That(v.Awaiting!.Seat, Is.EqualTo(0));
            Assert.That(v.Awaiting.CanPass, Is.True);
            Assert.That(v.TeamAScore, Is.EqualTo(300), "짝수 좌석은 팀이 그대로다");
            Assert.That(v.TeamBScore, Is.EqualTo(150));
        }

        [Test]
        public void Odd_seat_flips_teams_in_view_round_and_match_end()
        {
            var v = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(1), null), 1).View!;
            Assert.That(v.TeamAScore, Is.EqualTo(150), "좌석 1 의 팀은 서버 팀 B");
            Assert.That(v.TeamBScore, Is.EqualTo(300));

            var round = new RoundResult
            {
                TeamACardPoints = 70, TeamBCardPoints = 30, TeamATichuDelta = 100, TeamBTichuDelta = 0,
                TeamATotal = 170, TeamBTotal = 30
            };
            var r = SeatRotation.ToLocal(ServerEvent.RoundScored(round), 1).Round!;
            Assert.That(r.TeamACardPoints, Is.EqualTo(30));
            Assert.That(r.TeamBCardPoints, Is.EqualTo(70));
            Assert.That(r.TeamATichuDelta, Is.EqualTo(0));
            Assert.That(r.TeamBTichuDelta, Is.EqualTo(100));
            Assert.That(r.TeamATotal, Is.EqualTo(30));
            Assert.That(r.TeamBTotal, Is.EqualTo(170));
            Assert.That(round.TeamATotal, Is.EqualTo(170), "원본은 건드리지 않는다");

            var end = SeatRotation.ToLocal(ServerEvent.MatchEnded(0, 1000, 600), 3);
            Assert.That(end.WinningTeam, Is.EqualTo(1), "서버 팀 A 승리 = 좌석 3 에겐 상대 팀 승리");
            Assert.That(end.TeamA, Is.EqualTo(600));
            Assert.That(end.TeamB, Is.EqualTo(1000));
        }

        [Test]
        public void Applied_action_seats_rotate_and_missing_recipient_stays_minus_one()
        {
            var play = new AppliedAction { Kind = GameActionKind.Play, Seat = 3, RecipientSeat = -1 };
            var gift = new AppliedAction { Kind = GameActionKind.GiveDragon, Seat = 0, RecipientSeat = 1 };

            var p = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(2), null, play), 2).Applied!;
            var g = SeatRotation.ToLocal(ServerEvent.StateUpdate(View(2), null, gift), 2).Applied!;

            Assert.That(p.Seat, Is.EqualTo(1));
            Assert.That(p.RecipientSeat, Is.EqualTo(-1));
            Assert.That(g.Seat, Is.EqualTo(2));
            Assert.That(g.RecipientSeat, Is.EqualTo(3));
        }

        [Test]
        public void Lobby_seat_is_not_rotated()
        {
            var ev = SeatRotation.ToLocal(ServerEvent.Lobby(2, 1, 2), 2);
            Assert.That(ev.YourSeat, Is.EqualTo(2), "대기실이 회전 전에 읽는 절대 좌석 배정");
            Assert.That(ev.Connected, Is.EqualTo(1));
            Assert.That(ev.Required, Is.EqualTo(2));
        }

        [Test]
        public void Outgoing_actions_rotate_back_and_exchange_targets_stay_relative()
        {
            var gift = SeatRotation.ToServer(ClientAction.GiveDragon(0, 5, 1), 2);
            Assert.That(gift.Seat, Is.EqualTo(2));
            Assert.That(gift.RecipientSeat, Is.EqualTo(3));
            Assert.That(gift.ActionSeq, Is.EqualTo(5));

            var ex = SeatRotation.ToServer(ClientAction.Exchange(0, 6, Card.Dog, Card.Dragon, Card.Phoenix), 3);
            Assert.That(ex.Seat, Is.EqualTo(3));
            Assert.That(ex.ToLeft, Is.EqualTo(Card.Dog));
            Assert.That(ex.ToPartner, Is.EqualTo(Card.Dragon));
            Assert.That(ex.ToRight, Is.EqualTo(Card.Phoenix));

            var pass = SeatRotation.ToServer(ClientAction.Pass(0, 7), 1);
            Assert.That(pass.RecipientSeat, Is.EqualTo(0), "GiveDragon 이 아니면 RecipientSeat 을 건드리지 않는다");
        }

        [Test]
        public void Queued_fields_survive_rotation()
        {
            var local = SeatRotation.ToLocal(ServerEvent.Queued(3, 12, true, 2), 1);
            Assert.That(local.Kind, Is.EqualTo(ServerEventKind.Queued));
            Assert.That(local.Waiting, Is.EqualTo(3));
            Assert.That(local.FillInSeconds, Is.EqualTo(12));
            Assert.That(local.Busy, Is.True);
            Assert.That(local.Position, Is.EqualTo(2));
        }
    }
}
