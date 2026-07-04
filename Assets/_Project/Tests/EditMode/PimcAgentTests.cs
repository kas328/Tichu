using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>Pimc.Rollout + PimcAgent(다세계) 탐색 배선 검증.</summary>
    public class PimcAgentTests
    {
        private static GameState FreshPlayState(int turn)
        {
            var deck = Deck.CreateStandard();
            var hands = new IReadOnlyList<Card>[4];
            for (int i = 0; i < 4; i++)
                hands[i] = deck.GetRange(i * 14, 14);
            return GameFlowHelpers.PlayState(turn, hands);
        }

        // ── Rollout 정확성 ───────────────────────────────────────────────────────────

        [Test]
        public void Rollout_completes_without_throwing_and_returns_signed_reward()
        {
            // 결정화한 완전정보 세계를 디폴트 정책으로 끝까지 플레이 → throw 없이 정수 보상.
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int reward = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL, epsilon: 0.0);

            // 한 판 카드점수 합은 100 + 티츄델타라 절대값이 비현실적으로 크지 않다(완주 증거).
            Assert.That(reward, Is.InRange(-600, 600));
        }

        [Test]
        public void Rollout_is_deterministic_for_same_world_and_seed()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int a = Pimc.Rollout(world.Clone(), 0, 777UL, 0.0);
            int b = Pimc.Rollout(world.Clone(), 0, 777UL, 0.0);
            Assert.That(a, Is.EqualTo(b), "같은 세계+시드 → 같은 보상");
        }

        [Test]
        public void Rollout_reward_sign_is_relative_to_observer_team()
        {
            // 같은 세계를 팀0 관점(seat0)과 팀1 관점(seat1)으로 롤아웃하면 부호가 반대.
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int teamA = Pimc.Rollout(world.Clone(), observerSeat: 0, policySeed: 777UL, epsilon: 0.0);
            int teamB = Pimc.Rollout(world.Clone(), observerSeat: 1, policySeed: 777UL, epsilon: 0.0);
            Assert.That(teamA, Is.EqualTo(-teamB), "관측 팀 부호로 보상이 뒤집혀야 한다");
        }

        [Test]
        public void Rollout_with_epsilon_is_deterministic()
        {
            var src = FreshPlayState(0);
            var rng = new Rng(11UL);
            var world = Determinizer.Sample(src, 0, ref rng);

            int a = Pimc.Rollout(world.Clone(), 0, 555UL, 0.5);
            int b = Pimc.Rollout(world.Clone(), 0, 555UL, 0.5);
            Assert.That(a, Is.EqualTo(b), "같은 세계+시드+ε → 같은 보상(결정적)");
            Assert.That(a, Is.InRange(-600, 600));
        }

        // ── PimcAgent 다세계 탐색 배선 ─────────────────────────────────────────────────

        [Test]
        public void DecideTurn_returns_a_legal_non_null_move_on_lead()
        {
            var s = FreshPlayState(0);                 // seat0 리드(트릭 없음)
            var ctx = GameFlowHelpers.Context(s, 0);
            var d = new PimcAgent(2024UL, 0, PolicyConfig.Normal).DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False, "리드는 패스 불가 → 수를 내야 한다");
            Assert.That(d.Move, Is.Not.Null);
            // 반환 수는 합법수에 속해야 한다.
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True, "반환 수는 LegalMoves 안에 있어야 한다");
        }

        [Test]
        public void DecideTurn_is_deterministic_for_same_state_and_seed()
        {
            // 다세계(Normal) 탐색도 고정 노드수에서 결정적이어야 한다.
            var s1 = FreshPlayState(0);
            var s2 = FreshPlayState(0);
            var d1 = new PimcAgent(55UL, 0, PolicyConfig.Normal).DecideTurn(GameFlowHelpers.Context(s1, 0));
            var d2 = new PimcAgent(55UL, 0, PolicyConfig.Normal).DecideTurn(GameFlowHelpers.Context(s2, 0));

            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                Assert.That(d1.Move!.Type, Is.EqualTo(d2.Move!.Type));
                Assert.That(d1.Move!.Length, Is.EqualTo(d2.Move!.Length));
            }
        }

        [Test]
        public void Easy_config_returns_legal_move_without_search()
        {
            // Worlds=0 → 탐색 없이 ε-휴리스틱 직접 결정. 합법수여야 한다.
            var s = FreshPlayState(0);
            var ctx = GameFlowHelpers.Context(s, 0);
            var d = new PimcAgent(2024UL, 0, PolicyConfig.For(Difficulty.Easy)).DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False, "리드는 패스 불가");
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True);
        }

        [Test]
        public void NonTurn_decisions_delegate_to_heuristic()
        {
            // 강한 8장 → CallGrandTichu 는 휴리스틱과 동일하게 true.
            var strong = new List<Card>
            {
                Card.Dragon, Card.Phoenix,
                Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Sword),
                Card.Normal(13, Suit.Pagoda), Card.Normal(13, Suit.Star),
                Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Sword)
            };
            var s = GameFlowHelpers.PlayState(0, strong,
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Pagoda) });
            var ctx = GameFlowHelpers.Context(s, 0);

            var pimc = new PimcAgent(1UL, 0, PolicyConfig.Normal).CallGrandTichu(ctx);
            var heur = new AiAgent(1UL, 0).CallGrandTichu(ctx);
            Assert.That(pimc, Is.EqualTo(heur), "비-턴 결정은 휴리스틱에 위임돼야 한다");
        }

        [Test]
        public void Four_PimcAgents_complete_a_round_without_throwing()
        {
            // 다세계(Normal) 탐색이 재귀/폭주 없이 한 라운드를 완주하는지(배선 무결성).
            var cfg = PolicyConfig.Normal;
            var s = FreshPlayState(0);
            var agents = new IAgent[]
            {
                new PimcAgent(900UL, 0, cfg), new PimcAgent(900UL, 1, cfg),
                new PimcAgent(900UL, 2, cfg), new PimcAgent(900UL, 3, cfg)
            };
            var outcome = new GameDriver(agents).RunRound(s);
            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
        }

        // ── anytime (동기 코어) ────────────────────────────────────────────────────

        [Test]
        public void DecideTurnAnytime_with_no_tokens_equals_DecideTurn()
        {
            var s1 = FreshPlayState(0);
            var s2 = FreshPlayState(0);
            var d1 = new PimcAgent(55UL, 0, PolicyConfig.Normal).DecideTurn(GameFlowHelpers.Context(s1, 0));
            var d2 = new PimcAgent(55UL, 0, PolicyConfig.Normal)
                .DecideTurnAnytime(GameFlowHelpers.Context(s2, 0), CancellationToken.None, CancellationToken.None);
            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                Assert.That(d1.Move!.Type, Is.EqualTo(d2.Move!.Type));
            }
        }

        [Test]
        public void DecideTurnAnytime_budget_cancelled_returns_legal_best_so_far()
        {
            // 예산이 미리 취소돼 있어도 ≥1 샘플 완료 후 합법수(best-so-far)를 throw 없이 반환.
            var s = FreshPlayState(0);
            var ctx = GameFlowHelpers.Context(s, 0);
            using var budget = new CancellationTokenSource();
            budget.Cancel(); // 즉시 만료
            var d = new PimcAgent(7UL, 0, new PolicyConfig(8, 2, 0.1))
                .DecideTurnAnytime(ctx, budget.Token, CancellationToken.None);
            Assert.That(d.IsPass, Is.False);
            bool legal = false;
            foreach (var m in ctx.LegalMoves)
                if (m.Rank == d.Move!.Rank && m.Type == d.Move!.Type && m.Length == d.Move!.Length) legal = true;
            Assert.That(legal, Is.True, "예산 만료 시에도 합법 best-so-far");
        }

        [Test]
        public void DecideTurnAnytime_abort_cancelled_throws_OCE()
        {
            var s = FreshPlayState(0);
            var ctx = GameFlowHelpers.Context(s, 0);
            using var abort = new CancellationTokenSource();
            abort.Cancel();
            var agent = new PimcAgent(7UL, 0, new PolicyConfig(8, 2, 0.1));
            Assert.Throws<System.OperationCanceledException>(() =>
                agent.DecideTurnAnytime(ctx, CancellationToken.None, abort.Token));
        }

        [Test]
        public void DecideTurn_endgame_shedding_guard_leads_pair_run()
        {
            // #3: ≤5장(7,8,8,9,9) 리드에서 셰딩 가드 ON → EV(결정화) 전에 MostShedding = 8899 연페어(4장).
            // 가드가 결정화 전 단락하므로 합성 상태(손합≠56)여도 예외 없이 동작.
            var cfg = new PolicyConfig(4, 2, 0.10, useEndgameSheddingGuard: true);
            var hand = new List<Card> {
                Card.Normal(7, Suit.Jade), Card.Normal(8, Suit.Sword), Card.Normal(8, Suit.Pagoda),
                Card.Normal(9, Suit.Star), Card.Normal(9, Suit.Jade) };
            var s = GameFlowHelpers.PlayState(0, hand,
                new List<Card> { Card.Normal(2, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Pagoda) },
                new List<Card> { Card.Normal(2, Suit.Star) });
            var d = new PimcAgent(7UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False);
            Assert.That(d.Move!.Cards.Count, Is.EqualTo(4), "8899 연페어(4장 셰딩) 리드");
        }

        // ── 파트너-Top 가드(휴리스틱 규칙 공유) ───────────────────────────────────────

        private static Combination Pair(int rank) =>
            CombinationRecognizer.Recognize(
                new[] { Card.Normal(rank, Suit.Jade), Card.Normal(rank, Suit.Sword) }, TrickContext.Lead);

        private static GameState PartnerTopFollow(Combination top, params IReadOnlyList<Card>[] hands)
        {
            var s = GameFlowHelpers.PlayState(0, hands);   // seat0 가 행동, 파트너=seat2
            s.CurrentTrick = new Trick
            {
                LeadType = top.Type, LeadLength = top.Length,
                Top = top, TopOwnerSeat = 2, AccumulatedPoints = 0
            };
            return s;
        }

        [Test]
        public void DecideTurn_passes_when_partner_owns_top_and_safe()
        {
            // 파트너(seat2)가 Pair(J) 소유. seat0 는 Pair(Q)로 밟을 수 있으나 티츄 미선언·
            // 나가기 아님·파트너 낮지 않음 → EV 탐색 전에 가드가 패스시킨다(비싼 밟기 낭비 방지).
            var s = PartnerTopFollow(Pair(11),
                new List<Card> { Card.Normal(12, Suit.Jade), Card.Normal(12, Suit.Sword),
                                 Card.Normal(2, Suit.Pagoda), Card.Normal(3, Suit.Star) },
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Star) });
            var d = new PimcAgent(7UL, 0, PolicyConfig.Normal).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.True, "여유로우면 파트너 Top 위로 밟지 않고 패스");
        }

        [Test]
        public void DecideTurn_overtakes_partner_when_tichu_called()
        {
            // seat0 가 티츄 선언 → 나가야 하니 파트너 Top(Pair5) 위로 싸게(8 페어) 밟는다.
            var s = PartnerTopFollow(Pair(5),
                new List<Card> { Card.Normal(8, Suit.Jade), Card.Normal(8, Suit.Sword),
                                 Card.Normal(2, Suit.Pagoda), Card.Normal(3, Suit.Star) },
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Star) });
            s.Seats[0].Call = TichuCall.Tichu;
            var d = new PimcAgent(7UL, 0, PolicyConfig.Normal).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "티츄 선언 → 파트너 위로라도 밟아 나가기 추진");
            Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(d.Move!.Rank, Is.EqualTo(Pair(8).Rank), "싸게(8 페어)로 밟아야 한다");
        }

        // ── 상대-위협 블록 가드(D1) ───────────────────────────────────────────────────
        // 상대가 Top 소유 + 아웃/티츄 위협일 때, 순수 EV 전에 휴리스틱 블록 가드
        // (AiAgent.OpponentThreatBlockMove)를 건다. 플래그 OFF(기본)면 비트불변(가드 미개입).

        private static GameState OpponentTopFollow(Combination top, int owner, params IReadOnlyList<Card>[] hands)
        {
            var s = GameFlowHelpers.PlayState(0, hands);   // seat0 가 행동
            s.CurrentTrick = new Trick
            {
                LeadType = top.Type, LeadLength = top.Length,
                Top = top, TopOwnerSeat = owner, AccumulatedPoints = 0
            };
            return s;
        }

        [Test]
        public void DecideTurn_blocks_opponent_tichu_threat_when_flag_on()
        {
            // 상대(seat1) 티츄 콜 + 싼 single 8 Top. seat0 이기는 수 = single 10 뿐.
            // 플래그 ON → EV 전에 가드가 single 10 으로 저지.
            var s = OpponentTopFollow(SingleOf(8), 1,
                new List<Card> { Card.Normal(10, Suit.Jade), Card.Normal(2, Suit.Sword), Card.Normal(3, Suit.Pagoda) },
                new List<Card> { Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Sword), Card.Normal(7, Suit.Pagoda) },
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(4, Suit.Star), Card.Normal(6, Suit.Star), Card.Normal(8, Suit.Star) });
            s.Seats[1].Call = TichuCall.Tichu;
            var cfg = new PolicyConfig(4, 2, 0.1, useOpponentThreatBlock: true);
            var d = new PimcAgent(7UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "티츄 위협 → 가드가 막는다");
            Assert.That(d.Move!.Rank, Is.EqualTo(SingleOf(10).Rank), "single 10 으로 저지");
        }

        [Test]
        public void DecideTurn_locks_out_near_out_opponent_when_flag_on()
        {
            // 상대(seat1) 낮은 single 4 Top, 다른 상대(seat3) 1장. seat0 이기는 싱글 5·A.
            // 플래그 ON → 가장 높은 싱글(A)로 봉쇄(1장 상대가 받아 나가기 차단).
            var s = OpponentTopFollow(SingleOf(4), 1,
                new List<Card> { Card.Normal(14, Suit.Jade), Card.Normal(5, Suit.Sword), Card.Normal(2, Suit.Pagoda), Card.Normal(3, Suit.Star) },
                new List<Card> { Card.Normal(7, Suit.Jade), Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(6, Suit.Sword) });   // seat3: 1장(아웃 임박)
            var cfg = new PolicyConfig(4, 2, 0.1, useOpponentThreatBlock: true);
            var d = new PimcAgent(7UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "1장 상대 봉쇄 → 패스 금지");
            Assert.That(d.Move!.Rank, Is.EqualTo(SingleOf(14).Rank), "가장 높은 싱글(A)로 봉쇄");
        }

        [Test]
        public void DecideTurn_does_not_apply_guard_when_flag_off()
        {
            // 동일 봉쇄 시나리오, 플래그 OFF(기본) → 가드 미개입. ON(위 두 테스트)은 EV 전에 블록으로
            // 단락하지만, OFF 는 단락하지 않고 EV 경로(Determinizer)로 진입한다. 이 합성 fixture 는
            // 손패 합≠전체 덱이라 결정화가 InvalidOperationException 을 던지는데, 그 예외 자체가
            // "가드로 단락하지 않고 EV 로 진입했다"(=플래그가 가드를 게이트함)는 관측 증거다.
            var s = OpponentTopFollow(SingleOf(4), 1,
                new List<Card> { Card.Normal(14, Suit.Jade), Card.Normal(5, Suit.Sword), Card.Normal(2, Suit.Pagoda), Card.Normal(3, Suit.Star) },
                new List<Card> { Card.Normal(7, Suit.Jade), Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Jade) },
                new List<Card> { Card.Normal(6, Suit.Sword) });
            var cfgOff = new PolicyConfig(4, 2, 0.1);   // useOpponentThreatBlock 기본 false
            Assert.Throws<System.InvalidOperationException>(
                () => new PimcAgent(7UL, 0, cfgOff).DecideTurn(GameFlowHelpers.Context(s, 0)),
                "플래그 OFF → 가드 미개입 → EV 경로 진입(블록으로 단락하지 않음)");
        }

        // ── 콤보 밟기 팀킬 가드(Bug4) ─────────────────────────────────────────────────
        private static Combination ConsecPairs(int lowRank) =>
            CombinationRecognizer.Recognize(new[] {
                Card.Normal(lowRank, Suit.Jade), Card.Normal(lowRank, Suit.Sword),
                Card.Normal(lowRank + 1, Suit.Pagoda), Card.Normal(lowRank + 1, Suit.Star) }, TrickContext.Lead);

        [Test]
        public void DecideTurn_passes_wasteful_combo_overtake_when_flag_on()
        {
            // 상대(seat1) 7788 연페어. seat0 이길 수=991010(10s=점수)이나 팀 아웃용 콤보 → 플래그 ON 이면 EV 전에 패스.
            var s = OpponentTopFollow(ConsecPairs(7), 1,
                new List<Card> { Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword), Card.Normal(10, Suit.Pagoda), Card.Normal(10, Suit.Star), Card.Normal(2, Suit.Jade), Card.Normal(3, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Sword), Card.Normal(3, Suit.Sword), Card.Normal(4, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Pagoda) },
                new List<Card> { Card.Normal(2, Suit.Star), Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star) });
            var cfg = new PolicyConfig(4, 2, 0.1, useComboOvertakeGuard: true);
            var d = new PimcAgent(7UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.True, "팀킬 콤보 밟기 → EV 전에 패스");
        }

        [Test]
        public void DecideTurn_combo_overtake_guard_gated_off_by_default()
        {
            // 동일 상황, 플래그 OFF(기본) → 가드 미개입 → EV 경로 진입(합성 상태라 결정화 예외 = 게이팅 증거).
            var s = OpponentTopFollow(ConsecPairs(7), 1,
                new List<Card> { Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword), Card.Normal(10, Suit.Pagoda), Card.Normal(10, Suit.Star), Card.Normal(2, Suit.Jade), Card.Normal(3, Suit.Jade) },
                new List<Card> { Card.Normal(2, Suit.Sword), Card.Normal(3, Suit.Sword), Card.Normal(4, Suit.Sword) },
                new List<Card> { Card.Normal(2, Suit.Pagoda) },
                new List<Card> { Card.Normal(2, Suit.Star), Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star) });
            var cfgOff = new PolicyConfig(4, 2, 0.1);
            Assert.Throws<System.InvalidOperationException>(
                () => new PimcAgent(7UL, 0, cfgOff).DecideTurn(GameFlowHelpers.Context(s, 0)),
                "OFF → 가드 미개입 → EV 진입(단락 안 함)");
        }

        // ── 인-턴 폭탄 규율(③ 폭탄 타이밍) ───────────────────────────────────────────
        // 폭탄은 게이트된 DecideBomb(리치트릭 ≥15점)이 담당 → 인-턴 EV 후보에서 제외해
        // 싼 트릭에 폭탄을 낭비하지 않는다(휴리스틱 DecideLead/Follow 와 동일 규율).

        private static Combination FourBomb(int rank) =>
            CombinationRecognizer.Recognize(new[] {
                Card.Normal(rank, Suit.Jade), Card.Normal(rank, Suit.Sword),
                Card.Normal(rank, Suit.Pagoda), Card.Normal(rank, Suit.Star) }, TrickContext.Lead);

        private static Combination SingleOf(int rank) =>
            CombinationRecognizer.Recognize(new[] { Card.Normal(rank, Suit.Jade) }, TrickContext.Lead);

        [Test]
        public void TurnCandidates_excludes_bombs_when_nonbomb_exists()
        {
            var bomb = FourBomb(7);
            Assert.That(bomb.IsBomb, Is.True, "four-카드는 폭탄");
            var cands = PimcAgent.TurnCandidates(new List<Combination> { SingleOf(13), bomb });
            Assert.That(cands.Count, Is.EqualTo(1), "비폭탄만 후보");
            Assert.That(cands[0].IsBomb, Is.False);
        }

        [Test]
        public void TurnCandidates_falls_back_to_bombs_when_only_bombs()
        {
            var cands = PimcAgent.TurnCandidates(new List<Combination> { FourBomb(7) });
            Assert.That(cands.Count, Is.EqualTo(1), "비폭탄 없으면 폭탄 폴백");
            Assert.That(cands[0].IsBomb, Is.True);
        }

        // ── reach-prob 가중(Hard 경로) ────────────────────────────────────────────────

        [Test]
        public void ReachProb_weighted_decide_turn_is_deterministic_and_legal()
        {
            // 작은 reach-prob config(worlds=2)로 가중 경로 활성(Hard 16세계는 1결정 ~40s라 회피).
            var cfg = new PolicyConfig(2, 1, 0.1, useReachProb: true);
            var s1 = FreshPlayState(0); var s2 = FreshPlayState(0);
            s1.Seats[1].Call = TichuCall.Tichu; s2.Seats[1].Call = TichuCall.Tichu; // 콜 단서 → 가중 활성
            var ctx1 = GameFlowHelpers.Context(s1, 0);
            var d1 = new PimcAgent(55UL, 0, cfg).DecideTurn(ctx1);
            var d2 = new PimcAgent(55UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s2, 0));

            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                bool legal = false;
                foreach (var m in ctx1.LegalMoves)
                    if (m.Rank == d1.Move!.Rank && m.Type == d1.Move!.Type && m.Length == d1.Move!.Length) legal = true;
                Assert.That(legal, Is.True);
            }
        }

        // ── B1 α-μ 강건 백업(분산 페널티) ─────────────────────────────────────────────
        // 선택 규칙만 argmax(mean) → argmax(mean − λ·std). 전략 융합 탈출. 순수 함수로 격리 검증.
        // (X: EVs {200×3,−150×3} → mean 25·std 175 · Y: EVs {18×6} → mean 18·std 0)

        [Test]
        public void RobustScore_penalizes_variance()
        {
            double xScore = PimcAgent.RobustScore(150.0, 187500.0, 6, 0.2);   // 25 − 0.2·175 = −10
            double yScore = PimcAgent.RobustScore(108.0, 1944.0, 6, 0.2);     // 18 − 0 = 18
            Assert.That(yScore, Is.GreaterThan(xScore), "고분산 X 는 저분산 Y 보다 낮은 강건 점수");
            Assert.That(xScore, Is.EqualTo(-10.0).Within(1e-6));
            Assert.That(yScore, Is.EqualTo(18.0).Within(1e-6));
        }

        [Test]
        public void RobustScore_lambda_zero_equals_mean()
        {
            Assert.That(PimcAgent.RobustScore(150.0, 187500.0, 6, 0.0), Is.EqualTo(25.0).Within(1e-6), "λ=0 → 평균");
        }

        [Test]
        public void RobustScore_zero_count_is_negative_infinity()
        {
            Assert.That(PimcAgent.RobustScore(0.0, 0.0, 0, 0.5), Is.EqualTo(double.NegativeInfinity));
        }

        [Test]
        public void RobustArgmax_prefers_low_variance_candidate()
        {
            var candidates = new List<Combination> { SingleOf(13), SingleOf(5) }; // X=idx0, Y=idx1
            double[] robSum   = { 150.0, 108.0 };
            double[] robSumSq = { 187500.0, 1944.0 };
            int idx = PimcAgent.RobustArgmax(robSum, robSumSq, 6, 0.2, candidates);
            Assert.That(idx, Is.EqualTo(1), "λ=0.2 → 고분산 X(−10) 보다 저분산 Y(+18) 선택");
        }

        [Test]
        public void RobustArgmax_lambda_zero_prefers_higher_mean()
        {
            var candidates = new List<Combination> { SingleOf(13), SingleOf(5) };
            double[] robSum   = { 150.0, 108.0 };    // means 25 vs 18
            double[] robSumSq = { 187500.0, 1944.0 };
            int idx = PimcAgent.RobustArgmax(robSum, robSumSq, 6, 0.0, candidates);
            Assert.That(idx, Is.EqualTo(0), "λ=0 → 평균 최대(X)");
        }

        [Test]
        public void DecideTurn_robust_backup_is_deterministic_and_legal()
        {
            // robust ON(λ=0.25) 이 FreshPlayState(결정화 유효)에서 결정적·합법이어야 한다.
            var cfg = new PolicyConfig(4, 2, 0.1, useRobustBackup: true, robustLambda: 0.25);
            var s1 = FreshPlayState(0); var s2 = FreshPlayState(0);
            var ctx1 = GameFlowHelpers.Context(s1, 0);
            var d1 = new PimcAgent(88UL, 0, cfg).DecideTurn(ctx1);
            var d2 = new PimcAgent(88UL, 0, cfg).DecideTurn(GameFlowHelpers.Context(s2, 0));
            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
            {
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));
                bool legal = false;
                foreach (var m in ctx1.LegalMoves)
                    if (m.Rank == d1.Move!.Rank && m.Type == d1.Move!.Type && m.Length == d1.Move!.Length) legal = true;
                Assert.That(legal, Is.True, "robust 선택 수는 합법이어야 한다");
            }
        }
    }
}
