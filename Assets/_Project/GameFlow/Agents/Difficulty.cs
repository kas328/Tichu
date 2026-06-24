namespace Tichu.GameFlow.Agents
{
    /// <summary>AI 난이도 티어. 차이는 PolicyConfig(탐색 예산·노이즈)뿐 — 별 구현/룰 없음.</summary>
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard,
        Expert,
    }
}
