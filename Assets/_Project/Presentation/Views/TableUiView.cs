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
    /// 손패는 앞면 카드 칩(클릭 선택), 상대는 뒷면 칩(수량), 트릭은 중앙에 앞면 카드로 표시.
    /// 결정 버튼은 우측 패널에 모아 손패를 가리지 않는다.
    /// 합법성 판정은 ViewModel 이 하며(SubmitTurnDecision 게이팅) 뷰는 선택을 합법수와 대조해 전달만 한다.
    /// </summary>
    public sealed class TableUiView
    {
        private static readonly string[] SeatNames = { "나(South)", "West", "North(파트너)", "East" };

        private static readonly Color Felt    = new Color(0.07f, 0.22f, 0.13f);
        private static readonly Color Ink      = new Color(0.93f, 0.96f, 0.94f);
        private static readonly Color Warn     = new Color(1.00f, 0.80f, 0.40f);
        private static readonly Color BtnOn    = new Color(0.20f, 0.42f, 0.72f);
        private static readonly Color BtnGo    = new Color(0.18f, 0.55f, 0.30f);
        private static readonly Color BtnOff   = new Color(0.35f, 0.35f, 0.38f);
        private static readonly Color CardBg   = new Color(0.96f, 0.97f, 0.98f);
        private static readonly Color CardSel  = new Color(1.00f, 0.86f, 0.32f);
        private static readonly Color CardInk  = new Color(0.10f, 0.12f, 0.16f);
        private static readonly Color CardRed  = new Color(0.78f, 0.10f, 0.12f);
        private static readonly Color Back     = new Color(0.16f, 0.24f, 0.45f); // 카드 뒷면

        private TableViewModel _vm;
        private readonly CompositeDisposable _subs = new CompositeDisposable();

        private readonly Text[] _seatTexts = new Text[4];
        private readonly RectTransform[] _backRoots = new RectTransform[4]; // 상대 뒷면 패(1·2·3)
        private Text _phaseText, _resultText, _trickOwnerText, _promptLabel, _hintLabel;
        private RectTransform _handRoot, _trickRoot, _actionRoot;

        private readonly List<Card> _selection = new List<Card>();
        private DecisionRequest _activeReq;
        private IReadOnlyList<Card> _hand = new List<Card>();

        public void Bind(TableViewModel vm, Canvas canvas)
        {
            _vm = vm;
            BuildLayout(canvas);
            Subscribe();
        }

        // ── 레이아웃 ─────────────────────────────────────────────────────────

        private void BuildLayout(Canvas canvas)
        {
            var root = NewPanel("Root", canvas.transform);
            var rt = root.GetComponent<RectTransform>();
            StretchFull(rt);
            root.AddComponent<Image>().color = Felt;

            _phaseText = NewAnchoredText("Phase", rt, "Phase: -", 26,
                new Vector2(0, 1), new Vector2(20, -16), new Vector2(520, 40), TextAnchor.UpperLeft);
            _resultText = NewAnchoredText("Result", rt, "", 24,
                new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(900, 44), TextAnchor.UpperCenter);

            // 상대/내 좌석 라벨 + 상대 뒷면 패.
            _seatTexts[2] = NewAnchoredText("NorthLbl", rt, "", 26,
                new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(560, 38), TextAnchor.MiddleCenter);
            _backRoots[2] = NewBackRow("NorthBacks", rt, new Vector2(0.5f, 1), new Vector2(0, -104), new Vector2(720, 56), TextAnchor.MiddleCenter);

            _seatTexts[1] = NewAnchoredText("WestLbl", rt, "", 26,
                new Vector2(0, 0.5f), new Vector2(20, 320), new Vector2(320, 38), TextAnchor.MiddleLeft);
            _backRoots[1] = NewBackRow("WestBacks", rt, new Vector2(0, 0.5f), new Vector2(20, 270), new Vector2(360, 56), TextAnchor.MiddleLeft);

            _seatTexts[3] = NewAnchoredText("EastLbl", rt, "", 26,
                new Vector2(1, 0.5f), new Vector2(-20, 520), new Vector2(320, 38), TextAnchor.MiddleRight);
            _backRoots[3] = NewBackRow("EastBacks", rt, new Vector2(1, 0.5f), new Vector2(-20, 470), new Vector2(360, 56), TextAnchor.MiddleRight);

            // 중앙 트릭(앞면 카드) + 소유자.
            _trickRoot = NewCardRow("TrickRow", rt, new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(940, 120), TextAnchor.MiddleCenter, 8);
            _trickOwnerText = NewAnchoredText("TrickOwner", rt, "트릭: (없음)", 24,
                new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(700, 36), TextAnchor.MiddleCenter);

            _seatTexts[0] = NewAnchoredText("SouthLbl", rt, "", 28,
                new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(520, 40), TextAnchor.MiddleCenter);

            // 내 손패(앞면, 클릭 선택) — 하단.
            _handRoot = NewCardRow("Hand", rt, new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(1060, 150), TextAnchor.MiddleCenter, 5);
            // 하단 정렬을 위해 손패 행을 bottom-stretch 로 다시 잡는다.
            AnchorBottomStretch(_handRoot, height: 150, bottom: 16, sideInset: 8);

            // 결정 패널 — 우측 하단(손패 위). 라벨 + 힌트 + 세로 액션 버튼.
            var panel = NewPanel("Decision", rt);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1, 0); prt.anchorMax = new Vector2(1, 0); prt.pivot = new Vector2(1, 0);
            prt.sizeDelta = new Vector2(350, 600); prt.anchoredPosition = new Vector2(-12, 180);
            var pv = panel.AddComponent<VerticalLayoutGroup>();
            pv.spacing = 8; pv.childControlWidth = true; pv.childControlHeight = false;
            pv.childForceExpandWidth = true; pv.childForceExpandHeight = false;
            pv.childAlignment = TextAnchor.LowerCenter; pv.padding = new RectOffset(8, 8, 8, 8);

            _promptLabel = NewText("PromptLabel", panel.transform, "", 26);
            _promptLabel.alignment = TextAnchor.MiddleCenter;
            _hintLabel = NewText("HintLabel", panel.transform, "", 20);
            _hintLabel.alignment = TextAnchor.MiddleCenter; _hintLabel.color = Warn;

            var actions = NewPanel("Actions", panel.transform);
            _actionRoot = actions.GetComponent<RectTransform>();
            var al = actions.AddComponent<VerticalLayoutGroup>();
            al.spacing = 8; al.childAlignment = TextAnchor.LowerCenter;
            al.childControlWidth = false; al.childControlHeight = false;
            al.childForceExpandWidth = false; al.childForceExpandHeight = false;
            actions.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // ── 구독 ─────────────────────────────────────────────────────────────

        private void Subscribe()
        {
            _vm.Phase.Subscribe(p => _phaseText.text = $"Phase: {p}").AddTo(_subs);
            _vm.CurrentTrick.Subscribe(RenderTrick).AddTo(_subs);
            _vm.RoundResult.Subscribe(RenderResult).AddTo(_subs);
            _vm.MyHand.Subscribe(h => { _hand = h ?? new List<Card>(); _selection.Clear(); RenderHand(); }).AddTo(_subs);
            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                _vm.HandCount(seat).Subscribe(c =>
                {
                    _seatTexts[seat].text = $"{SeatNames[seat]} · {c}장";
                    if (seat != 0) RenderBacks(seat, c);
                }).AddTo(_subs);
            }
            _vm.PendingDecision.Subscribe(RenderPrompt).AddTo(_subs);
        }

        // ── 손패(앞면, 선택) ─────────────────────────────────────────────────

        private bool CardSelectable =>
            _activeReq != null &&
            (_activeReq.Kind == DecisionKind.Turn || _activeReq.Kind == DecisionKind.Bomb || _activeReq.Kind == DecisionKind.Exchange);

        private void RenderHand()
        {
            ClearChildren(_handRoot);
            foreach (var card in _hand.OrderBy(SortKey))
                AddHandChip(card);
        }

        private void AddHandChip(Card card)
        {
            bool sel = _selection.Contains(card);
            var go = NewCardChip("Card", _handRoot, sel ? CardSel : CardBg, 66, sel ? 112 : 100);
            var btn = go.AddComponent<Button>();
            btn.interactable = CardSelectable;
            var cap = card;
            btn.onClick.AddListener(() => ToggleCard(cap));
            AddCardLabel(go.transform, CardLabel(card), IsRed(card) ? CardRed : CardInk, 22);
        }

        private void ToggleCard(Card card)
        {
            if (!CardSelectable) return;
            if (_selection.Contains(card)) _selection.Remove(card);
            else
            {
                if (_activeReq.Kind == DecisionKind.Exchange && _selection.Count >= 3) return;
                _selection.Add(card);
            }
            RenderHand();
            RefreshActions();
        }

        // ── 상대 뒷면 패 ──────────────────────────────────────────────────────

        private void RenderBacks(int seat, int count)
        {
            var rootb = _backRoots[seat];
            if (rootb == null) return;
            ClearChildren(rootb);
            int show = Mathf.Min(count, 14);
            for (int i = 0; i < show; i++)
                NewCardChip("Back", rootb, Back, 30, 46);
        }

        // ── 트릭(중앙, 앞면) ──────────────────────────────────────────────────

        private void RenderTrick(Trick? trick)
        {
            ClearChildren(_trickRoot);
            if (trick?.Top == null) { _trickOwnerText.text = "트릭: (없음)"; return; }
            foreach (var card in trick.Top.Cards.OrderBy(SortKey))
            {
                var go = NewCardChip("TrickCard", _trickRoot, CardBg, 60, 88);
                AddCardLabel(go.transform, CardLabel(card), IsRed(card) ? CardRed : CardInk, 20);
            }
            _trickOwnerText.text = $"{trick.Top.Type} · 소유 {SeatNames[trick.TopOwnerSeat]}";
        }

        private void RenderResult(RoundResult? r)
        {
            _resultText.text = r == null ? "" :
                $"라운드 종료 — TeamA {r.TeamATotal} : TeamB {r.TeamBTotal}  (카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
        }

        // ── 결정 프롬프트(우측) ───────────────────────────────────────────────

        private void RenderPrompt(DecisionRequest? req)
        {
            _activeReq = req;
            _selection.Clear();
            RenderHand();
            RefreshActions();
        }

        private void RefreshActions()
        {
            ClearChildren(_actionRoot);
            _hintLabel.text = "";
            var req = _activeReq;
            if (req == null) { _promptLabel.text = ""; return; }

            switch (req.Kind)
            {
                case DecisionKind.GrandTichu:
                    _promptLabel.text = "큰 티츄?";
                    AddAction("선언", BtnOn, () => _vm.SubmitGrandTichu(true));
                    AddAction("패스", BtnOn, () => _vm.SubmitGrandTichu(false));
                    break;
                case DecisionKind.Tichu:
                    _promptLabel.text = "작은 티츄?";
                    AddAction("선언", BtnOn, () => _vm.SubmitTichu(true));
                    AddAction("패스", BtnOn, () => _vm.SubmitTichu(false));
                    break;
                case DecisionKind.Turn:
                    _promptLabel.text = _selection.Count == 0 ? "내 턴 — 카드를 고르세요"
                        : $"선택: {string.Join(" ", _selection.OrderBy(SortKey))}";
                    AddAction("내기", BtnGo, PlaySelectedTurn, _selection.Count > 0);
                    bool canPass = req.Context.CanPass;
                    AddAction("패스", BtnOn, () => _vm.SubmitTurnDecision(TurnDecision.Pass), canPass);
                    if (!canPass)
                        _hintLabel.text = req.Context.State.CurrentTrick == null
                            ? "리드 — 패스 불가, 카드를 내야 합니다" : "소원 강제 — 패스 불가";
                    break;
                case DecisionKind.Bomb:
                    _promptLabel.text = _selection.Count == 0 ? "폭탄? 카드를 고르거나 넘기기"
                        : $"선택: {string.Join(" ", _selection.OrderBy(SortKey))}";
                    AddAction("폭탄 내기", BtnGo, PlaySelectedBomb, _selection.Count > 0);
                    AddAction("넘기기", BtnOn, () => _vm.SubmitBomb(null));
                    break;
                case DecisionKind.DragonRecipient:
                    _promptLabel.text = "용 양도 — 상대 선택";
                    int l = req.Context.LeftSeat, r = req.Context.RightSeat;
                    AddAction($"왼쪽(seat{l})", BtnOn, () => _vm.SubmitDragonRecipient(l));
                    AddAction($"오른쪽(seat{r})", BtnOn, () => _vm.SubmitDragonRecipient(r));
                    break;
                case DecisionKind.Exchange:
                    string[] slot = { "왼쪽", "파트너", "오른쪽" };
                    _promptLabel.text = _selection.Count < 3
                        ? $"교환 — {slot[_selection.Count]}에게 줄 카드 선택"
                        : $"좌={_selection[0]} 파={_selection[1]} 우={_selection[2]}";
                    AddAction("확정", BtnGo, ConfirmExchange, _selection.Count == 3);
                    AddAction("초기화", BtnOff, () => { _selection.Clear(); RenderHand(); RefreshActions(); });
                    break;
            }
        }

        private void PlaySelectedTurn()
        {
            var move = _activeReq.Context.LegalMoves.FirstOrDefault(m => CardsMatch(m.Cards, _selection));
            if (move == null) { _hintLabel.text = "낼 수 없는 조합입니다"; return; }
            _vm.SubmitTurnDecision(TurnDecision.Play(move));
        }

        private void PlaySelectedBomb()
        {
            var bomb = _activeReq.Context.LegalMoves.FirstOrDefault(m => m.IsBomb && CardsMatch(m.Cards, _selection));
            if (bomb == null) { _hintLabel.text = "유효한 폭탄이 아닙니다"; return; }
            _vm.SubmitBomb(bomb);
        }

        private void ConfirmExchange()
        {
            if (_selection.Count != 3) return;
            var choice = new ExchangeChoice(_selection[0], _selection[1], _selection[2]);
            if (!_vm.SubmitExchange(choice)) { _hintLabel.text = "교환 거부됨 — 다시"; _selection.Clear(); RenderHand(); RefreshActions(); }
        }

        // ── 위젯 ──────────────────────────────────────────────────────────────

        private void AddAction(string label, Color color, System.Action onClick, bool interactable = true)
        {
            var go = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_actionRoot, false);
            go.GetComponent<Image>().color = interactable ? color : BtnOff;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 320; le.minWidth = 320; le.preferredHeight = 62;
            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;
            btn.onClick.AddListener(() => onClick());
            AddCardLabel(go.transform, label, Ink, 24);
        }

        private static GameObject NewCardChip(string name, RectTransform parent, Color color, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.minWidth = w; le.preferredHeight = h; le.minHeight = h;
            return go;
        }

        private static void AddCardLabel(Transform parent, string text, Color color, int size)
        {
            var t = NewText("L", parent, text, size);
            t.color = color; t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            StretchFull(t.rectTransform);
        }

        // ── 카드 표기/정렬/비교 ──────────────────────────────────────────────

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

        private static bool CardsMatch(IReadOnlyList<Card> a, IReadOnlyList<Card> b)
        {
            if (a.Count != b.Count) return false;
            var rem = new List<Card>(a);
            foreach (var c in b) if (!rem.Remove(c)) return false;
            return true;
        }

        // ── 생성 헬퍼 ─────────────────────────────────────────────────────────

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
            t.font = DefaultFont(); t.fontSize = fontSize; t.color = Ink; t.text = content;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static Text NewAnchoredText(string name, RectTransform parent, string content, int fontSize,
            Vector2 anchor, Vector2 pos, Vector2 size, TextAnchor align)
        {
            var t = NewText(name, parent, content, fontSize);
            t.alignment = align;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            return t;
        }

        /// <summary>가로 배치 카드 행(앵커 고정).</summary>
        private static RectTransform NewCardRow(string name, RectTransform parent,
            Vector2 anchor, Vector2 pos, Vector2 size, TextAnchor align, float spacing)
        {
            var go = NewPanel(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childAlignment = align;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            return rt;
        }

        private static RectTransform NewBackRow(string name, RectTransform parent,
            Vector2 anchor, Vector2 pos, Vector2 size, TextAnchor align)
            => NewCardRow(name, parent, anchor, pos, size, align, 3);

        private static void AnchorBottomStretch(RectTransform rt, float height, float bottom, float sideInset)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(sideInset, bottom); rt.offsetMax = new Vector2(-sideInset, bottom + height);
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
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
