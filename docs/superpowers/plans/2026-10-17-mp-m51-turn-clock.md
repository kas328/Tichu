# M5.1 차례 시계 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 차례 시간이 어떤 경로로도 20초로 되돌아가지 않게 하고, 다른 사람 차례의 남은 초를 그 사람 이름표 옆에 보여 준다.

**Architecture:** 서버 `RemoteAgent` 가 차례·용 양도 결정마다 마감 하나(`_decisionDeadlineUtc`)를 두고 재질문은 남은 초만 준다. 멈춤 복귀의 전체 시간 예외(`resumedFromPause`)는 지운다. `Awaiting.Redacted()` 가 `AskId`·`SecondsLeft` 를 남기고, 클라이언트 `TableViewModel` 이 남의 마감을 로컬 시계로 잡아 `SeatSecondsLeft(seat)` 로 내놓으며, `RuntimeTableView` 가 이름표를 매 프레임 조립한다.

**Tech Stack:** .NET 10 (core·server, NUnit, `dotnet test`), Unity 6000.3.17f1 (uGUI 코드 생성, R3, NUnit EditMode via UnityMCP).

**Spec:** `docs/superpowers/specs/2026-10-17-mp-m51-turn-clock-design.md`

## Global Constraints

- 브랜치 `feat/mp-m51` ← main `1a28acf`. 커밋 메시지는 한국어, 끝에 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` 와 `Claude-Session: https://claude.ai/code/session_01DKcF6kM8xBvvJwha5JQu1Y`.
- **core ↔ Unity 미러:** `core/src/Tichu.Net/*.cs` 와 `Assets/_Project/Net/*.cs`, `core/tests/Tichu.Core.Tests/RemoteAgentTests.cs` 와 `Assets/_Project/Tests/EditMode/RemoteAgentTests.cs` 는 바이트 동일해야 한다. core 를 고친 뒤 `cp` 로 덮고 `diff -q` 로 확인한다.
- 프로토콜 필드 추가·`ProtocolVersion` 변경 금지. 서버(`core/src/Tichu.Server`) 코드 변경 없음.
- ⚠️ Unity 러너에서 `Tichu.Core.Tests` 어셈블리 전체 실행 금지(시뮬 10만판 → 에디터 멈춤). Unity 쪽은 `test_names` 클래스 필터로만.
- UnityMCP 는 raw HTTP 헬퍼로 부른다: `bash C:/Users/user/AppData/Local/Temp/claude/C--Users-user-Desktop-Project-Tichu/b4aaf7e3-92df-48ee-a30f-6234df2b6c74/scratchpad/mcp.sh tools/call '<json>'` → 응답의 job_id 로 `get_test_job`(`wait_timeout` 90) 폴링, 결과는 `result.structuredContent.result.data`. MCP 가 응답하지 않으면 사용자에게 Unity 에디터를 열어 달라고 요청하고 기다린다. run_tests 전 PlayMode 가 꺼져 있어야 한다.
- 새 `.cs` 파일은 Unity 가 `.meta` 를 만들도록 `refresh_unity`(또는 `execute_code` 로 `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)`) 후 테스트하고, 생성된 `.meta` 도 커밋한다.
- 변경 줄은 모두 이 스펙으로 추적 가능해야 한다(주변 코드 개선 금지). 기존 주석 밀도·한국어 주석 관용을 따른다.

## Review Focus

1. **마감이 다음 결정으로 새는 경우** — `Yield` 뒤 드라이버가 같은 좌석에 다시 묻지 않고 라운드가 끝나면 남은 마감이 다음 라운드 첫 차례를 깎는다. 기대: 새 라운드 첫 차례는 전체 시간 → Task 1 `A_new_round_drops_a_pending_turn_deadline`.
2. **시간 초과·자리 비움 뒤 다음 차례** — 마감이 지나 대리 AI 가 둔 뒤 복귀한 사람의 다음 차례가 0초로 시작하면 영구 자리 비움 루프가 된다. 기대: 전체 시간 → Task 1 `Returning_after_the_deadline_hands_the_turn_to_the_stand_in_at_once` 후반부.
3. **벽시계 역행** — 서버 시계가 뒤로 가면 남은 초가 20 을 넘을 수 있다. 기대: 상한 20 → Task 1 `A_clock_that_jumps_back_never_grants_more_than_the_full_time`.
4. **재접속한 클라이언트의 남 시계** — 재접속 뒤 이전 판의 남 마감이 남아 엉뚱한 이름표에 초가 붙는다. 기대: `ResetForReconnect` 가 지움 → Task 2 `Reconnect_clears_another_seats_clock`.
5. **이름표 폭** — 24pt 이름표 폭 140 에 "오른쪽(AI) · 12초" 가 넘친다. 기대: 줄바꿈·잘림 없이 가운데 정렬로 보임 → Task 3 에서 `horizontalOverflow = Overflow` + 실기 확인.

