# 기본 AI 강화 (검증 후 승격) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기본 난이도(Normal)의 PIMC를 4세계→16세계로 강화하고, 관전 딜레이와 탐색 연산을 겹쳐 지연비용 없이 체감 약점(EV 노이즈)을 해소한다.

**Architecture:** (1) `PimcDecisionAgent`에서 스레드풀 탐색을 관전 딜레이와 동시 실행(per-move ≈ max(딜레이,연산)). (2) `PolicyConfig.For(Normal)`을 16세계 강설정으로 re-point + 예산을 딜레이 수준으로 상향. (3) 플레이테스트로 체감 검증 → 격리 스윕으로 객관 확인·튜닝.

**Tech Stack:** C# / Unity 6000.3 / UniTask / NUnit EditMode. 순수 탐색코어는 `Tichu.GameFlow.Agents`(UnityEngine 무의존), 비동기 어댑터는 `Tichu.Presentation`(UniTask).

## Global Constraints

- 신규 asmdef **0개**. `AiAgent`·`core/` dotnet 트리 **무수정**(이번 작업은 PIMC 파라미터·어댑터만).
- **caller-aggression(+22/R) 보존** — 승격 설정도 `useCallerAggression: true`.
- **오라클 테스트 불가침**(AsyncGameDriverTests 등 변경 금지).
- ⚠️ **전체 `Tichu.Core.Tests` 실행 금지**(Sim 10만판 → 메인스레드 점유 → MCP-stuck). `run_tests(test_names=["<클래스명>"])` 클래스 필터로만 실행. **run_tests 전 PlayMode 정지 필수.**
- 기존 .cs **수정만**(신규 .cs 생성 없음) → `refresh_unity`로 충분(신규파일 `AssetDatabase.ImportAsset` 댄스 불요).
- feature 브랜치 `feat/p2-default-ai-strength`(이미 체크아웃됨). 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- 난이도 변화는 **PolicyConfig 주입만**으로(별도 룰/구현 없음).

---

### Task 1: 딜레이·연산 겹치기 (P2 인에이블러)

`PimcDecisionAgent.DecideTurnAsync`를 "딜레이 후 연산"(순차)에서 "연산·딜레이 동시"(겹침)로 바꾼다. EditMode엔 PlayerLoop가 없어 `UniTask.Delay`가 완료되지 않으므로, 타이밍 테스트를 위해 **주입 가능한 delay seam**(`delayOverride`, 기본 null → 운영 경로 불변)을 추가한다.

**Files:**
- Modify: `Assets/_Project/Presentation/Agents/PimcDecisionAgent.cs`
- Test: `Assets/_Project/Presentation/Tests/PimcDecisionAgentTests.cs`

**Interfaces:**
- Consumes: `PimcAgent.DecideTurnAnytime(DecisionContext, CancellationToken budget, CancellationToken abort)`(기존), `UniTask.RunOnThreadPool`.
- Produces: `PimcDecisionAgent` 생성자에 trailing optional 파라미터 `Func<CancellationToken, UniTask> delayOverride = null` 추가. 기존 호출부(`RoundBootstrap`)는 인자 미전달 → 영향 없음.

- [ ] **Step 1: 실패하는 겹침 타이밍 테스트 작성**

`PimcDecisionAgentTests.cs`에 추가(파일 상단 `using System.Diagnostics;` 필요):

