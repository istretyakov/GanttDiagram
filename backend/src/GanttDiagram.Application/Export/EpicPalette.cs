namespace GanttDiagram.Application.Export;

/// <summary>
/// Палитра, закреплённая за ключом эпика. Тот же алгоритм и те же цвета используются
/// на frontend (src/shared/epicPalette.ts), поэтому цвета в экспорте и в приложении совпадают.
/// </summary>
public static class EpicPalette
{
    public static readonly string[] Colors =
    [
        "#9EC5FE", "#A3CFBB", "#FFDA6A", "#F1AEB5", "#C5B3E6",
        "#9EEAF9", "#FECBA1", "#A6E9D5", "#E2D9F3", "#D3D3D4",
    ];

    public const string NoEpicColor = "#CED4DA";

    public static string ColorFor(string? epicKey)
    {
        if (string.IsNullOrEmpty(epicKey)) return NoEpicColor;
        return Colors[Hash(epicKey) % (uint)Colors.Length];
    }

    /// <summary>FNV-1a по UTF-16 кодам символов.</summary>
    public static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return hash;
    }
}
