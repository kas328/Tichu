# D3 v1 구조 보존형 리드 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `AiAgent.DecideLead`(5장 초과·non-lockout 리드)가 가치 구조(스트레이트/트리플/풀하우스/연속페어)를 깨는 싱글을 흘리지 않게 만들고, HeuristicStrengthBench로 회귀 없음을 확인해 채택 여부를 결정한다.

**Architecture:** 현재 "점수 없는 가장 낮은 수" 선택 앞에 구조 보존 필터를 끼운다 — 손패를 보수적 그리디 분해해 길이≥3 콤보에 묶인 랭크를 "커밋"으로 표시하고, 커밋 랭크의 싱글 리드를 후보에서 제외한 뒤 `MoveOrder.Lowest`. 제외 후 비면 현행 폴백. 공유 `AiAgent`라 PIMC 롤아웃·라이브 리드·Easy 직접결정에 전파(의도됨).

**Tech Stack:** C# / Unity 6000.3 EditMode(NUnit) + .NET(core Tichu.Core.Tests) / 격리 자가대국 벤치 HeuristicStrengthBench.

## Global Constraints

- 스펙: `docs/superpowers/specs/2026-07-16-d3-structure-preserving-lead-design.md`.
- **미러 동기화 필수**: 동일 변경을 두 곳에 — Unity `Assets/_Project/GameFlow/Agents/AiAgent.cs` + core `core/src/Tichu.GameFlow/Agents/AiAgent.cs`.
- 파일 스타일 준수: LINQ·Clone 룩어헤드 금지, `MoveOrder`가 정렬/선택 단일 출처, RNG는 동점 처리만.
- **작업은 feature 브랜치에서.** 현재 main. 커밋은 하되 머지/푸시는 사용자 승인 후.
- Unity 테스트: MCP `run_tests(test_names=["...AiAgentTests"])` 클래스 필터만. **전체 Tichu.Core.Tests 실행 금지**(Sim 10만판 stuck). run_tests 전 PlayMode 정지. 신규/수정 .cs는 execute_code로 `AssetDatabase.ImportAsset`+`Refresh(ForceUpdate)` 후 컴파일 확인.
- HeuristicStrengthBench: core dotnet 전용, `[Explicit]` 임시 제거해 실행, 리포트는 `%TEMP%/tichu_heuristic_strength.txt`.
- **수용 기준**: 회귀(분명한 음수)면 폐기(플래그·플래너 확장 안 함, 파킹). 회귀 없으면 채택, 양수면 승리.

---

### Task 1: 브랜치 + 베이스라인 동결(OldAiAgent 재생성)

변경 **전** 현재 core `AiAgent`를 `OldAiAgent`로 동결해 벤치가 이번 변경만 격리 측정하게 한다.

**Files:**
- Create/overwrite: `core/tests/Tichu.Core.Tests/OldAiAgent.cs` (현재 core `AiAgent.cs`의 변환 사본)
- Read: `core/src/Tichu.GameFlow/Agents/AiAgent.cs`

**Interfaces:**
- Produces: `Tichu.Core.Tests.Bench.OldAiAgent : IAgent` — 벤치의 구(舊) 베이스라인. `new OldAiAgent(ulong seed, int seat)`.

- [ ] **Step 1: feature 브랜치 생성**

```bash
git checkout -b feat/d3-structure-preserving-lead
```

- [ ] **Step 2: OldAiAgent를 현재 core AiAgent에서 재생성**

Bash 도구(Git Bash)에서:

```bash
cd "C:/Users/user/Desktop/Project/Tichu"
SRC=core/src/Tichu.GameFlow/Agents/AiAgent.cs
DST=core/tests/Tichu.Core.Tests/OldAiAgent.cs
sed -e 's/^namespace Tichu\.GameFlow\.Agents/namespace Tichu.Core.Tests.Bench/' \
    -e 's/\bclass AiAgent\b/class OldAiAgent/' \
    -e 's/public AiAgent(/public OldAiAgent(/' \
    -e 's/^using Tichu\.Core\.Game;/using Tichu.Core.Game;\nusing Tichu.GameFlow;\nusing Tichu.GameFlow.Agents;/' \
    "$SRC" > "$DST"
```