---

### Task 1: 서버 — 한 결정에 마감 하나

**Files:**
- Modify: `core/src/Tichu.Net/RemoteAgent.cs` (필드 선언부 ~70-80행, `ResetForNewRound` ~318-340행, `DecideTurn` ~379-401행, `ChooseDragonRecipient` ~409-421행, `Await` 문서주석·본문 ~426-610행)
- Test: `core/tests/Tichu.Core.Tests/RemoteAgentTests.cs` (기존 `Seat_returning_from_a_pause_gets_the_full_timeout_not_the_expired_shared_window` 768행 교체 + 새 테스트를 `Play_question_keeps_the_full_timeout_even_with_a_phase_start` 뒤에 추가)
- Mirror: `Assets/_Project/Net/RemoteAgent.cs`, `Assets/_Project/Tests/EditMode/RemoteAgentTests.cs`

**Interfaces:**
- Consumes: 없음(기존 `RemoteAgent` 생성자 `utcNow`·`phaseStartedUtc` 주입, 테스트 `Harness.Create`·`FixedNow`·`LeadContext`·`PlayFive`·`WithAsk`·`WaitUntil`·`StubAgent`).
- Produces: 동작만 바뀐다(공개 API 변화 없음). 재질문의 `Awaiting.SecondsLeft` 가 남은 초가 된다 — Task 2 클라이언트가 그대로 소비.

- [ ] **Step 1: 기존 멈춤-복귀 테스트를 새 기대로 교체**

`RemoteAgentTests.cs` 의 `Seat_returning_from_a_pause_gets_the_full_timeout_not_the_expired_shared_window` 메서드 전체를 아래로 바꾼다.

```csharp
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
```

- [ ] **Step 2: 새 테스트 추가**

`Play_question_keeps_the_full_timeout_even_with_a_phase_start` 메서드 바로 뒤에 붙인다. (`now` 는 람다가 캡처하는 지역 변수 — 다른 스레드에서 읽는 `anyone` 과 같은 관용.)

```csharp
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
```

- [ ] **Step 3: 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests --filter "FullyQualifiedName~RemoteAgentTests" 2>&1 | tail -30`
Expected: FAIL 7건 — `Seat_returning_from_a_pause_gets_only_the_rest_of_the_shared_window`(5 ≠ 3), `Reconnect_during_the_turn_keeps_the_remaining_seconds`(20 ≠ 12), `A_tichu_yield_does_not_restart_the_turn_clock`(20 ≠ 15), `A_rejected_move_does_not_restart_the_turn_clock`(20 ≠ 13), `Returning_after_the_deadline_hands_the_turn_to_the_stand_in_at_once`(Join 2000 false), `The_turn_clock_keeps_running_while_the_room_is_paused`(20 ≠ 10), `A_rejected_dragon_recipient_does_not_restart_the_clock`(20 ≠ 16). 가드 4건(`A_question_first_asked_while_paused…`, `The_next_turn…`, `A_new_round…`, `A_clock_that_jumps_back…`)은 지금도 PASS. 나머지 기존 테스트 PASS.

- [ ] **Step 4: 필드 추가**

`RemoteAgent.cs` 의 `private bool _exchangeDecided;    // _gate` 줄 바로 아래에 추가:

```csharp
        private DateTime? _decisionDeadlineUtc; // _gate — 진행 중인 차례·용 양도 결정의 마감(재질문해도 유지, M5.1)
```

- [ ] **Step 5: `Await` 의 대기 상한 계산 교체**

`Await` 문서주석의 이 두 줄을

```csharp
        /// 대기 상한은 보통 _timeout 이지만 큰 티츄·교환은 단계 시작부터 전원이 같은 마감을 공유해 남은 시간만 기다린다
        /// (멈춤에서 돌아온 좌석은 예외 — 전체 _timeout).
```

아래로 바꾼다:

```csharp
        /// 대기 상한은 마감까지 남은 시간이다. 차례·용 양도는 그 결정을 처음 물을 때 마감을 정하고 다시 물어도 유지하며
        /// (재접속·티츄 Yield·거부 뒤 재질문 — 시간을 되돌리지 않는다), 큰 티츄·교환은 단계 시작부터 전원이 같은 마감을 공유한다.
        /// 멈춤에서 돌아와도 예외 없이 남은 시간만 준다(M5.1).
```

