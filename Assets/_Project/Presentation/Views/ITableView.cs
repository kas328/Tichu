using System.Threading;
using Tichu.Presentation.ViewModel;
using UnityEngine;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 테이블 뷰 계약. 현행 <see cref="RuntimeTableView"/>(런타임 빌드 uGUI)와 이후
    /// PrefabTableView(D2 — 프리팹/풀링/가로 앵커)가 동일 계약 뒤에서 교체된다(진실/연출 분리).
    /// <para>
    /// 구독 계약(누락 시 UI desync) — 구현체는 <see cref="TableViewModel"/>의 다음 스트림을
    /// 빠짐없이 구독해야 한다:
    /// Phase · CurrentTurn · MyHand · CurrentTrick · RoundResult · PendingDecision · Wish ·
    /// CumulativeA · CumulativeB · HandCount(0..3) · TichuAvailable · Played · PlaysCleared.
    /// (BombReserved · FastForward 는 차례밖 폭탄/스킵 표현용 — 현재는 직접 조회, D4 애니에서 구독화.)
    /// </para>
    /// </summary>
    public interface ITableView
    {
        /// <summary>VM 구독을 걸고 캔버스에 화면을 구성한다. sceneCt 취소 시 비동기 연출을 정리한다.</summary>
        void Bind(TableViewModel vm, Canvas canvas, CancellationToken sceneCt);
    }
}
