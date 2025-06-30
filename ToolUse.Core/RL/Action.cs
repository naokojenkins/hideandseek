namespace ToolUse.Core.RL;

public enum Action
{
    Up = 0,
    Down = 1,
    Left = 2,
    Right = 3
}

public static class Actions
{
    public static readonly (int dx, int dy)[] AllMoves =
    {
        (0, -1), // Up
        (0, 1),  // Down
        (-1, 0), // Left
        (1, 0)   // Right
    };
}