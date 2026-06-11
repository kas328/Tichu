using System.Collections.Generic;
using System.Linq;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Presentation.ViewModel;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 런타임에 코드로 빌드되는 placeholder uGUI 뷰.
    /// 프리팹/SerializeField 배선 없이 캔버스 아래에 위젯을 생성하고
    /// TableViewModel 의 ReactiveProperty 를 구독한다.
    /// 좌석 HUD 는 테이블 위치별로 배치하고, 내 손패(앞면)는 하단에 항상 표시하며,
    /// 결정 버튼은 고정 크기 그리드로 둔다. 게임 규칙 판정은 일절 하지 않고(ViewModel 게이팅)
    /// 입력을 vm.Submit… 로 전달만 한다.
    /// </summary>
    public sealed class TableUiView
    {
        // 좌석 라벨. 0=South(나), 1=West, 2=North(파트너), 3=East.
        private static readonly string[] SeatNames = { "나(South)", "West", "North(파트너)", "East" };

        private static readonly Color Felt      = new Color(0.07f, 0.22f, 0.13f);
        private static readonly Color Ink        = new Color(0.93f, 0.96f, 0.94f);
        private static readonly Color BtnOn       = new Color(0.20f, 0.42f, 0.72f);
        private static readonly Color BtnOff      = new Color(0.35f, 0.35f, 0.38f);
        private static readonly Color CardBg      = new Color(0.96f, 0.97f, 0.98f);
        private static readonly Color CardInk      = new Color(0.10f, 0.12f, 0.16f);
        private static readonly Color CardRed      = new Color(0.78f, 0.10f, 0.12f);

        private TableViewModel _vm;
        private readonly CompositeDisposable _subs = new CompositeDisposable();

        private readonly Text[] _seatTexts = new Text[4];
        private Text _trickText;
        private Text _phaseText;
        private Text _resultText;
        private RectTransform _handRoot;     // 내 손패(앞면) 카드 칩들
        private RectTransform _promptRoot;    // 결정 버튼 그리드
        private Text _promptLabel;

        // 교환 입력용 임시 상태(Left→Partner→Right).
        private readonly List<Card> _exchangePicks = new List<Card>(3);

        public void Bind(TableViewModel vm, Canvas canvas)
        {
            _vm = vm;
            BuildLayout(canvas);
            Subscribe();
        }

        // ── 레이아웃 빌드 ────────────────────────────────────────────────────

        private void BuildLayout(Canvas canvas)
        {
            // 배경(어두운 펠트) — 가독성 보장.
            var root = NewPanel("Root", canvas.transform);
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);
            var bg = root.AddComponent<Image>();
            bg.color = Felt;

            // 상단 정보.
            _phaseText = NewAnchoredText("PhaseText", rootRt, "Phase: -", 26,
                new Vector2(0, 1), new Vector2(20, -16), new Vector2(560, 40), TextAnchor.UpperLeft);
            _resultText = NewAnchoredText("ResultText", rootRt, "", 24,
                new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1000, 90), TextAnchor.UpperCenter);

            // 좌석 HUD — 테이블 위치별. 2=North(상), 1=West(좌), 3=East(우), 0=South(하·손패 위).
            _seatTexts[2] = NewAnchoredText("SeatNorth", rootRt, "", 26,
                new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(560, 48), TextAnchor.MiddleCenter);
            _seatTexts[1] = NewAnchoredText("SeatWest", rootRt, "", 26,
                new Vector2(0, 0.5f), new Vector2(24, 140), new Vector2(320, 48), TextAnchor.MiddleLeft);
            _seatTexts[3] = NewAnchoredText("SeatEast", rootRt, "", 26,
                new Vector2(1, 0.5f), new Vector2(-24, 140), new Vector2(320, 48), TextAnchor.MiddleRight);
            _seatTexts[0] = NewAnchoredText("SeatSouth", rootRt, "", 28,
                new Vector2(0.5f, 0), new Vector2(0, 188), new Vector2(760, 44), TextAnchor.MiddleCenter);

            // 현재 트릭 — 중앙.
            _trickText = NewAnchoredText("TrickText", rootRt, "트릭: (없음)", 26,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960, 130), TextAnchor.MiddleCenter);

            // 내 손패(앞면) — 하단 가로 스트립(항상 표시).
            var handPanel = NewPanel("HandStrip", rootRt);
            var handRt = handPanel.GetComponent<RectTransform>();
            AnchorBottomStretch(handRt, height: 150, bottom: 16, sideInset: 8);
            var handLayout = handPanel.AddComponent<HorizontalLayoutGroup>();
            handLayout.spacing = 5;
            handLayout.childAlignment = TextAnchor.MiddleCenter;
            handLayout.childControlWidth = true;
            handLayout.childControlHeight = true;
            handLayout.childForceExpandWidth = false;
            handLayout.childForceExpandHeight = false;
            handLayout.padding = new RectOffset(10, 10, 8, 8);
            _handRoot = handRt;

            // 결정 프롬프트 — 손패 위. 라벨 + 고정 크기 버튼 그리드.
            var promptPanel = NewPanel("PromptPanel", rootRt);
            var promptRt = promptPanel.GetComponent<RectTransform>();
            AnchorBottomStretch(promptRt, height: 380, bottom: 180, sideInset: 8);
            var promptV = promptPanel.AddComponent<VerticalLayoutGroup>();
            promptV.spacing = 8;
            promptV.childControlWidth = true;
            promptV.childControlHeight = false;
            promptV.childForceExpandWidth = true;
            promptV.childForceExpandHeight = false;
            promptV.childAlignment = TextAnchor.LowerCenter;
            promptV.padding = new RectOffset(16, 16, 10, 10);

            _promptLabel = NewText("PromptLabel", promptPanel.transform, "", 26);
            _promptLabel.alignment = TextAnchor.MiddleCenter;

            var grid = NewPanel("PromptButtons", promptPanel.transform);
            var gridLayout = grid.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(336, 52);
            gridLayout.spacing = new Vector2(10, 8);
            gridLayout.childAlignment = TextAnchor.UpperCenter;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 3;
            _promptRoot = grid.GetComponent<RectTransform>();
        }

        // ── 구독 ──────────────────────────────────────────────────────────────

        private void Subscribe()
        {
            _vm.Phase.Subscribe(p => _phaseText.text = $"Phase: {p}").AddTo(_subs);
            _vm.CurrentTrick.Subscribe(RenderTrick).AddTo(_subs);
            _vm.RoundResult.Subscribe(RenderResult).AddTo(_subs);
            _vm.MyHand.Subscribe(RenderHand).AddTo(_subs);

            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                _vm.HandCount(seat)
                    .Subscribe(cnt => _seatTexts[seat].text = $"{SeatNames[seat]}\n{cnt}장")
                    .AddTo(_subs);
            }

            _vm.PendingDecision.Subscribe(RenderPrompt).AddTo(_subs);
        }

        // ── 렌더러 ──────────────────────────────────────────────────────────

        private void RenderHand(IReadOnlyList<Card> hand)
        {
            ClearChildren(_handRoot);
            if (hand == null) return;
            // 보기 좋게 정렬해 표시(원본 손패는 건드리지 않음).
            foreach (var card in hand.OrderBy(SortKey))
                AddCardChip(card);
        }

        private void RenderTrick(Trick? trick)
        {
            if (trick?.Top == null) { _trickText.text = "트릭: (없음)"; return; }
            string cards = string.Join(" ", trick.Top.Cards);
            _trickText.text = $"트릭: {trick.Top.Type}\n[{cards}]  (소유 seat{trick.TopOwnerSeat})";
        }

        private void RenderResult(RoundResult? r)
        {
            _resultText.text = r == null ? "" :
                $"라운드 종료 — TeamA {r.TeamATotal} : TeamB {r.TeamBTotal}  " +
                $"(카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
        }

        private void RenderPrompt(DecisionRequest? req)
        {
            ClearChildren(_promptRoot);
            if (req == null) { _promptLabel.text = ""; return; }

            switch (req.Kind)
            {
                case DecisionKind.GrandTichu:
                    _promptLabel.text = "큰 티츄?";
                    AddButton("선언", () => _vm.SubmitGrandTichu(true));
                    AddButton("패스", () => _vm.SubmitGrandTichu(false));
                    break;

                case DecisionKind.Tichu:
                    _promptLabel.text = "작은 티츄?";
                    AddButton("선언", () => _vm.SubmitTichu(true));
                    AddButton("패스", () => _vm.SubmitTichu(false));
                    break;

                case DecisionKind.Turn:
                    RenderTurnPrompt(req.Context);
                    break;

                case DecisionKind.Bomb:
                    RenderBombPrompt(req.Context);
                    break;

                case DecisionKind.DragonRecipient:
                    RenderDragonPrompt(req.Context);
                    break;

                case DecisionKind.Exchange:
                    RenderExchangePrompt(req.Context);
                    break;
            }
        }

        private void RenderTurnPrompt(DecisionContext ctx)
        {
            _promptLabel.text = "내 턴 — 낼 수를 고르세요";
            foreach (var move in ctx.LegalMoves)
            {
                var captured = move;
                string label = $"{captured.Type}: {string.Join(" ", captured.Cards)}";
                AddButton(label, () => _vm.SubmitTurnDecision(TurnDecision.Play(captured)));
            }
            AddButton("패스", () => _vm.SubmitTurnDecision(TurnDecision.Pass), ctx.CanPass);
        }

        private void RenderBombPrompt(DecisionContext ctx)
        {
            _promptLabel.text = "폭탄 인터럽트?";
            foreach (var move in ctx.LegalMoves)
            {
                if (!move.IsBomb) continue;
                var captured = move;
                AddButton($"폭탄 {captured.Type}: {string.Join(" ", captured.Cards)}",
                    () => _vm.SubmitBomb(captured));
            }
            AddButton("넘기기", () => _vm.SubmitBomb(null));
        }

        private void RenderDragonPrompt(DecisionContext ctx)
        {
            _promptLabel.text = "용 트릭 양도 — 상대를 고르세요";
            int left = ctx.LeftSeat, right = ctx.RightSeat;
            AddButton($"왼쪽 상대(seat{left})", () => _vm.SubmitDragonRecipient(left));
            AddButton($"오른쪽 상대(seat{right})", () => _vm.SubmitDragonRecipient(right));
        }

        private void RenderExchangePrompt(DecisionContext ctx)
        {
            _exchangePicks.Clear();
            RebuildExchangeUi(ctx);
        }

        private void RebuildExchangeUi(DecisionContext ctx)
        {
            ClearChildren(_promptRoot);

            string[] slotNames = { "왼쪽", "파트너", "오른쪽" };
            int next = _exchangePicks.Count;
            string picked = _exchangePicks.Count == 0
                ? "(없음)"
                : string.Join(", ", _exchangePicks.Select((c, i) => $"{slotNames[i]}={c}"));
            string hint = next < 3 ? $"다음: {slotNames[next]}에 줄 카드" : "3장 완료 — 확정하세요";
            _promptLabel.text = $"카드 교환 — {hint}   선택: {picked}";

            if (_exchangePicks.Count < 3)
            {
                foreach (var card in ctx.MyHand.Distinct())
                {
                    if (_exchangePicks.Contains(card)) continue;
                    var captured = card;
                    AddButton($"{captured}", () => { _exchangePicks.Add(captured); RebuildExchangeUi(ctx); });
                }
            }

            AddButton("초기화", () => { _exchangePicks.Clear(); RebuildExchangeUi(ctx); });

            bool ready = _exchangePicks.Count == 3;
            AddButton("확정", () =>
            {
                if (_exchangePicks.Count != 3) return;
                var choice = new ExchangeChoice(_exchangePicks[0], _exchangePicks[1], _exchangePicks[2]);
                if (!_vm.SubmitExchange(choice)) { _exchangePicks.Clear(); RebuildExchangeUi(ctx); }
            }, ready);
        }

        // ── 위젯 헬퍼 ────────────────────────────────────────────────────────

        private void AddButton(string label, System.Action onClick, bool interactable = true)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_promptRoot, false);
            go.GetComponent<Image>().color = interactable ? BtnOn : BtnOff;

            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;
            btn.onClick.AddListener(() => onClick());

            var t = NewText("Label", go.transform, label, 20);
            t.color = Ink;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            StretchFull(t.rectTransform);
        }

        private void AddCardChip(Card card)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_handRoot, false);
            go.GetComponent<Image>().color = CardBg;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 66;
            le.preferredHeight = 100;
            le.minWidth = 66;

            var t = NewText("Label", go.transform, CardLabel(card), 22);
            t.color = IsRed(card) ? CardRed : CardInk;
            t.alignment = TextAnchor.MiddleCenter;
            StretchFull(t.rectTransform);
        }

        // ── 카드 표기/정렬 ───────────────────────────────────────────────────

        private static string CardLabel(Card c)
        {
            switch (c.Special)
            {
                case SpecialKind.Dragon:  return "龍";
                case SpecialKind.Phoenix: return "鳳";
                case SpecialKind.Dog:     return "犬";
                case SpecialKind.Mahjong: return "1";
                default: return $"{RankLabel(c.Rank)}\n{SuitGlyph(c.Suit)}";
            }
        }

        private static string RankLabel(int r)
        {
            switch (r) { case 14: return "A"; case 13: return "K"; case 12: return "Q"; case 11: return "J"; default: return r.ToString(); }
        }

        private static string SuitGlyph(Suit s)
        {
            switch (s) { case Suit.Jade: return "♣"; case Suit.Sword: return "♠"; case Suit.Pagoda: return "♦"; case Suit.Star: return "♥"; default: return ""; }
        }

        private static bool IsRed(Card c) => !c.IsSpecial && (c.Suit == Suit.Pagoda || c.Suit == Suit.Star);

        // 정렬용: 개=0, 마작=1, 봉황=2, 일반=Rank, 용=15.
        private static int SortKey(Card c)
        {
            switch (c.Special)
            {
                case SpecialKind.Dog: return 0;
                case SpecialKind.Mahjong: return 1;
                case SpecialKind.Phoenix: return 2;
                case SpecialKind.Dragon: return 15;
                default: return c.Rank;
            }
        }

        // ── 생성 헬퍼 ────────────────────────────────────────────────────────

        private static GameObject NewPanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Text NewText(string name, Transform parent, string content, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont();
            t.fontSize = fontSize;
            t.color = Ink;
            t.text = content;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>자유 배치(앵커+오프셋+크기)된 텍스트 위젯.</summary>
        private static Text NewAnchoredText(string name, RectTransform parent, string content, int fontSize,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size, TextAnchor align)
        {
            var t = NewText(name, parent, content, fontSize);
            t.alignment = align;
            var rt = t.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return t;
        }

        private static void AnchorBottomStretch(RectTransform rt, float height, float bottom, float sideInset)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(sideInset, bottom);
            rt.offsetMax = new Vector2(-sideInset, bottom + height);
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void ClearChildren(RectTransform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.Destroy(root.GetChild(i).gameObject);
        }

        private static Font DefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}