```csharp
// 겹침 검증: 주입 delay(Task.Delay 기반 — EditMode 스레드풀 타이머로 완료)와 탐색이
// 동시에 진행되면 per-move ≈ max(delay, compute). 순차면 delay+compute.
// compute-only(c) 대비, delay 300ms를 끼워도 총시간 f < delay + 0.5*c 이어야 겹친 것.
[Test, Timeout(60000)]
public async Task DecideTurnAsync_overlaps_delay_with_compute()
{
    var cfg = new PolicyConfig(8, 4, 0.10);   // 측정가능한 연산량
    Func<CancellationToken, UniTask> immediate = _ => UniTask.CompletedTask;
    Func<CancellationToken, UniTask> delay300 = ct => Task.Delay(300, ct).AsUniTask();

    // compute-only 기준 c
    var swc = Stopwatch.StartNew();
    await new PimcDecisionAgent(Seed, 0, cfg, 5000, 0, null, immediate)
        .DecideTurnAsync(PlayCtx(0), default).AsTask();
    swc.Stop();
    long c = swc.ElapsedMilliseconds;

    // delay 300 끼운 f
    var swf = Stopwatch.StartNew();
    await new PimcDecisionAgent(Seed, 0, cfg, 5000, 0, null, delay300)
        .DecideTurnAsync(PlayCtx(0), default).AsTask();
    swf.Stop();
    long f = swf.ElapsedMilliseconds;

    Assert.That(f, Is.LessThan(300 + 0.5 * c),
        $"겹침이면 f≈max(300,c)≈300. 순차면 f≈300+c. (c={c}ms, f={f}ms)");
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `run_tests(test_names=["PimcDecisionAgentTests"])` (Unity, PlayMode 정지 후)
Expected: 컴파일 실패 — 생성자에 7번째 파라미터(`delayOverride`) 없음.

- [ ] **Step 3: delayOverride seam + 겹치기 구현**

`PimcDecisionAgent.cs` 생성자·필드·`DelayAsync`·`DecideTurnAsync` 수정:

```csharp
        private readonly Func<bool> _fastForward;
        private readonly Func<CancellationToken, UniTask> _delayOverride;

        public PimcDecisionAgent(ulong roundSeed, int seat, PolicyConfig config,
            int budgetMs, int delayMs, Func<bool> fastForward = null,
            Func<CancellationToken, UniTask> delayOverride = null)
        {
            _pimc = new PimcAgent(roundSeed, seat, config);
            _config = config;
            _budgetMs = budgetMs;
            _delayMs = delayMs;
            _fastForward = fastForward;
            _delayOverride = delayOverride;
        }

        private UniTask DelayAsync(CancellationToken ct)
        {
            if (_delayOverride != null) return _delayOverride(ct);
            return _fastForward != null && _fastForward() ? UniTask.CompletedTask : UniTask.Delay(_delayMs, cancellationToken: ct);
        }
```

`DecideTurnAsync` 본문 교체:

```csharp
        public async UniTask<TurnDecision> DecideTurnAsync(DecisionContext ctx, CancellationToken ct)
        {
            // Easy(탐색 OFF): 겹칠 연산이 없으므로 딜레이 후 즉시 휴리스틱.
            if (_config.Worlds <= 0)
            {
                await DelayAsync(ct);
                return _pimc.DecideTurn(ctx);
            }

            var snap = ctx.State.Clone();   // 가변 공유 차단(백그라운드 진입 전, 메인스레드).
            int seat = ctx.Seat;
            using var budgetCts = new CancellationTokenSource(_budgetMs);   // anytime 예산.
            var budget = budgetCts.Token;

            // 탐색을 스레드풀에 먼저 띄우고(핫 스타트) 관전 딜레이와 겹쳐 진행.
            // per-move ≈ max(딜레이, 연산) — 딜레이 이내 연산은 추가 지연 0.
            var compute = UniTask.RunOnThreadPool(
                () => _pimc.DecideTurnAnytime(new DecisionContext(snap, seat), budget, ct),
                cancellationToken: ct);
            await DelayAsync(ct);
            return await compute;
        }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `run_tests(test_names=["PimcDecisionAgentTests"])`
Expected: 5 기존 + 1 신규 = **6 PASS**. (겹침 테스트 PASS. 만약 FAIL이면 `RunOnThreadPool`이 콜드 스타트일 가능성 → `.Preserve()` 부착 또는 `compute`를 `var compute = ...;` 직후 `compute.Forget()` 금지하고 await 순서 유지로 재확인.)

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Agents/PimcDecisionAgent.cs Assets/_Project/Presentation/Tests/PimcDecisionAgentTests.cs
git commit -m "$(printf 'feat(p2f): DecideTurnAsync 딜레이·연산 겹치기\n\n관전 딜레이와 스레드풀 탐색을 동시 실행 → per-move=max(딜레이,연산).\n딜레이 이내 연산은 추가 지연 0. 테스트용 delayOverride seam 추가.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 2: Normal을 16세계 강설정으로 승격 (P3)

