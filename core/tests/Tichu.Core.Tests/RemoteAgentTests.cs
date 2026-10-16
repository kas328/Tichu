using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// RemoteAgent — 원격 좌석. 액션이 도착할 때까지 블로킹하고, 묻기 전에 온 액션도
    /// 보관하며(교환 동시 제출), 불법 입력은 상태를 바꾸지 않고 거부한 뒤 다시 기다린다.
    /// </summary>
    public class RemoteAgentTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private sealed class Harness
        {
            public readonly List<ServerEvent> Sent = new List<ServerEvent>();
            public readonly List<Awaiting> Awaits = new List<Awaiting>();
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            private long _arrival;
            public Action<Awaiting>? OnAwait;   // 방송 콜백 안에서 끼어들기용

            /// <summary>
            /// anyTichuPending 을 주지 않으면 세션처럼 자기 예약을 확인하도록 잇는다(좌석 하나뿐인 세션).
            /// </summary>
            public RemoteAgent Create(int seat, TimeSpan? timeout = null,
                Action? onTichuReserved = null, Func<bool>? anyTichuPending = null,
                IAgent? standIn = null, Func<bool>? anyConnected = null, int maxTimeouts = 2,
                Func<DateTime?>? phaseStartedUtc = null, Func<DateTime>? utcNow = null)
            {
                RemoteAgent agent = null!;
                agent = new RemoteAgent(
                    seat,
                    (s, ev) => { lock (Sent) Sent.Add(ev); },
                    (s, aw) => { lock (Awaits) Awaits.Add(aw); OnAwait?.Invoke(aw); },
                    () => Interlocked.Increment(ref _arrival),
                    timeout ?? TimeSpan.FromSeconds(5),
                    Cts.Token,
                    onTichuReserved,
                    anyTichuPending ?? (Func<bool>)(() => agent.HasReservedTichu),
                    standIn,
                    anyConnected,
                    null,
                    maxTimeouts,
                    phaseStartedUtc,
                    utcNow);
                return agent;
            }

            public Awaiting LastAwait
            {
                get { lock (Awaits) return Awaits[Awaits.Count - 1]; }
            }

            public bool Rejected(string reason)
            {
                lock (Sent) return Sent.Exists(e => e.Kind == ServerEventKind.ActionRejected && e.Reason == reason);
            }

            public int AwaitCount
            {
                get { lock (Awaits) return Awaits.Count; }
            }

            public int RejectCount
            {
                get { lock (Sent) { int n = 0; foreach (var e in Sent) if (e.Kind == ServerEventKind.ActionRejected) n++; return n; } }
            }
        }

        /// <summary>대리 AI 대역 — 불린 횟수만 센다. 결과의 합법성은 RemoteAgent 의 관심사가 아니다(드라이버가 검증).</summary>
        private sealed class StubAgent : IAgent
        {
            public int Turns, Exchanges, Grands, Dragons;
            public bool CallGrandTichu(in DecisionContext ctx) { Grands++; return true; }
            public ExchangeChoice ChooseExchange(in DecisionContext ctx)
            {
                Exchanges++;
                var h = ctx.State.Seats[ctx.Seat].Hand;
                return new ExchangeChoice(h[0], h[1], h[2]);
            }
            public bool CallTichu(in DecisionContext ctx) => true;
            public TurnDecision DecideTurn(in DecisionContext ctx) { Turns++; return TurnDecision.Pass; }
            public Tichu.Core.Combinations.Combination? DecideBomb(in DecisionContext ctx) => null;
            public int ChooseDragonRecipient(in DecisionContext ctx) { Dragons++; return (ctx.Seat + 1) % 4; }
        }

        private static ClientAction WithAsk(ClientAction a, long askId) { a.AskId = askId; return a; }

        private static ClientAction PlayFive(long seq, long askId = 0)
        {
            var a = ClientAction.Play(0, seq, new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null);
            a.AskId = askId;
            return a;
        }

        private static void WaitUntil(Func<bool> cond, int ms = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!cond())
            {
                if (sw.ElapsedMilliseconds > ms) Assert.Fail("조건이 성립하지 않았다");
                Thread.Sleep(5);
            }
        }

        private static DecisionContext LeadContext(out GameState state)
        {
            state = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));
            return GameFlowHelpers.Context(state, 0);
        }

        [Test]
        public void Action_buffered_before_the_question_is_consumed_without_blocking()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var s = GameEngine.NewRound(42UL);

            // 아직 아무것도 묻지 않은 상태에서 큰 티츄 패스가 먼저 도착한다.
            agent.Submit(ClientAction.DeclineGrandTichu(0, 1L));

            var ctx = new DecisionContext(s, 0);
            Assert.That(agent.CallGrandTichu(ctx), Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(0), "미리 온 액션을 거부해선 안 된다");
        }

        [Test]
        public void Wrong_seat_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.DeclineGrandTichu(seat: 2, actionSeq: 1L));

            Assert.That(h.RejectCount, Is.EqualTo(1));
        }

        [Test]
        public void Duplicate_or_backward_action_seq_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.CallTichu(0, 5L));
            agent.Submit(ClientAction.CallTichu(0, 5L)); // 중복
            agent.Submit(ClientAction.CallTichu(0, 3L)); // 역행

            Assert.That(h.RejectCount, Is.EqualTo(2));
        }

        [Test]
        public void Reserved_tichu_is_taken_once()
        {
            var h = new Harness();
            var agent = h.Create(0);

            agent.Submit(ClientAction.CallTichu(0, 1L));

            Assert.That(agent.TakeReservedTichu(), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.False, "예약은 한 번만 소비된다");
        }

        [Test]
        public void DecideTurn_returns_the_submitted_legal_move()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False);
            Assert.That(d.Move, Is.Not.Null);
            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Illegal_move_is_rejected_and_the_agent_keeps_waiting()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            // 손에 없는 카드를 먼저 보내고, 그 다음 합법수를 보낸다.
            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(13, Suit.Star) }, rank: 0, wish: null));
            agent.Submit(ClientAction.Play(0, 2L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(1), "불법 입력 1건만 거부돼야 한다");
        }

        [Test]
        public void Illegal_pass_on_lead_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Pass(0, 1L));                      // 리드에서는 패스 불가
            agent.Submit(ClientAction.Play(0, 2L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));

            var d = agent.DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(1));
        }

        [Test]
        public void Awaiting_is_announced_with_legal_moves_before_blocking()
        {
            var h = new Harness();
            var agent = h.Create(0);
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));
            agent.DecideTurn(ctx);

            Assert.That(h.Awaits.Count, Is.GreaterThanOrEqualTo(1));
            var aw = h.Awaits[0];
            Assert.That(aw.Kind, Is.EqualTo(StepKind.Play));
            Assert.That(aw.Seat, Is.EqualTo(0));
            Assert.That(aw.LegalMoves, Is.Not.Null.And.Count.GreaterThan(0));
            Assert.That(aw.Redacted().LegalMoves, Is.Null, "축약판엔 합법수가 없어야 한다");
        }

        [Test]
        public void Redacted_view_keeps_the_ask_id_and_seconds_left_but_not_the_moves()
        {
            // 남의 차례 남은 초를 다른 좌석도 보게 한다(M5.1). 합법수는 은닉 정보라 계속 뺀다.
            var aw = new Awaiting
            {
                Kind = StepKind.Play, Seat = 1, AskId = 7, SecondsLeft = 12, CanPass = true,
                LegalMoves = new List<Tichu.Core.Combinations.Combination>(),
            };

            var r = aw.Redacted();

            Assert.That(r.Kind, Is.EqualTo(StepKind.Play));
            Assert.That(r.Seat, Is.EqualTo(1));
            Assert.That(r.AskId, Is.EqualTo(7));
            Assert.That(r.SecondsLeft, Is.EqualTo(12));
            Assert.That(r.LegalMoves, Is.Null);
            Assert.That(r.CanPass, Is.False);
        }

        [Test]
        public void Blocking_wait_is_woken_by_a_later_submit()
        {
            // 이 테스트만이 _matched.Enqueue + Release → Wait + Dequeue 경로를 탄다.
            // 나머지 테스트는 모두 블로킹 호출 전에 Submit 하므로 Await 의 선행 버퍼에서 끝난다.
            const int submitDelayMs = 150;
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(10));
            var ctx = LeadContext(out _);

            var submitter = new Thread(() =>
            {
                Thread.Sleep(submitDelayMs);
                agent.Submit(ClientAction.Play(0, 1L,
                    new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));
            }) { IsBackground = true };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            submitter.Start();
            var d = agent.DecideTurn(ctx);   // 실제로 블로킹된다
            sw.Stop();

            Assert.That(submitter.Join(TimeSpan.FromSeconds(5)), Is.True, "제출 스레드가 끝나지 않았다");

            // 경로 식별: 선행 버퍼였다면 밀리초 단위로 즉시 돌아온다.
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(80),
                "즉시 반환됐다 — 선행 버퍼 경로를 탄 것이므로 교차 스레드 깨우기를 검증하지 못했다");

            Assert.That(d.IsPass, Is.False);
            Assert.That(d.Move, Is.Not.Null);
            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(0));
            Assert.That(h.Awaits.Count, Is.EqualTo(1), "대기 공지는 한 번이어야 한다");
        }

        [Test]
        public void Timeout_throws()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(50));
            var ctx = LeadContext(out _);

            Assert.Throws<TimeoutException>(() => agent.DecideTurn(ctx));
        }

        [Test]
        public void Cancellation_unblocks_the_wait()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(30));
            var ctx = LeadContext(out _);

            var t = new Thread(() =>
            {
                Thread.Sleep(50);
                h.Cts.Cancel();
            }) { IsBackground = true };
            t.Start();

            Assert.Throws<OperationCanceledException>(() => agent.DecideTurn(ctx));
        }

        [Test]
        public void Buffered_wrong_kind_action_does_not_answer_a_question()
        {
            // 질문 전에 도착한 Pass 는 선행 큐에 들어간다. 큰 티츄 질문의 답이 되어선 안 된다.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(300));

            agent.Submit(ClientAction.Pass(0, 1L));

            var ctx = new DecisionContext(GameEngine.NewRound(42UL), 0);
            Assert.Throws<TimeoutException>(() => agent.CallGrandTichu(ctx),
                "종류가 다른 버퍼 액션이 큰 티츄 결정에 쓰였다");
        }

        [Test]
        public void Leftover_matched_action_does_not_answer_a_different_question()
        {
            // ⚠️ 검출력 미입증: 수정 전 코드에서 10회 돌려 0회 실패했다. 두 번째 Submit 이
            // _awaiting 이 지워지기 전에 도착해야 _matched 에 잔여물이 생기는데, 깨어난 주
            // 스레드가 _gate 를 먼저 잡는 쪽으로 거의 항상 기울어 Pass 가 선행 큐로 간다.
            // 그래서 이 테스트는 '지나가다 걸릴지도 모르는 덫' 이고, Critical 의 근거가 아니다.
            // 실제 근거는 구조다 — Await 가 대기 전에 _matched 를 비우므로 대기 후 Dequeue 는
            // 매칭 종류만 볼 수 있다. 같은 부류의 결정적 검증은
            // Buffered_wrong_kind_action_does_not_answer_a_question 이 담당한다.
            //
            // 한 질문에 매칭 액션 2건이 도착해 첫 건이 수락되면 두 번째가 _matched 에 남는다.
            // 그 잔여물이 다음 질문(다른 StepKind)의 답으로 쓰이면 서버가 플레이어가 하지 않은
            // 결정을 조작한다 — 큰 티츄 자동 패스, 용 점수 무단 헌납.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(400));
            var ctx = LeadContext(out _);

            var submitter = new Thread(() =>
            {
                Thread.Sleep(120);                  // DecideTurn 이 대기에 들어간 뒤
                agent.Submit(ClientAction.Play(0, 1L,
                    new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));
                agent.Submit(ClientAction.Pass(0, 2L));   // 같은 질문에 온 두 번째 액션
            }) { IsBackground = true };
            submitter.Start();

            var d = agent.DecideTurn(ctx);
            Assert.That(submitter.Join(TimeSpan.FromSeconds(5)), Is.True, "제출 스레드가 끝나지 않았다");
            Assert.That(d.IsPass, Is.False, "첫 번째 액션이 답이어야 한다");

            // 다음 질문은 종류가 다르다. 잔여 Pass 가 답이 되면 큰 티츄가 조작된다.
            var grandCtx = new DecisionContext(GameEngine.NewRound(42UL), 0);
            Assert.Throws<TimeoutException>(() => agent.CallGrandTichu(grandCtx),
                "잔여 매칭 액션이 다른 종류의 질문에 답으로 쓰였다");
        }

        /// <summary>
        /// 다음 대기 공지(onAwait)를 본 뒤에 액션을 제출하는 스레드. 고정 지연 대신 실제로
        /// 대기에 들어선 것을 보고 보낸다. 공지와 _awaiting 설정 사이의 틈은 Play 대기라면
        /// 대기 직후의 예약 확인이 메우고, 다른 질문은 어차피 깨우지 않으므로 결과가 같다.
        /// 블로킹 호출 **전에** 만들어야 한다(그 시점의 공지 수를 기준으로 삼는다).
        /// </summary>
        private static Thread SubmitOnAwait(Harness h, RemoteAgent agent, ClientAction a)
        {
            int baseline = h.AwaitCount;
            var t = new Thread(() =>
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (h.AwaitCount <= baseline && DateTime.UtcNow < deadline) Thread.Sleep(1);
                agent.Submit(a);
            }) { IsBackground = true };
            t.Start();
            return t;
        }

        [Test]
        public void CallTichu_while_awaiting_the_turn_wakes_DecideTurn_with_Yield()
        {
            // 자기 차례 프롬프트가 떠 있는 동안 외친 작은 티츄는 그 사람이 수를 낼 때까지
            // 기다리면 안 된다. 대기를 깨워 Yield 를 돌려주면 드라이버가 예약을 먼저 적용한다.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(10));
            var ctx = LeadContext(out _);

            var submitter = SubmitOnAwait(h, agent, ClientAction.CallTichu(0, 1L));
            var d = agent.DecideTurn(ctx);   // 블로킹 → CallTichu 로 깨어난다

            Assert.That(submitter.Join(TimeSpan.FromSeconds(5)), Is.True, "제출 스레드가 끝나지 않았다");
            Assert.That(d.IsYield, Is.True, "CallTichu 가 차례 대기를 깨워 Yield 로 돌아와야 한다");
            Assert.That(d.IsPass, Is.False);
            Assert.That(d.Move, Is.Null);
            Assert.That(agent.TakeReservedTichu(), Is.True, "예약은 그대로 남아 있어야 한다");
            Assert.That(agent.TakeReservedTichu(), Is.False, "예약은 한 번만 소비된다");
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void After_a_Yield_the_turn_is_answered_by_the_next_move()
        {
            // Yield 로 깨운 표식이 다음 질문의 답으로 남거나 거부 통보로 새어 나가면 안 된다.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(10));
            var ctx = LeadContext(out _);

            var t1 = SubmitOnAwait(h, agent, ClientAction.CallTichu(0, 1L));
            Assert.That(agent.DecideTurn(ctx).IsYield, Is.True, "테스트 전제: 첫 대기는 Yield");
            Assert.That(t1.Join(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.True);

            var t2 = SubmitOnAwait(h, agent, ClientAction.Play(0, 2L,
                new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null));
            var d = agent.DecideTurn(ctx);
            Assert.That(t2.Join(TimeSpan.FromSeconds(5)), Is.True);

            Assert.That(d.IsYield, Is.False);
            Assert.That(d.Move!.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Second_CallTichu_in_a_round_is_ignored()
        {
            // 라운드당 예약은 한 번. 받아 주면 연타 한 번마다 대기를 깨우거나 드라이버 0단계를
            // 한 스텝씩 돌려(MaxSteps) 매치를 죽일 수 있다. 새 라운드에는 다시 받는다.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(400));
            var ctx = LeadContext(out _);

            var t1 = SubmitOnAwait(h, agent, ClientAction.CallTichu(0, 1L));
            Assert.That(agent.DecideTurn(ctx).IsYield, Is.True, "테스트 전제: 첫 CallTichu 는 깨운다");
            Assert.That(t1.Join(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.True);

            var t2 = SubmitOnAwait(h, agent, ClientAction.CallTichu(0, 2L));
            Assert.Throws<TimeoutException>(() => agent.DecideTurn(ctx), "두 번째 CallTichu 가 다시 깨웠다");
            Assert.That(t2.Join(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.False, "같은 라운드의 두 번째 CallTichu 는 예약되지 않아야 한다");
            Assert.That(h.RejectCount, Is.EqualTo(0));

            agent.ResetForNewRound();
            agent.Submit(ClientAction.CallTichu(0, 3L));
            Assert.That(agent.TakeReservedTichu(), Is.True, "새 라운드에는 다시 예약을 받아야 한다");
        }

        [Test]
        public void Reservation_callback_fires_once_per_round()
        {
            var h = new Harness();
            int fired = 0;
            RemoteAgent agent = null!;
            agent = h.Create(0, onTichuReserved: () =>
            {
                fired++;
                // 대기 좌석이 깨어나 0단계가 돌 때 예약이 보여야 한다 — 예약을 세운 뒤에 알린다.
                Assert.That(agent.HasReservedTichu, Is.True, "콜백 시점엔 예약이 이미 서 있어야 한다");
            });

            agent.Submit(ClientAction.CallTichu(0, 1L));
            agent.Submit(ClientAction.CallTichu(0, 2L));
            Assert.That(fired, Is.EqualTo(1), "같은 라운드에서는 한 번만 알린다");

            agent.ResetForNewRound();
            agent.Submit(ClientAction.CallTichu(0, 3L));
            Assert.That(fired, Is.EqualTo(2), "새 라운드에는 다시 알린다");
        }

        [Test]
        public void CallTichu_from_another_seat_wakes_the_seat_awaiting_its_turn()
        {
            // 세션 배선의 축소판: B 의 예약 콜백이 A 를 깨우고, A 는 대기 직후 두 좌석의 예약을 본다.
            var h = new Harness();
            RemoteAgent a = null!;
            RemoteAgent b = null!;
            Func<bool> anyPending = () => a.HasReservedTichu || b.HasReservedTichu;
            a = h.Create(0, TimeSpan.FromSeconds(10), () => a.WakeTurnForTichu(), anyPending);
            b = h.Create(1, TimeSpan.FromSeconds(10), () => a.WakeTurnForTichu(), anyPending);
            var ctx = LeadContext(out _);

            var t = SubmitOnAwait(h, b, ClientAction.CallTichu(1, 1L));
            var d = a.DecideTurn(ctx);
            Assert.That(t.Join(TimeSpan.FromSeconds(5)), Is.True);

            Assert.That(d.IsYield, Is.True, "다른 좌석의 콜이 차례 대기를 깨워야 한다");
            Assert.That(b.TakeReservedTichu(), Is.True, "예약은 콜한 좌석에 있어야 한다");
            Assert.That(b.TakeReservedTichu(), Is.False, "예약은 한 번만 소비된다");
            Assert.That(a.TakeReservedTichu(), Is.False, "대기 좌석에 예약이 생기면 안 된다");
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Tichu_reserved_before_the_turn_wait_begins_still_yields()
        {
            // 0단계가 지난 뒤, _awaiting 이 서기 전에 도착한 콜: 제출 쪽 깨우기는 대기가 아니라
            // 무동작이지만, 대기 직후의 예약 확인이 잡아 Yield 해야 한다(수를 낼 때까지 묶이지 않게).
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(10));
            var ctx = LeadContext(out _);

            agent.Submit(ClientAction.CallTichu(0, 1L));   // 아직 아무것도 묻지 않았다 → 예약만
            var d = agent.DecideTurn(ctx);

            Assert.That(d.IsYield, Is.True, "대기 시작 시 남아 있는 예약이 대기를 깨워야 한다");
            Assert.That(agent.TakeReservedTichu(), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.False);
        }

        [Test]
        public void CallTichu_while_awaiting_grand_tichu_only_reserves()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(400));
            var ctx = new DecisionContext(GameEngine.NewRound(42UL), 0);

            var t = SubmitOnAwait(h, agent, ClientAction.CallTichu(0, 1L));
            Assert.Throws<TimeoutException>(() => agent.CallGrandTichu(ctx), "큰 티츄 대기가 CallTichu 로 깨어났다");
            Assert.That(t.Join(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.True, "예약은 받아야 한다");
            Assert.That(agent.TakeReservedTichu(), Is.False);
        }

        [Test]
        public void CallTichu_while_awaiting_exchange_only_reserves()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(400));
            var ctx = new DecisionContext(GameEngine.NewRound(42UL), 0);

            var t = SubmitOnAwait(h, agent, ClientAction.CallTichu(0, 1L));
            Assert.Throws<TimeoutException>(() => agent.ChooseExchange(ctx), "교환 대기가 CallTichu 로 깨어났다");
            Assert.That(t.Join(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(agent.TakeReservedTichu(), Is.True, "예약은 받아야 한다");
            Assert.That(agent.TakeReservedTichu(), Is.False);
        }

        [Test]
        public void ResetForNewRound_discards_early_buffered_action_and_rejects_it()
        {
            // 라운드 경계 폐기 — 아직 아무것도 묻지 않은 시점에 도착한 액션은 선행 큐에
            // 보관된다. ResetForNewRound 가 그걸 비우면, 다음 라운드에 같은 종류를 다시
            // 물어도 아무도 답하지 않은 것이므로 타임아웃해야 한다(조용히 답으로 쓰이면 안
            // 된다). 그리고 폐기된 액션은 발신자에게 거부 통보가 가야 한다.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(100));

            agent.Submit(ClientAction.DeclineGrandTichu(0, 1L));   // 선행 큐에 들어간다

            agent.ResetForNewRound();

            var ctx = new DecisionContext(GameEngine.NewRound(42UL), 0);
            Assert.Throws<TimeoutException>(() => agent.CallGrandTichu(ctx),
                "라운드 경계에서 폐기됐어야 할 잔여 액션이 여전히 답으로 쓰였다");

            Assert.That(h.RejectCount, Is.EqualTo(1), "폐기된 잔여 액션에 거부 통보가 가야 한다");
        }

        [Test]
        public void Timeout_lets_the_stand_in_play_that_turn_only()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: stub);

            var d = agent.DecideTurn(LeadContext(out _));

            Assert.That(d.IsPass, Is.True, "대리 AI 의 결정이 돌아와야 한다");
            Assert.That(stub.Turns, Is.EqualTo(1));
            Assert.That(agent.IsAway, Is.False, "한 번 초과는 자리 비움이 아니다");
        }

        [Test]
        public void Two_timeouts_make_the_seat_away_and_the_next_question_skips_the_wait()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(300), standIn: stub);
            var ctx = LeadContext(out _);

            agent.DecideTurn(ctx);
            agent.DecideTurn(ctx);
            Assert.That(agent.IsAway, Is.True);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            agent.DecideTurn(ctx);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(250), "자리 비움이면 기다리지 않는다");
            Assert.That(stub.Turns, Is.EqualTo(3));
            Assert.That(h.LastAwait.SecondsLeft, Is.Not.Null, "마지막으로 사람에게 물은 질문엔 타이머가 있었다");
        }

        [Test]
        public void Any_action_brings_an_away_seat_back()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: stub);
            var ctx = LeadContext(out _);
            agent.DecideTurn(ctx);
            agent.DecideTurn(ctx);
            Assert.That(agent.IsAway, Is.True);

            agent.Submit(ClientAction.Resume(0, 1));
            Assert.That(agent.IsAway, Is.False);
            agent.Submit(PlayFive(2));                    // 차례 밖 → 차례가 오면 사람의 답

            var d = agent.DecideTurn(ctx);
            Assert.That(d.IsPass, Is.False);
            Assert.That(stub.Turns, Is.EqualTo(2), "복귀 뒤엔 대리 AI 를 부르지 않는다");
        }

        [Test]
        public void Answer_carrying_the_current_ask_id_is_accepted()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(5), standIn: new StubAgent());
            var ctx = LeadContext(out _);
            TurnDecision? got = null;
            var t = new Thread(() => got = agent.DecideTurn(ctx));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            var aw = h.LastAwait;
            Assert.That(aw.AskId, Is.GreaterThan(0));
            Assert.That(aw.SecondsLeft, Is.EqualTo(5));
            agent.Submit(PlayFive(1, aw.AskId));
            t.Join(3000);

            Assert.That(got!.Value.IsPass, Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Late_answer_to_a_timed_out_question_is_rejected()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: new StubAgent());
            agent.DecideTurn(LeadContext(out _));          // 사람이 못 답해 대리 AI 가 둠
            long oldAsk = h.LastAwait.AskId;

            agent.Submit(PlayFive(1, oldAsk));             // 늦게 도착한 답

            Assert.That(h.Rejected("stale answer"), Is.True);
            Assert.That(agent.PeekReservedBomb(), Is.Null, "늦은 답이 차례 밖 큐에 남으면 다음 차례에 쓰인다");
        }

        [Test]
        public void Disconnect_while_waiting_hands_the_turn_to_the_stand_in_at_once()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromSeconds(10), standIn: stub, anyConnected: () => true);
            var ctx = LeadContext(out _);
            TurnDecision? got = null;
            var t = new Thread(() => got = agent.DecideTurn(ctx));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            agent.SetConnected(false);
            Assert.That(t.Join(2000), Is.True);

            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1500));
            Assert.That(stub.Turns, Is.EqualTo(1));
            Assert.That(agent.IsAway, Is.True);
        }

        [Test]
        public void With_no_one_connected_the_seat_waits_until_it_returns()
        {
            var h = new Harness();
            var stub = new StubAgent();
            bool anyone = false;
            // 복귀 뒤 사람에게 다시 묻는 질문이 시간 초과되지 않도록 넉넉히(멈춤 대기 자체는 무기한).
            var agent = h.Create(0, TimeSpan.FromSeconds(2), standIn: stub, anyConnected: () => anyone);
            agent.SetConnected(false);
            var ctx = LeadContext(out _);
            TurnDecision? got = null;
            var t = new Thread(() => got = agent.DecideTurn(ctx));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            Assert.That(h.LastAwait.SecondsLeft, Is.Null, "멈춤 중엔 타이머가 없다");
            Assert.That(t.Join(400), Is.False, "아무도 없으면 대리 AI 로 넘기지 않고 기다린다");

            anyone = true;
            agent.SetConnected(true);                      // 재접속 → 사람에게 다시 묻는다
            WaitUntil(() => h.AwaitCount == 2);
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);

            Assert.That(got!.Value.IsPass, Is.False, "돌아온 사람이 둔다");
            Assert.That(stub.Turns, Is.EqualTo(0));
        }

        [Test]
        public void Grand_tichu_on_timeout_is_declined_and_a_late_answer_is_rejected()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80), standIn: stub);
            var s = GameEngine.NewRound(777UL);

            bool called = agent.CallGrandTichu(GameFlowHelpers.Context(s, 0));

            Assert.That(called, Is.False, "대리 AI 는 큰 티츄를 대신 부르지 않는다");
            Assert.That(stub.Grands, Is.EqualTo(0));
            agent.Submit(ClientAction.CallGrandTichu(0, 1));
            Assert.That(h.Rejected("already decided this round"), Is.True);
        }

        // 큰 티츄·교환은 단계 시작부터 전원이 같은 마감을 공유한다 — 늦게 물린 좌석은 남은 시간만 받는다.
        // 시계는 고정: 단계 시작은 "지금"보다 pastSeconds 전.
        private static readonly DateTime FixedNow = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

        private RemoteAgent SharedWindowAgent(Harness h, double timeoutSeconds, double pastSeconds, StubAgent stub) =>
            h.Create(0, TimeSpan.FromSeconds(timeoutSeconds), standIn: stub,
                phaseStartedUtc: () => FixedNow.AddMilliseconds(-pastSeconds * 1000),
                utcNow: () => FixedNow);

        [Test]
        public void Grand_tichu_shares_the_phase_deadline_so_a_late_asked_seat_gets_only_the_remainder()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = SharedWindowAgent(h, 1.0, 0.8, stub);   // 남은 시간 0.2초
            var s = GameEngine.NewRound(779UL);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool called = agent.CallGrandTichu(GameFlowHelpers.Context(s, 0));

            Assert.That(called, Is.False, "시간 초과 = 거절");
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(600), "전체 1초가 아니라 남은 0.2초만 기다린다");
            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(1), "ceil(0.2) = 1");
        }

        [Test]
        public void Grand_tichu_asked_after_the_shared_deadline_does_not_wait_at_all()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = SharedWindowAgent(h, 5.0, 6.0, stub);   // 이미 1초 지났다
            var s = GameEngine.NewRound(780UL);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool called = agent.CallGrandTichu(GameFlowHelpers.Context(s, 0));

            Assert.That(called, Is.False);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(500), "마감이 지났으면 기다리지 않는다");
            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(0));
        }

        [Test]
        public void Early_buffered_grand_answer_is_still_used_when_the_shared_deadline_has_passed()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = SharedWindowAgent(h, 5.0, 6.0, stub);
            var s = GameEngine.NewRound(781UL);

            agent.Submit(ClientAction.CallGrandTichu(0, 1));   // 마감 전에 미리 낸 답

            Assert.That(agent.CallGrandTichu(GameFlowHelpers.Context(s, 0)), Is.True, "버퍼의 답을 먼저 쓴다");
            Assert.That(agent.IsAway, Is.False, "답이 있었으니 시간 초과가 아니다");
        }

        [Test]
        public void Seat_returning_from_a_pause_gets_only_the_rest_of_the_shared_window()
        {
            // 멈춤에서 돌아와도 시간을 되돌리지 않는다 — 단계 공유 마감의 남은 시간만 받는다(M5.1).
            var h = new Harness();
            var stub = new StubAgent();
            bool anyone = false;
            var agent = h.Create(0, TimeSpan.FromSeconds(5), standIn: stub, anyConnected: () => anyone,
                phaseStartedUtc: () => FixedNow.AddSeconds(-2), utcNow: () => FixedNow);
            agent.SetConnected(false);
            var s = GameEngine.NewRound(782UL);
            bool? called = null;
            var t = new Thread(() => called = agent.CallGrandTichu(GameFlowHelpers.Context(s, 0)));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            anyone = true;
            agent.SetConnected(true);                       // 돌아왔다
            WaitUntil(() => h.AwaitCount == 2);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(3), "단계 시작부터 흐른 2초를 뺀 남은 시간");
            agent.Submit(WithAsk(ClientAction.CallGrandTichu(0, 1), h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);

            Assert.That(called, Is.True, "사람의 답이 적용된다");
            Assert.That(stub.Grands, Is.EqualTo(0));
            Assert.That(agent.IsAway, Is.False);
        }

        [Test]
        public void Play_question_keeps_the_full_timeout_even_with_a_phase_start()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = SharedWindowAgent(h, 5.0, 4.0, stub);
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _)));
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(5), "차례는 묻는 때부터 전체 시간");
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            t.Join(3000);
        }

        // ── M5.1: 한 결정에 마감 하나 ─────────────────────────────────────────

        [Test]
        public void Reconnect_during_the_turn_keeps_the_remaining_seconds()
        {
            var h = new Harness();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), utcNow: () => now);
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _))) { IsBackground = true };
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(20));
            Assert.That(t.Join(200), Is.False, "차례를 기다리는 중");

            now = FixedNow.AddSeconds(8);
            agent.SetConnected(true);                       // 끊김을 못 본 채 새 연결이 붙었다 → 다시 묻는다
            WaitUntil(() => h.AwaitCount == 2);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(12), "처음 물은 때부터 흐른 시간만큼 줄어 있다");
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);
        }

        [Test]
        public void A_tichu_yield_does_not_restart_the_turn_clock()
        {
            var h = new Harness();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), utcNow: () => now);
            var ctx = LeadContext(out _);
            TurnDecision first = default;
            var t1 = new Thread(() => first = agent.DecideTurn(ctx)) { IsBackground = true };
            t1.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(t1.Join(200), Is.False, "차례를 기다리는 중");

            now = FixedNow.AddSeconds(5);
            agent.Submit(ClientAction.CallTichu(0, 1L));
            Assert.That(t1.Join(2000), Is.True);
            Assert.That(first.IsYield, Is.True, "테스트 전제: 콜이 차례 대기를 깨운다");
            agent.TakeReservedTichu();

            var t2 = new Thread(() => agent.DecideTurn(ctx)) { IsBackground = true };   // 드라이버가 같은 좌석에 다시 묻는다
            t2.Start();
            WaitUntil(() => h.AwaitCount == 2);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(15));
            agent.Submit(PlayFive(2, h.LastAwait.AskId));
            Assert.That(t2.Join(2000), Is.True);
        }

        [Test]
        public void A_rejected_move_does_not_restart_the_turn_clock()
        {
            var h = new Harness();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), utcNow: () => now);
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _))) { IsBackground = true };
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(t.Join(200), Is.False, "차례를 기다리는 중");

            now = FixedNow.AddSeconds(7);
            agent.Submit(WithAsk(ClientAction.Play(0, 1L,
                new List<Card> { Card.Normal(13, Suit.Star) }, rank: 0, wish: null), h.LastAwait.AskId));   // 손에 없는 카드
            WaitUntil(() => h.AwaitCount == 2);

            Assert.That(h.Rejected("illegal or ambiguous move"), Is.True);
            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(13));
            agent.Submit(PlayFive(2, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);
        }

        [Test]
        public void Returning_after_the_deadline_hands_the_turn_to_the_stand_in_at_once()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), standIn: stub, maxTimeouts: 1, utcNow: () => now);
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _))) { IsBackground = true };
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(t.Join(200), Is.False, "차례를 기다리는 중");

            now = FixedNow.AddSeconds(25);
            agent.SetConnected(true);                       // 마감이 지난 뒤 다시 붙었다
            Assert.That(t.Join(2000), Is.True, "마감이 지났으니 기다리지 않는다");
            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(0));
            Assert.That(stub.Turns, Is.EqualTo(1), "대리 AI 가 둔다");
            Assert.That(agent.IsAway, Is.True, "시간 초과 1회 = 자리 비움(maxTimeouts 1)");

            // 복귀 뒤 다음 차례는 새 결정 — 지난 마감이 남아 있으면 0초로 시작해 다시 자리 비움이 된다.
            agent.SetConnected(true);
            var t2 = new Thread(() => agent.DecideTurn(LeadContext(out _))) { IsBackground = true };
            t2.Start();
            WaitUntil(() => h.AwaitCount == 3);
            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(20), "결정이 끝나면 마감을 지운다");
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t2.Join(2000), Is.True);
        }

        [Test]
        public void The_turn_clock_keeps_running_while_the_room_is_paused()
        {
            var h = new Harness();
            var stub = new StubAgent();
            bool anyone = true;
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), standIn: stub, anyConnected: () => anyone, utcNow: () => now);
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _))) { IsBackground = true };
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(t.Join(200), Is.False, "차례를 기다리는 중");

            now = FixedNow.AddSeconds(6);
            anyone = false;
            agent.SetConnected(false);                      // 유일한 사람이 끊겼다 → 멈춤
            WaitUntil(() => h.AwaitCount == 2);
            Assert.That(h.LastAwait.SecondsLeft, Is.Null, "멈춤 중엔 타이머가 없다");
            Assert.That(t.Join(200), Is.False, "아무도 없으면 기다린다");

            now = FixedNow.AddSeconds(10);
            anyone = true;
            agent.SetConnected(true);
            WaitUntil(() => h.AwaitCount == 3);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(10), "멈춘 동안에도 흘렀다 — 20초를 새로 주지 않는다");
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);
            Assert.That(stub.Turns, Is.EqualTo(0));
        }

        [Test]
        public void A_question_first_asked_while_paused_starts_the_clock_on_return()
        {
            // 회귀 가드: 사람에게 물은 적이 없으면 시계도 시작되지 않았다 — 돌아와 처음 물을 때 전체 시간.
            var h = new Harness();
            var stub = new StubAgent();
            bool anyone = false;
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), standIn: stub, anyConnected: () => anyone, utcNow: () => now);
            agent.SetConnected(false);
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _))) { IsBackground = true };
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(h.LastAwait.SecondsLeft, Is.Null);
            Assert.That(t.Join(200), Is.False);

            now = FixedNow.AddSeconds(30);
            anyone = true;
            agent.SetConnected(true);
            WaitUntil(() => h.AwaitCount == 2);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(20), "사람에게 처음 묻는 순간 시계가 시작된다");
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);
        }

        [Test]
        public void The_next_turn_gets_the_full_time_again()
        {
            // 회귀 가드: 결정이 끝나면 마감을 지운다.
            var h = new Harness();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), utcNow: () => now);
            var ctx = LeadContext(out _);

            agent.Submit(PlayFive(1));                      // 묻기 전에 낸 수 — 첫 차례를 바로 끝낸다
            agent.DecideTurn(ctx);
            now = FixedNow.AddSeconds(8);
            agent.Submit(PlayFive(2));
            agent.DecideTurn(ctx);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(20));
        }

        [Test]
        public void A_new_round_drops_a_pending_turn_deadline()
        {
            // Yield 로 마감이 남은 채 라운드가 끝나도 다음 라운드 차례는 전체 시간이다.
            var h = new Harness();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), utcNow: () => now);
            var ctx = LeadContext(out _);
            TurnDecision first = default;
            var t1 = new Thread(() => first = agent.DecideTurn(ctx)) { IsBackground = true };
            t1.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(t1.Join(200), Is.False);
            agent.Submit(ClientAction.CallTichu(0, 1L));
            Assert.That(t1.Join(2000), Is.True);
            Assert.That(first.IsYield, Is.True, "테스트 전제: 마감이 남은 채 결정이 멈췄다");

            agent.ResetForNewRound();
            now = FixedNow.AddSeconds(8);
            agent.Submit(PlayFive(2));
            agent.DecideTurn(ctx);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(20));
        }

        [Test]
        public void A_rejected_dragon_recipient_does_not_restart_the_clock()
        {
            var h = new Harness();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), utcNow: () => now);
            var ctx = LeadContext(out _);
            int got = -1;
            var t = new Thread(() => got = agent.ChooseDragonRecipient(ctx)) { IsBackground = true };
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(t.Join(200), Is.False);

            now = FixedNow.AddSeconds(4);
            agent.Submit(WithAsk(ClientAction.GiveDragon(0, 1L, 2), h.LastAwait.AskId));   // 파트너 — 거부
            WaitUntil(() => h.AwaitCount == 2);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(16));
            agent.Submit(WithAsk(ClientAction.GiveDragon(0, 2L, 1), h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);
            Assert.That(got, Is.EqualTo(1));
        }

        [Test]
        public void A_clock_that_jumps_back_never_grants_more_than_the_full_time()
        {
            // 회귀 가드: 벽시계가 뒤로 가도 남은 초는 전체 시간을 넘지 않는다.
            var h = new Harness();
            var now = FixedNow;
            var agent = h.Create(0, TimeSpan.FromSeconds(20), utcNow: () => now);
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _))) { IsBackground = true };
            t.Start();
            WaitUntil(() => h.AwaitCount == 1);
            Assert.That(t.Join(200), Is.False);

            now = FixedNow.AddSeconds(-30);
            agent.SetConnected(true);
            WaitUntil(() => h.AwaitCount == 2);

            Assert.That(h.LastAwait.SecondsLeft, Is.EqualTo(20));
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);
        }

        [Test]
        public void Early_grand_tichu_call_survives_a_disconnect_before_the_question()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, standIn: stub, anyConnected: () => true);
            var s = GameEngine.NewRound(778UL);

            agent.Submit(ClientAction.CallGrandTichu(0, 1));   // 아직 묻기 전에 먼저 낸 답
            agent.SetConnected(false);

            Assert.That(agent.CallGrandTichu(GameFlowHelpers.Context(s, 0)), Is.True, "미리 낸 큰 티츄 콜을 대리 AI 가 덮어쓰지 않는다");
            Assert.That(stub.Grands, Is.EqualTo(0));
        }

        [Test]
        public void Early_exchange_survives_a_disconnect_before_the_question()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, standIn: stub, anyConnected: () => true);
            var state = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(9, Suit.Sword), Card.Normal(7, Suit.Star), Card.Normal(8, Suit.Pagoda)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            agent.Submit(ClientAction.Exchange(0, 1, Card.Normal(9, Suit.Sword), Card.Normal(7, Suit.Star), Card.Normal(8, Suit.Pagoda)));
            agent.SetConnected(false);

            var choice = agent.ChooseExchange(GameFlowHelpers.Context(state, 0));
            Assert.That(choice.ToLeft, Is.EqualTo(Card.Normal(9, Suit.Sword)), "사람이 고른 교환이 그대로 쓰인다");
            Assert.That(stub.Exchanges, Is.EqualTo(0));
        }

        [Test]
        public void ResetActionSeq_lets_a_new_client_start_from_one()
        {
            var h = new Harness();
            var agent = h.Create(0);
            agent.Submit(ClientAction.Pass(0, 5));

            agent.ResetActionSeq();
            agent.Submit(ClientAction.Pass(0, 1));

            Assert.That(h.Rejected("duplicate or out-of-order actionSeq"), Is.False);
        }

        [Test]
        public void Without_a_stand_in_a_disconnected_seat_still_waits_for_its_human()
        {
            // 로컬 싱글플레이·기존 경로: 대리 AI 가 없으면 연결 상태가 바뀌어도 동작이 그대로다.
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(80));
            Assert.Throws<TimeoutException>(() => agent.DecideTurn(LeadContext(out _)));
        }

        [Test]
        public void Reconnect_inside_the_broadcast_window_still_wakes_a_paused_seat()
        {
            var h = new Harness();
            bool anyone = false;
            var agent = h.Create(0, TimeSpan.FromSeconds(2), standIn: new StubAgent(), anyConnected: () => anyone);
            agent.SetConnected(false);
            bool fired = false;
            h.OnAwait = aw =>
            {
                if (fired) return;
                fired = true;
                anyone = true;
                agent.SetConnected(true);   // 방송과 대기 공개 사이의 틈
            };
            var t = new Thread(() => agent.DecideTurn(LeadContext(out _)));
            t.Start();

            WaitUntil(() => h.AwaitCount >= 2, 2000);
            agent.Submit(PlayFive(1, h.LastAwait.AskId));
            Assert.That(t.Join(2000), Is.True);
        }

        [Test]
        public void Answer_with_current_ask_id_before_the_wait_is_published_is_accepted()
        {
            var h = new Harness();
            var agent = h.Create(0, TimeSpan.FromSeconds(5), standIn: new StubAgent());
            h.OnAwait = aw => agent.Submit(PlayFive(1, aw.AskId));   // 대기 공개 전에 도착
            var d = agent.DecideTurn(LeadContext(out _));

            Assert.That(d.IsPass, Is.False);
            Assert.That(h.RejectCount, Is.EqualTo(0));
        }

        [Test]
        public void Duplicate_numbered_answer_after_the_turn_is_rejected_not_reused()
        {
            var h = new Harness();
            var stub = new StubAgent();
            var agent = h.Create(0, TimeSpan.FromMilliseconds(150), standIn: stub);
            var ctx = LeadContext(out _);
            long x = 0;
            h.OnAwait = aw => { x = aw.AskId; agent.Submit(PlayFive(1, x)); };
            Assert.That(agent.DecideTurn(ctx).IsPass, Is.False);
            h.OnAwait = null;

            var dup = ClientAction.Pass(0, 2);
            dup.AskId = x;
            agent.Submit(dup);                              // 끝난 질문 번호의 중복 답

            Assert.That(h.Rejected("stale answer"), Is.True);
            Assert.That(agent.DecideTurn(ctx).IsPass, Is.True, "재사용되지 않고 시간 초과 → 대리 AI(Pass)");
            Assert.That(stub.Turns, Is.EqualTo(1), "중복 Pass 가 답으로 쓰이지 않고 대리 AI 가 뒀다");
        }
    }
}