본문 맨 앞의 다음 세 줄을 지운다:

```csharp
            // 멈춤(아무도 연결 안 됨)에서 깨어난 뒤의 재질문은 공유 마감을 무시하고 전체 시간을 받는다 —
            // 멈춘 동안 단계 시작이 계속 늙어 돌아온 사람이 곧바로 자리 비움이 되는 것을 막는다.
            bool resumedFromPause = false;
```

그리고 다음 블록을

```csharp
                // 큰 티츄·교환은 단계 시작부터 전원이 같은 마감을 공유한다 — 늦게 물린 좌석은 남은 시간만 받는다.
                // 콜백은 잠금 밖에서 부른다(세션의 읽기 전용 값이라 잠금은 없다).
                TimeSpan waitFor = _timeout;
                if (human && !resumedFromPause && _timeout != Timeout.InfiniteTimeSpan
                    && (kind == StepKind.GrandTichu || kind == StepKind.Exchange))
                {
                    var phaseStart = _phaseStartedUtc?.Invoke();
                    if (phaseStart.HasValue)
                    {
                        waitFor = _timeout - (_utcNow() - phaseStart.Value);
                        if (waitFor < TimeSpan.Zero) waitFor = TimeSpan.Zero;
                        if (waitFor > _timeout) waitFor = _timeout;   // 벽시계가 뒤로 가도 전체 시간을 넘지 않는다
                    }
                }
```

아래로 바꾼다:

```csharp
                // 사람에게 물을 때만 시계가 돈다(멈춤 대기는 무기한). 큰 티츄·교환은 단계 시작부터 전원이 같은
                // 마감을 공유하고, 차례·용 양도는 결정마다 마감 하나 — 다시 물어도 남은 시간만 준다.
                // 콜백은 잠금 밖에서 부른다(세션의 읽기 전용 값이라 잠금은 없다).
                TimeSpan waitFor = _timeout;
                if (human && _timeout != Timeout.InfiniteTimeSpan)
                {
                    var now = _utcNow();
                    if (kind == StepKind.GrandTichu || kind == StepKind.Exchange)
                    {
                        var phaseStart = _phaseStartedUtc?.Invoke();
                        if (phaseStart.HasValue) waitFor = Remaining(phaseStart.Value + _timeout, now);
                    }
                    else
                    {
                        DateTime deadline;
                        lock (_gate) deadline = (_decisionDeadlineUtc ??= now + _timeout);
                        waitFor = Remaining(deadline, now);
                    }
                }
```

`Await` 끝부분의 Resume 처리를

```csharp
                if (a.Kind == ClientActionKind.Resume)
                {
                    if (paused) resumedFromPause = true;
                    continue;   // 연결·복귀 변화 — 다시 판단
                }
```

아래로 바꾼다:

```csharp
                if (a.Kind == ClientActionKind.Resume) continue;   // 연결·복귀 변화 — 다시 판단
```

- [ ] **Step 6: 보조 메서드 추가**

`TakeEarlyLocked` 메서드 바로 앞에 추가:

```csharp
        /// <summary>마감까지 남은 시간 — 0 아래면 0, 벽시계가 뒤로 가도 전체 시간을 넘지 않는다.</summary>
        private TimeSpan Remaining(DateTime deadlineUtc, DateTime now)
        {
            var left = deadlineUtc - now;
            if (left < TimeSpan.Zero) return TimeSpan.Zero;
            return left > _timeout ? _timeout : left;
        }

        /// <summary>차례·용 양도 결정이 끝났다 — 다음 결정은 새 마감을 받는다.</summary>
        private void ClearDecisionDeadline()
        {
            lock (_gate) _decisionDeadlineUtc = null;
        }
```

- [ ] **Step 7: 결정 종료 시 해제**

`DecideTurn` 을 아래로 바꾼다(본문 루프는 `DecideTurnCore` 로 그대로 옮긴다):

```csharp
        /// <inheritdoc/>
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var d = DecideTurnCore(ctx);
            if (!d.IsYield) ClearDecisionDeadline();   // Yield 는 같은 결정을 다시 묻는다 — 마감 유지
            return d;
        }

        private TurnDecision DecideTurnCore(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Play, state, seat);
                if (a == null) return _standIn!.DecideTurn(ctx);

                // CallTichu 로 깨어났다 — 드라이버가 예약을 먼저 적용하고 다시 묻게 한다.
                if (a.Kind == ClientActionKind.CallTichu) return TurnDecision.Yield;

                if (a.Kind == ClientActionKind.Pass)
                {
                    if (LegalMoveGenerator.CanPass(state, seat)) return TurnDecision.Pass;
                    Reject(a, "cannot pass now");
                    continue;
                }

                var move = MoveResolver.ResolveMove(state, seat, a);
                if (move == null) { Reject(a, "illegal or ambiguous move"); continue; }
                return TurnDecision.Play(move, a.Wish);
            }
        }
```