- [ ] **Step 3: 컴파일 확인(구 베이스라인이 현재 상태를 반영)**

Run: `dotnet build core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj -v q`
Expected: 빌드 성공(0 errors). `OldAiAgent.cs`의 `SmallTichuThreshold`가 7(현재값)로 갱신됐는지 눈으로 확인 — 이번 변경 직전 상태여야 함.

- [ ] **Step 4: 커밋**

```bash
git add core/tests/Tichu.Core.Tests/OldAiAgent.cs docs/superpowers/specs/2026-07-16-d3-structure-preserving-lead-design.md docs/superpowers/plans/2026-07-16-d3-structure-preserving-lead.md
git commit -m "chore(bench): D3 착수 — OldAiAgent 베이스라인 동결 + 스펙/플랜"
```

---

### Task 2: 실패 테스트 — 구조 보존형 리드 (Unity + core 미러)

**Files:**
- Modify: `Assets/_Project/Tests/EditMode/AiAgentTests.cs` (테스트 4개 추가)
- Modify: `core/tests/Tichu.Core.Tests/AiAgentTests.cs` (동일 4개 미러)

**Interfaces:**
- Consumes: 기존 헬퍼 `GameFlowHelpers.PlayState(int turn, params IReadOnlyList<Card>[] hands)`, `GameFlowHelpers.Context(GameState, int seat)`, `Hand(...)`, `N(int rank, Suit)`, `Single(int rank)`; `AiAgent.DecideTurn` → `TurnDecision`(`.IsPass`, `.Move!.Type`, `.Move!.Rank`, `.Move!.Cards`).

- [ ] **Step 1: 실패 테스트 4개 작성 (Unity `AiAgentTests.cs`의 리드 테스트 섹션 근처에 추가)**

```csharp
// ── D3: 구조 보존형 리드 ──────────────────────────────────────────────────────

[Test]
public void DecideLead_does_not_break_straight_with_low_single()
{
    // seat0 리드(8장): 스트레이트 2-3-4-5-6 + 헐거운 9,J,K. 상대는 모두 3장(near-out 아님).
    // 현행: 최저 싱글(2)을 흘려 스트레이트를 깬다. → 구조 밖 최저 싱글(9)로 리드해야 한다.
    var s = GameFlowHelpers.PlayState(0,
        Hand(N(2, Suit.Jade), N(3, Suit.Jade), N(4, Suit.Jade), N(5, Suit.Jade), N(6, Suit.Jade),
             N(9, Suit.Sword), N(11, Suit.Pagoda), N(13, Suit.Star)),
        Hand(N(2, Suit.Sword), N(7, Suit.Sword), N(8, Suit.Sword)),
        Hand(N(2, Suit.Pagoda), N(7, Suit.Pagoda), N(8, Suit.Pagoda)),
        Hand(N(2, Suit.Star), N(7, Suit.Star), N(8, Suit.Star)));
    var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
    Assert.That(d.IsPass, Is.False);
    Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Single));
    Assert.That(d.Move!.Rank, Is.EqualTo(Single(9).Rank),
        "스트레이트(2-6) 구성 싱글이 아닌 구조 밖 최저 싱글(9)로 리드");
}

[Test]
public void DecideLead_does_not_fracture_triple_with_low_single()
{
    // seat0 리드(8장): 트리플 4-4-4 + 헐거운 6,8,9,J,Q. 상대 모두 3장.
    // 현행: 최저 싱글(4)을 흘려 트리플을 쪼갠다. → 4 싱글을 홀로 내지 않는다(구조 밖 싱글 또는 트리플 통째).
    var s = GameFlowHelpers.PlayState(0,
        Hand(N(4, Suit.Jade), N(4, Suit.Sword), N(4, Suit.Pagoda),
             N(6, Suit.Star), N(8, Suit.Jade), N(9, Suit.Sword), N(11, Suit.Pagoda), N(12, Suit.Star)),
        Hand(N(2, Suit.Sword), N(7, Suit.Sword), N(3, Suit.Sword)),
        Hand(N(2, Suit.Pagoda), N(7, Suit.Pagoda), N(3, Suit.Pagoda)),
        Hand(N(2, Suit.Star), N(7, Suit.Star), N(3, Suit.Star)));
    var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
    Assert.That(d.IsPass, Is.False);
    bool fracturesTriple = d.Move!.Type == CombinationType.Single && d.Move!.Rank == Single(4).Rank;
    Assert.That(fracturesTriple, Is.False, "트리플(4)을 싱글로 쪼개 리드하지 않는다");
}

[Test]
public void DecideLead_unchanged_when_no_structure()
{
    // 회귀 가드: 스트레이트/트리플/연속페어 없는 흩어진 손 → 현행대로 최저 싱글(2).
    var s = GameFlowHelpers.PlayState(0,
        Hand(N(2, Suit.Jade), N(4, Suit.Sword), N(6, Suit.Pagoda), N(8, Suit.Star),
             N(9, Suit.Jade), N(11, Suit.Sword), N(12, Suit.Pagoda), N(14, Suit.Star)),
        Hand(N(3, Suit.Sword), N(7, Suit.Sword), N(5, Suit.Sword)),
        Hand(N(3, Suit.Pagoda), N(7, Suit.Pagoda), N(5, Suit.Pagoda)),
        Hand(N(3, Suit.Star), N(7, Suit.Star), N(5, Suit.Star)));
    var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
    Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Single));
    Assert.That(d.Move!.Rank, Is.EqualTo(Single(2).Rank), "구조 없음 → 최저 싱글(2) 불변");
}

[Test]
public void DecideLead_returns_legal_lead_when_all_low_singles_committed()
{
    // 폴백 가드: 저싱글이 전부 구조에 묶임({3,3,3,4,4,4}) → 패스 아님 + 합법 리드.
    var s = GameFlowHelpers.PlayState(0,
        Hand(N(3, Suit.Jade), N(3, Suit.Sword), N(3, Suit.Pagoda),
             N(4, Suit.Jade), N(4, Suit.Sword), N(4, Suit.Pagoda)),
        Hand(N(2, Suit.Sword), N(7, Suit.Sword), N(9, Suit.Sword)),
        Hand(N(2, Suit.Pagoda), N(7, Suit.Pagoda), N(9, Suit.Pagoda)),
        Hand(N(2, Suit.Star), N(7, Suit.Star), N(9, Suit.Star)));
    var ctx = GameFlowHelpers.Context(s, 0);
    var d = new AiAgent(1UL, 0).DecideTurn(ctx);
    Assert.That(d.IsPass, Is.False);
    Assert.That(ctx.LegalMoves.Any(m => m.Rank == d.Move!.Rank && m.Type == d.Move!.Type), Is.True);
}
```

