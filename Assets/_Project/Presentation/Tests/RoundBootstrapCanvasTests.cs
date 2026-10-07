using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 테이블 캔버스는 테이블 씬에 속해야 한다. 활성 씬(App)에 생기면 테이블 씬을 내려도 남아
    /// 매치마다 쌓이고, 정렬 순서가 같으면 오래된 캔버스가 위에 그려져 새 화면을 덮는다
    /// (실기: 실패한 접속의 "서버 접속 중…" 대기실이 다음 접속의 화면을 가렸다).
    /// </summary>
    public class RoundBootstrapCanvasTests
    {
        [Test]
        public void Table_canvas_is_created_in_the_given_scene_so_unloading_it_removes_the_canvas()
        {
            // 테스트 러너의 씬은 저장 안 된 빈 씬이라 추가 씬을 못 연다 — 별도 프리뷰 씬을 '테이블 씬'으로 쓴다.
            var table = EditorSceneManager.NewPreviewScene();
            try
            {
                Assert.That(SceneManager.GetActiveScene(), Is.Not.EqualTo(table), "전제: 테이블 씬은 활성 씬이 아니다");

                var canvas = RoundBootstrap.CreateCanvas(table);

                Assert.That(canvas.gameObject.scene, Is.EqualTo(table),
                    "캔버스가 활성 씬에 생기면 테이블 씬 언로드 뒤에도 남는다");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(table);
            }
        }
    }
}