`ChooseDragonRecipient` 도 같은 모양으로:

```csharp
        /// <inheritdoc/>
        public int ChooseDragonRecipient(in DecisionContext ctx)
        {
            int r = ChooseDragonRecipientCore(ctx);
            ClearDecisionDeadline();
            return r;
        }

        private int ChooseDragonRecipientCore(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.DragonGift, state, seat);
                if (a == null) return _standIn!.ChooseDragonRecipient(ctx);
                int r = a.RecipientSeat;
                bool isOpponent = r >= 0 && r < 4 && (r % 2) != (seat % 2);
                if (!isOpponent) { Reject(a, "dragon recipient must be an opponent"); continue; }
                return r;
            }
        }
```

`ResetForNewRound` 의 `_awaiting = null;` 바로 아래에 추가:

```csharp
                _decisionDeadlineUtc = null;   // 끝나지 않은 결정의 마감이 새 라운드로 새지 않게
```

- [ ] **Step 8: 통과 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests --filter "FullyQualifiedName~RemoteAgentTests" 2>&1 | tail -5`
Expected: PASS, 실패 0 (기존 + 새 10건 + 교체 1건).

- [ ] **Step 9: core 전체·서버 회귀**

Run: `dotnet test core/tests/Tichu.Core.Tests 2>&1 | tail -3; dotnet test core/tests/Tichu.Server.Tests 2>&1 | tail -3`
Expected: 둘 다 실패 0. 서버 79/79. (core 전체는 시뮬 포함이라 수 분 걸릴 수 있다 — `timeout` 600000.) `EndToEndTests.A_dropped_solo_player_rejoins_the_same_seat_and_finishes` 는 멈춤 복귀가 남은 시간을 받게 됐으므로 특히 확인한다 — 실패하면 시간 초과 로그가 생긴 것이니 원인을 조사한다(테스트 기대를 고치지 말고 먼저 보고).

- [ ] **Step 10: Unity 미러 동기화 + Unity 테스트**

```bash
cp core/src/Tichu.Net/RemoteAgent.cs Assets/_Project/Net/RemoteAgent.cs
cp core/tests/Tichu.Core.Tests/RemoteAgentTests.cs Assets/_Project/Tests/EditMode/RemoteAgentTests.cs
diff -q core/src/Tichu.Net/RemoteAgent.cs Assets/_Project/Net/RemoteAgent.cs && diff -q core/tests/Tichu.Core.Tests/RemoteAgentTests.cs Assets/_Project/Tests/EditMode/RemoteAgentTests.cs
```

그 뒤 `refresh_unity` 로 컴파일하고 `run_tests` `{"mode":"EditMode","test_names":["Tichu.Core.Tests.RemoteAgentTests"]}` 실행.
Expected: diff 출력 없음, Unity RemoteAgentTests 전부 PASS.

- [ ] **Step 11: 커밋**

```bash
git add core/src/Tichu.Net/RemoteAgent.cs core/tests/Tichu.Core.Tests/RemoteAgentTests.cs Assets/_Project/Net/RemoteAgent.cs Assets/_Project/Tests/EditMode/RemoteAgentTests.cs
git commit -m "feat(net): 한 결정에 마감 하나 — 재접속·티츄·거부·멈춤 복귀로 차례 시간이 되돌아가지 않음

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DKcF6kM8xBvvJwha5JQu1Y"
```

---

### Task 2: 남의 차례 남은 초 — 축약판 + 뷰모델

**Files:**
- Modify: `core/src/Tichu.Net/Awaiting.cs:35-37` (`Redacted`)
- Test: `core/tests/Tichu.Core.Tests/RemoteAgentTests.cs` (`Awaiting_is_announced_with_legal_moves_before_blocking` 뒤에 1건)
- Mirror: `Assets/_Project/Net/Awaiting.cs`, `Assets/_Project/Tests/EditMode/RemoteAgentTests.cs`
- Modify: `Assets/_Project/Presentation/ViewModel/TableViewModel.cs` (필드 ~31-33행, `TurnSecondsLeft` 옆 ~42행, `Apply` ~176-182행, `ResetForReconnect` ~414-426행)
- Test: `Assets/_Project/Presentation/Tests/TableViewModelTests.cs` (`Grand_and_exchange_countdown_comes_from_the_shared_phase_window_once_per_phase` 뒤)

**Interfaces:**
- Consumes: Task 1 의 재질문 `SecondsLeft`(남은 초)·새 `AskId`.
- Produces: `public int? TableViewModel.SeatSecondsLeft(int seat)` — 로컬 좌석 번호, 그 좌석에 차례·용 양도를 묻는 중이고 초가 있으면 `max(0, ceil(마감 − 지금))`, 아니면 null. Task 3 이 소비.

- [ ] **Step 1: 축약판 테스트 작성**

`RemoteAgentTests.cs` 의 `Awaiting_is_announced_with_legal_moves_before_blocking` 뒤에:

```csharp
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
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests --filter "FullyQualifiedName~Redacted_view_keeps" 2>&1 | tail -8`
Expected: FAIL — `AskId` 0 ≠ 7.

- [ ] **Step 3: `Redacted` 수정**

`Awaiting.cs` 의

```csharp
        /// <summary>다른 좌석에게 보낼 축약판 — 차례 표시에 필요한 것만 남긴다.</summary>
        public Awaiting Redacted() =>
            new Awaiting { Kind = Kind, Seat = Seat, DeadlineUtc = DeadlineUtc };
