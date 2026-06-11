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
    /// 프리팹/SerializeField 배선 없이 캔버스 아래에 텍스트·버튼을 생성하고
    /// TableViewModel 의 ReactiveProperty 를 구독한다.
    /// 게임 규칙 판정은 일절 하지 않으며(ViewModel 이 게이팅) 입력을 vm.Submit… 로 전달만 한다.
    /// </summary>
    public sealed class TableUiView
    {
        // 좌석 라벨(나=South). 시계 방향: 0=South, 1=West, 2=North, 3=East.
        private static readonly string[] SeatNames = { "나(South)", "West", "North", "East" };

        private TableViewModel _vm;
        private readonly CompositeDisposable _subs = new CompositeDisposable();

        // ── 빌드된 위젯 핸들 ──────────────────────────────────────────────────
        private readonly Text[] _seatTexts = new Text[4];
        private Text _trickText;
        private Text _phaseText;
        private Text _resultText;
        private RectTransform _promptRoot;   // 결정 프롬프트 버튼들이 채워지는 컨테이너

        // 교환 입력용 임시 상태(3장을 Left→Partner→Right 순서로 채운다).
        private readonly List<Card> _exchangePicks = new List<Card>(3);

        /// <summary>캔버스 아래에 placeholder UI 를 빌드하고 ViewModel 을 구독한다.</summary>
        public void Bind(TableViewModel vm, Canvas canvas)
        {
            _vm = vm;
            BuildLayout(canvas);
            Subscribe();
        }

        // ── 레이아웃 빌드 ────────────────────────────────────────────────────

        private void BuildLayout(Canvas canvas)
        {
            // 루트 세로 레이아웃.
            var root = NewPanel("Root", canvas.transform);
            StretchFull(root.GetComponent<RectTransform>());
            var rootLayout = root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(16, 16, 16, 16);
            rootLayout.spacing = 8;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = false;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            // Phase / Result 헤더.
            _phaseText = NewText("PhaseText", root.transform, "Phase: -", 22);
            _resultText = NewText("ResultText", root.transform, "", 22);

            // 좌석 4개(라벨 + 손패 수).
            for (int i = 0; i < 4; i++)
                _seatTexts[i] = NewText($"Seat{i}Text", root.transform, "", 20);

            // 현재 트릭.
            _trickText = NewText("TrickText", root.transform, "Trick: (없음)", 20);

            // 결정 프롬프트 컨테이너(세로 배치).
            var promptPanel = NewPanel("PromptPanel", root.transform);
            var promptLayout = promptPanel.AddComponent<VerticalLayoutGroup>();
            promptLayout.spacing = 4;
            promptLayout.childControlWidth = true;
            promptLayout.childControlHeight = true;
            promptLayout.childForceExpandWidth = true;
            promptLayout.childForceExpandHeight = false;
            promptPanel.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            _promptRoot = promptPanel.GetComponent<RectTransform>();
        }

        // ── 구독 ──────────────────────────────────────────────────────────────

        private void Subscribe()
        {
            _vm.Phase.Subscribe(p => _phaseText.text = $"Phase: {p}").AddTo(_subs);

            _vm.CurrentTrick.Subscribe(RenderTrick).AddTo(_subs);

            _vm.RoundResult.Subscribe(RenderResult).AddTo(_subs);

            // 좌석별 손패 수.
            for (int i = 0; i < 4; i++)
            {
                int seat = i; // 캡처 고정
                _vm.HandCount(seat)
                    .Subscribe(cnt => _seatTexts[seat].text = $"{SeatNames[seat]} — 손패 {cnt}장")
                    .AddTo(_subs);
            }

            // 결정 프롬프트.
            _vm.PendingDecision.Subscribe(RenderPrompt).AddTo(_subs);
        }

        // ── 렌더러 ──────────────────────────────────────────────────────────

        private void RenderTrick(Trick? trick)
        {
            if (trick?.Top == null)
            {
                _trickText.text = "Trick: (없음)";
                return;
            }
            string cards = string.Join(" ", trick.Top.Cards);
            _trickText.text = $"Trick: {trick.Top.Type} [{cards}] (소유 seat{trick.TopOwnerSeat})";
        }

        private void RenderResult(RoundResult? r)
        {
            if (r == null)
            {
                _resultText.text = "";
                return;
            }
            _resultText.text =
                $"라운드 종료 — TeamA {r.TeamATotal} : TeamB {r.TeamBTotal} " +
                $"(카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
        }

        private void RenderPrompt(DecisionRequest? req)
        {
            ClearPrompt();
            if (req == null) return;

            switch (req.Kind)
            {
                case DecisionKind.GrandTichu:
                    AddPromptLabel("큰 티츄?");
                    AddButton("선언", () => _vm.SubmitGrandTichu(true));
                    AddButton("패스", () => _vm.SubmitGrandTichu(false));
                    break;

                case DecisionKind.Tichu:
                    AddPromptLabel("작은 티츄?");
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
            AddPromptLabel("내 턴 — 낼 수를 고르세요");
            // 합법 수 1개당 버튼 1개(카드 나열을 라벨로).
            foreach (var move in ctx.LegalMoves)
            {
                var captured = move; // 캡처 고정
                string label = $"{captured.Type}: {string.Join(" ", captured.Cards)}";
                AddButton(label, () => _vm.SubmitTurnDecision(TurnDecision.Play(captured)));
            }
            // 패스는 ctx.CanPass 일 때만 활성.
            AddButton("패스", () => _vm.SubmitTurnDecision(TurnDecision.Pass), ctx.CanPass);
        }

        private void RenderBombPrompt(DecisionContext ctx)
        {
            AddPromptLabel("폭탄 인터럽트?");
            foreach (var move in ctx.LegalMoves)
            {
                if (!move.IsBomb) continue;
                var captured = move; // 캡처 고정
                string label = $"폭탄 {captured.Type}: {string.Join(" ", captured.Cards)}";
                AddButton(label, () => _vm.SubmitBomb(captured));
            }
            AddButton("넘기기", () => _vm.SubmitBomb(null));
        }

        private void RenderDragonPrompt(DecisionContext ctx)
        {
            AddPromptLabel("용 트릭 양도 — 상대를 고르세요");
            int left = ctx.LeftSeat;
            int right = ctx.RightSeat;
            AddButton($"왼쪽 상대(seat{left})", () => _vm.SubmitDragonRecipient(left));
            AddButton($"오른쪽 상대(seat{right})", () => _vm.SubmitDragonRecipient(right));
        }

        private void RenderExchangePrompt(DecisionContext ctx)
        {
            _exchangePicks.Clear();
            RebuildExchangeUi(ctx);
        }

        /// <summary>
        /// 교환 UI 를 현재 선택 상태에 맞춰 다시 그린다.
        /// 카드를 탭하면 다음 슬롯(Left→Partner→Right)에 채워지고,
        /// 3장이 모이면 [확정] 으로 SubmitExchange 한다.
        /// 교환은 서로 다른 3장이어야 하므로(ViewModel 게이팅) 이미 고른 카드값은 버튼에서 제외한다.
        /// </summary>
        private void RebuildExchangeUi(DecisionContext ctx)
        {
            ClearPrompt();

            string[] slotNames = { "왼쪽", "파트너", "오른쪽" };
            int next = _exchangePicks.Count; // 다음 채울 슬롯 인덱스(0..3)

            string picked = _exchangePicks.Count == 0
                ? "(없음)"
                : string.Join(", ", _exchangePicks.Select((c, i) => $"{slotNames[i]}={c}"));
            string hint = next < 3 ? $"다음 선택: {slotNames[next]}" : "3장 선택 완료";
            AddPromptLabel($"카드 교환 — {hint}\n선택: {picked}");

            // 손패의 서로 다른 카드값을 버튼으로 나열(이미 선택한 값은 제외).
            if (_exchangePicks.Count < 3)
            {
                foreach (var card in ctx.MyHand.Distinct())
                {
                    if (_exchangePicks.Contains(card)) continue;
                    var captured = card; // 캡처 고정
                    AddButton($"{captured}", () =>
                    {
                        _exchangePicks.Add(captured);
                        RebuildExchangeUi(ctx);
                    });
                }
            }

            // 초기화 버튼.
            AddButton("초기화", () =>
            {
                _exchangePicks.Clear();
                RebuildExchangeUi(ctx);
            });

            // 확정 버튼(3장 모였을 때만 활성).
            bool ready = _exchangePicks.Count == 3;
            AddButton("확정", () =>
            {
                if (_exchangePicks.Count != 3) return;
                var choice = new ExchangeChoice(_exchangePicks[0], _exchangePicks[1], _exchangePicks[2]);
                if (!_vm.SubmitExchange(choice))
                {
                    // 거부되면 선택을 초기화하고 다시 시도.
                    _exchangePicks.Clear();
                    RebuildExchangeUi(ctx);
                }
            }, ready);
        }

        // ── 프롬프트 위젯 헬퍼 ───────────────────────────────────────────────

        private void ClearPrompt()
        {
            for (int i = _promptRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(_promptRoot.GetChild(i).gameObject);
        }

        private void AddPromptLabel(string text)
        {
            NewText("PromptLabel", _promptRoot, text, 20);
        }

        private void AddButton(string label, System.Action onClick, bool interactable = true)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_promptRoot, false);
            var img = go.GetComponent<Image>();
            img.color = interactable ? new Color(0.25f, 0.45f, 0.75f) : new Color(0.4f, 0.4f, 0.4f);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 36;
            le.preferredHeight = 36;

            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;
            // 반환값은 ViewModel 의 수락 여부일 뿐, 버튼은 결과를 신경 쓰지 않는다(거부 시 프롬프트 유지).
            btn.onClick.AddListener(() => onClick());

            var labelText = NewText("Label", go.transform, label, 18);
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleCenter;
            StretchFull(labelText.rectTransform);
        }

        // ── 일반 위젯 생성 헬퍼 ──────────────────────────────────────────────

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
            t.color = Color.black;
            t.text = content;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 8;

            return t;
        }

        private static Font DefaultFont()
        {
            // 내장 폰트(LegacyRuntime/Arial 둘 중 존재하는 것).
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