기본값 `Normal`을 현 `(4,2,0.10,caller)`에서 16세계 강설정 `(16,4,0.05,caller)`로 re-point하고, 예산을 딜레이 수준(900ms)으로 올린다(Task 1 겹치기 전제 → 추가 지연 0). 16세계 PIMC는 P2-D에서 휴리스틱 대비 +156~162/R·81% 승률로 이미 강함이 실측됨 → 근거 있는 승격(객관 확인은 Task 4).

**Files:**
- Modify: `Assets/_Project/GameFlow/Agents/PolicyConfig.cs:43`
- Modify: `Assets/_Project/Presentation/RoundBootstrap.cs:123`
- Test: `Assets/_Project/Tests/EditMode/PolicyConfigTests.cs`

**Interfaces:**
- Consumes: `PolicyConfig(int worlds, int rolloutsPerWorld, double epsilon, bool useReachProb = false, bool useCallerAggression = false)`(기존).
- Produces: `PolicyConfig.For(Difficulty.Normal)` == `(16, 4, 0.05, useReachProb:false, useCallerAggression:true)`. `BudgetMsFor(Normal)` == 900.

- [ ] **Step 1: 실패하는 프리셋 핀 테스트 작성**

`PolicyConfigTests.cs`에 추가(기존 테스트는 느슨해서 그대로 통과하므로 신규 프리셋을 핀):

```csharp
[Test]
public void Normal_promoted_to_16_world_strong_preset()
{
    var n = PolicyConfig.For(Difficulty.Normal);
    Assert.That(n.Worlds, Is.EqualTo(16), "P2-F 기본 강화: 16세계(EV 노이즈↓)");
    Assert.That(n.RolloutsPerWorld, Is.EqualTo(4));
    Assert.That(n.Epsilon, Is.EqualTo(0.05).Within(1e-9));
    Assert.That(n.UseReachProb, Is.False);
    Assert.That(n.UseCallerAggression, Is.True, "콜러 패스억제(+22/R) 보존");
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `run_tests(test_names=["PolicyConfigTests"])`
Expected: `Normal_promoted_to_16_world_strong_preset` FAIL (현재 Worlds=4).

- [ ] **Step 3: Normal 프리셋 + 예산 변경**

`PolicyConfig.cs:43` 교체:

```csharp
                case Difficulty.Normal: return new PolicyConfig(16, 4, 0.05, useCallerAggression: true);  // P2-F: 기본 강화 16세계(EV 노이즈↓), caller(+22/R) 보존
```

`RoundBootstrap.cs:123` 교체:

```csharp
                case Tichu.GameFlow.Agents.Difficulty.Normal: return 900;  // P2-F: 겹치기 전제, 관전 딜레이(~900ms) 이내로 탐색 흡수