```

를 아래로:

```csharp
        /// <summary>다른 좌석에게 보낼 축약판 — 차례 표시와 남은 초(M5.1)에 필요한 것만 남긴다. 합법수는 뺀다.</summary>
        public Awaiting Redacted() =>
            new Awaiting { Kind = Kind, Seat = Seat, DeadlineUtc = DeadlineUtc, AskId = AskId, SecondsLeft = SecondsLeft };
```

- [ ] **Step 4: 통과 + 미러**

Run: `dotnet test core/tests/Tichu.Core.Tests --filter "FullyQualifiedName~RemoteAgentTests|FullyQualifiedName~ProtocolJsonTests|FullyQualifiedName~SeatRotationTests|FullyQualifiedName~LoopbackMatchTests" 2>&1 | tail -3`
Expected: 실패 0.

```bash
cp core/src/Tichu.Net/Awaiting.cs Assets/_Project/Net/Awaiting.cs
cp core/tests/Tichu.Core.Tests/RemoteAgentTests.cs Assets/_Project/Tests/EditMode/RemoteAgentTests.cs
diff -q core/src/Tichu.Net/Awaiting.cs Assets/_Project/Net/Awaiting.cs && diff -q core/tests/Tichu.Core.Tests/RemoteAgentTests.cs Assets/_Project/Tests/EditMode/RemoteAgentTests.cs
```
Expected: 출력 없음.

- [ ] **Step 5: 뷰모델 테스트 작성**

`TableViewModelTests.cs` 의 `Grand_and_exchange_countdown_comes_from_the_shared_phase_window_once_per_phase` 메서드 뒤에:

```csharp
        // ── M5.1: 남의 차례 남은 초 ───────────────────────────────────────────

        private static Awaiting AskOther(int seat, long askId, int? secs, StepKind kind = StepKind.Play) =>
            new Awaiting { Kind = kind, Seat = seat, AskId = askId, SecondsLeft = secs };

        [Test]
        public void Another_seats_turn_counts_down_from_when_its_question_arrived()
        {
            var s = GameEngine.NewRound(777UL);
            double now = 100;
            var vm = new TableViewModel(0, () => now);
            vm.Apply(MyTurnView(s, 3, AskOther(3, 1, 20)));

            Assert.That(vm.SeatSecondsLeft(3), Is.EqualTo(20));
            Assert.That(vm.SeatSecondsLeft(1), Is.Null, "묻지 않는 좌석엔 초가 없다");
            Assert.That(vm.TurnSecondsLeft, Is.Null, "내 타이머와 섞이지 않는다");
            now = 107.5;
            Assert.That(vm.SeatSecondsLeft(3), Is.EqualTo(13));
            now = 130;
            Assert.That(vm.SeatSecondsLeft(3), Is.EqualTo(0), "0 아래로 내려가지 않는다");
        }

        [Test]
        public void A_resent_question_does_not_push_another_seats_clock_back()
        {
            var s = GameEngine.NewRound(777UL);
            double now = 100;
            var vm = new TableViewModel(0, () => now);
            vm.Apply(MyTurnView(s, 3, AskOther(3, 1, 20)));

            now = 105;
            vm.Apply(MyTurnView(s, 3, AskOther(3, 1, 20)));   // 같은 질문의 재전송
            Assert.That(vm.SeatSecondsLeft(3), Is.EqualTo(15));

            vm.Apply(MyTurnView(s, 3, AskOther(3, 2, 15)));   // 서버가 다시 물었다 — 서버의 남은 초를 따른다
            now = 110;
            Assert.That(vm.SeatSecondsLeft(3), Is.EqualTo(10));
        }

        [Test]
        public void Another_seats_clock_clears_when_the_turn_ends()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0, () => 100);
            vm.Apply(MyTurnView(s, 3, AskOther(3, 1, 20)));

            vm.Apply(MyTurnView(s, 0, null));                  // 수가 적용됐다(질문 없는 갱신)

            Assert.That(vm.SeatSecondsLeft(3), Is.Null);
        }

        [Test]
        public void Grand_tichu_and_exchange_questions_show_no_clock_for_other_seats()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0, () => 100);

            var grand = MyTurnView(s, 1, AskOther(1, 1, 20, StepKind.GrandTichu));
            grand.Phase = RoundPhase.GrandTichuDecision;
            vm.Apply(grand);
            Assert.That(vm.SeatSecondsLeft(1), Is.Null, "네 좌석 동시 질문 — 남의 초는 혼란만 준다");

            var exchange = MyTurnView(s, 2, AskOther(2, 1, 20, StepKind.Exchange));
            exchange.Phase = RoundPhase.Exchange;
            vm.Apply(exchange);
            Assert.That(vm.SeatSecondsLeft(2), Is.Null);
        }

        [Test]
        public void Another_seats_dragon_gift_shows_its_clock()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0, () => 100);
            vm.Apply(MyTurnView(s, 1, AskOther(1, 4, 20, StepKind.DragonGift)));

            Assert.That(vm.SeatSecondsLeft(1), Is.EqualTo(20));
        }

        [Test]
        public void A_question_without_seconds_shows_no_clock()
        {
            // 예전 서버·타이머 없는 질문 — 초를 지어내지 않는다.
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0, () => 100);
            vm.Apply(MyTurnView(s, 3, AskOther(3, 1, null)));

            Assert.That(vm.SeatSecondsLeft(3), Is.Null);
        }

        [Test]
        public void My_own_question_is_not_another_seats_clock()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0, () => 100);
            vm.Apply(MyTurnView(s, 0, AskPlay(s.Seats[0].Hand[0], askId: 1, secs: 30)));

            for (int seat = 0; seat < 4; seat++)
                Assert.That(vm.SeatSecondsLeft(seat), Is.Null);
            Assert.That(vm.TurnSecondsLeft, Is.EqualTo(30));
        }

        [Test]
        public void Reconnect_clears_another_seats_clock()
        {
            var s = GameEngine.NewRound(777UL);
            var vm = new TableViewModel(0, () => 100);
            vm.Apply(MyTurnView(s, 3, AskOther(3, 1, 20)));

            vm.ResetForReconnect();

            Assert.That(vm.SeatSecondsLeft(3), Is.Null);
        }
