# Grand Tichu 콜 헤드 (B1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** crude 휴리스틱 게이트 `HandPower(hand)>=10` 을, self-play로 학습한 로지스틱 헤드 `P(이 8장이 먼저 나갈 확률)>τ` 로 교체하고, 격리 벤치(Wilson LB>0.5)로 채택/파킹 판정한다.

**Architecture:** 5개 격리 유닛 — 순수 인코더(`GrandTichuFeatures`), 로지스틱 추론(`CallNet`+`GrandTichuWeights.g.cs`), `AiAgent.CallGrandTichu` 플래그 통합(전 티어 위임 체인의 단일 그라운드트루스), 오프라인 트레이너/벤치(`core/tests`, `[Explicit]`). 플래그 OFF면 오늘과 비트 동일.

**Tech Stack:** C# / .NET (netstandard2.1 런타임, net9.0 NUnit 테스트), Unity 6000.x. 파이썬 0 · 패키지 0.

## Global Constraints

- **미러**: 런타임 유닛은 `Assets\_Project\GameFlow\Agents\` 와 `core\src\Tichu.GameFlow\Agents\` 에 **바이트 동일**하게 둔다. 단위테스트는 `Assets\_Project\Tests\EditMode\` 와 `core\tests\Tichu.Core.Tests\` 양쪽. 오프라인 유닛은 `core\tests\Tichu.Core.Tests\` 만.
- **비트불변**: 새 플래그는 전부 기본 `false`. OFF면 기존 동작·기존 테스트 보존.
- **패키지 0 · 새 asmdef 0 · 파이썬 0**. csproj/asmdef는 폴더 글로빙이라 새 `.cs`는 자동 포함(편집 불필요).
- **데이터 위생**: 학습 시드 `[1 .. N]`, 벤치 시드 `[10_000_000 ..]` — 완전 분리.
- ⚠️ **전체 `Tichu.Core.Tests` 실행 금지**(Sim 10만판 hang). 항상 `--filter "FullyQualifiedName~<클래스>"`.
- ⚠️ Unity 신규 `.cs`는 `refresh_unity` 후 `read_console`로 컴파일 확인.
- 네임스페이스: 런타임 = `Tichu.GameFlow.Agents`, 테스트 = 기존 파일 관례 따름.
- API 참조: `Card.Rank`(A=14,K=13,Q=12,J=11), `Card.Special`(`SpecialKind{None,Mahjong,Dog,Phoenix,Dragon}`), `Card.Normal(rank, Suit)`, static `Card.Dragon/Phoenix/Dog/Mahjong`, `Suit{Jade,Sword,Pagoda,Star,Special}`. `DecisionContext.MyHand → IReadOnlyList<Card>`. `GameEngine.NewRound(seed)`→Phase=GrandTichuDecision·좌석당 8장. `RoundOutcome.State.Seats[i].FinishOrder`(1=먼저 나감).

---

### Task 1: GrandTichuFeatures (인코더)

**Files:**
- Create (양 미러, 동일): `Assets\_Project\GameFlow\Agents\GrandTichuFeatures.cs`, `core\src\Tichu.GameFlow\Agents\GrandTichuFeatures.cs`
- Test (양 미러, 동일): `Assets\_Project\Tests\EditMode\GrandTichuFeaturesTests.cs`, `core\tests\Tichu.Core.Tests\GrandTichuFeaturesTests.cs`

**Interfaces:**
- Produces: `public static class GrandTichuFeatures { public const int FeatureCount = 16; public static float[] Encode(IReadOnlyList<Card> hand); }`

- [ ] **Step 1: 실패 테스트 작성** — 양 테스트 파일에 동일 내용.

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.GameFlow.Agents;

namespace Tichu.GameFlow.Tests
{
    public class GrandTichuFeaturesTests
    {
        // 손패: A♦ A♠ K♦ Q♦ J♦ 10♦ 용 봉황 (8장)
        private static List<Card> SampleHand() => new List<Card>
        {
            Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Sword),
            Card.Normal(13, Suit.Jade), Card.Normal(12, Suit.Jade),
            Card.Normal(11, Suit.Jade), Card.Normal(10, Suit.Jade),
            Card.Dragon, Card.Phoenix,
        };

        [Test]
        public void Encode_length_is_FeatureCount()
        {
            Assert.That(GrandTichuFeatures.Encode(SampleHand()).Length, Is.EqualTo(GrandTichuFeatures.FeatureCount));
        }

        [Test]
        public void Encode_sample_hand_matches_expected_vector()
        {
            var x = GrandTichuFeatures.Encode(SampleHand());
            Assert.That(x[0], Is.EqualTo(0.5f).Within(1e-4));   // #A = 2/4
            Assert.That(x[1], Is.EqualTo(0.25f).Within(1e-4));  // #K
            Assert.That(x[2], Is.EqualTo(0.25f).Within(1e-4));  // #Q
            Assert.That(x[3], Is.EqualTo(0.25f).Within(1e-4));  // #J
            Assert.That(x[4], Is.EqualTo(0.25f).Within(1e-4));  // #10
            Assert.That(x[5], Is.EqualTo(1f));                  // 용
            Assert.That(x[6], Is.EqualTo(1f));                  // 봉황
            Assert.That(x[7], Is.EqualTo(0f));                  // 마작
            Assert.That(x[8], Is.EqualTo(0f));                  // 개
            Assert.That(x[9], Is.EqualTo(0.25f).Within(1e-4));  // #페어 = 1/4 (랭크 14가 2장)
            Assert.That(x[10], Is.EqualTo(0f));                 // #트리플
            Assert.That(x[11], Is.EqualTo(0f));                 // #폭탄
            Assert.That(x[12], Is.EqualTo(0.625f).Within(1e-4));// 최장 스트레이트 5/8 (10..14)
            Assert.That(x[13], Is.EqualTo(0.625f).Within(1e-4));// 랭크>=11 장수 5/8
            Assert.That(x[14], Is.EqualTo(74f/112f).Within(1e-4));// 랭크합 74/112
            Assert.That(x[15], Is.EqualTo(1f));                 // 고특수(용+봉황) 2/2
        }

        [Test]
        public void Encode_is_deterministic()
        {
            var a = GrandTichuFeatures.Encode(SampleHand());
            var b = GrandTichuFeatures.Encode(SampleHand());
            Assert.That(a, Is.EqualTo(b));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~GrandTichuFeaturesTests"`
