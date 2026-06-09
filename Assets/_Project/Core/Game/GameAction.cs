#nullable enable
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Game
{
    public enum GameActionKind
    {
        CallGrandTichu,
        DeclineGrandTichu,
        Exchange,
        CallTichu,
        Play,
        Pass
    }

    public sealed class GameAction
    {
        public GameActionKind Kind { get; }
        public int Seat { get; }

        // Play payload
        public IReadOnlyList<Card>? Cards { get; }
        public int? Wish { get; }

        // Exchange payload
        public IReadOnlyList<Card>? ExchangeToLeft { get; }
        public IReadOnlyList<Card>? ExchangePartner { get; }
        public IReadOnlyList<Card>? ExchangeToRight { get; }

        private GameAction(
            GameActionKind kind,
            int seat,
            IReadOnlyList<Card>? cards = null,
            int? wish = null,
            IReadOnlyList<Card>? exchangeToLeft = null,
            IReadOnlyList<Card>? exchangePartner = null,
            IReadOnlyList<Card>? exchangeToRight = null)
        {
            Kind = kind;
            Seat = seat;
            Cards = cards;
            Wish = wish;
            ExchangeToLeft = exchangeToLeft;
            ExchangePartner = exchangePartner;
            ExchangeToRight = exchangeToRight;
        }

        public static GameAction CallGrandTichu(int seat) =>
            new GameAction(GameActionKind.CallGrandTichu, seat);

        public static GameAction DeclineGrandTichu(int seat) =>
            new GameAction(GameActionKind.DeclineGrandTichu, seat);

        public static GameAction Exchange(
            int seat,
            IReadOnlyList<Card> toLeft,
            IReadOnlyList<Card> toPartner,
            IReadOnlyList<Card> toRight) =>
            new GameAction(GameActionKind.Exchange, seat,
                exchangeToLeft: toLeft,
                exchangePartner: toPartner,
                exchangeToRight: toRight);

        public static GameAction CallTichu(int seat) =>
            new GameAction(GameActionKind.CallTichu, seat);

        public static GameAction Play(int seat, IReadOnlyList<Card> cards, int? wish = null) =>
            new GameAction(GameActionKind.Play, seat, cards: cards, wish: wish);

        public static GameAction Pass(int seat) =>
            new GameAction(GameActionKind.Pass, seat);
    }
}