```

- [ ] **Step 6: 실패 확인 (Unity)**

`refresh_unity` 후 `run_tests` `{"mode":"EditMode","test_names":["Tichu.Presentation.Tests.TableViewModelTests"]}`.
Expected: 컴파일 에러 — `TableViewModel` 에 `SeatSecondsLeft` 정의 없음(CS1061). 이것이 이 단계의 RED.

- [ ] **Step 7: 뷰모델 구현**

`TableViewModel.cs` 필드 `private RoundPhase? _phaseDeadlinePhase;` 줄 아래에:

```csharp
        private int _otherSeat = -1;        // 지금 남에게 온 차례·용 양도 질문의 좌석(-1 = 없음)
        private long _otherAsk;             // 그 질문 번호 — 같은 질문의 재전송이 시계를 되돌리지 않게
        private double? _otherDeadline;     // 그 마감(로컬 시계)
```

`TurnSecondsLeft` 속성 바로 아래에:

```csharp
        /// <summary>다른 좌석의 차례·용 양도 남은 초(로컬 좌석 번호). 그 좌석에 묻는 중이 아니면 null.</summary>
        public int? SeatSecondsLeft(int seat) =>
            seat == _otherSeat && _otherDeadline.HasValue
                ? Math.Max(0, (int)Math.Ceiling(_otherDeadline.Value - _now())) : (int?)null;
```

`Apply` 의 다음 세 줄을

```csharp
            // 서버가 내 좌석에 직접 물으면 그 질문의 초가 실제 마감이다 — 단계 창과 보통 같고,
            // 멈췄다 돌아온 좌석은 서버가 전체 시간을 새로 준다.
            TrackDeadline(v.Awaiting);
