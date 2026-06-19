using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow.Agents;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 런타임 코드로 빌드되는 placeholder uGUI 뷰.
    /// 순서는 반시계(다음 차례=내 오른쪽): seat0=나(하), seat1=오른쪽, seat2=파트너(상), seat3=왼쪽.
    /// 손패=앞면 클릭 선택, 상대=뒷면(상=가로/좌우=세로), 트릭=중앙 앞면, 결정=우하단(가로 버튼),
    /// 누적점수·소원=좌상단, 최근 플레이 로그=우상단. 마작 포함 시 소원 선택 UI, 교환은 방향 버튼.
    /// </summary>
    public sealed class RuntimeTableView : ITableView
    {
        // 시각 위치 기준 이름. seat1=오른쪽, seat2=파트너, seat3=왼쪽.
        private static readonly string[] SeatNames = { "나", "오른쪽", "파트너", "왼쪽" };

        private static readonly Color Felt   = new Color(0.07f, 0.22f, 0.13f);
        private static readonly Color Ink     = new Color(0.93f, 0.96f, 0.94f);
        private static readonly Color Warn    = new Color(1.00f, 0.80f, 0.40f);
        private static readonly Color BtnOn   = new Color(0.20f, 0.42f, 0.72f);
        private static readonly Color BtnGo   = new Color(0.18f, 0.55f, 0.30f);
        private static readonly Color BtnOff  = new Color(0.35f, 0.35f, 0.38f);
        private static readonly Color CardUse = new Color(0.55f, 0.80f, 0.62f); // 교환 배정(슬롯 버튼)
        private static readonly Color TurnHi  = new Color(0.45f, 0.95f, 0.55f); // 현재 차례 이름 강조

        private TableViewModel _vm;
        private readonly CompositeDisposable _subs = new CompositeDisposable();

        private readonly Text[] _seatTexts = new Text[4];
        private readonly RectTransform[] _backRoots = new RectTransform[4];
        private readonly Text[] _countTexts = new Text[4];
        private Text _phaseText, _scoreText, _wishText, _trickOwnerText, _promptLabel, _resultText;
        private RectTransform _handRoot, _trickRoot, _actionRoot, _wishPickRoot, _exchangeRoot, _playsRoot;

        private readonly List<Card> _selection = new List<Card>(); // 차례/폭탄
        private readonly List<GameObject> _playEntries = new List<GameObject>(); // 최근 플레이 항목
        private GameObject _tichuButton; // 상시 스몰 티츄 버튼
        private GameObject _resultPanel; // 라운드 결과 중앙 배너
        private GameObject _skipButton;  // 빠른 진행(스킵) 버튼 — 내가 out 됐을 때만
        private Card? _exVL, _exVP, _exVR, _exPick;                // 교환(시각 왼쪽/파트너/오른쪽/현재픽)
        private Combination _wishMove;                             // 마작 포함 차례 — 소원 대기
        private DecisionRequest _activeReq;
        private IReadOnlyList<Card> _hand = new List<Card>();
        private int _cumA, _cumB;
        private CancellationToken _sceneCt;
        private CardView _cardViewPrefab;
        private CardSpriteAtlas _atlas;
        private CardChipPool _handPool;
        private CardChipPool _trickPool;
        private readonly CardChipPool[] _backPools = new CardChipPool[4]; // 좌석0(나)=null
        private readonly IPlayAnimator _anim;
        private readonly List<CardView> _trickChips = new List<CardView>(); // PlayedIn 전달용(GC 회피 재사용)

        public RuntimeTableView(IPlayAnimator anim = null) => _anim = anim ?? new NoOpPlayAnimator();

        public void Bind(TableViewModel vm, Canvas canvas, CancellationToken sceneCt)
        {
            _vm = vm;
            _sceneCt = sceneCt;
            _cardViewPrefab = Resources.Load<CardView>("CardView");
            _atlas = Resources.Load<CardSpriteAtlas>("CardSpriteAtlas");
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

            // 콘텐츠 컨테이너 — SafeArea 인셋 대상(펠트 배경은 전체화면 유지, 노치 뒤까지).
            var content = NewPanel("Content", root.transform);
            var rt = content.GetComponent<RectTransform>();
            StretchFull(rt);
            content.AddComponent<SafeAreaFitter>();

            // 좌상단: 누적점수 / 페이즈 / 소원.
            _scoreText = NewAnchoredText("Score", rt, "총점  우리 0 : 상대 0", 28, new Vector2(0, 1), new Vector2(20, -16), new Vector2(560, 38), TextAnchor.UpperLeft);
            _phaseText = NewAnchoredText("Phase", rt, "Phase: -", 22, new Vector2(0, 1), new Vector2(20, -56), new Vector2(560, 32), TextAnchor.UpperLeft);
            _wishText  = NewAnchoredText("Wish",  rt, "", 26, new Vector2(0, 1), new Vector2(20, -90), new Vector2(560, 34), TextAnchor.UpperLeft);
            _wishText.color = Warn;
            // 우상단: 최근 플레이 로그(항목별로 5초 후 사라짐).
            NewAnchoredText("PlaysHeader", rt, "최근 플레이", 22, new Vector2(1, 1), new Vector2(-20, -16), new Vector2(440, 30), TextAnchor.UpperRight).color = new Color(0.82f, 0.88f, 0.96f);
            _playsRoot = NewRow("PlaysRoot", rt, new Vector2(1, 1), new Vector2(-20, -52), new Vector2(440, 300), TextAnchor.UpperRight, true);
            // 상대 = 프로필 박스 + 이름 + 장수 + 카드(뒷면). 카드가 이름/장수와 겹치지 않게 배치.
            BuildOpponent(rt, 2, new Vector2(0.5f, 1), new Vector2(0, -12),  new Vector2(0, -170), false, new Vector2(700, 50)); // 파트너(상): 정보 위, 카드 아래
            BuildOpponent(rt, 3, new Vector2(0, 0.5f), new Vector2(16, 0),   new Vector2(160, 0),  true,  new Vector2(84, 540)); // 왼쪽: 프로필 왼끝, 카드 오른쪽
            BuildOpponent(rt, 1, new Vector2(1, 0.5f), new Vector2(-16, 0),  new Vector2(-160, 0), true,  new Vector2(84, 540)); // 오른쪽: 프로필 오른끝, 카드 왼쪽

            // 중앙 트릭(앞면) + 소유자.
            _trickRoot = NewRow("TrickRow", rt, new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(940, 120), TextAnchor.MiddleCenter, false);
            _trickPool = MakeDynamicPool(_trickRoot, interactive: false);
            _trickOwnerText = NewAnchoredText("TrickOwner", rt, "트릭: (없음)", 24, new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(700, 34), TextAnchor.MiddleCenter);
            // 중앙: 라운드 결과 배너(결과 있을 때만 표시; 트릭 위 레이어).
            _resultPanel = NewPanel("ResultPanel", rt);
            var rprt = _resultPanel.GetComponent<RectTransform>();
            rprt.anchorMin = rprt.anchorMax = rprt.pivot = new Vector2(0.5f, 0.5f);
            rprt.sizeDelta = new Vector2(960, 100); rprt.anchoredPosition = Vector2.zero;
            _resultPanel.AddComponent<Image>().color = new Color(0.04f, 0.10f, 0.06f, 0.92f);
            _resultText = NewAnchoredText("Result", rprt, "", 30, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920, 92), TextAnchor.MiddleCenter);
            _resultPanel.SetActive(false);
            // 소원 선택 UI(중앙, 마작 포함 차례 때만 채워짐).
            var wp = NewPanel("WishPick", rt);
            var wprt = wp.GetComponent<RectTransform>();
            wprt.anchorMin = wprt.anchorMax = wprt.pivot = new Vector2(0.5f, 0.5f);
            wprt.sizeDelta = new Vector2(640, 200); wprt.anchoredPosition = new Vector2(0, -150);
            var wpg = wp.AddComponent<GridLayoutGroup>();
            wpg.cellSize = new Vector2(80, 56); wpg.spacing = new Vector2(8, 8);
            wpg.constraint = GridLayoutGroup.Constraint.FixedColumnCount; wpg.constraintCount = 7;
            wpg.childAlignment = TextAnchor.MiddleCenter;
            _wishPickRoot = wprt;

            // 내 좌석 + 교환 방향 버튼(손패 위) + 손패.
            _seatTexts[0] = NewAnchoredText("SouthLbl", rt, "", 26, new Vector2(0.5f, 0), new Vector2(0, 320), new Vector2(520, 36), TextAnchor.MiddleCenter);
            _exchangeRoot = NewRow("ExchangeRow", rt, new Vector2(0.5f, 0), new Vector2(0, 180), new Vector2(900, 70), TextAnchor.MiddleCenter, false);
            _handRoot = NewRow("Hand", rt, new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(1060, 150), TextAnchor.MiddleCenter, false);
            AnchorBottomStretch(_handRoot, height: 150, bottom: 16, sideInset: 8);
            _handPool = MakeDynamicPool(_handRoot, interactive: true);

            // 결정 패널 — 손패 옆(우하단), 라벨(위) + 가로 버튼(아래). 힌트 라벨 없음.
            var panel = NewPanel("Decision", rt);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1, 0);
            prt.sizeDelta = new Vector2(360, 120); prt.anchoredPosition = new Vector2(-48, 125);
            var pv = panel.AddComponent<VerticalLayoutGroup>();
            pv.spacing = 4; pv.childControlWidth = true; pv.childControlHeight = false;
            pv.childForceExpandWidth = true; pv.childForceExpandHeight = false;
            pv.childAlignment = TextAnchor.LowerCenter; pv.padding = new RectOffset(8, 8, 8, 8);
            // 순서: 라벨(위) → 버튼(아래).
            _promptLabel = NewText("PromptLabel", panel.transform, "", 26); _promptLabel.alignment = TextAnchor.MiddleCenter;
            var actions = NewPanel("Actions", panel.transform);
            _actionRoot = actions.GetComponent<RectTransform>();
            var al = actions.AddComponent<HorizontalLayoutGroup>();
            al.spacing = 10; al.childAlignment = TextAnchor.MiddleCenter;
            al.childControlWidth = false; al.childControlHeight = false;
            al.childForceExpandWidth = false; al.childForceExpandHeight = false;
            actions.AddComponent<LayoutElement>().preferredHeight = 64;

            // 상시 "스몰 티츄" 버튼(손패 위, 선언 가능할 때만 표시).
            var tb = new GameObject("TichuButton", typeof(RectTransform), typeof(Image), typeof(Button));
            tb.transform.SetParent(rt, false); // 부모 누락 시 캔버스 밖이라 안 보였음(버튼 미표시 버그)
            var tbrt = tb.GetComponent<RectTransform>();
            tbrt.anchorMin = tbrt.anchorMax = tbrt.pivot = new Vector2(0, 0);
            tbrt.sizeDelta = new Vector2(220, 56); tbrt.anchoredPosition = new Vector2(40, 60);
            tb.GetComponent<Image>().color = new Color(0.66f, 0.32f, 0.62f);
            tb.GetComponent<Button>().onClick.AddListener(() => _vm.DeclareSmallTichu());
            AddCardLabel(tb.transform, "스몰 티츄 선언", Ink, 24);
            tb.SetActive(false);
            _tichuButton = tb;

            // 빠른 진행(스킵) 버튼 — 우하단, 내가 out 됐을 때만 표시. 누르면 AI 딜레이를 건너뛴다.
            var skip = new GameObject("SkipButton", typeof(RectTransform), typeof(Image), typeof(Button));
            skip.transform.SetParent(rt, false);
            var skrt = skip.GetComponent<RectTransform>();
            skrt.anchorMin = skrt.anchorMax = skrt.pivot = new Vector2(1, 0);
            skrt.sizeDelta = new Vector2(220, 56); skrt.anchoredPosition = new Vector2(-48, 50);
            skip.GetComponent<Image>().color = BtnGo;
            skip.GetComponent<Button>().onClick.AddListener(() => { _vm.FastForward = true; UpdateSkipButton(); });
            AddCardLabel(skip.transform, "▶▶ 스킵", Ink, 24);
            skip.SetActive(false);
            _skipButton = skip;
        }

        // 상대 1명: 프로필 박스(placeholder) + 이름 + 장수를 세로 스택(가운데 정렬)으로 묶고, 카드는 별도 위치.
        private void BuildOpponent(RectTransform rt, int seat, Vector2 anchor, Vector2 infoPos, Vector2 cardsPos, bool cardsVertical, Vector2 cardsSize)
        {
            var info = NewPanel($"Info{seat}", rt);
            var irt = info.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = irt.pivot = anchor;
            irt.sizeDelta = new Vector2(150, 152); irt.anchoredPosition = infoPos;
            var iv = info.AddComponent<VerticalLayoutGroup>();
            iv.spacing = 4; iv.childAlignment = TextAnchor.MiddleCenter;
            iv.childControlWidth = true; iv.childControlHeight = true;
            iv.childForceExpandWidth = false; iv.childForceExpandHeight = false;

            var prof = new GameObject($"Prof{seat}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            prof.transform.SetParent(info.transform, false);
            prof.GetComponent<Image>().color = new Color(0.30f, 0.36f, 0.44f);
            var po = prof.AddComponent<Outline>(); po.effectColor = new Color(0.10f, 0.13f, 0.18f); po.effectDistance = new Vector2(2, 2);
            var ple = prof.GetComponent<LayoutElement>(); ple.preferredWidth = 84; ple.preferredHeight = 84;

            _seatTexts[seat] = NewText($"Name{seat}", info.transform, SeatNames[seat], 24);
            _seatTexts[seat].alignment = TextAnchor.MiddleCenter;
            var nle = _seatTexts[seat].gameObject.AddComponent<LayoutElement>(); nle.preferredWidth = 140; nle.preferredHeight = 28;

            _countTexts[seat] = NewText($"Cnt{seat}", info.transform, "14장", 22);
            _countTexts[seat].alignment = TextAnchor.MiddleCenter;
            var cle = _countTexts[seat].gameObject.AddComponent<LayoutElement>(); cle.preferredWidth = 140; cle.preferredHeight = 26;

            _backRoots[seat] = NewRow($"Backs{seat}", rt, anchor, cardsPos, cardsSize, TextAnchor.MiddleCenter, cardsVertical);
            _backRoots[seat].GetComponent<HorizontalOrVerticalLayoutGroup>().spacing = 8; // 파트너·측면 동일 간격(비겹침)
            _backPools[seat] = MakeDynamicPool(_backRoots[seat], interactive: false);
        }

        // ── 구독 ─────────────────────────────────────────────────────────────

        private void Subscribe()
        {
            _vm.Phase.Subscribe(p => { _phaseText.text = $"Phase: {p}"; UpdateSkipButton(); }).AddTo(_subs);
            _vm.Wish.Subscribe(w => _wishText.text = w.HasValue ? $"소원(콜): {RankLabel(w.Value)}" : "").AddTo(_subs);
            _vm.TichuAvailable.Subscribe(v => _tichuButton.SetActive(v)).AddTo(_subs);
            _vm.CurrentTurn.Subscribe(UpdateTurnHighlight).AddTo(_subs);
            _vm.CumulativeA.Subscribe(a => { _cumA = a; UpdateScore(); }).AddTo(_subs);
            _vm.CumulativeB.Subscribe(b => { _cumB = b; UpdateScore(); }).AddTo(_subs);
            _vm.Played.Subscribe(AddPlayEntry).AddTo(_subs);
            _vm.PlaysCleared.Subscribe(_ => { ClearChildren(_playsRoot); _playEntries.Clear(); }).AddTo(_subs);
            _vm.CurrentTrick.Subscribe(RenderTrick).AddTo(_subs);
            _vm.RoundResult.Subscribe(RenderResult).AddTo(_subs);
            // 손패 갱신 시, 손에서 빠진 카드만 선택에서 제거(상대 턴 중 폭탄 선택은 유지).
            _vm.MyHand.Subscribe(h => { _hand = h ?? new List<Card>(); _selection.RemoveAll(c => !_hand.Contains(c)); RenderHand(); }).AddTo(_subs);
            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                _vm.HandCount(seat).Subscribe(c =>
                {
                    if (seat == 0)
                    {
                        _seatTexts[0].text = $"{SeatNames[0]} · {c}장";
                        UpdateSkipButton(); // out 여부 변동 → 스킵 버튼 갱신
                    }
                    else { _countTexts[seat].text = $"{c}장"; RenderBacks(seat, c); }
                }).AddTo(_subs);
            }
            _vm.PendingDecision.Subscribe(RenderPrompt).AddTo(_subs);
        }

        private void UpdateScore() => _scoreText.text = $"총점  우리(A) {_cumA} : 상대(B) {_cumB}";

        // 플레이 1건을 우상단에 추가. 각 항목은 5초 뒤 ~1.2초에 걸쳐 페이드 후 제거(순차).
        private void AddPlayEntry(GameAction a)
        {
            var go = new GameObject("PlayEntry", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(_playsRoot, false);
            go.transform.SetSiblingIndex(0); // 최신을 맨 위로
            var t = go.GetComponent<Text>();
            t.font = DefaultFont(); t.fontSize = 22; t.color = new Color(0.82f, 0.88f, 0.96f);
            t.text = FormatAction(a); t.alignment = TextAnchor.UpperRight;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<LayoutElement>().preferredHeight = 28;
            // 최대 5개 — 반드시 리스트 카운트(즉시 반영)로 제한. Object.Destroy 는 프레임 끝까지
            // 지연되므로 childCount 로 while 을 돌면 무한 루프(메모리 폭발)가 된다.
            _playEntries.Insert(0, go);
            while (_playEntries.Count > 5)
            {
                var oldest = _playEntries[_playEntries.Count - 1];
                _playEntries.RemoveAt(_playEntries.Count - 1);
                if (oldest != null) Object.Destroy(oldest);
            }
            FadeEntryAsync(go, t).Forget();
        }

        private async UniTaskVoid FadeEntryAsync(GameObject go, Text t)
        {
            try
            {
                await UniTask.Delay(5000, cancellationToken: _sceneCt);
                float a = 1f;
                int guard = 0;
                while (a > 0f && go != null && guard++ < 600) // guard: deltaTime=0 무한방지
                {
                    a -= Time.deltaTime / 1.2f;
                    if (t != null) { var c = t.color; c.a = Mathf.Max(a, 0f); t.color = c; }
                    await UniTask.Yield(_sceneCt);
                }
                _playEntries.Remove(go);
                if (go != null) Object.Destroy(go);
            }
            catch (System.OperationCanceledException) { /* 씬 종료 */ }
        }

        // ── 손패(앞면, 선택) ─────────────────────────────────────────────────

        private bool TurnLike => _activeReq != null && (_activeReq.Kind == DecisionKind.Turn || _activeReq.Kind == DecisionKind.Bomb);
        private bool Exchanging => _activeReq != null && _activeReq.Kind == DecisionKind.Exchange;
        // 차례밖 폭탄 가능: Play 중 내 차례가 아니고 격파 대상 트릭이 있을 때(seat0=나).
        private bool BombInterruptable =>
            _vm.Phase.CurrentValue == RoundPhase.Play
            && _vm.CurrentTurn.CurrentValue != 0
            && _vm.CurrentTrick.CurrentValue?.Top != null;
        private bool CardSelectable => (TurnLike && _wishMove == null) || Exchanging || BombInterruptable;

        private void RenderHand()
        {
            _handPool.Begin();
            foreach (var card in _hand.OrderBy(SortKey))
            {
                var cv = _handPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(66, 100);

                CardView.Highlight h = CardView.Highlight.Normal;
                if (Exchanging)
                {
                    if (_exPick.HasValue && card.Equals(_exPick.Value)) h = CardView.Highlight.Selected;
                    else if (IsAssigned(card)) h = CardView.Highlight.Assigned;
                }
                else if (_selection.Contains(card)) h = CardView.Highlight.Selected;
                cv.SetHighlight(h);

                var cap = card;
                cv.SetInteractable(CardSelectable, () => ToggleCard(cap));
            }
            _handPool.End();
        }

        private bool IsAssigned(Card c) =>
            (_exVL.HasValue && c.Equals(_exVL.Value)) || (_exVP.HasValue && c.Equals(_exVP.Value)) || (_exVR.HasValue && c.Equals(_exVR.Value));

        private void ToggleCard(Card card)
        {
            if (!CardSelectable) return;
            if (Exchanging)
            {
                // 어느 슬롯에 배정돼 있으면 회수 후 픽.
                if (_exVL.HasValue && card.Equals(_exVL.Value)) _exVL = null;
                else if (_exVP.HasValue && card.Equals(_exVP.Value)) _exVP = null;
                else if (_exVR.HasValue && card.Equals(_exVR.Value)) _exVR = null;
                _exPick = card.Equals(_exPick.GetValueOrDefault()) && _exPick.HasValue ? (Card?)null : card;
            }
            else
            {
                if (_selection.Contains(card)) _selection.Remove(card);
                else _selection.Add(card);
            }
            RenderHand();
            RefreshActions();
        }

        // ── 상대 뒷면 ─────────────────────────────────────────────────────────

        private void RenderBacks(int seat, int count)
        {
            var pool = _backPools[seat];
            if (pool == null) return; // 좌석0(나)은 뒷면 없음
            bool side = (seat == 1 || seat == 3);
            float w = side ? 44 : 30, h = side ? 30 : 44; // 측면도 파트너와 같은 카드 치수(눕힌 44×30)
            int show = Mathf.Min(count, 14);
            pool.Begin();
            for (int i = 0; i < show; i++)
            {
                var cv = pool.Next();
                cv.Set(default(Card), _atlas, faceUp: false);
                cv.SetSize(w, h);
            }
            pool.End();
        }

        // ── 트릭(중앙 앞면) ───────────────────────────────────────────────────

        private void RenderTrick(Trick? trick)
        {
            if (trick?.Top == null)
            {
                _trickPool.Begin(); _trickPool.End(); // 모든 칩 비활성화
                _trickOwnerText.text = "트릭: (없음)";
                return;
            }
            _trickPool.Begin();
            _trickChips.Clear();
            foreach (var card in trick.Top.Cards.OrderBy(SortKey))
            {
                var cv = _trickPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(60, 88);
                _trickChips.Add(cv);
            }
            _trickPool.End();
            _trickOwnerText.text = $"{TypeKo(trick.Top.Type)} · 소유 {SeatNames[trick.TopOwnerSeat]}";
            _anim.PlayedIn(_trickChips, _vm.FastForward);
        }

        private void RenderResult(RoundResult? r)
        {
            _resultPanel.SetActive(r != null);
            _resultText.text = r == null ? "" :
                $"라운드 종료 — 우리 {r.TeamATotal} : 상대 {r.TeamBTotal}  (카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
            if (r != null) _anim.ResultShown((RectTransform)_resultPanel.transform);
        }

        // 현재 차례 좌석 이름을 강조색으로(나머지는 기본).
        private void UpdateTurnHighlight(int turn)
        {
            for (int i = 0; i < 4; i++)
                if (_seatTexts[i] != null)
                    _seatTexts[i].color = (i == turn) ? TurnHi : Ink;
            if (turn >= 0 && turn < 4 && _seatTexts[turn] != null)
                _anim.TurnChanged(_seatTexts[turn]);
        }

        // 빠른 진행 버튼: 내가 out(0장)·Play 중·아직 스킵 안 눌렀을 때만 표시.
        private void UpdateSkipButton()
        {
            _skipButton.SetActive(
                _vm.HandCount(0).CurrentValue == 0
                && _vm.Phase.CurrentValue == RoundPhase.Play
                && !_vm.FastForward);
        }

        // ── 결정 프롬프트 ─────────────────────────────────────────────────────

        private void RenderPrompt(DecisionRequest? req)
        {
            _activeReq = req;
            // 선택(_selection)은 유지한다 — 차례·패스 전환 때 클릭해 둔 카드가 풀리지 않도록.
            // 손패에서 빠진 카드는 MyHand 구독이 정리한다.
            _exVL = _exVP = _exVR = _exPick = null;
            _wishMove = null;
            RenderHand();
            RefreshActions();
        }

        private void RefreshActions()
        {
            ClearChildren(_actionRoot);
            ClearChildren(_wishPickRoot);
            ClearChildren(_exchangeRoot);
            var req = _activeReq;
            if (req == null) { RenderBombInterrupt(); return; }

            // 마작 포함 차례 → 소원 선택 모드.
            if (_wishMove != null) { RenderWishPicker(); return; }

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
                    var selC = RecognizeSelection();
                    bool isBomb = selC != null && selC.IsBomb;
                    _promptLabel.text = _selection.Count == 0 ? "내 턴 — 카드 선택" : $"선택: {string.Join(" ", _selection.OrderBy(SortKey))}";
                    AddAction(isBomb ? "폭탄 내기" : "내기", BtnGo, PlaySelectedTurn, _selection.Count > 0);
                    bool canPass = req.Context.CanPass;
                    AddAction("패스", BtnOn, () => _vm.SubmitTurnDecision(TurnDecision.Pass), canPass);
                    break;
                case DecisionKind.Bomb:
                    _promptLabel.text = _selection.Count == 0 ? "폭탄? 카드 선택/넘기기" : $"선택: {string.Join(" ", _selection.OrderBy(SortKey))}";
                    AddAction("폭탄 내기", BtnGo, PlaySelectedBomb, _selection.Count > 0);
                    AddAction("넘기기", BtnOn, () => _vm.SubmitBomb(null));
                    break;
                case DecisionKind.DragonRecipient:
                    _promptLabel.text = "용 양도 — 상대 선택";
                    // 게임 LeftSeat=시각 오른쪽(seat1), RightSeat=시각 왼쪽(seat3). 버튼 위치=실제 대상 일치.
                    int rightPlayer = req.Context.LeftSeat, leftPlayer = req.Context.RightSeat;
                    AddAction("왼쪽", BtnOn, () => _vm.SubmitDragonRecipient(leftPlayer));
                    AddAction("오른쪽", BtnOn, () => _vm.SubmitDragonRecipient(rightPlayer));
                    break;
                case DecisionKind.Exchange:
                    _promptLabel.text = "교환 — 카드 선택 후 방향";
                    RenderExchangeRow();
                    AddAction("확정", BtnGo, ConfirmExchange, _exVL.HasValue && _exVP.HasValue && _exVR.HasValue);
                    AddAction("초기화", BtnOff, () => { _exVL = _exVP = _exVR = _exPick = null; RenderHand(); RefreshActions(); });
                    break;
            }
        }

        // ── 교환 방향 버튼(손패 위) ───────────────────────────────────────────

        private void RenderExchangeRow()
        {
            AddSlot("왼쪽", _exVL, () => Assign(ref _exVL));
            AddSlot("파트너", _exVP, () => Assign(ref _exVP));
            AddSlot("오른쪽", _exVR, () => Assign(ref _exVR));
        }

        private void Assign(ref Card? slot)
        {
            if (!_exPick.HasValue) return;
            slot = _exPick; _exPick = null;
            RenderHand(); RefreshActions();
        }

        private void AddSlot(string label, Card? card, System.Action onClick)
        {
            var go = new GameObject("Slot", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_exchangeRoot, false);
            go.GetComponent<Image>().color = card.HasValue ? CardUse : BtnOn;
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 250; le.preferredHeight = 60;
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
            AddCardLabel(go.transform, card.HasValue ? $"{label} {CardShort(card.Value)}" : label, Ink, 22);
        }

        // ── 소원 선택 UI ──────────────────────────────────────────────────────

        private void RenderWishPicker()
        {
            _promptLabel.text = "마작 — 소원을 고르세요";
            AddWishBtn("소원 없음", null);
            for (int r = 2; r <= 14; r++) { int rank = r; AddWishBtn(RankLabel(rank), rank); }
        }

        private void AddWishBtn(string label, int? wish)
        {
            var go = new GameObject("Wish", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_wishPickRoot, false);
            go.GetComponent<Image>().color = wish.HasValue ? BtnOn : BtnOff;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                var move = _wishMove; _wishMove = null;
                _vm.SubmitTurnDecision(TurnDecision.Play(move, wish));
            });
            AddCardLabel(go.transform, label, Ink, 22);
        }

        // ── 제출 ──────────────────────────────────────────────────────────────

        private void PlaySelectedTurn()
        {
            var sel = RecognizeSelection();
            if (sel == null) return;
            if (!LegalByTypeRank(sel)) return;
            // 마작 포함 → 소원 선택 후 제출.
            if (sel.Cards.Any(c => c.Special == SpecialKind.Mahjong)) { _wishMove = sel; RenderHand(); RefreshActions(); return; }
            _vm.SubmitTurnDecision(TurnDecision.Play(sel)); // 내가 고른 실제 카드로 제출
        }

        private void PlaySelectedBomb()
        {
            var sel = RecognizeSelection();
            if (sel == null || !sel.IsBomb || !LegalByTypeRank(sel)) return;
            _vm.SubmitBomb(sel);
        }

        // ── 차례밖 폭탄(상대 턴 인터럽트) ───────────────────────────────────────

        // 상대 턴 중: 폭탄 조합 선택 시 "폭탄 내기" 버튼, 예약되면 대기 표시.
        private void RenderBombInterrupt()
        {
            if (_vm.HasPendingBomb) { _promptLabel.text = "폭탄 발동!"; return; }
            // 폭탄으로 인식되는 선택일 때만 버튼을 노출한다(일반 선택은 차례밖에 낼 수 없으므로 표시 안 함).
            var bomb = (BombInterruptable && _selection.Count > 0) ? RecognizeBombSelection() : null;
            if (bomb != null)
            {
                _promptLabel.text = $"차례밖 폭탄: {string.Join(" ", _selection.OrderBy(SortKey))}";
                AddAction("폭탄 내기", BtnGo, ReserveSelectedBomb);
            }
            else _promptLabel.text = "";
        }

        // 선택 카드가 폭탄 조합이면 반환(아니면 null). 트릭 문맥 무관(폭탄 자체 인식).
        private Combination RecognizeBombSelection()
        {
            if (_selection.Count == 0) return null;
            var c = CombinationRecognizer.Recognize(_selection.ToArray(), TrickContext.Lead);
            return (c.Type != CombinationType.Invalid && c.IsBomb) ? c : null;
        }

        private void ReserveSelectedBomb()
        {
            var bomb = RecognizeBombSelection();
            if (bomb == null) return;
            _vm.ReserveBomb(bomb);
            _selection.Clear();
            RenderHand();
            RefreshActions();
        }

        // 선택한 카드를 현재 트릭 문맥으로 조합 인식.
        private Combination RecognizeSelection()
        {
            if (_selection.Count == 0) return null;
            var trick = _activeReq.Context.State.CurrentTrick;
            TrickContext tc;
            if (trick?.Top == null) tc = TrickContext.Lead;
            else { bool single = trick.Top.Type == CombinationType.Single; tc = new TrickContext(false, single, single ? trick.Top.Rank : 0); }
            var c = CombinationRecognizer.Recognize(_selection.ToArray(), tc);
            return c.Type == CombinationType.Invalid ? null : c;
        }

        // (Type, Rank) 등가로 합법수 존재 확인 — 무늬 무관(같은 랭크 다른 무늬도 허용).
        private bool LegalByTypeRank(Combination sel)
        {
            var lm = _activeReq.Context.LegalMoves;
            for (int i = 0; i < lm.Count; i++) if (lm[i].Type == sel.Type && lm[i].Rank == sel.Rank) return true;
            return false;
        }

        private void ConfirmExchange()
        {
            if (!(_exVL.HasValue && _exVP.HasValue && _exVR.HasValue)) return;
            // 시각 왼쪽(seat3)=ToRight, 파트너=ToPartner, 시각 오른쪽(seat1)=ToLeft.
            var choice = new ExchangeChoice(_exVR.Value, _exVP.Value, _exVL.Value);
            if (!_vm.SubmitExchange(choice)) { _exVL = _exVP = _exVR = _exPick = null; RenderHand(); RefreshActions(); }
        }

        // ── 위젯 ──────────────────────────────────────────────────────────────

        private void AddAction(string label, Color color, System.Action onClick, bool interactable = true)
        {
            var go = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_actionRoot, false);
            go.GetComponent<Image>().color = interactable ? color : BtnOff;
            var le = go.AddComponent<LayoutElement>(); le.preferredWidth = 170; le.minWidth = 150; le.preferredHeight = 60;
            var btn = go.GetComponent<Button>(); btn.interactable = interactable;
            btn.onClick.AddListener(() => onClick());
            AddCardLabel(go.transform, label, Ink, 24);
        }

        private static void AddCardLabel(Transform parent, string text, Color color, int size)
        {
            var t = NewText("L", parent, text, size);
            t.color = color; t.alignment = TextAnchor.MiddleCenter; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            StretchFull(t.rectTransform);
        }

        // ── 표기/포맷 ─────────────────────────────────────────────────────────

        private static string FormatAction(GameAction a)
        {
            string who = a.Seat >= 0 && a.Seat < 4 ? SeatNames[a.Seat] : "?";
            if (a.Kind == GameActionKind.Pass) return $"{who} · 패스";
            if (a.Kind == GameActionKind.GiveDragon) return $"{who} · 용 양도";
            if (a.Kind == GameActionKind.Play && a.Cards != null && a.Cards.Count > 0)
            {
                var combo = CombinationRecognizer.Recognize(a.Cards.ToArray(), TrickContext.Lead);
                return $"{who} · {TopRankLabel(a.Cards)} {TypeKo(combo.Type)}";
            }
            return $"{who} · ?";
        }

        private static string TopRankLabel(IReadOnlyList<Card> cards)
        {
            if (cards.Count == 1 && cards[0].IsSpecial) return CardLabel(cards[0]).Replace("\n", "");
            int max = 0; foreach (var c in cards) if (!c.IsSpecial && c.Rank > max) max = c.Rank;
            return max == 0 ? "" : RankLabel(max);
        }

        private static string CardShort(Card c)
        {
            if (c.IsSpecial) return CardLabel(c).Replace("\n", "");
            return $"{RankLabel(c.Rank)}{SuitGlyph(c.Suit)}";
        }

        private static string TypeKo(CombinationType t)
        {
            switch (t)
            {
                case CombinationType.Single: return "싱글";
                case CombinationType.Pair: return "페어";
                case CombinationType.Triple: return "트리플";
                case CombinationType.FullHouse: return "풀하우스";
                case CombinationType.Straight: return "스트레이트";
                case CombinationType.ConsecutivePairs: return "연속페어";
                case CombinationType.FourBomb: return "폭탄";
                case CombinationType.StraightFlushBomb: return "스플폭탄";
                default: return t.ToString();
            }
        }

        // 카드 면 포맷은 CardFormat으로 추출(CardView와 공용·DRY). 아래는 호출부 호환 위임.
        private static string CardLabel(Card c) => CardFormat.Label(c);
        private static string RankLabel(int r) => CardFormat.RankLabel(r);
        private static string SuitGlyph(Suit s) => CardFormat.SuitGlyph(s);
        private static int SortKey(Card c) => CardFormat.SortKey(c);

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
            t.alignment = TextAnchor.MiddleLeft; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static Text NewAnchoredText(string name, RectTransform parent, string content, int fontSize,
            Vector2 anchor, Vector2 pos, Vector2 size, TextAnchor align)
        {
            var t = NewText(name, parent, content, fontSize);
            t.alignment = align;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor; rt.sizeDelta = size; rt.anchoredPosition = pos;
            return t;
        }

        private static RectTransform NewRow(string name, RectTransform parent,
            Vector2 anchor, Vector2 pos, Vector2 size, TextAnchor align, bool vertical)
        {
            var go = NewPanel(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor; rt.sizeDelta = size; rt.anchoredPosition = pos;
            if (vertical)
            {
                var v = go.AddComponent<VerticalLayoutGroup>();
                v.spacing = 3; v.childAlignment = align;
                v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            }
            else
            {
                var h = go.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 4; h.childAlignment = align;
                h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            }
            return rt;
        }

        private static void AnchorBottomStretch(RectTransform rt, float height, float bottom, float sideInset)
        {
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(sideInset, bottom); rt.offsetMax = new Vector2(-sideInset, bottom + height);
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void ClearChildren(RectTransform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);
        }

        // 동적 루트를 서브 Canvas 로 격리(리배칭 분리)하고 풀을 만든다. interactive=손패만(버튼 클릭용 레이캐스터).
        private CardChipPool MakeDynamicPool(RectTransform root, bool interactive)
        {
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = false; // 드로 순서 보존(결과배너 > 트릭)
            if (interactive) root.gameObject.AddComponent<GraphicRaycaster>();
            return new CardChipPool(root, _cardViewPrefab);
        }

        private static Font DefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}
