#nullable enable
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Game
{
    /// <summary>라운드 셋업 페이즈(Deal8~Exchange 완료 전)의 임시 상태.</summary>
    internal sealed class RoundSetup
    {
        /// <summary>Deal8 이후 남은 24장. Deal6 후 비워진다.</summary>
        public List<Card> Undealt { get; set; }

        /// <summary>큰 티츄 결정을 이미 했는지 여부. 인덱스 = SeatIndex.</summary>
        public bool[] GrandTichuDecided { get; set; }

        /// <summary>교환 버퍼: 아직 확정 전의 각 자리 선택. null이면 미제출.</summary>
        public List<Card>[]? ExchangeToLeft    { get; set; }
        public List<Card>[]? ExchangeToPartner { get; set; }
        public List<Card>[]? ExchangeToRight   { get; set; }
        public bool[]?       ExchangeSubmitted  { get; set; }

        public RoundSetup()
        {
            Undealt = new List<Card>(GameEngine.SeatCount * GameEngine.Deal6Count);
            GrandTichuDecided = new bool[GameEngine.SeatCount];
        }

        /// <summary>교환 버퍼를 초기화한다 (Exchange 페이즈 진입 시 호출).</summary>
        public void InitExchangeBuffers()
        {
            ExchangeToLeft    = new List<Card>[GameEngine.SeatCount];
            ExchangeToPartner = new List<Card>[GameEngine.SeatCount];
            ExchangeToRight   = new List<Card>[GameEngine.SeatCount];
            ExchangeSubmitted  = new bool[GameEngine.SeatCount];
        }

        /// <summary>딥 카피.</summary>
        public RoundSetup Clone()
        {
            var c = new RoundSetup();
            c.Undealt.AddRange(Undealt);
            for (int i = 0; i < GameEngine.SeatCount; i++)
                c.GrandTichuDecided[i] = GrandTichuDecided[i];

            if (ExchangeSubmitted != null)
            {
                c.InitExchangeBuffers();
                for (int i = 0; i < GameEngine.SeatCount; i++)
                {
                    c.ExchangeSubmitted![i] = ExchangeSubmitted[i];
                    if (ExchangeToLeft![i] != null)
                        c.ExchangeToLeft![i] = new List<Card>(ExchangeToLeft[i]);
                    if (ExchangeToPartner![i] != null)
                        c.ExchangeToPartner![i] = new List<Card>(ExchangeToPartner[i]);
                    if (ExchangeToRight![i] != null)
                        c.ExchangeToRight![i] = new List<Card>(ExchangeToRight[i]);
                }
            }
            return c;
        }
    }
}