- [ ] **Step 2: 동일 4개를 core `AiAgentTests.cs`에 미러 추가** (같은 헬퍼가 core에 존재; 파일 상단 `using System.Linq;` 유무 확인 후 `.Any(...)` 사용 위해 없으면 추가)

- [ ] **Step 3: Unity 임포트 + 컴파일 확인**

execute_code로 `AssetDatabase.ImportAsset("Assets/_Project/Tests/EditMode/AiAgentTests.cs", ImportAssetOptions.ForceUpdate)` + `AssetDatabase.Refresh()` 후 `read_console`로 컴파일 에러 0 확인.

- [ ] **Step 4: 실패 확인 (Unity + core)**

Unity: `run_tests(test_names=["Tichu.Core.Tests.AiAgentTests"])` (PlayMode 정지 상태).
Expected: `DecideLead_does_not_break_straight_with_low_single` FAIL(현행이 단일 2 리드), `DecideLead_does_not_fracture_triple_with_low_single` FAIL(현행이 단일 4). 나머지 2개는 가드라 PASS 가능.
core: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~AiAgentTests"`
Expected: 위 2개 FAIL.

- [ ] **Step 5: 커밋(레드 상태)**

```bash
git add Assets/_Project/Tests/EditMode/AiAgentTests.cs core/tests/Tichu.Core.Tests/AiAgentTests.cs
git commit -m "test(ai): D3 구조 보존형 리드 실패 테스트(스트레이트/트리플 보존)"
```

---

### Task 3: 구현 — StructurePreservingLeads + CommittedRanks (Unity + core 미러)

**Files:**
- Modify: `Assets/_Project/GameFlow/Agents/AiAgent.cs` (DecideLead 251–253 대체 + 헬퍼 2개 추가)
- Modify: `core/src/Tichu.GameFlow/Agents/AiAgent.cs` (동일)