```

- [ ] **Step 4: 테스트 통과 확인 (회귀 포함)**

Run: `run_tests(test_names=["PolicyConfigTests"])`
Expected: 신규 1 + 기존 5 = **6 PASS**. (기존 monotonic: 0≤16≤16≤24 ✓; Easy.ε0.25>Normal.ε0.05 ✓; caller Normal=true ✓.)

Run: `run_tests(test_names=["PimcAgentTests"])`
Expected: 전부 PASS(불변 — Normal 프리셋 값 변경은 PimcAgent 로직 무관).

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/GameFlow/Agents/PolicyConfig.cs Assets/_Project/Presentation/RoundBootstrap.cs Assets/_Project/Tests/EditMode/PolicyConfigTests.cs
git commit -m "$(printf 'feat(p2f): 기본 Normal을 16세계 강설정으로 승격\n\nNormal 4→16세계(EV 노이즈↓), rollouts 2→4, ε0.10→0.05, caller 보존.\n예산 80→900ms(겹치기로 흡수). 16세계 PIMC는 휴리스틱 대비 +156~162/R 실측.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 3: 플레이테스트 체크포인트 (체감 검증)

승격된 기본 AI를 인게임에서 직접 플레이해 (a) 응답성(겹치기 동작) (b) 수 품질(체감 약점 해소)을 확인. 이것이 "세계수↑가 체감 약점을 고치는가" 가설의 1차 검증 venue.

**Files:** 없음(수동 검증).

- [ ] **Step 1: 컴파일·회귀 그린 확인**

`refresh_unity` 후 `read_console`로 0 errors 확인. PlayMode 정지 상태에서:
Run: `run_tests(test_names=["PolicyConfigTests"])` → PASS
Run: `run_tests(test_names=["PimcDecisionAgentTests"])` → PASS

- [ ] **Step 2: 인게임 한 판 플레이**

Unity 에디터에서 PlayMode 진입 → AI 대전 한 라운드 이상. 관찰:
- **응답성**: AI 1수 간격이 ~900ms(딜레이) 수준인가(≈1.8s가 아니라). → 겹치기 OK.
- **수 품질**: 지난 약점(K부터 리드, 저-싱글 전쟁, 티츄 외치고 패스, 트리플 흘림)이 줄었는가.
- **무에러 완주**: 특수상황(폭탄·용·개·마작) 정상.

- [ ] **Step 3: 관찰 기록**

체감 결과를 사용자와 공유. 약점이 줄었으면 가설 1차 확인. 여전하면(고카드 리드 등) → 그 수가 노이즈인지 옳은 EV인지 Task 4 데이터로 판별. PlayMode 정지.

> 커밋 없음(수동 검증 단계).

---

### Task 4: 격리 스윕으로 객관 확인·튜닝 (P1 검증)

기존 `PimcBench.RunMirrored`를 후보 프리셋에 동일 시드로 돌려 `세계수→강도`·`세계수→ms/라운드`를 측정. 신규 코드 0 — **운영 스윕**(execute_code 백그라운드 Task.Run + 파일 폴링). Task 3과 병렬 실행 가능(플레이테스트 동안 백그라운드 진행).

**Files:** 없음(execute_code 운영 스크립트, 커밋 안 함). 리포트 `티츄_기본AI강화_검증.html`(루트, 선택).

**Interfaces:**
- Consumes: `Tichu.Core.Tests.Bench.PimcBench.RunMirrored(int pairs, ulong baseSeed, PolicyConfig)` → `BenchResult{Rounds, PimcDiffSum, PimcWins}`; `Tichu.Core.Tests.Bench.BenchStats.WilsonLowerBound(wins, n)`.

- [ ] **Step 1: 스윕 스크립트 백그라운드 실행**

`execute_code`로 (테스트 어셈블리 타입 직접 호출, 메인스레드 비차단):

```csharp
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Tichu.GameFlow.Agents;
using Tichu.Core.Tests.Bench;

var outPath = Path.Combine(Application.dataPath, "../sweep_result.txt");
var configs = new (string name, PolicyConfig cfg)[] {
    ("today  (4,2,.10)",  new PolicyConfig(4,  2, 0.10, useCallerAggression: true)),
    ("cand8  (8,4,.05)",  new PolicyConfig(8,  4, 0.05, useCallerAggression: true)),
    ("cand16 (16,4,.05)", new PolicyConfig(16, 4, 0.05, useCallerAggression: true)),
    ("cand24 (24,6,.00)", new PolicyConfig(24, 6, 0.00, useCallerAggression: true)),
};
const int pairs = 12;            // 24 라운드/설정(파일럿). 신호 좋으면 ↑.
const ulong baseSeed = 100000UL; // 전 설정 동일딜(페어링).

