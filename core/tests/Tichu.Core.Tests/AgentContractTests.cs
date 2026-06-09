using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    [TestFixture]
    public class AgentContractTests
    {
        [Test]
        public void Context_exposes_correct_relative_seats()
        {
            var s = GameEngine.NewRound(123);
            var ctx = new DecisionContext(s, 1);

            Assert.That(ctx.Seat, Is.EqualTo(1));
            Assert.That(ctx.LeftSeat, Is.EqualTo(2));
            Assert.That(ctx.PartnerSeat, Is.EqualTo(3));
            Assert.That(ctx.RightSeat, Is.EqualTo(0));
        }

        [Test]
        public void MyHand_delegates_to_seat_hand()
        {
            var s = GameEngine.NewRound(123);
            var ctx = new DecisionContext(s, 1);

            Assert.That(ctx.MyHand, Is.EquivalentTo(s.Seats[1].Hand));
            Assert.That(ctx.MyHand.Count, Is.EqualTo(8));
        }

        [Test]
        public void LegalMoves_and_CanPass_empty_outside_play()
        {
            var s = GameEngine.NewRound(123);
            // Phase is GrandTichuDecision — not Play
            var ctx = new DecisionContext(s, 0);

            Assert.That(ctx.LegalMoves, Is.Empty);
            Assert.That(ctx.CanPass, Is.False);
        }

        [Test]
        public void TurnDecision_pass_and_play()
        {
            var pass = TurnDecision.Pass;
            Assert.That(pass.IsPass, Is.True);
            Assert.That(pass.Move, Is.Null);
            Assert.That(pass.Wish, Is.Null);

            var combo = CombinationRecognizer.Recognize(
                new[] { Card.Normal(13, Suit.Jade) },
                TrickContext.Lead);

            var play = TurnDecision.Play(combo);
            Assert.That(play.IsPass, Is.False);
            Assert.That(play.Move, Is.SameAs(combo));
            Assert.That(play.Wish, Is.Null);

            var playWithWish = TurnDecision.Play(combo, 5);
            Assert.That(playWithWish.Wish, Is.EqualTo(5));
        }

        [Test]
        public void ExchangeChoice_stores_three_directions()
        {
            var left = Card.Normal(2, Suit.Jade);
            var partner = Card.Normal(3, Suit.Sword);
            var right = Card.Normal(4, Suit.Pagoda);

            var choice = new ExchangeChoice(left, partner, right);

            Assert.That(choice.ToLeft, Is.EqualTo(left));
            Assert.That(choice.ToPartner, Is.EqualTo(partner));
            Assert.That(choice.ToRight, Is.EqualTo(right));
        }
    }
}