```

아래로 바꾼다(주석은 M5.1 에서 사실이 아니게 되어 고친다):

```csharp
            // 서버가 내 좌석에 직접 물으면 그 질문의 초가 실제 마감이다 — 다시 물어도 서버가 남은 초만 준다.
            TrackDeadline(v.Awaiting);
            TrackOtherDeadline(v.Awaiting);
```

`TrackDeadline` 메서드 바로 아래에:

```csharp
        /// <summary>
        /// 남의 차례·용 양도 질문이면 그 마감을 잡는다(번호가 다를 때만 — 받은 시각 + 남은 초).
        /// 질문이 없거나 내 질문·큰 티츄·교환(네 좌석 동시)·초 없는 질문이면 지운다.
        /// </summary>
        private void TrackOtherDeadline(Awaiting aw)
        {
            bool timed = aw != null && aw.Seat != _mySeat && aw.SecondsLeft.HasValue
                && (aw.Kind == StepKind.Play || aw.Kind == StepKind.DragonGift);
            if (!timed) { ClearOtherDeadline(); return; }
            if (aw.Seat == _otherSeat && aw.AskId == _otherAsk) return;
            _otherSeat = aw.Seat;
            _otherAsk = aw.AskId;
            _otherDeadline = _now() + aw.SecondsLeft.Value;
        }

        private void ClearOtherDeadline()
        {
            _otherSeat = -1;
            _otherAsk = 0;
            _otherDeadline = null;
        }
```

`ResetForReconnect` 의 `_phaseDeadlinePhase = null;` 아래에:

```csharp
            ClearOtherDeadline();
```

- [ ] **Step 8: 통과 확인 (Unity)**

`refresh_unity` 후 `run_tests` `{"mode":"EditMode","assembly_names":["Tichu.Presentation.Tests"]}` 와 `{"mode":"EditMode","test_names":["Tichu.Core.Tests.RemoteAgentTests"]}`.
Expected: Presentation 전부 PASS(271 + 8 = 279), RemoteAgentTests 전부 PASS.

- [ ] **Step 9: 커밋**

```bash
git add core/src/Tichu.Net/Awaiting.cs core/tests/Tichu.Core.Tests/RemoteAgentTests.cs Assets/_Project/Net/Awaiting.cs Assets/_Project/Tests/EditMode/RemoteAgentTests.cs Assets/_Project/Presentation/ViewModel/TableViewModel.cs Assets/_Project/Presentation/Tests/TableViewModelTests.cs
git commit -m "feat(presentation): 남의 차례·용 양도 남은 초 — 축약판에 남은 초·질문 번호, 뷰모델 SeatSecondsLeft

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DKcF6kM8xBvvJwha5JQu1Y"
```

---

### Task 3: 이름표 옆 남은 초

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs` (이름표 생성 ~271-273행, `SeatAway` 구독 ~376-380행, `EveryUpdate` ~381-388행)
- Create: `Assets/_Project/Presentation/Tests/SeatLabelTests.cs` (+ Unity 가 만드는 `.meta`)

**Interfaces:**
- Consumes: `TableViewModel.SeatSecondsLeft(int seat)`(Task 2), 기존 `TableViewModel.SeatAway(int seat)`(`ReadOnlyReactiveProperty<bool>`).
- Produces: `public static string RuntimeTableView.SeatLabel(string name, bool away, int? secondsLeft)`.

- [ ] **Step 1: 테스트 작성**

`Assets/_Project/Presentation/Tests/SeatLabelTests.cs`:

```csharp
using NUnit.Framework;
using Tichu.Presentation.Views;

namespace Tichu.Presentation.Tests
{
    /// <summary>좌석 이름표 조립 — 이름, 자리 비움이면 (AI), 남은 초가 있으면 · N초(M5.1).</summary>
    [TestFixture]
    public class SeatLabelTests
    {
        [Test]
        public void Name_only_when_nothing_else_applies() =>
            Assert.That(RuntimeTableView.SeatLabel("왼쪽", false, null), Is.EqualTo("왼쪽"));

        [Test]
        public void Away_seat_is_marked_AI() =>
            Assert.That(RuntimeTableView.SeatLabel("왼쪽", true, null), Is.EqualTo("왼쪽(AI)"));

        [Test]
        public void Seconds_follow_the_name() =>
            Assert.That(RuntimeTableView.SeatLabel("오른쪽", false, 12), Is.EqualTo("오른쪽 · 12초"));

        [Test]
        public void Zero_seconds_are_still_shown() =>
            Assert.That(RuntimeTableView.SeatLabel("파트너", false, 0), Is.EqualTo("파트너 · 0초"));
    }
}
```