Task.Run(() => {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"[sweep] pairs={pairs} (rounds/cfg={pairs*2}) baseSeed={baseSeed} caller=true");
    foreach (var (name, cfg) in configs) {
        var sw = Stopwatch.StartNew();
        var r = PimcBench.RunMirrored(pairs, baseSeed, cfg);
        sw.Stop();
        double avg = (double)r.PimcDiffSum / r.Rounds;
        double wl  = BenchStats.WilsonLowerBound(r.PimcWins, r.Rounds);
        double msPerRound = (double)sw.ElapsedMilliseconds / r.Rounds;
        sb.AppendLine($"{name} | avgDiff={avg,7:F1} | win={r.PimcWins}/{r.Rounds} ({100.0*r.PimcWins/r.Rounds:F0}%) | wilsonL={wl:F3} | {msPerRound:F0} ms/round");
        File.WriteAllText(outPath, sb.ToString());   // 점진 기록(폴링용)
    }
    sb.AppendLine("[done]");
    File.WriteAllText(outPath, sb.ToString());
});
"sweep started → poll sweep_result.txt";
```

⚠️ 도메인 리로드 시 Task.Run ThreadAbort 가능 → 재실행. ⚠️ cand16/24는 라운드당 ~수~십수 초 → 전체 수십 분(백그라운드). pairs를 줄여 빠른 신호 후 확대 가능.

- [ ] **Step 2: 결과 폴링**

`sweep_result.txt`를 주기적으로 Read. `[done]` 나올 때까지. 각 설정의 avgDiff·승률·wilsonL·ms/round 수집.

- [ ] **Step 3: 결정 게이트**

읽고 판정:
- **cand16 ≫ today (avg·wilsonL 뚜렷↑)** 그리고 cand24가 cand16 대비 유의 향상 아님 → **Normal=16 유지**(Task 2 그대로). 완료.
- **cand8 ≈ cand16 (포화)** → Normal을 8세계로 낮춰 동급 강도에 더 빠른 응답(Task 2 재방문: `(8,4,0.05,caller)`, 예산 재산정).
- **cand24 ≫ cand16 + 지연 허용범위** → Normal=24 고려(예산·체감 재확인).
- **어느 것도 today 대비 뚜렷↑ 아님** → 가설 약함. 승격 근거 재검(체감 약점이 세계수 외 원인일 가능성) — Task 3 정성 관찰과 대조해 사용자와 방향 재논의.

- [ ] **Step 4: (선택) 결과 리포트 + 정리**

핵심 수치를 `티츄_기본AI강화_검증.html`로 정리(SVG 곡선 선택). `sweep_result.txt`는 임시파일 → 삭제. 결정에 따라 Task 2 재방문 시 그 커밋에 반영.

---

## Self-Review

**1. Spec coverage:**
- 스펙 §3(P1 검증) → Task 4(RunMirrored 스윕 + ms/round 지연). ✅ (스펙의 TimingAgent는 ms/round 프록시로 단순화 — YAGNI; ms/move는 ÷결정수 + 플레이테스트로 충분.)
- 스펙 §4(P2 겹치기) → Task 1. ✅
- 스펙 §5(P3 승격 + 킬 브랜치) → Task 2 + Task 4 Step 3 결정 게이트. ✅
- 스펙 §6(테스트) → 각 Task TDD + 회귀 클래스필터 + 플레이테스트(Task 3). ✅
- caller-aggression 보존 → Task 2 프리셋 `caller:true` + 핀 테스트. ✅

**2. Placeholder scan:** 모든 코드 스텝에 실제 코드. "TBD/적절히" 없음. ✅

**3. Type consistency:** `PolicyConfig(worlds, rollouts, epsilon, useReachProb, useCallerAggression)`·`RunMirrored(pairs, baseSeed, cfg)`·`BenchResult{Rounds,PimcDiffSum,PimcWins}`·`WilsonLowerBound(wins,n)`·`DecideTurnAnytime(ctx, budget, abort)`·생성자 신규 파라미터 `delayOverride` — 전 Task 일관. ✅

**의도적 스펙 편차(투명):** 스펙 §3.2의 `TimingAgent` 데코레이터는 만들지 않는다 — 지연은 `RunMirrored`를 감싼 Stopwatch의 ms/round로 측정(CLAUDE.md 단순함). 정밀 ms/move·모바일 실측은 플레이테스트 + 별도 D6 실기기 게이트가 진실원.

**순서 메모:** 스펙은 P1→P2→P3이나, 사용자 지시("일단 구현하고 플레이테스트")에 따라 P2(겹치기)→P3(승격)→플레이테스트→P1(객관 확인)로 재배열. 16세계 강도는 P2-D 실측으로 이미 근거 있어 선승격이 맹목 아님. 킬/튜닝 분기는 Task 4 결정 게이트에 보존.