**Interfaces:**
- Produces (private static): `List<Combination> StructurePreservingLeads(IReadOnlyList<Card> hand, IReadOnlyList<Combination> candidates)`, `bool[] CommittedRanks(IReadOnlyList<Card> hand)`. 기존 `StraightRanks(IReadOnlyList<Card>)` 재사용.

- [ ] **Step 1: DecideLead의 noPoint 선택부를 구조 필터로 교체**

`AiAgent.cs` 현행:

```csharp
                    // 평소: 점수 없는 가장 낮은 수를 선호. 없으면 그냥 가장 낮은 수.
                    var noPoint = new List<Combination>();
                    for (int i = 0; i < pool.Count; i++)
                        if (pool[i].PointsInPlay == 0) noPoint.Add(pool[i]);
                    chosen = MoveOrder.Lowest(noPoint.Count > 0 ? noPoint : pool)!;
```

교체:

```csharp
                    // 평소: 점수 없는 가장 낮은 수를 선호. 없으면 그냥 가장 낮은 수.
                    var noPoint = new List<Combination>();
                    for (int i = 0; i < pool.Count; i++)
                        if (pool[i].PointsInPlay == 0) noPoint.Add(pool[i]);
                    var candidates = noPoint.Count > 0 ? noPoint : pool;
                    // D3: 가치 구조(스트레이트≥5·트리플·연속페어)를 홀로 깨는 싱글은 후보에서 제외.
                    var structured = StructurePreservingLeads(ctx.MyHand, candidates);
                    chosen = MoveOrder.Lowest(structured)!;
```

- [ ] **Step 2: 헬퍼 두 개 추가 (`StraightRanks` 부근, 보조 섹션에)**

```csharp
        /// <summary>D3: 리드 후보 중 "가치 구조를 홀로 깨는 싱글"을 제외한다. 제외 후 비면 원본 유지(폴백).
        /// 커밋된 랭크의 싱글 리드만 거른다(콤보 리드·구조 밖 싱글은 유지) — 콤보 통째 셰딩은 허용.</summary>
        private static List<Combination> StructurePreservingLeads(
            IReadOnlyList<Card> hand, IReadOnlyList<Combination> candidates)
        {
            var committed = CommittedRanks(hand);
            var kept = new List<Combination>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                var m = candidates[i];
                if (m.Type == CombinationType.Single && m.Cards.Count == 1)
                {
                    var c = m.Cards[0];
                    if (!c.IsSpecial && c.Rank >= 1 && c.Rank <= 14 && committed[c.Rank])
                        continue;   // 구조 깨는 싱글 → 제외
                }
                kept.Add(m);
            }
            return kept.Count > 0 ? kept : new List<Combination>(candidates);
        }

        /// <summary>D3 커밋 랭크: 길이≥3 가치 콤보(스트레이트≥5·트리플/풀하우스 트리플부·연속페어)에 묶인 랭크.
        /// 보수적 — 애매하면 커밋 아님(과-제외 시 폴백이 원본 복원). rank 인덱스 bool[15].</summary>
        private static bool[] CommittedRanks(IReadOnlyList<Card> hand)
        {
            var committed = new bool[15];
            // ① 스트레이트(≥5) — 기존 로직 재사용.
            var inRun = StraightRanks(hand);
            for (int r = 1; r <= 14; r++) if (inRun[r]) committed[r] = true;
            // 랭크별 장수.
            var count = new int[15];
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                if (!c.IsSpecial && c.Rank >= 2 && c.Rank <= 14) count[c.Rank]++;
            }
            // ② 트리플/포카드(≥3장) → 커밋(풀하우스의 트리플부 포함).
            for (int r = 2; r <= 14; r++) if (count[r] >= 3) committed[r] = true;
            // ③ 연속페어(stairs, ≥2쌍) → 커밋.
            for (int r = 2; r <= 13; r++)
                if (count[r] >= 2 && count[r + 1] >= 2) { committed[r] = true; committed[r + 1] = true; }
            return committed;
        }
```

- [ ] **Step 3: core `AiAgent.cs`에 동일 변경 미러**