(네임스페이스 `Tichu.Presentation.Views` 는 `RuntimeTableView.cs` 의 실제 namespace 선언을 열어 확인하고 맞춘다.)

- [ ] **Step 2: 실패 확인 (Unity)**

`refresh_unity`(새 파일 임포트 — 안 잡히면 `execute_code` 로 `AssetDatabase.ImportAsset("Assets/_Project/Presentation/Tests/SeatLabelTests.cs"); AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);`) 후 `run_tests` `{"mode":"EditMode","test_names":["Tichu.Presentation.Tests.SeatLabelTests"]}`.
Expected: 컴파일 에러 — `RuntimeTableView` 에 `SeatLabel` 정의 없음.

- [ ] **Step 3: 구현**

`RuntimeTableView.cs` 의 `SeatNames` 선언 아래에:

```csharp
        /// <summary>좌석 이름표 — 이름, 자리 비움이면 (AI), 남은 초가 있으면 · N초.</summary>
        public static string SeatLabel(string name, bool away, int? secondsLeft) =>
            (away ? $"{name}(AI)" : name) + (secondsLeft.HasValue ? $" · {secondsLeft.Value}초" : "");
```

이름표 생성부의

```csharp
            _seatTexts[seat].alignment = TextAnchor.MiddleCenter;
```

아래에 한 줄 추가(남은 초가 붙어도 줄바꿈·잘림 없이 가운데에서 넘치게):

```csharp
            _seatTexts[seat].horizontalOverflow = HorizontalWrapMode.Overflow;
```

`SeatAway` 구독 루프

```csharp
            for (int i = 1; i < 4; i++)
            {
                int seat = i;
                _vm.SeatAway(seat).Subscribe(a => _seatTexts[seat].text = a ? $"{SeatNames[seat]}(AI)" : SeatNames[seat]).AddTo(_subs);
            }
```

를 지우고, 바로 아래 `Observable.EveryUpdate` 본문을 아래로 바꾼다:

```csharp
            Observable.EveryUpdate(_sceneCt).Subscribe(_ =>
            {
                if (_noticeToast != null && _noticeToast.activeSelf && Time.realtimeSinceStartupAsDouble >= _noticeUntil)
                    _noticeToast.SetActive(false);
                // 남의 이름표 — 자리 비움·남은 초가 프레임마다 바뀌므로 여기서 조립하고 바뀔 때만 대입한다.
                for (int i = 1; i < 4; i++)
                {
                    if (_seatTexts[i] == null) continue;
                    var label = SeatLabel(SeatNames[i], _vm.SeatAway(i).CurrentValue, _vm.SeatSecondsLeft(i));
                    if (_seatTexts[i].text != label) _seatTexts[i].text = label;
                }
                if (_timerText == null) return;
                var left = _vm.TurnSecondsLeft;
                _timerText.text = left.HasValue ? $"남은 시간 {left}초" : "";
            }).AddTo(_subs);
```

- [ ] **Step 4: 통과 확인 (Unity)**

`refresh_unity` 후 `run_tests` `{"mode":"EditMode","assembly_names":["Tichu.Presentation.Tests"]}`.
Expected: 전부 PASS(279 + 4 = 283). 콘솔에 컴파일 경고 새로 없음.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/Tests/SeatLabelTests.cs Assets/_Project/Presentation/Tests/SeatLabelTests.cs.meta
git commit -m "feat(presentation): 남의 차례 남은 초를 이름표 옆에 — '왼쪽 · 12초'

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DKcF6kM8xBvvJwha5JQu1Y"
```

---

## 마무리 (태스크 뒤)

- 최종 회귀: core dotnet 전체, 서버 79, Unity Presentation 전체, Unity `RemoteAgentTests` 클래스 필터. `git diff main --stat -- core/src/Tichu.Server` 는 비어 있어야 한다.
- 개발 APK 빌드(`Tichu/Build Dev APK`, MCP 는 빌드 중 끊기므로 Editor.log `[DevBuild] 성공` 확인) → S23 설치 → 빌드가 바꾼 `ProjectSettings/ProjectSettings.asset` 은 `git checkout` 으로 되돌린다.
- 로컬 서버 `dotnet run --project core/src/Tichu.Server -- --fill-after 30`(PC LAN 192.168.45.213) 로 스펙 §7 실기 3건을 사용자에게 요청한다.
