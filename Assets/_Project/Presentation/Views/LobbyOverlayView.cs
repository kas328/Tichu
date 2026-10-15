using Tichu.Presentation.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 네트워크 매치의 대기 화면(테이블 위 반투명 판). 제목·4칸 자리·설명 줄·취소 버튼.
    /// 무엇을 보일지는 <see cref="LobbyWaiter.Display"/> 가 정하고 여기서는 그리기만 한다.
    /// </summary>
    public sealed class LobbyOverlayView
    {
        private static readonly Color Backdrop = new Color(0.04f, 0.10f, 0.06f, 0.92f);
        private static readonly Color Ink = new Color(0.92f, 0.94f, 0.98f);
        private static readonly Color SeatFilled = new Color(0.18f, 0.44f, 0.37f, 1f);
        private static readonly Color SeatEmpty = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ButtonColor = new Color(0.20f, 0.24f, 0.34f, 1f);

        public GameObject Root { get; private set; }
        public Button Cancel { get; private set; }

        private Text _title, _detail;
        private GameObject _seatRow;
        private readonly Image[] _seatBg = new Image[4];
        private readonly Text[] _seatLabel = new Text[4];

        public static LobbyOverlayView Create(Canvas canvas)
        {
            var v = new LobbyOverlayView();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            v.Root = new GameObject("LobbyOverlay", typeof(RectTransform), typeof(Image));
            v.Root.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)v.Root.transform);
            v.Root.GetComponent<Image>().color = Backdrop;

            v._title = NewText("Title", v.Root.transform, font, 48, new Vector2(1200f, 80f), new Vector2(0f, 190f));

            v._seatRow = new GameObject("Seats", typeof(RectTransform));
            v._seatRow.transform.SetParent(v.Root.transform, false);
            var row = (RectTransform)v._seatRow.transform;
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(4 * 180f + 3 * 24f, 160f);
            row.anchoredPosition = new Vector2(0f, 40f);
            for (int i = 0; i < 4; i++)
            {
                var seat = new GameObject("Seat" + i, typeof(RectTransform), typeof(Image));
                seat.transform.SetParent(row, false);
                var srt = (RectTransform)seat.transform;
                srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0.5f);
                srt.sizeDelta = new Vector2(180f, 160f);
                srt.anchoredPosition = new Vector2(i * (180f + 24f), 0f);
                v._seatBg[i] = seat.GetComponent<Image>();
                v._seatLabel[i] = NewText("Label", seat.transform, font, 32, Vector2.zero, Vector2.zero);
                Stretch(v._seatLabel[i].rectTransform);
            }

            v._detail = NewText("Detail", v.Root.transform, font, 30, new Vector2(1400f, 60f), new Vector2(0f, -90f));

            var btn = new GameObject("Btn_취소", typeof(RectTransform), typeof(Image), typeof(Button));
            btn.transform.SetParent(v.Root.transform, false);
            var brt = (RectTransform)btn.transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(360f, 96f); brt.anchoredPosition = new Vector2(0f, -200f);
            btn.GetComponent<Image>().color = ButtonColor;
            v.Cancel = btn.GetComponent<Button>();
            var lbl = NewText("L", btn.transform, font, 36, Vector2.zero, Vector2.zero);
            Stretch(lbl.rectTransform);
            lbl.text = "취소";

            v.Render(LobbyDisplay.Connecting);
            return v;
        }

        public void Render(LobbyDisplay d)
        {
            _title.text = d.Title;
            _detail.text = d.Detail;
            _seatRow.SetActive(d.Seats > 0);
            for (int i = 0; i < 4; i++)
            {
                bool filled = i < d.Seats;
                _seatBg[i].color = filled ? SeatFilled : SeatEmpty;
                _seatLabel[i].text = i == 0 ? "나" : filled ? "플레이어" : "빈자리";
            }
        }

        public void Destroy() => Object.Destroy(Root);

        private static Text NewText(string name, Transform parent, Font font, int size, Vector2 box, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter; t.color = Ink;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = box; rt.anchoredPosition = pos;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
