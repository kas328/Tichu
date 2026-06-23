using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.Presentation.Audio;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// vm 관측 → IAudioService 라우팅의 EditMode 검증. PlayAnimatorWiringTests 미러(스파이 주입).
    /// 분류는 SfxMap(SfxMapTests에서 전수)에 위임 — 여기선 '발화/Last값/전이 1회'만 단언.
    /// </summary>
    public class AudioWiringTests
    {
        private sealed class RecordingAudioService : IAudioService
        {
            public int Count;
            public SfxId Last = SfxId.None;
            public void PlaySfx(SfxId id) { Count++; Last = id; }
        }

        private static GameObject Build(out TableViewModel vm, out RecordingAudioService rec)
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            vm = new TableViewModel(0);
            rec = new RecordingAudioService();
            ITableView view = new RuntimeTableView(audio: rec);
            view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);
            return canvasGo;
        }

        private static GameAction Bomb(int seat) => GameAction.Play(seat, new[]
        {
            Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
            Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda),
        });

        // ── T4: Played → SFX ─────────────────────────────────────────────────

        [Test]
        public void Played_normal_play_routes_CardPlay()
        {
            var canvas = Build(out var vm, out var rec);
            try
            {
                vm.RecordPlay(GameAction.Play(1, new[] { Card.Normal(7, Suit.Jade) }));
                Assert.AreEqual(SfxId.CardPlay, rec.Last);
                Assert.AreEqual(1, rec.Count);
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void Played_pass_routes_Pass()
        {
            var canvas = Build(out var vm, out var rec);
            try
            {
                vm.RecordPlay(GameAction.Pass(1));
                Assert.AreEqual(SfxId.Pass, rec.Last);
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void Played_bomb_routes_Bomb()
        {
            var canvas = Build(out var vm, out var rec);
            try
            {
                vm.RecordPlay(Bomb(1));
                Assert.AreEqual(SfxId.Bomb, rec.Last);
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        // ── T5: RoundResult → RoundEnd · SeatCall 전이 → TichuCall ────────────

        [Test]
        public void RoundResult_set_routes_RoundEnd_and_null_is_silent()
        {
            var canvas = Build(out var vm, out var rec);
            try
            {
                Assert.AreEqual(0, rec.Count, "구독 직후 초기 null 발화 0");
                vm.RoundResult.Value = new RoundResult { TeamATotal = 50, TeamBTotal = 50 };
                Assert.AreEqual(SfxId.RoundEnd, rec.Last);
                int after = rec.Count;
                vm.RoundResult.Value = null;   // 라운드 리셋
                Assert.AreEqual(after, rec.Count, "리셋 null은 발화 0");
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void SeatCall_transition_routes_once()
        {
            var canvas = Build(out var vm, out var rec);
            try
            {
                vm.SeatCall(2).Value = TichuCall.GrandTichu;
                Assert.AreEqual(SfxId.GrandTichuCall, rec.Last);
                int after = rec.Count;
                vm.SeatCall(2).Value = TichuCall.GrandTichu;   // 동일 재투영
                Assert.AreEqual(after, rec.Count, "동일 콜 재투영 발화 0");
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        // ── T2: 기본 ctor(NoOp 폴백) 무예외 — 3 소스 모두 푸시(S6) ───────────

        [Test]
        public void Default_ctor_uses_noop_and_does_not_throw()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView();   // 기본 = NoOp(뱅크 부재 → Resources.Load null → NoOp)
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);
                Assert.DoesNotThrow(() =>
                {
                    vm.RecordPlay(GameAction.Play(1, new[] { Card.Normal(7, Suit.Jade) }));
                    vm.RecordPlay(GameAction.Pass(1));
                    vm.RoundResult.Value = new RoundResult { TeamATotal = 0, TeamBTotal = 0 };
                    vm.SeatCall(2).Value = TichuCall.Tichu;
                });
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
    }
}
