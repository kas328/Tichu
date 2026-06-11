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
    /// 손패 카드를 직접 클릭해 선택하고 [내기]/[패스] 로 결정한다(Tichu.be 식).
    /// 합법성 판정은 ViewModel 이 하며(SubmitTurnDecision 게이팅), 뷰는 선택을 합법수와 대조해 전달만 한다.
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
        private static readonly Color CardSel  = new Color(1.00f, 0.86f, 0.32f); // 선택된 카드
        private static readonly Color CardInk  = new Color(0.10f, 0.12f, 0.16f);
        private static readonly Color CardRed  = new Color(0.78f, 0.10f, 0.12f);

        private TableViewModel _vm;
        private readonly CompositeDisposable _subs = new CompositeDisposable();

        private readonly Text[] _seatTexts = new Text[4];
        private Text _trickText, _phaseText, _resultText, _promptLabel, _hintLabel;
        private RectTransform _handRoot, _actionRoot;

        // 손패 선택 상태(탭 순서 유지 — 교환의 좌/파트너/우 순서에 사용).
        private readonly List<Card> _selection = new List<Card>();
        private DecisionRequest _activeReq;            // 현재 대기 결정(없으면 null)
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
            var rootRt = root.GetComponent<RectTransform>();
            StretchFull(rootRt);
            root.AddComponent<Image>().color = Felt;

            _phaseText = NewAnchoredText("PhaseText", rootRt, "Phase: -", 26,
                new Vector2(0, 1), new Vector2(20, -16), new Vector2(560, 40), TextAnchor.UpperLeft);
            _resultText = NewAnchoredText("ResultText", rootRt, "", 24,
                new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(1000, 90), TextAnchor.UpperCenter);

            _seatTexts[2] = NewAnchoredText("SeatNorth", rootRt, "", 26,
                new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(560, 56), TextAnchor.MiddleCenter);
            _seatTexts[1] = NewAnchoredText("SeatWest", rootRt, "", 26,
                new Vector2(0, 0.5f), new Vector2(24, 150), new Vector2(320, 56), TextAnchor.MiddleLeft);
            _seatTexts[3] = NewAnchoredText("SeatEast", rootRt, "", 26,
                new Vector2(1, 0.5f), new Vector2(-24, 150), new Vector2(320, 56), TextAnchor.MiddleRight);
            _seatTexts[0] = NewAnchoredText("SeatSouth", rootRt, "", 28,
                new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(760, 40), TextAnchor.MiddleCenter);

            _trickText = NewAnchoredText("TrickText", rootRt, "트릭: (없음)", 26,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960, 130), TextAnchor.MiddleCenter);

            // 내 손패(앞면, 클릭 선택) — 하단 가로 스트립.
            var handPanel = NewPanel("HandStrip", rootRt);
            _handRoot = handPanel.GetComponent<RectTransform>();
            AnchorBottomStretch(_handRoot, height: 150, bottom: 16, sideInset: 8);
            var hl = handPanel.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 5; hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            hl.padding = new RectOffset(10, 10, 8, 8);

            // 결정 영역 — 손패 위. 라벨 + 힌트 + 액션 버튼 줄.
            var prompt = NewPanel("PromptPanel", rootRt);
            var promptRt = prompt.GetComponent<RectTransform>();
            AnchorBottomStretch(promptRt, height: 180, bottom: 176, sideInset: 8);
            var pv = prompt.AddComponent<VerticalLayoutGroup>();
            pv.spacing = 6; pv.childControlWidth = true; pv.childControlHeight = false;
            pv.childForceExpandWidth = true; pv.childForceExpandHeight = false;
            pv.childAlignment = TextAnchor.LowerCenter; pv.padding = new RectOffset(16, 16, 8, 8);

            _promptLabel = NewText("PromptLabel", prompt.transform, "", 26);
            _promptLabel.alignment = TextAnchor.MiddleCenter;
            _hintLabel = NewText("HintLabel", prompt.transform, "", 20);
            _hintLabel.alignment = TextAnchor.MiddleCenter;
            _hintLabel.color = Warn;

            var actions = NewPanel("Actions", prompt.transform);
            _actionRoot = actions.GetComponent<RectTransform>();
            var al = actions.AddComponent<HorizontalLayoutGroup>();
            al.spacing = 12; al.childAlignment = TextAnchor.MiddleCenter;
            al.childControlWidth = false; al.childControlHeight = false;
            al.childForceExpandWidth = false; al.childForceExpandHeight = false;
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
                _vm.HandCount(seat).Subscribe(c => _seatTexts[seat].text = $"{SeatNames[seat]}\n{c}장").AddTo(_subs);
            }
            _vm.PendingDecision.Subscribe(RenderPrompt).AddTo(_subs);
        }

        // ── 손패 렌더(선택 가능) ──────────────────────────────────────────────

        private bool CardSelectable =>
            _activeReq != null &&
            (_activeReq.Kind == DecisionKind.Turn ||
             _activeReq.Kind == DecisionKind.Bomb ||
             _activeReq.Kind == DecisionKind.Exchange);

        private void RenderHand()
        {
            ClearChildren(_handRoot);
            foreach (var card in _hand.OrderBy(SortKey))
                AddCardChip(card);
        }

        private void AddCardChip(Card card)
        {
            bool selected = _selection.Contains(card);
            var go = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_handRoot, false);
            go.GetComponent<Image>().color = selected ? CardSel : CardBg;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 66; le.minWidth = 66; le.preferredHeight = selected ? 112 : 100;

            var btn = go.GetComponent<Button>();
            btn.interactable = CardSelectable;
            var captured = card;
            btn.onClick.AddListener(() => ToggleCard(captured));

            var t = NewText("L", go.transform, CardLabel(card), 22);
            t.color = IsRed(card) ? CardRed : CardInk;
            t.alignment = TextAnchor.MiddleCenter;
            StretchFull(t.rectTransform);
        }

        private void ToggleCard(Card card)
        {
            if (!CardSelectable) return;
            if (_selection.Contains(card)) _selection.Remove(card);
            else
            {
                if (_activeReq.Kind == DecisionKind.Exchange && _selection.Count >= 3) return; // 교환은 3장까지
                _selection.Add(card);
            }
            RenderHand();
            RefreshActions(); // 선택 상태에 따라 버튼/라벨 갱신
        }

        // ── 결정 프롬프트 ─────────────────────────────────────────────────────

        private void RenderPrompt(DecisionRequest? req)
        {
            _activeReq = req;
            _selection.Clear();
            RenderHand(); // 선택 가능 여부(버튼 interactable) 반영
            RefreshActions();
        }

        /// <summary>현재 결정과 선택 상태에 맞춰 라벨·힌트·액션 버튼을 다시 그린다.</summary>
        private void RefreshActions()
        {
            ClearChildren(_actionRoot);
            _hintLabel.text = "";
            var req = _activeReq;
            if (req == null) { _promptLabel.text = ""; return; }

            switch (req.Kind)
            {
                case DecisionKind.GrandTichu:
                    _promptLabel.text = "큰 티츄?  (손패를 보고 결정)";
                    AddAction("선언", BtnOn, () => _vm.SubmitGrandTichu(true));
                    AddAction("패스", BtnOn, () => _vm.SubmitGrandTichu(false));
                    break;

                case DecisionKind.Tichu:
                    _promptLabel.text = "작은 티츄?";
                    AddAction("선언", BtnOn, () => _vm.SubmitTichu(true));
                    AddAction("패스", BtnOn, () => _vm.SubmitTichu(false));
                    break;

                case DecisionKind.Turn:
                    _promptLabel.text = _selection.Count == 0
                        ? "내 턴 — 낼 카드를 손패에서 고르세요"
                        : $"선택: {string.Join(" ", _selection.OrderBy(SortKey))}";
                    AddAction("내기", BtnGo, PlaySelectedTurn, _selection.Count > 0);
                    bool canPass = req.Context.CanPass;
                    AddAction("패스", BtnOn, () => _vm.SubmitTurnDecision(TurnDecision.Pass), canPass);
                    if (!canPass)
                        _hintLabel.text = req.Context.State.CurrentTrick == null
                            ? "리드라 패스할 수 없습니다 — 카드를 내야 합니다"
                            : "소원이 걸려 패스할 수 없습니다";
                    break;

                case DecisionKind.Bomb:
                    _promptLabel.text = _selection.Count == 0
                        ? "폭탄 인터럽트? — 폭탄 카드를 고르거나 [넘기기]"
                        : $"선택: {string.Join(" ", _selection.OrderBy(SortKey))}";
                    AddAction("폭탄 내기", BtnGo, PlaySelectedBomb, _selection.Count > 0);
                    AddAction("넘기기", BtnOn, () => _vm.SubmitBomb(null));
                    break;

                case DecisionKind.DragonRecipient:
                    _promptLabel.text = "용 트릭 양도 — 상대를 고르세요";
                    int l = req.Context.LeftSeat, r = req.Context.RightSeat;
                    AddAction($"왼쪽(seat{l})", BtnOn, () => _vm.SubmitDragonRecipient(l));
                    AddAction($"오른쪽(seat{r})", BtnOn, () => _vm.SubmitDragonRecipient(r));
                    break;

                case DecisionKind.Exchange:
                    string[] slot = { "왼쪽", "파트너", "오른쪽" };
                    _promptLabel.text = _selection.Count < 3
                        ? $"카드 교환 — {slot[_selection.Count]}에게 줄 카드를 고르세요  (선택순=좌/파트너/우)"
                        : $"교환: 좌={_selection[0]} 파트너={_selection[1]} 우={_selection[2]}";
                    AddAction("확정", BtnGo, ConfirmExchange, _selection.Count == 3);
                    AddAction("초기화", BtnOff, () => { _selection.Clear(); RenderHand(); RefreshActions(); });
                    break;
            }
        }

        private void PlaySelectedTurn()
        {
            var move = _activeReq.Context.LegalMoves.FirstOrDefault(m => CardsMatch(m.Cards, _selection));
            if (move == null) { _hintLabel.text = "낼 수 없는 조합입니다 — 다시 고르세요"; return; }
            _vm.SubmitTurnDecision(TurnDecision.Play(move));
        }

        private void PlaySelectedBomb()
        {
            var bomb = _activeReq.Context.LegalMoves.FirstOrDefault(m => m.IsBomb && CardsMatch(m.Cards, _selection));
            if (bomb == null) { _hintLabel.text = "유효한 폭탄이 아닙니다 — 다시 고르세요"; return; }
            _vm.SubmitBomb(bomb);
        }

        private void ConfirmExchange()
        {
            if (_selection.Count != 3) return;
            var choice = new ExchangeChoice(_selection[0], _selection[1], _selection[2]);
            if (!_vm.SubmitExchange(choice)) { _hintLabel.text = "교환이 거부됐습니다 — 다시 고르세요"; _selection.Clear(); RenderHand(); RefreshActions(); }
        }

        // ── 상단/중앙 렌더 ────────────────────────────────────────────────────

        private void RenderTrick(Trick? trick)
        {
            if (trick?.Top == null) { _trickText.text = "트릭: (없음)"; return; }
            _trickText.text = $"트릭: {trick.Top.Type}\n[{string.Join(" ", trick.Top.Cards)}]  (소유 seat{trick.TopOwnerSeat})";
        }

        private void RenderResult(RoundResult? r)
        {
            _resultText.text = r == null ? "" :
                $"라운드 종료 — TeamA {r.TeamATotal} : TeamB {r.TeamBTotal}  " +
                $"(카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
        }

        // ── 위젯 헬퍼 ─────────────────────────────────────────────────────────

        private void AddAction(string label, Color color, System.Action onClick, bool interactable = true)
        {
            var go = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_actionRoot, false);
            go.GetComponent<Image>().color = interactable ? color : BtnOff;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 220; le.preferredHeight = 64; le.minWidth = 160;
            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;
            btn.onClick.AddListener(() => onClick());
            var t = NewText("L", go.transform, label, 24);
            t.color = Ink; t.alignment = TextAnchor.MiddleCenter;
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

        /// <summary>두 카드 목록이 멀티셋으로 동일한지(순서 무관).</summary>
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
            t.font = DefaultFont();
            t.fontSize = fontSize; t.color = Ink; t.text = content;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static Text NewAnchoredText(string name, RectTransform parent, string content, int fontSize,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size, TextAnchor align)
        {
            var t = NewText(name, parent, content, fontSize);
            t.alignment = align;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.sizeDelta = size; rt.anchoredPosition = anchoredPos;
            return t;
        }

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