Expected: FAIL — `GrandTichuFeatures` 미정의(컴파일 에러).

- [ ] **Step 3: 인코더 구현** — 양 미러에 동일 파일.

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// Grand Tichu 콜 헤드 입력 인코더. 8장 손패 → 고정 길이 피처 벡터(hand-only).
    /// 데이터생성과 추론이 이 한 함수를 공유해 train/serve 스큐를 차단한다. 순수·결정적.
    /// </summary>
    public static class GrandTichuFeatures
    {
        public const int FeatureCount = 16;

        public static float[] Encode(IReadOnlyList<Card> hand)
        {
            var rankCount = new int[16]; // 인덱스 = Rank (마작=1, 일반 2..14)
            int aces = 0, kings = 0, queens = 0, jacks = 0, tens = 0, highCount = 0, rankSum = 0;
            bool hasDragon = false, hasPhoenix = false, hasMahjong = false, hasDog = false;

            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                switch (c.Special)
                {
                    case SpecialKind.Dragon:  hasDragon = true; break;
                    case SpecialKind.Phoenix: hasPhoenix = true; break;
                    case SpecialKind.Mahjong: hasMahjong = true; rankCount[1]++; break;
                    case SpecialKind.Dog:     hasDog = true; break;
                    default: // None (일반 카드)
                        int r = c.Rank;
                        if (r >= 1 && r <= 15) rankCount[r]++;
                        rankSum += r;
                        if (r == 14) aces++;
                        else if (r == 13) kings++;
                        else if (r == 12) queens++;
                        else if (r == 11) jacks++;
                        else if (r == 10) tens++;
                        if (r >= 11) highCount++;
                        break;
                }
            }

            int pairs = 0, triples = 0, bombs = 0;
            for (int r = 1; r <= 15; r++)
            {
                if (rankCount[r] >= 2) pairs++;
                if (rankCount[r] >= 3) triples++;
                if (rankCount[r] == 4) bombs++;
            }
            int longest = LongestStraight(rankCount);

            var x = new float[FeatureCount];
            x[0]  = aces   / 4f;
            x[1]  = kings  / 4f;
            x[2]  = queens / 4f;
            x[3]  = jacks  / 4f;
            x[4]  = tens   / 4f;
            x[5]  = hasDragon  ? 1f : 0f;
            x[6]  = hasPhoenix ? 1f : 0f;
            x[7]  = hasMahjong ? 1f : 0f;
            x[8]  = hasDog     ? 1f : 0f;
            x[9]  = pairs   / 4f;
            x[10] = triples / 2f;
            x[11] = bombs   / 2f;
            x[12] = longest / 8f;
            x[13] = highCount / 8f;
            x[14] = rankSum / 112f; // 최대 8*14
            x[15] = ((hasDragon ? 1 : 0) + (hasPhoenix ? 1 : 0)) / 2f;
            return x;
        }

        // 존재하는 랭크의 최장 연속 길이(마작=1 포함, 봉황 와일드 미적용).
        private static int LongestStraight(int[] rankCount)
        {
            int best = 0, run = 0;
            for (int r = 1; r <= 14; r++)
            {
                if (rankCount[r] > 0) { run++; if (run > best) best = run; }
                else run = 0;
            }
            return best;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~GrandTichuFeaturesTests"`
Expected: PASS (3 tests). 이어서 Unity: `refresh_unity` → `read_console`(에러 0) → EditMode `GrandTichuFeaturesTests` 그린.

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/GrandTichuFeatures.cs" "core/src/Tichu.GameFlow/Agents/GrandTichuFeatures.cs" "Assets/_Project/Tests/EditMode/GrandTichuFeaturesTests.cs" "core/tests/Tichu.Core.Tests/GrandTichuFeaturesTests.cs"
git commit -m "feat(ai): B1 GrandTichuFeatures 인코더(8장→16피처, 순수)"
```

---

### Task 2: CallNet (로지스틱 추론) + 플레이스홀더 가중치

**Files:**
- Create (양 미러, 동일): `Assets\_Project\GameFlow\Agents\CallNet.cs`, `core\src\Tichu.GameFlow\Agents\CallNet.cs`
- Create (양 미러, 동일): `Assets\_Project\GameFlow\Agents\GrandTichuWeights.g.cs`, `core\src\Tichu.GameFlow\Agents\GrandTichuWeights.g.cs` (플레이스홀더 — Task 4에서 학습값으로 교체)
- Test (양 미러, 동일): `Assets\_Project\Tests\EditMode\CallNetTests.cs`, `core\tests\Tichu.Core.Tests\CallNetTests.cs`

**Interfaces:**
- Consumes: (없음)
- Produces:
  - `public sealed class CallNet { public CallNet(double[] weights, double bias); public double PredictProb(float[] x); public static readonly CallNet Grand; }`
  - `public static class GrandTichuWeights { public const int FeatureCount = 16; public static readonly double[] Weights; public const double Bias; public const double Threshold; }`

- [ ] **Step 1: 실패 테스트 작성** — 양 테스트 파일 동일.

```csharp
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.GameFlow.Tests
{
    public class CallNetTests
    {
        [Test]
        public void Zero_weights_give_half()
        {
            var net = new CallNet(new double[3], 0.0);
            Assert.That(net.PredictProb(new float[] { 1f, 2f, 3f }), Is.EqualTo(0.5).Within(1e-9));
        }

        [Test]
        public void Dot_plus_bias_through_sigmoid()
        {
            // z = 2*0.5 + (-1) = 0 → σ(0) = 0.5
            var net = new CallNet(new double[] { 2.0, 0.0 }, -1.0);
            Assert.That(net.PredictProb(new float[] { 0.5f, 9f }), Is.EqualTo(0.5).Within(1e-9));
            // z = 10*1 = 10 → σ(10) ≈ 0.9999546
            var net2 = new CallNet(new double[] { 10.0 }, 0.0);
            Assert.That(net2.PredictProb(new float[] { 1f }), Is.EqualTo(0.9999546).Within(1e-5));
        }

        [Test]
        public void Prob_is_bounded()
        {
            var net = new CallNet(new double[] { 1000.0 }, 0.0);
            Assert.That(net.PredictProb(new float[] { 1f }), Is.LessThanOrEqualTo(1.0));
            Assert.That(net.PredictProb(new float[] { -1f }), Is.GreaterThanOrEqualTo(0.0));
        }

        [Test]
        public void Grand_singleton_matches_weights_length()
        {
            Assert.That(GrandTichuWeights.Weights.Length, Is.EqualTo(GrandTichuFeatures.FeatureCount));
            Assert.That(CallNet.Grand, Is.Not.Null);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~CallNetTests"`
Expected: FAIL — `CallNet`/`GrandTichuWeights` 미정의.

- [ ] **Step 3: CallNet 구현** — 양 미러 동일.

```csharp
using System;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// 콜 헤드 추론기(로지스틱 회귀): p = σ(w·x + b). 가중치는 오프라인 트레이너가
    /// 베이크(GrandTichuWeights). 무의존·결정적·~30줄.
    /// </summary>
    public sealed class CallNet
    {
        private readonly double[] _w;
        private readonly double _b;

        public CallNet(double[] weights, double bias)
        {
            _w = weights;
            _b = bias;
        }

        /// <summary>σ(w·x + b) ∈ (0,1). x.Length 는 w.Length 이상이어야 한다(초과분 무시).</summary>
        public double PredictProb(float[] x)
        {
            double z = _b;
            for (int i = 0; i < _w.Length; i++) z += _w[i] * x[i];
            return 1.0 / (1.0 + Math.Exp(-z));
        }

        /// <summary>Grand Tichu 콜 헤드 싱글턴(베이크된 가중치).</summary>
        public static readonly CallNet Grand = new CallNet(GrandTichuWeights.Weights, GrandTichuWeights.Bias);
    }
}
```

- [ ] **Step 4: 플레이스홀더 가중치 작성** — 양 미러 동일. (Task 4에서 학습값으로 덮어씀. 전부 0 → Predict=0.5 → 임계값 0.5 초과 안 함 → ON이어도 콜 안 함, 무해.)

```csharp
// <auto-generated> B1 Grand 콜 헤드 가중치. CallNetTrainer 가 생성. 손으로 편집 금지. </auto-generated>
namespace Tichu.GameFlow.Agents
{
    public static class GrandTichuWeights
    {
        public const int FeatureCount = 16;
        public const double Bias = 0.0;
        public const double Threshold = 0.5;
        public static readonly double[] Weights = new double[16]; // 플레이스홀더(전부 0)
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~CallNetTests"`
Expected: PASS (4 tests). Unity: `refresh_unity`→`read_console`(에러 0)→EditMode `CallNetTests` 그린.

- [ ] **Step 6: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/CallNet.cs" "core/src/Tichu.GameFlow/Agents/CallNet.cs" "Assets/_Project/GameFlow/Agents/GrandTichuWeights.g.cs" "core/src/Tichu.GameFlow/Agents/GrandTichuWeights.g.cs" "Assets/_Project/Tests/EditMode/CallNetTests.cs" "core/tests/Tichu.Core.Tests/CallNetTests.cs"
git commit -m "feat(ai): B1 CallNet 로지스틱 추론 + 플레이스홀더 가중치"
```

---

### Task 3: 통합 — PolicyConfig 플래그 + AiAgent.CallGrandTichu + 위임 체인 배선

**Files:**
- Modify (양 미러): `…\GameFlow\Agents\PolicyConfig.cs` — `UseGrandCallNet` 필드/생성자 파라미터 추가
- Modify (양 미러): `…\GameFlow\Agents\AiAgent.cs` — 생성자 플래그·임계값 + `CallGrandTichu` ON/OFF
- Modify (양 미러): `…\GameFlow\Agents\HeuristicRolloutPolicy.cs` — 플래그를 내부 AiAgent로 전달
- Modify (양 미러): `…\GameFlow\Agents\PimcAgent.cs` — `config.UseGrandCallNet` 를 정책으로 전달
- Test (양 미러): `…\Tests\…\CallGrandTichuIntegrationTests.cs`

**Interfaces:**
- Consumes: `GrandTichuFeatures.Encode`, `CallNet.Grand.PredictProb`, `GrandTichuWeights.Threshold`
- Produces:
  - `PolicyConfig` 에 `public readonly bool UseGrandCallNet;` (생성자 끝에 `bool useGrandCallNet = false`)
  - `AiAgent(ulong roundSeed, int seat, bool useGrandCallNet = false, double grandThreshold = GrandTichuWeights.Threshold)`
  - `HeuristicRolloutPolicy(ulong seed, int seat, double epsilon, bool useGrandCallNet = false, double grandThreshold = GrandTichuWeights.Threshold)`

- [ ] **Step 1: 실패 테스트 작성** — 양 테스트 파일 동일. ON 경로가 CallNet을 타는지, OFF가 HandPower와 동일한지 검증. (플레이스홀더 가중치는 Predict=0.5 → 임계값 조작으로 라우팅 증명.)

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;

namespace Tichu.GameFlow.Tests
{
    public class CallGrandTichuIntegrationTests
    {
        // 강패(HandPower = A2*2 + 용4 + 봉황3 = 11 ≥ 10) 8장.
        private static GameState StateWithGrandHand()
        {
            // NewRound 는 무작위 배분이므로, 결정적 강패를 직접 구성한다.
            var s = GameEngine.NewRound(1);
            var hand = s.Seats[0].Hand;
            hand.Clear();
            hand.Add(Card.Normal(14, Suit.Jade));
            hand.Add(Card.Normal(14, Suit.Sword));
            hand.Add(Card.Dragon);
            hand.Add(Card.Phoenix);
            hand.Add(Card.Normal(13, Suit.Jade));
            hand.Add(Card.Normal(12, Suit.Jade));
            hand.Add(Card.Normal(9, Suit.Jade));
            hand.Add(Card.Normal(8, Suit.Jade));
            return s;
        }

        [Test]
        public void Flag_off_matches_HandPower_gate()
        {
            var s = StateWithGrandHand();
            var agent = new AiAgent(1, 0); // 기본 OFF
            Assert.That(agent.CallGrandTichu(new DecisionContext(s, 0)), Is.True); // HandPower 11 ≥ 10
        }

        [Test]
        public void Flag_on_routes_through_CallNet_threshold()
        {
            var s = StateWithGrandHand();
            // 플레이스홀더 가중치 → Predict=0.5. 임계값<0.5 면 콜, >0.5 면 패스 → CallNet 경로 증명.
            Assert.That(new AiAgent(1, 0, useGrandCallNet: true, grandThreshold: 0.49).CallGrandTichu(new DecisionContext(s, 0)), Is.True);
            Assert.That(new AiAgent(1, 0, useGrandCallNet: true, grandThreshold: 0.51).CallGrandTichu(new DecisionContext(s, 0)), Is.False);
        }

        [Test]
        public void PolicyConfig_flag_defaults_false()
        {
            Assert.That(PolicyConfig.Normal.UseGrandCallNet, Is.False);
            Assert.That(new PolicyConfig(16, 4, 0.05).UseGrandCallNet, Is.False);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~CallGrandTichuIntegrationTests"`
Expected: FAIL — `useGrandCallNet` 파라미터/`UseGrandCallNet` 프로퍼티 미존재(컴파일 에러).

- [ ] **Step 3a: PolicyConfig 수정** (양 미러) — 필드 추가 + 생성자 마지막 파라미터.

`UseNearOutLeadOrder` 필드 바로 아래에 추가:
```csharp
        /// <summary>true면 큰 티츄 콜을 학습된 헤드(P>τ)로 판정한다(B1). OFF면 현행 HandPower≥10.</summary>
        public readonly bool UseGrandCallNet;
```
생성자 시그니처 끝에 `, bool useGrandCallNet = false` 추가하고 본문에 `UseGrandCallNet = useGrandCallNet;` 추가. (프리셋 `For(...)` 는 이 태스크에서 변경하지 않음 — 채택 시 Task 6에서 켠다.)

- [ ] **Step 3b: AiAgent 수정** (양 미러) — 생성자·필드·CallGrandTichu.

필드 추가(`private readonly int _seat;` 아래):
```csharp
        private readonly bool _useGrandCallNet;
        private readonly double _grandThreshold;
```
생성자 교체:
```csharp
        public AiAgent(ulong roundSeed, int seat, bool useGrandCallNet = false, double grandThreshold = GrandTichuWeights.Threshold)
        {
            _rng = new Rng(roundSeed ^ 0xA1A1_0000_0000_0001UL ^ (ulong)seat);
            _seat = seat;
            _useGrandCallNet = useGrandCallNet;
            _grandThreshold = grandThreshold;
        }
```
`CallGrandTichu` 교체:
```csharp
        public bool CallGrandTichu(in DecisionContext ctx)
        {
            if (_useGrandCallNet)
                return CallNet.Grand.PredictProb(GrandTichuFeatures.Encode(ctx.MyHand)) > _grandThreshold;
            return HandPower(ctx.MyHand) >= GrandThreshold;
        }
```

- [ ] **Step 3c: HeuristicRolloutPolicy 수정** (양 미러) — 플래그 전달.

생성자 교체:
```csharp
        public HeuristicRolloutPolicy(ulong seed, int seat, double epsilon, bool useGrandCallNet = false, double grandThreshold = GrandTichuWeights.Threshold)
        {
            _heuristic = new AiAgent(seed, seat, useGrandCallNet, grandThreshold);
            _epsilon = epsilon;
            _rng = new Rng(seed ^ 0xB0E1_0000_0000_0001UL ^ (ulong)seat);
        }
```

- [ ] **Step 3d: PimcAgent 수정** (양 미러) — config 플래그 전달.

생성자 내 `_policy = new HeuristicRolloutPolicy(roundSeed, seat, config.Epsilon);` 를:
```csharp
            _policy = new HeuristicRolloutPolicy(roundSeed, seat, config.Epsilon, config.UseGrandCallNet);
```
로 교체.

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~CallGrandTichuIntegrationTests"`
Expected: PASS (3 tests).
회귀 확인(OFF 비트불변): `dotnet test … --filter "FullyQualifiedName~AiAgentTests"` 및 `~PimcAgentTests` 그린 유지.
Unity: `refresh_unity`→`read_console`(에러 0)→EditMode `CallGrandTichuIntegrationTests`+`AiAgentTests`+`PimcAgentTests` 그린.

- [ ] **Step 5: 커밋**

```bash
git add "Assets/_Project/GameFlow/Agents/PolicyConfig.cs" "core/src/Tichu.GameFlow/Agents/PolicyConfig.cs" "Assets/_Project/GameFlow/Agents/AiAgent.cs" "core/src/Tichu.GameFlow/Agents/AiAgent.cs" "Assets/_Project/GameFlow/Agents/HeuristicRolloutPolicy.cs" "core/src/Tichu.GameFlow/Agents/HeuristicRolloutPolicy.cs" "Assets/_Project/GameFlow/Agents/PimcAgent.cs" "core/src/Tichu.GameFlow/Agents/PimcAgent.cs" "Assets/_Project/Tests/EditMode/CallGrandTichuIntegrationTests.cs" "core/tests/Tichu.Core.Tests/CallGrandTichuIntegrationTests.cs"
git commit -m "feat(ai): B1 콜 헤드 통합 — UseGrandCallNet 플래그(전 티어 위임체인, 기본 OFF)"
```

---

### Task 4: CallNetTrainer (데이터생성 + SGD) — `[Explicit]`, core only

**Files:**
- Create: `core\tests\Tichu.Core.Tests\CallNetTrainer.cs`
- (실행 후) Overwrite (양 미러): `…\GameFlow\Agents\GrandTichuWeights.g.cs` — 학습값

**Interfaces:**
- Consumes: `GameEngine.NewRound`, `GameDriver.RunRound`, `AiAgent`, `GrandTichuFeatures.Encode`, `RoundOutcome.State.Seats[i].FinishOrder`
- Produces: `static (float[][] X, int[] y) GenerateData(int rounds, ulong baseSeed)`, `static (double[] w, double b, double logloss, double acc) TrainLogistic(float[][] X, int[] y, int epochs, double lr, double l2, ulong seed)`

- [ ] **Step 1: 데이터생성 캡처 정확성 테스트(비-Explicit)** 작성.

```csharp
using NUnit.Framework;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    public class CallNetTrainerTests
    {
        [Test]
        public void GenerateData_captures_8card_hands_and_one_winner_per_round()
        {
            var (X, y) = CallNetTrainer.GenerateData(rounds: 20, baseSeed: 1);
            Assert.That(X.Length, Is.EqualTo(80));                 // 20라운드 × 4좌석
            Assert.That(y.Length, Is.EqualTo(80));
            for (int i = 0; i < X.Length; i++)
                Assert.That(X[i].Length, Is.EqualTo(GrandTichuFeatures.FeatureCount));
            // 라운드마다 정확히 한 좌석이 먼저 나감(label=1).
            for (int r = 0; r < 20; r++)
            {
                int wins = 0;
                for (int seat = 0; seat < 4; seat++) wins += y[r * 4 + seat];
                Assert.That(wins, Is.EqualTo(1), $"round {r}");
            }
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~CallNetTrainerTests"`
Expected: FAIL — `CallNetTrainer` 미정의.

- [ ] **Step 3: CallNetTrainer 구현.**

```csharp
using System;
using System.IO;
using System.Text;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// B1 오프라인 트레이너. 휴리스틱 self-play 로 (8장 피처, out-first 라벨) 을 만들고,
    /// 손짠 SGD 로 로지스틱 회귀를 학습해 가중치를 방출한다. 순수 C#·파이썬 0.
    /// GenerateData/TrainLogistic 는 결정적. 무거운 학습·방출은 [Explicit].
    /// </summary>
    public static class CallNetTrainer
    {
        /// <summary>rounds 라운드 휴리스틱 self-play. 각 라운드 4좌석의 8장 피처 + FinishOrder==1 라벨.</summary>
        public static (float[][] X, int[] y) GenerateData(int rounds, ulong baseSeed)
        {
            var X = new float[rounds * 4][];
            var y = new int[rounds * 4];
            for (int r = 0; r < rounds; r++)
            {
                ulong seed = baseSeed + (ulong)r;
                var s = GameEngine.NewRound(seed);
                // 8장 손패 스냅샷(플레이 전 — RunRound 가 손패를 비운다).
                var feats = new float[4][];
                for (int seat = 0; seat < 4; seat++)
                    feats[seat] = GrandTichuFeatures.Encode(s.Seats[seat].Hand);
                // 순수 휴리스틱 self-play(콜 헤드 OFF — 라벨은 콜과 무관).
                var agents = new IAgent[4];
                for (int seat = 0; seat < 4; seat++) agents[seat] = new AiAgent(seed, seat);
                var outcome = new GameDriver(agents).RunRound(s);
                for (int seat = 0; seat < 4; seat++)
                {
                    X[r * 4 + seat] = feats[seat];
                    y[r * 4 + seat] = outcome.State.Seats[seat].FinishOrder == 1 ? 1 : 0;
                }
            }
            return (X, y);
        }

        /// <summary>로지스틱 회귀 SGD(이진 크로스엔트로피 + L2). 마지막 10%는 검증셋.</summary>
        public static (double[] w, double b, double logloss, double acc) TrainLogistic(
            float[][] X, int[] y, int epochs, double lr, double l2, ulong seed)
        {
            int n = X.Length, dim = GrandTichuFeatures.FeatureCount;
            int valStart = (int)(n * 0.9);
            var w = new double[dim];
            double b = 0.0;
            var rng = new Tichu.Core.Rng(seed);
            var idx = new int[valStart];
            for (int i = 0; i < valStart; i++) idx[i] = i;

            for (int e = 0; e < epochs; e++)
            {
                // Fisher–Yates 셔플(학습셋만).
                for (int i = valStart - 1; i > 0; i--)
                {
                    int j = rng.NextInt(i + 1);
                    (idx[i], idx[j]) = (idx[j], idx[i]);
                }
                for (int t = 0; t < valStart; t++)
                {
                    int i = idx[t];
                    double z = b;
                    for (int d = 0; d < dim; d++) z += w[d] * X[i][d];
                    double p = 1.0 / (1.0 + Math.Exp(-z));
                    double g = p - y[i]; // dL/dz
                    for (int d = 0; d < dim; d++) w[d] -= lr * (g * X[i][d] + l2 * w[d]);
                    b -= lr * g;
                }
            }

            // 검증 지표.
            double ll = 0; int correct = 0, m = n - valStart;
            for (int i = valStart; i < n; i++)
            {
                double z = b;
                for (int d = 0; d < dim; d++) z += w[d] * X[i][d];
                double p = 1.0 / (1.0 + Math.Exp(-z));
                double pc = Math.Min(Math.Max(p, 1e-12), 1 - 1e-12);
                ll += -(y[i] * Math.Log(pc) + (1 - y[i]) * Math.Log(1 - pc));
                if ((p >= 0.5 ? 1 : 0) == y[i]) correct++;
            }
            return (w, b, ll / m, correct / (double)m);
        }

        /// <summary>가중치를 GrandTichuWeights.g.cs 소스로 직렬화(양 미러에 붙여넣을 원문).</summary>
        public static string EmitWeightsSource(double[] w, double b, double threshold)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated> B1 Grand 콜 헤드 가중치. CallNetTrainer 가 생성. 손으로 편집 금지. </auto-generated>");
            sb.AppendLine("namespace Tichu.GameFlow.Agents");
            sb.AppendLine("{");
            sb.AppendLine("    public static class GrandTichuWeights");
            sb.AppendLine("    {");
            sb.AppendLine($"        public const int FeatureCount = {w.Length};");
            sb.AppendLine($"        public const double Bias = {b:R};");
            sb.AppendLine($"        public const double Threshold = {threshold:R};");
            sb.Append("        public static readonly double[] Weights = new double[] { ");
            for (int i = 0; i < w.Length; i++) { sb.Append(w[i].ToString("R")); if (i < w.Length - 1) sb.Append(", "); }
            sb.AppendLine(" };");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        [Explicit, Category("Bench")]
        [Test]
        public void Train_and_emit_weights()
        {
            const int Rounds = 50_000;      // ×4 = 20만 행
            var (X, y) = GenerateData(Rounds, baseSeed: 1);
            int pos = 0; for (int i = 0; i < y.Length; i++) pos += y[i];
            var (w, b, ll, acc) = TrainLogistic(X, y, epochs: 40, lr: 0.1, l2: 1e-4, seed: 12345);
            string src = EmitWeightsSource(w, b, 0.5);
            string outPath = Path.Combine(Path.GetTempPath(), "GrandTichuWeights.g.cs");
            File.WriteAllText(outPath, src);
            var report = $"rows={X.Length} baseRate={pos / (double)y.Length:F3} valLogloss={ll:F4} valAcc={acc:F3}\nweights → {outPath}";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_callnet_train.txt"), report);
            TestContext.Progress.WriteLine(report);
            TestContext.Progress.WriteLine(src);
        }
    }
}
```

- [ ] **Step 4: 캡처 테스트 통과 확인**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~CallNetTrainerTests"`
Expected: PASS (1 test).

- [ ] **Step 5: 학습 실행 → 가중치 방출**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~CallNetTrainer.Train_and_emit_weights"`
(⚠️ `[Explicit]` 이 이름필터로 안 잡히면 임시로 `[Explicit]` 제거 후 실행, 완료 후 원복.)
Expected: PASS. `%TEMP%/tichu_callnet_train.txt` 에 baseRate≈0.25·valLogloss·valAcc, `%TEMP%/GrandTichuWeights.g.cs` 에 학습된 소스. baseRate가 0.25 근처(정상), valAcc가 baseRate보다 의미있게 높은지 확인(예측력 존재).

- [ ] **Step 6: 방출된 가중치를 양 미러에 반영** — `%TEMP%/GrandTichuWeights.g.cs` 내용을 `Assets\_Project\GameFlow\Agents\GrandTichuWeights.g.cs` 와 `core\src\Tichu.GameFlow\Agents\GrandTichuWeights.g.cs` 에 바이트 동일하게 기록. Threshold=0.5 유지(Task 5에서 스윕).

- [ ] **Step 7: 회귀 확인 + 커밋** — 가중치 반영 후 CallNetTests·CallGrandTichuIntegrationTests 재실행(그린), Unity refresh+console 확인.

```bash
git add "core/tests/Tichu.Core.Tests/CallNetTrainer.cs" "core/tests/Tichu.Core.Tests/CallNetTrainerTests.cs" "Assets/_Project/GameFlow/Agents/GrandTichuWeights.g.cs" "core/src/Tichu.GameFlow/Agents/GrandTichuWeights.g.cs"
git commit -m "feat(ai): B1 CallNetTrainer(self-play 데이터+SGD) + 학습 가중치 베이크"
```

---

### Task 5: GrandCallHeadBench (격리 DoD) — `[Explicit]`, core only

**Files:**
- Create: `core\tests\Tichu.Core.Tests\GrandCallHeadBench.cs`

**Interfaces:**
- Consumes: `AiAgent(seed, seat, useGrandCallNet, grandThreshold)`, `GameDriver`, `GameEngine.NewRound`, `RoundResult.TeamATotal/TeamBTotal`
- Produces: (콘솔/파일 리포트만)

- [ ] **Step 1: 벤치 작성** (HeuristicStrengthBench 패턴 미러 — 플래그 토글 + τ 스윕 + Wilson LB).

```csharp
using System;
using System.IO;
using System.Text;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests.Bench
{
    /// <summary>
    /// B1 격리 벤치. A팀 = 콜 헤드 ON, B팀 = OFF(현행 HandPower), 그 외 전부 동일 휴리스틱.
    /// 미러드(양 배치)로 딜 편향 제거. 학습 시드[1..]와 분리된 벤치 시드[10_000_000..].
    /// τ 스윕으로 최적 임계값 탐색. 채택: margin≥0 & WilsonLB>0.5 & 회귀없음.
    /// [Explicit] — 기본 스위트 제외.
    /// </summary>
    [Explicit, Category("Bench")]
    public class GrandCallHeadBench
    {
        [Test]
        public void CallHead_on_vs_off_mirrored_sweep()
        {
            const int Pairs = 3000;             // ×2 미러 = 6000 라운드/τ
            const ulong BaseSeed = 10_000_000;  // 학습과 분리
            double[] taus = { 0.45, 0.50, 0.55, 0.60, 0.65 };
            var sb = new StringBuilder();

            foreach (double tau in taus)
            {
                long diffSum = 0; int onWins = 0, ties = 0, rounds = 0;
                for (int s = 1; s <= Pairs; s++)
                {
                    ulong seed = BaseSeed + (ulong)(s * 7919);
                    for (int mirror = 0; mirror < 2; mirror++)
                    {
                        bool onTeamA = (mirror == 0);
                        var agents = new IAgent[4];
                        for (int i = 0; i < 4; i++)
                        {
                            bool teamA = (i % 2 == 0);
                            bool on = onTeamA ? teamA : !teamA;
                            agents[i] = new AiAgent(seed, i, useGrandCallNet: on, grandThreshold: tau);
                        }
                        var outcome = new GameDriver(agents).RunRound(GameEngine.NewRound(seed));
                        int onScore = onTeamA ? outcome.Result.TeamATotal : outcome.Result.TeamBTotal;
                        int offScore = onTeamA ? outcome.Result.TeamBTotal : outcome.Result.TeamATotal;
                        int diff = onScore - offScore;
                        diffSum += diff;
                        if (diff > 0) onWins++; else if (diff == 0) ties++;
                        rounds++;
                    }
                }
                double wilson = WilsonLB(onWins, rounds);
                sb.AppendLine($"tau={tau:F2} rounds={rounds} onAvg={diffSum / (double)rounds:F2}/R onWins={onWins}/{rounds} ({onWins / (double)rounds:P1}) ties={ties} WilsonLB={wilson:F3}");
            }
            var report = sb.ToString();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tichu_grand_callhead_bench.txt"), report);
            TestContext.Progress.WriteLine(report);
        }

        private static double WilsonLB(int wins, int n, double z = 1.96)
        {
            if (n == 0) return 0;
            double phat = (double)wins / n, z2 = z * z;
            double denom = 1 + z2 / n;
            double center = phat + z2 / (2 * n);
            double margin = z * Math.Sqrt(phat * (1 - phat) / n + z2 / (4.0 * n * n));
            double lb = (center - margin) / denom;
            return lb < 0 ? 0 : (lb > 1 ? 1 : lb);
        }
    }
}
```

- [ ] **Step 2: 벤치 실행 → 판정**

Run: `dotnet test core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj --filter "FullyQualifiedName~GrandCallHeadBench"`
(⚠️ `[Explicit]` 미선택 시 임시 제거 후 실행·원복. ⚠️ 오래 걸릴 수 있음 → 필요시 `run_in_background`.)
Expected: `%TEMP%/tichu_grand_callhead_bench.txt` 에 τ별 onAvg/R·승률·WilsonLB. **판정**: 최적 τ에서 `onAvg≥0` **and** `WilsonLB>0.5` → 채택. 아니면 파킹.

- [ ] **Step 3: 커밋** (판정 무관, 벤치 코드 보존)

```bash
git add "core/tests/Tichu.Core.Tests/GrandCallHeadBench.cs"
git commit -m "test(ai): B1 GrandCallHeadBench 격리 벤치(ON/OFF 미러드·τ 스윕·Wilson)"
```

---

### Task 6: 채택/파킹 결정 + 라이브 배선

**Files:**
- (채택 시) Modify (양 미러): `…\GameFlow\Agents\PolicyConfig.cs` — `For(...)` 프리셋에 `useGrandCallNet: true`
- (채택 시) Modify (양 미러): `…\GameFlow\Agents\GrandTichuWeights.g.cs` — Threshold = 벤치 최적 τ
- Create: `티츄_B1_벤치결과.html` (결과 리포트)

- [ ] **Step 1: 결과 판정**
  - **채택**(onAvg≥0 & WilsonLB>0.5): 아래 진행.
  - **파킹**(wash/음수): 플래그 OFF 유지·코드 보존. 리포트에 근거 기록 후 종료(기존 레버 관례).

- [ ] **Step 2 (채택 시): 라이브 켜기** — `PolicyConfig.For` 의 Normal/Hard/Expert 프리셋에 `useGrandCallNet: true` 추가(양 미러). `GrandTichuWeights.g.cs` 의 `Threshold` 를 벤치 최적 τ로 갱신(양 미러). Easy(Worlds=0)는 현행 유지(약체 티어).

- [ ] **Step 3: 회귀·통합 확인** — `AiAgentTests`·`PimcAgentTests`·`PolicyConfigTests` 그린. Unity refresh+console 0. 라이브 플레이 1판(선택) 육안 — Grand 콜 빈도가 합리적인지.

- [ ] **Step 4: 결과 리포트 + 커밋**

```bash
git add -A
git commit -m "feat(ai): B1 Grand 콜 헤드 <채택: 전 티어 ON, τ=X / 파킹: OFF 보존> + 벤치 리포트"
```

- [ ] **Step 5: 적대 리뷰(워크플로)** — 구현 전반 다각도 적대 검증(벤치 방법론·라벨 누수·미러 정합·비트불변)·발견 수정 후 종합.

---

## Self-Review

**Spec coverage:** §1 목표→Task3(게이트교체)·Task4(학습). §2 DoD→Task5(Wilson벤치)·Task6(판정). §3 통합구조→Task3(위임체인). §4 아키텍처 5유닛→Task1~5. §5 피처→Task1. §6 모델사다리→Task4(로지스틱 v0). §7 τ→Task5(스윕). §8 위생→Global Constraints(시드분리)+Task4/5. §9 테스트→각 Task TDD. §10 YAGNI→플래그 기본OFF·Small 제외. 누락 없음.

**Placeholder scan:** 모든 코드 스텝에 실제 코드. Task4 가중치 수치는 실행 산출물(코드는 완전 명시 — placeholder 아님). Task6 채택분기는 벤치 결과 의존(정상적 조건 분기).

**Type consistency:** `GrandTichuFeatures.Encode(IReadOnlyList<Card>)→float[]`·`FeatureCount=16` 전 태스크 일관. `CallNet.PredictProb(float[])→double`·`CallNet.Grand`·`GrandTichuWeights.{Weights,Bias,Threshold}` 일관. `AiAgent(ulong,int,bool,double)`·`HeuristicRolloutPolicy(ulong,int,double,bool,double)`·`PolicyConfig.UseGrandCallNet` 일관.