동일한 DecideLead 교체 + 헬퍼 2개를 `core/src/Tichu.GameFlow/Agents/AiAgent.cs`에 적용. 두 파일 diff가 동일해야 함.

- [ ] **Step 4: Unity 임포트 + 컴파일 확인**

execute_code로 `AiAgent.cs` ForceUpdate 임포트 + Refresh, `read_console` 에러 0 확인.

- [ ] **Step 5: 그린 확인 (Unity + core)**

Unity: `run_tests(test_names=["Tichu.Core.Tests.AiAgentTests"])` → 4개 신규 테스트 + 기존 리드/팔로우 테스트 전부 PASS.
core: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~AiAgentTests"` → PASS.
기존 잠금 테스트가 깨지면 **의도된 변경만** 갱신(예상 밖이면 멈추고 원인 규명).

- [ ] **Step 6: 커밋(그린)**

```bash
git add Assets/_Project/GameFlow/Agents/AiAgent.cs core/src/Tichu.GameFlow/Agents/AiAgent.cs
git commit -m "feat(ai): D3 구조 보존형 리드 — 스트레이트/트리플/연속페어 깨는 싱글 제외"
```

---

### Task 4: 강도 벤치 — HeuristicStrengthBench 4000R + 채택 판정

**Files:**
- Modify(임시): `core/tests/Tichu.Core.Tests/HeuristicStrengthBench.cs` (`[Explicit]` 임시 제거 → 실행 → 복원)

**Interfaces:**
- Consumes: `New = AiAgent`(변경본), `Old = OldAiAgent`(Task 1 동결). Produces: `%TEMP%/tichu_heuristic_strength.txt`(avg/R·win%·WilsonLB).

- [ ] **Step 1: `[Explicit]` 임시 제거**

`HeuristicStrengthBench.cs`의 `[Explicit, Category("Bench")]` → `[Category("Bench")]`.

- [ ] **Step 2: 벤치 실행(백그라운드, ms 단위지만 4000R)**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~HeuristicStrengthBench"`
Expected: PASS(어서션 없음). 결과 텍스트를 `%TEMP%/tichu_heuristic_strength.txt`에서 읽는다.

- [ ] **Step 3: 결과 기록 + 판정**

리포트 `rounds=4000 newAvg=?/R newWins=?/4000 (?%) WilsonLB=?` 를 읽는다.
- **회귀(newAvg 분명한 음수 또는 WilsonLB≪0.5)** → 변경 폐기(Task 6-B).
- **회귀 없음(±노이즈)** → 채택(관측 교정형, #2/C1 선례). **양수 + WilsonLB>0.5** → 강도 승리(① 선례).

- [ ] **Step 4: `[Explicit]` 복원**

`[Category("Bench")]` → `[Explicit, Category("Bench")]`.

---

### Task 5: 마무리 — 채택 경로 (회귀 없음)

- [ ] **Step 1: 결과 리포트 HTML 작성** — `티츄_D3_구조보존리드_벤치결과.html`(프로젝트 컨벤션, SVG로 avg/R·WilsonLB). 스펙 대비 결론 명시.

- [ ] **Step 2: 전체 관련 스위트 그린 재확인** — Unity `run_tests` AiAgentTests + PimcAgentTests(공유 가드 영향 없음 확인), core dotnet AiAgentTests. 알려진 `WastefulComboOvertake` 발산은 기존 이슈.

- [ ] **Step 3: 커밋 + 브랜치 마무리**

```bash
git add -A
git commit -m "docs(ai): D3 구조 보존형 리드 벤치 결과 + 채택"
```
그 후 superpowers:finishing-a-development-branch로 머지/PR 옵션 제시(머지·푸시는 사용자 승인 후).

---

### Task 6-B: 마무리 — 폐기 경로 (회귀 발생 시)

- [ ] **Step 1** 구현 커밋(Task 3) revert, 헬퍼 제거. Task 1의 OldAiAgent 동결은 되돌릴 필요 없음(다음 레버 baseline로 유효하나, 변경 없으면 New==Old라 롤백 권장).
- [ ] **Step 2** 스펙/플랜/벤치 결과에 "파킹 — 회귀 X.X/R" 기록(리드계열 회귀 선례 축적).
- [ ] **Step 3** 사용자에게 결과 보고 후 다음 레버(D2 텔레그래핑 등) 논의.
