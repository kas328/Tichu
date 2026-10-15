using NUnit.Framework;
using Tichu.Presentation.Net;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Tests
{
    public class LobbyOverlayViewTests
    {
        private static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static string Label(GameObject root, int seat) =>
            Find(Find(root, "Seat" + seat).gameObject, "Label").GetComponent<Text>().text;

        [Test]
        public void Create_shows_the_connecting_state_with_a_cancel_button()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var view = LobbyOverlayView.Create(canvasGo.GetComponent<Canvas>());

                Assert.That(view.Root.transform.parent, Is.EqualTo(canvasGo.transform));
                Assert.That(view.Cancel, Is.Not.Null);
                Assert.That(Find(canvasGo, "Title").GetComponent<Text>().text, Is.EqualTo("서버 접속 중…"));
                Assert.That(Find(canvasGo, "Seats").gameObject.activeSelf, Is.False);
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Render_fills_seats_from_the_left_with_me_first()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var view = LobbyOverlayView.Create(canvasGo.GetComponent<Canvas>());

                view.Render(new LobbyDisplay("상대를 찾는 중", 2, "18초 뒤 빈자리는 AI로 채우고 시작합니다"));

                Assert.That(Find(canvasGo, "Seats").gameObject.activeSelf, Is.True);
                Assert.That(new[] { Label(canvasGo, 0), Label(canvasGo, 1), Label(canvasGo, 2), Label(canvasGo, 3) },
                    Is.EqualTo(new[] { "나", "플레이어", "빈자리", "빈자리" }));
                Assert.That(Find(canvasGo, "Detail").GetComponent<Text>().text,
                    Is.EqualTo("18초 뒤 빈자리는 AI로 채우고 시작합니다"));
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Render_with_zero_seats_hides_the_row()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var view = LobbyOverlayView.Create(canvasGo.GetComponent<Canvas>());
                view.Render(new LobbyDisplay("상대를 찾는 중", 3, "x"));

                view.Render(LobbyDisplay.Reconnecting);

                Assert.That(Find(canvasGo, "Seats").gameObject.activeSelf, Is.False);
                Assert.That(Find(canvasGo, "Title").GetComponent<Text>().text, Is.EqualTo("연결이 끊겼습니다"));
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
    }
}
