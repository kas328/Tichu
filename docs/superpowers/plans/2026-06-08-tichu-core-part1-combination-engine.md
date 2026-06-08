# Tichu.Core Part 1 — 도메인 모델 · 덱 · 조합 엔진 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 순수 C# 룰엔진(`Tichu.Core`)의 토대 — 카드/덱/결정적 셔플과, 카드 묶음을 합법 조합으로 판별(`Recognize`)하고 두 조합의 우열을 가리는(`Beats`) 엔진 — 을 `dotnet test`로 검증되는 헤드리스 라이브러리로 구축한다.

**Architecture:** Unity 비의존 .NET 라이브러리(`netstandard2.1`, C# 9). 카드는 값 타입 `struct`. 조합 비교값(`Rank`)은 부동소수점 결정성 문제를 피하려고 **정수 ×2 스케일**(일반값 v→2v, 봉황 단독 반칸→홀수)로 표현한다. 셔플 RNG는 시드 주입식 결정적 PRNG(SplitMix64)로 직접 구현해 서버/클라/AI/리플레이 간 비트 단위 동일성을 보장한다.

**Tech Stack:** C# 9 / .NET (`netstandard2.1` 라이브러리, `net9.0` 테스트), NUnit. Unity·UnityEngine·UniTask·R3·DOTween·Photon 일절 미사용(Part 1 범위는 순수 도메인).

**기획서 매핑:** `티츄_프로토타입_기획서.html` 2장(덱/특수카드), 3장(조합/폭탄), 11.1(C# 타입), 11.3①②(Recognize/Beats), 11.4(비교 우선순위), 11.8(테스트 전략).

---

## File Structure

Part 1에서 생성하는 파일 (모두 `core/` 하위 — 추후 Unity 통합 시 소스를 `Assets/_Project/Core/`로 이전, 네임스페이스 불변):

```
core/
├─ Tichu.sln
├─ src/Tichu.Core/
│  ├─ Tichu.Core.csproj                 # netstandard2.1, C# 9, Nullable enable, 참조 없음
│  ├─ Rng.cs                            # 결정적 PRNG (SplitMix64)
│  ├─ Cards/
│  │  ├─ Suit.cs                        # enum Suit, SpecialKind
│  │  ├─ Card.cs                        # readonly struct Card + 팩토리
│  │  └─ Deck.cs                        # 56장 생성 + 셔플
│  └─ Combinations/
│     ├─ Combination.cs                 # enum CombinationType, class Combination, struct TrickContext
│     ├─ CombinationRecognizer.cs       # Recognize() + 분석/판별 헬퍼
│     └─ CombinationComparer.cs         # Beats()
└─ tests/Tichu.Core.Tests/
   ├─ Tichu.Core.Tests.csproj           # net9.0, NUnit, → Tichu.Core 참조
   ├─ CardTests.cs
   ├─ RngTests.cs
   ├─ DeckTests.cs
   ├─ RecognizeSingleTests.cs
   ├─ RecognizePairTripleFullTests.cs
   ├─ RecognizeStraightPairsTests.cs
   ├─ RecognizeBombTests.cs
   └─ BeatsTests.cs
```

**책임 분리:** `Card`/`Deck` = 카드 표현·구성, `Rng` = 결정성, `CombinationRecognizer` = "이 카드들이 무슨 조합인가", `CombinationComparer` = "이 조합이 저 조합을 이기는가". 각 파일 단일 책임.

**Rank ×2 스케일 규약 (전 태스크 공통):**
- 일반 카드 값 v(2~14, A=14) → `Rank = 2*v` (예: K=13→26, A=14→28).
- 마작 값 1 → `Rank = 2`.
- 용 값 15(A보다 위) → `Rank = 30`.
- 봉황 **단독**: 추종이면 직전 단독 `Rank + 1`(=+0.5), 선(리드)/직전이 단독 아님이면 `Rank = 3`(=1.5).
- 봉황이 **조합의 와일드**로 쓰일 땐 대체하는 일반 값의 정수 ×2(짝수). 즉 홀수 Rank는 "봉황 단독"에서만 등장.
- `Card.Rank` 원본값: 일반 2~14, 마작=1, 용=15, 개=0, 봉황=0(문맥 의존).

---

## Task 0: 솔루션 · 프로젝트 스캐폴딩

**Files:**
- Create: `core/Tichu.sln`, `core/src/Tichu.Core/Tichu.Core.csproj`, `core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj`
- Modify: `.gitignore` (루트), Create: `.gitattributes` (루트)

- [ ] **Step 1: dotnet CLI로 솔루션/프로젝트 생성**

Run (레포 루트에서):
```bash
dotnet new sln -n Tichu -o core
dotnet new classlib -n Tichu.Core -o core/src/Tichu.Core -f netstandard2.1
dotnet new nunit -n Tichu.Core.Tests -o core/tests/Tichu.Core.Tests -f net9.0
dotnet sln core/Tichu.sln add core/src/Tichu.Core/Tichu.Core.csproj core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj
dotnet add core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj reference core/src/Tichu.Core/Tichu.Core.csproj
```

- [ ] **Step 2: 템플릿 더미 파일 삭제**

Run:
```bash
rm -f core/src/Tichu.Core/Class1.cs core/tests/Tichu.Core.Tests/UnitTest1.cs
```

- [ ] **Step 3: Core .csproj 설정 고정**

`core/src/Tichu.Core/Tichu.Core.csproj` 전체를 아래로 교체:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>Tichu.Core</RootNamespace>
  </PropertyGroup>
</Project>
```
> `netstandard2.1` + `LangVersion 9.0` = Unity 6(C# 9) 호환 보장. `record`/`init`/파일범위 네임스페이스(전부 C# 10+ 또는 폴리필 필요)는 사용 금지.

- [ ] **Step 4: 테스트 .csproj가 Core를 참조하는지 확인**

`core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj`에 아래 `ItemGroup`이 있어야 함(없으면 추가):
```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Tichu.Core\Tichu.Core.csproj" />
  </ItemGroup>
```

- [ ] **Step 5: `.gitignore`에 손수 작성한 .NET 프로젝트 파일 추적 예외 추가**

`.gitignore` 맨 끝에 다음 블록을 추가(루트 `*.csproj`/`*.sln` 무시 규칙을 `core/`에 한해 해제, 빌드 산출물은 계속 무시):
```gitignore

# --- Tichu.Core standalone .NET solution (Phase 0) — 손수 작성, 추적 대상 ---
!core/**/*.csproj
!core/**/*.sln
core/**/[Bb]in/
core/**/[Oo]bj/
```

- [ ] **Step 6: `.gitattributes` 생성 (소스 줄바꿈 정규화)**

`.gitattributes` (루트) 생성:
```gitattributes
* text=auto eol=lf
*.sln text eol=crlf
*.png binary
*.jpg binary
```

- [ ] **Step 7: 빈 빌드/테스트가 통과하는지 검증**

Run:
```bash
dotnet test core/Tichu.sln
```
Expected: 빌드 성공, `Passed! - Failed: 0, Passed: 0` (테스트 0개라도 종료코드 0).

- [ ] **Step 8: 커밋**

```bash
git add core .gitignore .gitattributes
git commit -m "chore: scaffold Tichu.Core .NET solution (netstandard2.1 + NUnit)"
```

---

## Task 1: Card 모델 (Suit, SpecialKind, Card struct, Points)

**Files:**
- Create: `core/src/Tichu.Core/Cards/Suit.cs`, `core/src/Tichu.Core/Cards/Card.cs`
- Test: `core/tests/Tichu.Core.Tests/CardTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/CardTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Core.Cards;

namespace Tichu.Core.Tests
{
    public class CardTests
    {
        [Test]
        public void Normal_card_points_are_correct()
        {
            Assert.That(Card.Normal(13, Suit.Jade).Points, Is.EqualTo(10)); // K
            Assert.That(Card.Normal(10, Suit.Star).Points, Is.EqualTo(10)); // 10
            Assert.That(Card.Normal(5, Suit.Sword).Points, Is.EqualTo(5));  // 5
            Assert.That(Card.Normal(14, Suit.Pagoda).Points, Is.EqualTo(0)); // A
            Assert.That(Card.Normal(2, Suit.Jade).Points, Is.EqualTo(0));
        }

        [Test]
        public void Special_cards_have_correct_meta()
        {
            Assert.That(Card.Dragon.Points, Is.EqualTo(25));
            Assert.That(Card.Phoenix.Points, Is.EqualTo(-25));
            Assert.That(Card.Mahjong.Points, Is.EqualTo(0));
            Assert.That(Card.Dog.Points, Is.EqualTo(0));
            Assert.That(Card.Mahjong.Rank, Is.EqualTo(1));
            Assert.That(Card.Dragon.Rank, Is.EqualTo(15));
            Assert.That(Card.Phoenix.IsSpecial, Is.True);
            Assert.That(Card.Normal(7, Suit.Jade).IsSpecial, Is.False);
        }

        [Test]
        public void Equality_compares_rank_suit_special()
        {
            Assert.That(Card.Normal(7, Suit.Jade), Is.EqualTo(Card.Normal(7, Suit.Jade)));
            Assert.That(Card.Normal(7, Suit.Jade), Is.Not.EqualTo(Card.Normal(7, Suit.Star)));
            Assert.That(Card.Dragon, Is.Not.EqualTo(Card.Phoenix));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — `Card`/`Suit` 미정의로 컴파일 에러.

- [ ] **Step 3: 최소 구현**

`core/src/Tichu.Core/Cards/Suit.cs`:
```csharp
namespace Tichu.Core.Cards
{
    public enum Suit { Jade, Sword, Pagoda, Star, Special }

    public enum SpecialKind { None, Mahjong, Dog, Phoenix, Dragon }
}
```

`core/src/Tichu.Core/Cards/Card.cs`:
```csharp
using System;

namespace Tichu.Core.Cards
{
    /// <summary>카드 1장(값 타입). Rank: 일반 2~14(A=14), 마작=1, 용=15, 개/봉황=0(문맥의존).</summary>
    public readonly struct Card : IEquatable<Card>
    {
        public readonly int Rank;
        public readonly Suit Suit;
        public readonly SpecialKind Special;
        public readonly int Points;

        public Card(int rank, Suit suit, SpecialKind special, int points)
        {
            Rank = rank; Suit = suit; Special = special; Points = points;
        }

        public bool IsSpecial => Suit == Suit.Special;

        public static Card Normal(int rank, Suit suit) =>
            new Card(rank, suit, SpecialKind.None, PointsFor(rank));

        public static readonly Card Mahjong = new Card(1, Suit.Special, SpecialKind.Mahjong, 0);
        public static readonly Card Dog = new Card(0, Suit.Special, SpecialKind.Dog, 0);
        public static readonly Card Phoenix = new Card(0, Suit.Special, SpecialKind.Phoenix, -25);
        public static readonly Card Dragon = new Card(15, Suit.Special, SpecialKind.Dragon, 25);

        private static int PointsFor(int rank)
        {
            switch (rank)
            {
                case 13: return 10; // K
                case 10: return 10; // 10
                case 5: return 5;   // 5
                default: return 0;
            }
        }

        public bool Equals(Card o) => Rank == o.Rank && Suit == o.Suit && Special == o.Special;
        public override bool Equals(object? o) => o is Card c && Equals(c);
        public override int GetHashCode() => (Rank * 397) ^ ((int)Suit * 17) ^ (int)Special;

        public override string ToString() =>
            IsSpecial ? Special.ToString() : $"{Rank}{Suit.ToString()[0]}";
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS (3 tests).

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Cards core/tests/Tichu.Core.Tests/CardTests.cs
git commit -m "feat(core): add Card struct with suits, specials and point values"
```

---

## Task 2: 결정적 RNG (SplitMix64)

**Files:**
- Create: `core/src/Tichu.Core/Rng.cs`
- Test: `core/tests/Tichu.Core.Tests/RngTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/RngTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Core;

namespace Tichu.Core.Tests
{
    public class RngTests
    {
        [Test]
        public void Same_seed_produces_same_sequence()
        {
            var a = new Rng(12345UL);
            var b = new Rng(12345UL);
            for (int i = 0; i < 100; i++)
                Assert.That(a.NextULong(), Is.EqualTo(b.NextULong()));
        }

        [Test]
        public void Different_seeds_diverge()
        {
            var a = new Rng(1UL);
            var b = new Rng(2UL);
            Assert.That(a.NextULong(), Is.Not.EqualTo(b.NextULong()));
        }

        [Test]
        public void NextInt_stays_in_range()
        {
            var r = new Rng(99UL);
            for (int i = 0; i < 1000; i++)
            {
                int v = r.NextInt(10);
                Assert.That(v, Is.InRange(0, 9));
            }
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — `Rng` 미정의.

- [ ] **Step 3: 최소 구현**

`core/src/Tichu.Core/Rng.cs`:
```csharp
using System;

namespace Tichu.Core
{
    /// <summary>시드 주입식 결정적 PRNG(SplitMix64). 서버/클라/AI/리플레이 비트 동일성 보장.</summary>
    public struct Rng
    {
        private ulong _state;

        public Rng(ulong seed) { _state = seed; }

        public ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0, maxExclusive) 정수. 셔플용(56장 규모에서 모듈로 편향 무시 가능).</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextULong() % (ulong)maxExclusive);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Rng.cs core/tests/Tichu.Core.Tests/RngTests.cs
git commit -m "feat(core): add deterministic SplitMix64 RNG"
```

---

## Task 3: 덱 구성 + 결정적 셔플

**Files:**
- Create: `core/src/Tichu.Core/Cards/Deck.cs`
- Test: `core/tests/Tichu.Core.Tests/DeckTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/DeckTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;

namespace Tichu.Core.Tests
{
    public class DeckTests
    {
        [Test]
        public void Standard_deck_has_56_cards_and_100_points()
        {
            var deck = Deck.CreateStandard();
            Assert.That(deck.Count, Is.EqualTo(56));
            Assert.That(deck.Sum(c => c.Points), Is.EqualTo(100));
        }

        [Test]
        public void Standard_deck_has_52_normals_and_4_specials()
        {
            var deck = Deck.CreateStandard();
            Assert.That(deck.Count(c => !c.IsSpecial), Is.EqualTo(52));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Mahjong), Is.EqualTo(1));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Dog), Is.EqualTo(1));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Phoenix), Is.EqualTo(1));
            Assert.That(deck.Count(c => c.Special == SpecialKind.Dragon), Is.EqualTo(1));
            // 각 문양 13랭크씩
            foreach (var suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
                Assert.That(deck.Count(c => !c.IsSpecial && c.Suit == suit), Is.EqualTo(13));
        }

        [Test]
        public void Shuffle_is_deterministic_for_same_seed()
        {
            var d1 = Deck.CreateStandard(); var r1 = new Rng(777UL); Deck.Shuffle(d1, ref r1);
            var d2 = Deck.CreateStandard(); var r2 = new Rng(777UL); Deck.Shuffle(d2, ref r2);
            Assert.That(d1, Is.EqualTo(d2)); // 순서까지 동일
        }

        [Test]
        public void Shuffle_differs_for_different_seed_but_preserves_multiset()
        {
            var d1 = Deck.CreateStandard(); var r1 = new Rng(1UL); Deck.Shuffle(d1, ref r1);
            var d2 = Deck.CreateStandard(); var r2 = new Rng(2UL); Deck.Shuffle(d2, ref r2);
            Assert.That(d1, Is.Not.EqualTo(d2));                 // 순서는 다름
            Assert.That(d1.OrderBy(C).ToList(),
                        Is.EqualTo(d2.OrderBy(C).ToList()));     // 구성은 동일
        }

        private static int C(Card c) => ((int)c.Suit << 8) | (c.Rank << 3) | (int)c.Special;
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — `Deck` 미정의.

- [ ] **Step 3: 최소 구현**

`core/src/Tichu.Core/Cards/Deck.cs`:
```csharp
using System.Collections.Generic;

namespace Tichu.Core.Cards
{
    public static class Deck
    {
        public const int Size = 56;

        public static List<Card> CreateStandard()
        {
            var cards = new List<Card>(Size);
            foreach (var suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
                for (int r = 2; r <= 14; r++)
                    cards.Add(Card.Normal(r, suit));
            cards.Add(Card.Mahjong);
            cards.Add(Card.Dog);
            cards.Add(Card.Phoenix);
            cards.Add(Card.Dragon);
            return cards;
        }

        /// <summary>Fisher–Yates 셔플(in-place). 동일 시드 → 동일 순서.</summary>
        public static void Shuffle(IList<Card> cards, ref Rng rng)
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                Card tmp = cards[i]; cards[i] = cards[j]; cards[j] = tmp;
            }
        }
    }
}
```
> `Deck.cs`는 `Rng`를 쓰므로 `using Tichu.Core;`가 필요할 수 있으나, `Rng`가 `Tichu.Core` 네임스페이스라 `Deck`(=`Tichu.Core.Cards`)에서 한정명으로 접근하려면 파일 상단에 `using Tichu.Core;` 추가. (위 코드에 맞춰 `Deck.cs` 첫 줄에 `using Tichu.Core;` 넣을 것.)

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Cards/Deck.cs core/tests/Tichu.Core.Tests/DeckTests.cs
git commit -m "feat(core): add 56-card deck (100 pts) with deterministic shuffle"
```

---

## Task 4: 조합 타입 (CombinationType, Combination, TrickContext)

**Files:**
- Create: `core/src/Tichu.Core/Combinations/Combination.cs`
- Test: (이 태스크는 데이터 타입 정의 — 별도 테스트 없이 다음 태스크 테스트가 사용. 컴파일만 검증)

- [ ] **Step 1: 타입 정의 작성**

`core/src/Tichu.Core/Combinations/Combination.cs`:
```csharp
using System;
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    public enum CombinationType
    {
        Invalid,
        Single,
        Pair,
        Triple,
        FullHouse,
        Straight,
        ConsecutivePairs,
        FourBomb,
        StraightFlushBomb
    }

    /// <summary>판별된 조합. Rank는 ×2 스케일(일반값 v→2v, 봉황 단독 반칸→홀수).</summary>
    public sealed class Combination
    {
        public CombinationType Type { get; }
        public IReadOnlyList<Card> Cards { get; }
        public int Length { get; }
        public int Rank { get; }
        public int PointsInPlay { get; }

        public bool IsBomb =>
            Type == CombinationType.FourBomb || Type == CombinationType.StraightFlushBomb;

        public Combination(CombinationType type, IReadOnlyList<Card> cards, int length, int rank, int pointsInPlay)
        {
            Type = type; Cards = cards; Length = length; Rank = rank; PointsInPlay = pointsInPlay;
        }

        public static readonly Combination Invalid =
            new Combination(CombinationType.Invalid, Array.Empty<Card>(), 0, 0, 0);
    }

    /// <summary>봉황 단독 비교값 산출에 필요한 트릭 문맥.</summary>
    public readonly struct TrickContext
    {
        public readonly bool IsLead;                  // 따라갈 트릭이 없으면 true
        public readonly bool TopIsSingle;             // 현재 Top이 단독인가
        public readonly int CurrentSingleRankScaled;  // TopIsSingle일 때 그 단독의 ×2 Rank

        public TrickContext(bool isLead, bool topIsSingle, int currentSingleRankScaled)
        {
            IsLead = isLead; TopIsSingle = topIsSingle; CurrentSingleRankScaled = currentSingleRankScaled;
        }

        public static readonly TrickContext Lead = new TrickContext(true, false, 0);
    }
}
```

- [ ] **Step 2: 컴파일 검증**

Run: `dotnet build core/Tichu.sln`
Expected: 성공.

- [ ] **Step 3: 커밋**

```bash
git add core/src/Tichu.Core/Combinations/Combination.cs
git commit -m "feat(core): add CombinationType, Combination and TrickContext"
```

---

## Task 5: Recognize — 단독 (일반 + 특수 4종, 봉황 단독 문맥값)

**Files:**
- Create: `core/src/Tichu.Core/Combinations/CombinationRecognizer.cs`
- Test: `core/tests/Tichu.Core.Tests/RecognizeSingleTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/RecognizeSingleTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizeSingleTests
    {
        private static Combination R(TrickContext ctx, params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, ctx);

        [Test]
        public void Normal_single_rank_is_value_times_two()
        {
            var c = R(TrickContext.Lead, Card.Normal(13, Suit.Jade)); // K
            Assert.That(c.Type, Is.EqualTo(CombinationType.Single));
            Assert.That(c.Length, Is.EqualTo(1));
            Assert.That(c.Rank, Is.EqualTo(26)); // 13*2
        }

        [Test]
        public void Mahjong_and_dragon_singles_have_extreme_ranks()
        {
            Assert.That(R(TrickContext.Lead, Card.Mahjong).Rank, Is.EqualTo(2));   // 1*2
            Assert.That(R(TrickContext.Lead, Card.Dragon).Rank, Is.EqualTo(30));   // 15*2
        }

        [Test]
        public void Phoenix_lead_single_is_one_and_half()
        {
            Assert.That(R(TrickContext.Lead, Card.Phoenix).Rank, Is.EqualTo(3));   // 1.5*2
        }

        [Test]
        public void Phoenix_following_single_is_half_above_top()
        {
            // 직전 단독이 K(26). 봉황 = 26+1 = 27 (=13.5)
            var ctx = new TrickContext(false, true, 26);
            Assert.That(R(ctx, Card.Phoenix).Rank, Is.EqualTo(27));
        }

        [Test]
        public void Dog_single_is_recognized_structurally()
        {
            var c = R(TrickContext.Lead, Card.Dog);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Single)); // 흐름 레이어(Part 2)가 특수 처리
        }

        [Test]
        public void Empty_is_invalid()
        {
            Assert.That(CombinationRecognizer.Recognize(System.Array.Empty<Card>(), TrickContext.Lead).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — `CombinationRecognizer` 미정의.

- [ ] **Step 3: 최소 구현 (Recognize 디스패처 + 단독)**

`core/src/Tichu.Core/Combinations/CombinationRecognizer.cs`:
```csharp
using System;
using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    public static class CombinationRecognizer
    {
        public static Combination Recognize(ReadOnlySpan<Card> cards, TrickContext ctx)
        {
            int n = cards.Length;
            if (n == 0) return Combination.Invalid;
            if (n == 1) return RecognizeSingle(cards[0], ctx);

            // 멀티카드 판별은 Task 6~8에서 확장.
            return Combination.Invalid;
        }

        private static Combination RecognizeSingle(Card c, TrickContext ctx)
        {
            if (c.Special == SpecialKind.Phoenix)
            {
                int rank = (ctx.IsLead || !ctx.TopIsSingle) ? 3 : ctx.CurrentSingleRankScaled + 1;
                return new Combination(CombinationType.Single, new[] { c }, 1, rank, c.Points);
            }
            // 일반/마작/용/개: Rank 원본값 ×2 (개=0).
            return new Combination(CombinationType.Single, new[] { c }, 1, c.Rank * 2, c.Points);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Combinations/CombinationRecognizer.cs core/tests/Tichu.Core.Tests/RecognizeSingleTests.cs
git commit -m "feat(core): recognize single combinations incl. contextual phoenix"
```

---

## Task 6: Recognize — 페어/트리플/풀하우스 (봉황 와일드 포함)

**규칙:** 페어=같은 랭크 2(또는 봉황+일반1), 트리플=같은 랭크 3(또는 봉황+같은랭크2), 풀하우스=트리플+페어(봉황은 페어 또는 트리플의 빈자리 1장 대체). 마작(랭크1)·개·용은 이들 조합에 불가. `Rank`=핵심 랭크 값 ×2(풀하우스는 트리플 랭크 기준).

**Files:**
- Modify: `core/src/Tichu.Core/Combinations/CombinationRecognizer.cs`
- Test: `core/tests/Tichu.Core.Tests/RecognizePairTripleFullTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/RecognizePairTripleFullTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizePairTripleFullTests
    {
        private static Combination R(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        [Test]
        public void Pair_of_same_rank()
        {
            var c = R(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));
            Assert.That(c.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(c.Length, Is.EqualTo(2));
            Assert.That(c.Rank, Is.EqualTo(18)); // 9*2
        }

        [Test]
        public void Pair_with_phoenix()
        {
            var c = R(Card.Normal(9, Suit.Jade), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(c.Rank, Is.EqualTo(18));
            Assert.That(c.PointsInPlay, Is.EqualTo(-25)); // 봉황 점수 유지
        }

        [Test]
        public void Triple_plain_and_with_phoenix()
        {
            Assert.That(R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star), Card.Normal(7, Suit.Sword)).Type,
                        Is.EqualTo(CombinationType.Triple));
            var ph = R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star), Card.Phoenix);
            Assert.That(ph.Type, Is.EqualTo(CombinationType.Triple));
            Assert.That(ph.Rank, Is.EqualTo(14)); // 7*2
        }

        [Test]
        public void FullHouse_rank_is_triple_rank()
        {
            var c = R(Card.Normal(8, Suit.Jade), Card.Normal(8, Suit.Star), Card.Normal(8, Suit.Sword),
                      Card.Normal(4, Suit.Jade), Card.Normal(4, Suit.Star));
            Assert.That(c.Type, Is.EqualTo(CombinationType.FullHouse));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(16)); // 8*2 (트리플 기준)
        }

        [Test]
        public void FullHouse_with_phoenix_completing_the_pair()
        {
            // 8,8,8 + 4 + 봉황 → 봉황이 4의 짝 → 풀하우스(트리플 8)
            var c = R(Card.Normal(8, Suit.Jade), Card.Normal(8, Suit.Star), Card.Normal(8, Suit.Sword),
                      Card.Normal(4, Suit.Jade), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.FullHouse));
            Assert.That(c.Rank, Is.EqualTo(16));
        }

        [Test]
        public void Mahjong_cannot_form_pair()
        {
            Assert.That(R(Card.Mahjong, Card.Phoenix).Type, Is.EqualTo(CombinationType.Invalid));
        }

        [Test]
        public void Two_different_ranks_is_not_a_pair()
        {
            Assert.That(R(Card.Normal(9, Suit.Jade), Card.Normal(8, Suit.Star)).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — 멀티카드 미구현(Invalid 반환).

- [ ] **Step 3: 구현 — 분석 헬퍼 + 페어/트리플/풀하우스 추가**

`CombinationRecognizer.cs`의 `Recognize` 디스패처를 아래로 교체하고, 헬퍼들을 클래스에 추가:
```csharp
        public static Combination Recognize(ReadOnlySpan<Card> cards, TrickContext ctx)
        {
            int n = cards.Length;
            if (n == 0) return Combination.Invalid;
            if (n == 1) return RecognizeSingle(cards[0], ctx);

            var h = HandShape.Analyze(cards);
            if (h.HasDog || h.HasDragon) return Combination.Invalid; // 개/용은 단독만

            Combination c;
            if ((c = RecognizeBomb(h)).Type != CombinationType.Invalid) return c;          // Task 8
            if ((c = RecognizePairTripleFull(h)).Type != CombinationType.Invalid) return c; // Task 6
            if ((c = RecognizeStraight(h)).Type != CombinationType.Invalid) return c;        // Task 7
            if ((c = RecognizeConsecutivePairs(h)).Type != CombinationType.Invalid) return c;// Task 7
            return Combination.Invalid;
        }
```
> Task 7·8의 `RecognizeBomb`/`RecognizeStraight`/`RecognizeConsecutivePairs`가 아직 없으므로, **이 태스크에서는 그 세 메서드의 빈 스텁**(항상 `Combination.Invalid` 반환)을 먼저 추가해 컴파일을 통과시킨다. Task 7·8에서 본구현으로 교체.

분석 헬퍼(파일에 추가):
```csharp
        // 멀티카드 판별용 손 모양 요약.
        private readonly struct HandShape
        {
            public readonly int[] Counts;      // index 1..14 (마작=1), 일반/마작 랭크별 개수
            public readonly int PhoenixCount;  // 0 또는 1
            public readonly bool HasDog;
            public readonly bool HasDragon;
            public readonly bool HasMahjong;
            public readonly int Points;        // 조합 총 점수
            public readonly int CardCount;     // 봉황 포함 전체 장수
            public readonly Card[] Source;     // 원본 카드(점수/문양 참조용)

            private HandShape(int[] counts, int phoenix, bool dog, bool dragon, bool mahjong,
                              int points, int cardCount, Card[] source)
            {
                Counts = counts; PhoenixCount = phoenix; HasDog = dog; HasDragon = dragon;
                HasMahjong = mahjong; Points = points; CardCount = cardCount; Source = source;
            }

            public static HandShape Analyze(ReadOnlySpan<Card> cards)
            {
                var counts = new int[15];
                int phoenix = 0, points = 0; bool dog = false, dragon = false, mahjong = false;
                var src = new Card[cards.Length];
                for (int i = 0; i < cards.Length; i++)
                {
                    Card c = cards[i]; src[i] = c; points += c.Points;
                    switch (c.Special)
                    {
                        case SpecialKind.Phoenix: phoenix++; break;
                        case SpecialKind.Dog: dog = true; break;
                        case SpecialKind.Dragon: dragon = true; break;
                        case SpecialKind.Mahjong: mahjong = true; counts[1]++; break;
                        default: counts[c.Rank]++; break;
                    }
                }
                return new HandShape(counts, phoenix, dog, dragon, mahjong, points, cards.Length, src);
            }
        }

        // 마작(랭크1)이 들어가면 페어/트리플/풀하우스/연속페어 불가.
        private static bool UsesMahjong(in HandShape h) => h.Counts[1] > 0;

        private static Combination RecognizePairTripleFull(in HandShape h)
        {
            if (UsesMahjong(h)) return Combination.Invalid;
            int n = h.CardCount;

            if (n == 2) // 페어
            {
                int rank = SingleRankWithPhoenix(h, needed: 2);
                return rank > 0
                    ? new Combination(CombinationType.Pair, h.Source, 2, rank * 2, h.Points)
                    : Combination.Invalid;
            }
            if (n == 3) // 트리플
            {
                int rank = SingleRankWithPhoenix(h, needed: 3);
                return rank > 0
                    ? new Combination(CombinationType.Triple, h.Source, 3, rank * 2, h.Points)
                    : Combination.Invalid;
            }
            if (n == 5) // 풀하우스 = 트리플 + 페어
            {
                int tripleRank = 0, pairRank = 0;
                if (h.PhoenixCount == 0)
                {
                    for (int r = 2; r <= 14; r++)
                    {
                        int cnt = h.Counts[r];
                        if (cnt == 0) continue;
                        if (cnt == 3) { if (tripleRank != 0) return Combination.Invalid; tripleRank = r; }
                        else if (cnt == 2) { if (pairRank != 0) return Combination.Invalid; pairRank = r; }
                        else return Combination.Invalid;
                    }
                }
                else // 봉황 1장: 자연 4장 패턴은 (3+1) 또는 (2+2)
                {
                    int triple = 0, single = 0, pairA = 0, pairB = 0;
                    for (int r = 2; r <= 14; r++)
                    {
                        int cnt = h.Counts[r];
                        if (cnt == 0) continue;
                        if (cnt == 3) { if (triple != 0) return Combination.Invalid; triple = r; }
                        else if (cnt == 2) { if (pairA == 0) pairA = r; else if (pairB == 0) pairB = r; else return Combination.Invalid; }
                        else if (cnt == 1) { if (single != 0) return Combination.Invalid; single = r; }
                        else return Combination.Invalid;
                    }
                    if (triple != 0 && single != 0 && pairA == 0)        // 3 + 1 + 봉황 → 봉황이 페어 완성
                    { tripleRank = triple; pairRank = single; }
                    else if (pairA != 0 && pairB != 0 && triple == 0 && single == 0) // 2 + 2 + 봉황 → 봉황이 트리플 완성(높은 페어)
                    { tripleRank = pairA > pairB ? pairA : pairB; pairRank = pairA > pairB ? pairB : pairA; }
                    else return Combination.Invalid;
                }

                if (tripleRank != 0 && pairRank != 0)
                    return new Combination(CombinationType.FullHouse, h.Source, 5, tripleRank * 2, h.Points);
                return Combination.Invalid;
            }
            return Combination.Invalid;
        }

        // n장이 한 랭크로 모이는지(봉황 1장 대체 허용) 검사 → 그 랭크 반환(실패 0).
        private static int SingleRankWithPhoenix(in HandShape h, int needed)
        {
            int found = 0;
            for (int r = 2; r <= 14; r++)
            {
                if (h.Counts[r] == 0) continue;
                if (found != 0) return 0;            // 두 종류 이상 → 실패
                found = r;
            }
            if (found == 0) return 0;
            int have = h.Counts[found] + h.PhoenixCount;
            return have == needed ? found : 0;
        }
```
> 봉황 풀하우스는 자연 4장이 (3+1)이면 봉황이 페어를, (2+2)이면 봉황이 더 높은 페어를 트리플로 완성한다. `FullHouse_with_phoenix_completing_the_pair`(8,8,8+4+봉황 → triple=8/single=4 → 트리플8·페어4)로 검증.

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS (Card/Rng/Deck/RecognizeSingle/PairTripleFull 전부).

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Combinations/CombinationRecognizer.cs core/tests/Tichu.Core.Tests/RecognizePairTripleFullTests.cs
git commit -m "feat(core): recognize pair/triple/fullhouse with phoenix wild"
```

---

## Task 7: Recognize — 스트레이트 & 연속 페어 (봉황 와일드 · 마작 최하단)

**규칙:**
- **스트레이트**: 서로 다른 5장 이상의 연속 랭크. 마작(1)은 최하단으로 가능(1,2,3,4,5…). A=14 최상단, 랩어라운드 없음. 봉황은 내부 빈칸 1개를 메우거나(gap==1) 빈칸이 없으면 한쪽 끝을 확장(gap==0). `Rank`=최상단 값 ×2. `Length`=장수.
- **연속 페어**: 짝수 4장 이상, 연속 랭크가 각각 2장(봉황은 한 랭크의 빠진 1장을 메움). 마작 불가. `Rank`=최상단 페어 값 ×2.

**Files:**
- Modify: `core/src/Tichu.Core/Combinations/CombinationRecognizer.cs` (Task 6의 스텁을 본구현으로 교체)
- Test: `core/tests/Tichu.Core.Tests/RecognizeStraightPairsTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/RecognizeStraightPairsTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizeStraightPairsTests
    {
        private static Combination R(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        private static Card N(int r) => Card.Normal(r, Suit.Jade);     // 문양 섞기용
        private static Card M(int r, Suit s) => Card.Normal(r, s);

        [Test]
        public void Straight_of_five_mixed_suits()
        {
            var c = R(M(5, Suit.Jade), M(6, Suit.Star), M(7, Suit.Sword), M(8, Suit.Pagoda), M(9, Suit.Jade));
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(18)); // top 9 *2
        }

        [Test]
        public void Straight_can_start_at_mahjong()
        {
            var c = R(Card.Mahjong, M(2, Suit.Jade), M(3, Suit.Star), M(4, Suit.Sword), M(5, Suit.Pagoda));
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Rank, Is.EqualTo(10)); // top 5 *2
        }

        [Test]
        public void Straight_with_phoenix_filling_internal_gap()
        {
            // 5,6,_,8,9 + 봉황 → 7 메움
            var c = R(M(5, Suit.Jade), M(6, Suit.Star), M(8, Suit.Sword), M(9, Suit.Pagoda), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(18)); // top 9 *2
        }

        [Test]
        public void Straight_with_phoenix_extending_top()
        {
            // 5,6,7,8 + 봉황 (gap 없음) → 9로 상단 확장
            var c = R(M(5, Suit.Jade), M(6, Suit.Star), M(7, Suit.Sword), M(8, Suit.Pagoda), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(c.Rank, Is.EqualTo(18)); // 상단 확장 9 *2
        }

        [Test]
        public void Four_cards_is_too_short_for_straight()
        {
            Assert.That(R(M(5, Suit.Jade), M(6, Suit.Star), M(7, Suit.Sword), M(8, Suit.Pagoda)).Type,
                        Is.Not.EqualTo(CombinationType.Straight));
        }

        [Test]
        public void Consecutive_pairs_two()
        {
            var c = R(M(5, Suit.Jade), M(5, Suit.Star), M(6, Suit.Sword), M(6, Suit.Pagoda));
            Assert.That(c.Type, Is.EqualTo(CombinationType.ConsecutivePairs));
            Assert.That(c.Length, Is.EqualTo(4));
            Assert.That(c.Rank, Is.EqualTo(12)); // top pair 6 *2
        }

        [Test]
        public void Consecutive_pairs_with_phoenix()
        {
            // 5,5,6 + 봉황 → 봉황이 6의 짝 → (5,5)(6,6)
            var c = R(M(5, Suit.Jade), M(5, Suit.Star), M(6, Suit.Sword), Card.Phoenix);
            Assert.That(c.Type, Is.EqualTo(CombinationType.ConsecutivePairs));
            Assert.That(c.Rank, Is.EqualTo(12)); // top 6 *2
        }

        [Test]
        public void Non_consecutive_pairs_is_invalid()
        {
            Assert.That(R(M(5, Suit.Jade), M(5, Suit.Star), M(8, Suit.Sword), M(8, Suit.Pagoda)).Type,
                        Is.EqualTo(CombinationType.Invalid));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — 스텁이 Invalid 반환.

- [ ] **Step 3: 구현 — 스트레이트 & 연속 페어 (Task 6 스텁 교체)**

`CombinationRecognizer.cs`의 `RecognizeStraight`/`RecognizeConsecutivePairs` 스텁을 아래로 교체:
```csharp
        private static Combination RecognizeStraight(in HandShape h)
        {
            int n = h.CardCount;
            if (n < 5) return Combination.Invalid;

            // 일반/마작 랭크는 각 1장만 허용(중복 시 스트레이트 아님). 봉황 0/1.
            int min = 0, max = 0, distinct = 0;
            for (int r = 1; r <= 14; r++)
            {
                int cnt = h.Counts[r];
                if (cnt == 0) continue;
                if (cnt > 1) return Combination.Invalid;
                if (min == 0) min = r;
                max = r; distinct++;
            }
            if (distinct == 0) return Combination.Invalid;

            int span = max - min + 1;
            int gaps = span - distinct;          // [min,max] 내부 빠진 랭크 수
            int phoenix = h.PhoenixCount;
            int topValue;

            if (phoenix == 0)
            {
                if (gaps != 0 || distinct != n) return Combination.Invalid;
                topValue = max;
            }
            else // phoenix == 1
            {
                if (distinct + 1 != n) return Combination.Invalid;
                if (gaps == 1) topValue = max;                 // 내부 빈칸 메움
                else if (gaps == 0)                            // 끝 확장
                {
                    if (max + 1 <= 14) topValue = max + 1;     // 상단 확장 우선
                    else if (min - 1 >= 1) topValue = max;     // 상단 불가 → 하단 확장
                    else return Combination.Invalid;
                }
                else return Combination.Invalid;               // 봉황 1장으로 메울 수 없음
            }

            if (n < 5) return Combination.Invalid;
            return new Combination(CombinationType.Straight, h.Source, n, topValue * 2, h.Points);
        }

        private static Combination RecognizeConsecutivePairs(in HandShape h)
        {
            int n = h.CardCount;
            if (n < 4 || (n % 2) != 0) return Combination.Invalid;
            if (UsesMahjong(h)) return Combination.Invalid;

            int min = 0, max = 0, distinct = 0, singles = 0;
            for (int r = 2; r <= 14; r++)
            {
                int cnt = h.Counts[r];
                if (cnt == 0) continue;
                if (cnt > 2) return Combination.Invalid;
                if (cnt == 1) singles++;
                if (min == 0) min = r;
                max = r; distinct++;
            }
            if (distinct == 0) return Combination.Invalid;

            int needPairs = n / 2;
            // 봉황이 단수 랭크 1개를 페어로 완성. 봉황 없으면 단수 0이어야.
            if (h.PhoenixCount == 0 && singles != 0) return Combination.Invalid;
            if (h.PhoenixCount == 1 && singles != 1) return Combination.Invalid;
            if (distinct != needPairs) return Combination.Invalid;       // 연속이며 빈칸 없음
            if (max - min + 1 != needPairs) return Combination.Invalid;  // 랭크가 연속

            return new Combination(CombinationType.ConsecutivePairs, h.Source, n, max * 2, h.Points);
        }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Combinations/CombinationRecognizer.cs core/tests/Tichu.Core.Tests/RecognizeStraightPairsTests.cs
git commit -m "feat(core): recognize straights and consecutive pairs with phoenix"
```

---

## Task 8: Recognize — 폭탄 (포카드 · 스트레이트 플러시, 봉황 제외)

**규칙:** 포카드 폭탄=같은 랭크 4장(봉황 불가). 스트레이트 플러시 폭탄=같은 문양 연속 5장 이상(봉황 불가). 폭탄 판별은 다른 조합보다 우선(`Recognize` 디스패처에서 이미 첫 순위). `FourBomb.Rank`=랭크 값 ×2, `StraightFlushBomb.Rank`=최상단 값 ×2 + `Length`.

**Files:**
- Modify: `core/src/Tichu.Core/Combinations/CombinationRecognizer.cs` (Task 6의 `RecognizeBomb` 스텁 교체)
- Test: `core/tests/Tichu.Core.Tests/RecognizeBombTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/RecognizeBombTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class RecognizeBombTests
    {
        private static Combination R(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        [Test]
        public void Four_of_a_kind_is_a_bomb()
        {
            var c = R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                      Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda));
            Assert.That(c.Type, Is.EqualTo(CombinationType.FourBomb));
            Assert.That(c.IsBomb, Is.True);
            Assert.That(c.Rank, Is.EqualTo(14)); // 7*2
        }

        [Test]
        public void Straight_flush_is_a_bomb_and_beats_plain_straight_recognition()
        {
            // 같은 문양(Jade) 5,6,7,8,9 → 스트레이트 플러시 (일반 스트레이트로 인식되면 안 됨)
            var c = R(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                      Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade));
            Assert.That(c.Type, Is.EqualTo(CombinationType.StraightFlushBomb));
            Assert.That(c.Length, Is.EqualTo(5));
            Assert.That(c.Rank, Is.EqualTo(18)); // top 9 *2
        }

        [Test]
        public void Phoenix_cannot_make_a_bomb()
        {
            // 7,7,7 + 봉황 → 포카드 폭탄 아님(트리플로 인식)
            var c = R(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                      Card.Normal(7, Suit.Sword), Card.Phoenix);
            Assert.That(c.Type, Is.Not.EqualTo(CombinationType.FourBomb));
            // 같은 문양 연속 + 봉황 → 스트레이트 플러시 아님(일반 스트레이트)
            var sf = R(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                       Card.Normal(8, Suit.Jade), Card.Phoenix);
            Assert.That(sf.Type, Is.EqualTo(CombinationType.Straight));
            Assert.That(sf.Type, Is.Not.EqualTo(CombinationType.StraightFlushBomb));
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — `RecognizeBomb` 스텁이 Invalid 반환(포카드가 Invalid 또는 잘못 인식).

- [ ] **Step 3: 구현 — RecognizeBomb (Task 6 스텁 교체)**

`CombinationRecognizer.cs`의 `RecognizeBomb` 스텁을 아래로 교체:
```csharp
        private static Combination RecognizeBomb(in HandShape h)
        {
            if (h.PhoenixCount > 0) return Combination.Invalid; // 봉황은 폭탄 불가
            int n = h.CardCount;

            if (n == 4) // 포카드
            {
                if (UsesMahjong(h)) return Combination.Invalid;
                for (int r = 2; r <= 14; r++)
                    if (h.Counts[r] == 4)
                        return new Combination(CombinationType.FourBomb, h.Source, 4, r * 2, h.Points);
                return Combination.Invalid;
            }

            if (n >= 5) // 스트레이트 플러시
            {
                // 같은 문양 + 연속 + 각 랭크 1장
                Suit suit = h.Source[0].Suit;
                int min = 0, max = 0, distinct = 0;
                for (int i = 0; i < h.Source.Length; i++)
                {
                    Card c = h.Source[i];
                    if (c.IsSpecial || c.Suit != suit) return Combination.Invalid;
                }
                for (int r = 2; r <= 14; r++)
                {
                    int cnt = h.Counts[r];
                    if (cnt == 0) continue;
                    if (cnt > 1) return Combination.Invalid;
                    if (min == 0) min = r;
                    max = r; distinct++;
                }
                if (distinct == n && (max - min + 1) == n)
                    return new Combination(CombinationType.StraightFlushBomb, h.Source, n, max * 2, h.Points);
                return Combination.Invalid;
            }

            return Combination.Invalid;
        }
```
> 마작은 문양이 `Special`이라 위 문양검사에서 자동 배제(스플 불가). 정상.

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS (전체 Recognize 스위트).

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Combinations/CombinationRecognizer.cs core/tests/Tichu.Core.Tests/RecognizeBombTests.cs
git commit -m "feat(core): recognize four-of-a-kind and straight-flush bombs"
```

---

## Task 9: Beats — 비교 (비폭탄 · 폭탄 계층 · 용 · 봉황 단독)

**규칙(기획서 11.4):**
- 비폭탄 vs 비폭탄: **같은 타입·같은 장수**이고 `Rank`가 더 높아야 이김.
- 폭탄 vs 비폭탄: 폭탄이 무조건 이김.
- 포카드 vs 포카드: 랭크 높은 쪽.
- 스플 vs 포카드: 스플이 이김. 포카드 vs 스플: 패배.
- 스플 vs 스플: **더 길면** 이김, 같은 길이면 `Rank` 높은 쪽.
- 용 단독 = 최강(Rank 30). **봉황은 용을 못 이김**(직전이 용 단독이면 봉황 단독은 패배).

**Files:**
- Create: `core/src/Tichu.Core/Combinations/CombinationComparer.cs`
- Test: `core/tests/Tichu.Core.Tests/BeatsTests.cs`

- [ ] **Step 1: 실패하는 테스트 작성**

`core/tests/Tichu.Core.Tests/BeatsTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class BeatsTests
    {
        private static Combination Single(Card c, TrickContext ctx) =>
            CombinationRecognizer.Recognize(new[] { c }, ctx);
        private static Combination Lead(params Card[] cards) =>
            CombinationRecognizer.Recognize(cards, TrickContext.Lead);

        [Test]
        public void Higher_same_type_beats_lower()
        {
            var top = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));   // 페어 9
            var cand = Lead(Card.Normal(11, Suit.Jade), Card.Normal(11, Suit.Star)); // 페어 J
            Assert.That(CombinationComparer.Beats(cand, top), Is.True);
            Assert.That(CombinationComparer.Beats(top, cand), Is.False);
        }

        [Test]
        public void Different_type_or_length_does_not_beat()
        {
            var top = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star));    // 페어
            var cand = Lead(Card.Normal(13, Suit.Jade));                              // 단독
            Assert.That(CombinationComparer.Beats(cand, top), Is.False);
        }

        [Test]
        public void Four_bomb_beats_non_bomb_and_higher_bomb_wins()
        {
            var pair = Lead(Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Star)); // 페어 A
            var bomb7 = Lead(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Star),
                             Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda));
            var bomb9 = Lead(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star),
                             Card.Normal(9, Suit.Sword), Card.Normal(9, Suit.Pagoda));
            Assert.That(CombinationComparer.Beats(bomb7, pair), Is.True);
            Assert.That(CombinationComparer.Beats(bomb9, bomb7), Is.True);
            Assert.That(CombinationComparer.Beats(bomb7, bomb9), Is.False);
        }

        [Test]
        public void Straight_flush_beats_four_bomb_and_longer_sf_wins()
        {
            var four = Lead(Card.Normal(14, Suit.Jade), Card.Normal(14, Suit.Star),
                            Card.Normal(14, Suit.Sword), Card.Normal(14, Suit.Pagoda));
            var sf5 = Lead(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                           Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade));
            var sf6 = Lead(Card.Normal(5, Suit.Star), Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star),
                           Card.Normal(8, Suit.Star), Card.Normal(9, Suit.Star), Card.Normal(10, Suit.Star));
            Assert.That(CombinationComparer.Beats(sf5, four), Is.True);
            Assert.That(CombinationComparer.Beats(sf6, sf5), Is.True);   // 더 긺
            Assert.That(CombinationComparer.Beats(sf5, sf6), Is.False);
        }

        [Test]
        public void Dragon_is_highest_single_and_phoenix_cannot_beat_it()
        {
            var dragon = Single(Card.Dragon, TrickContext.Lead);                  // Rank 30
            var aceTop = new TrickContext(false, true, 28);                       // 직전 A(28)
            var phoenixOverAce = Single(Card.Phoenix, aceTop);                    // 29
            Assert.That(CombinationComparer.Beats(dragon, phoenixOverAce), Is.True);

            var dragonTop = new TrickContext(false, true, 30);
            var phoenixOverDragon = Single(Card.Phoenix, dragonTop);             // 구조상 31이지만
            Assert.That(CombinationComparer.Beats(phoenixOverDragon, dragon), Is.False); // 용은 못 이김
        }

        [Test]
        public void Phoenix_beats_a_normal_single_by_half()
        {
            var kingTop = new TrickContext(false, true, 26);                      // K
            var phoenix = Single(Card.Phoenix, kingTop);                          // 27 (13.5)
            var king = Single(Card.Normal(13, Suit.Jade), TrickContext.Lead);    // 26
            Assert.That(CombinationComparer.Beats(phoenix, king), Is.True);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

Run: `dotnet test core/Tichu.sln`
Expected: FAIL — `CombinationComparer` 미정의.

- [ ] **Step 3: 최소 구현**

`core/src/Tichu.Core/Combinations/CombinationComparer.cs`:
```csharp
using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    public static class CombinationComparer
    {
        /// <summary>candidate가 현재 top을 이기는가(추종 상황). top이 null이면 비교 불요.</summary>
        public static bool Beats(Combination candidate, Combination top)
        {
            if (candidate == null || candidate.Type == CombinationType.Invalid) return false;
            if (top == null || top.Type == CombinationType.Invalid) return false;

            // 봉황 단독은 용 단독을 못 이김.
            if (candidate.Type == CombinationType.Single && top.Type == CombinationType.Single
                && candidate.Cards[0].Special == SpecialKind.Phoenix
                && top.Cards[0].Special == SpecialKind.Dragon)
                return false;

            bool cb = candidate.IsBomb, tb = top.IsBomb;
            if (cb && !tb) return true;
            if (!cb && tb) return false;

            if (cb && tb) // 폭탄 vs 폭탄
            {
                bool cSf = candidate.Type == CombinationType.StraightFlushBomb;
                bool tSf = top.Type == CombinationType.StraightFlushBomb;
                if (cSf && !tSf) return true;     // 스플 > 포카드
                if (!cSf && tSf) return false;    // 포카드 < 스플
                if (!cSf && !tSf) return candidate.Rank > top.Rank;          // 포카드끼리
                if (candidate.Length != top.Length) return candidate.Length > top.Length; // 스플: 길이 우선
                return candidate.Rank > top.Rank;
            }

            // 비폭탄 vs 비폭탄: 타입·장수 일치 + 값↑
            if (candidate.Type != top.Type) return false;
            if (candidate.Length != top.Length) return false;
            return candidate.Rank > top.Rank;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test core/Tichu.sln`
Expected: PASS (전체 Part 1 스위트).

- [ ] **Step 5: 커밋**

```bash
git add core/src/Tichu.Core/Combinations/CombinationComparer.cs core/tests/Tichu.Core.Tests/BeatsTests.cs
git commit -m "feat(core): add combination comparison (bombs, dragon, phoenix)"
```

---

## Part 1 완료 기준 (DoD)

- [ ] `dotnet test core/Tichu.sln` 전체 통과 (Card/Rng/Deck/Recognize×4/Beats).
- [ ] `Tichu.Core`가 `UnityEngine`을 일절 참조하지 않음(=`netstandard2.1` 빌드 성공 자체가 보증).
- [ ] 봉황 비교에 `float` 미사용(전부 정수 ×2 스케일) — 결정성 확보.
- [ ] 기획서 11.4 비교 매트릭스의 각 칸이 `BeatsTests`로 1개 이상 검증됨.

> **Part 1은 여기까지.** Part 2에서 `GameState`/`Trick`/`PlayerSeat`/`ScoreBoard`/`GameAction` 등 상태 타입과 `LegalMoves`(+마작 소원 강제)·`Apply`(딜/교환/트릭/패스/폭탄/개/원-투/라운드종료 FSM)·`ScoreRound`(정산), 그리고 헤드리스 시뮬레이터 + 프로퍼티/결정성/10만 판 무결성 테스트를 다룬다. Part 2 계획서는 Part 1 실행·검토 후 별도 작성.

## Self-Review 결과 (작성자 점검)
- **Spec 커버리지:** 덱 100점·56장(Task3), 특수카드 메타(Task1), 6종 조합+폭탄 판별(Task5~8), 봉황 와일드/단독(Task5~7), 비교 우선순위 11.4(Task9) — 모두 태스크로 매핑됨. (소원·정산·FSM은 의도적으로 Part 2로 분리.)
- **Placeholder:** 없음(모든 코드 단계에 실제 코드 수록). Task 6에서 Task7·8 메서드의 "빈 스텁 → 본구현 교체"를 명시(전방참조 컴파일 처리).
- **타입 일관성:** `Recognize(ReadOnlySpan<Card>, TrickContext)`, `Beats(Combination, Combination)`, `Rng.NextInt/NextULong`, `Deck.CreateStandard/Shuffle`, `Combination(Type,Cards,Length,Rank,PointsInPlay)` — 전 태스크 시그니처 일치 확인.
- **알려진 주의점:** 봉황 와일드/스트레이트 끝확장 등 분기는 Step1 테스트로 1차 고정하되, Part 2 헤드리스 10만 판 시뮬이 손패 전수에 대한 회귀를 추가로 자연 검증한다(기획서 11.8 프로퍼티 테스트).
