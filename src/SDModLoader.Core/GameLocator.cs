using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SDModLoader.Core;

/// <summary>Поиск Sleeping Dogs: Definitive Edition через Steam (реестр + libraryfolders.vdf).</summary>
public static class GameLocator
{
    public const string FolderName = "SleepingDogsDefinitiveEdition";

    public static bool IsValidGameDir(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
        Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly).Any();

    static readonly string[] NotTheGame = { "unins", "redist", "setup", "crash", "report", "install", "dotnet", "dxwebsetup" };

    /// <summary>Все exe в папке игры, кроме инсталляторов/редистрибутивов (для выбора exe запуска).</summary>
    public static List<string> ListExecutables(string gameDir)
    {
        if (!Directory.Exists(gameDir)) return new();
        return Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(f => !NotTheGame.Any(b => Path.GetFileName(f).Contains(b, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Пытается определить основной exe игры (debug-версии не выбираются автоматически). null — нужно спросить пользователя.</summary>
    public static string? FindExecutable(string gameDir)
    {
        var all = ListExecutables(gameDir);
        var normal = all.Where(f => !Path.GetFileName(f).Contains("debug", StringComparison.OrdinalIgnoreCase)).ToList();
        if (normal.Count == 0) normal = all;
        if (normal.Count == 1) return normal[0];
        return normal.FirstOrDefault(f =>
        {
            var n = Path.GetFileName(f);
            return n.Contains("ship", StringComparison.OrdinalIgnoreCase) || n.Contains("sleeping", StringComparison.OrdinalIgnoreCase)
                   || n.StartsWith("SDHD", StringComparison.OrdinalIgnoreCase) || n.StartsWith("HK", StringComparison.OrdinalIgnoreCase);
        });
    }

    public static string? Find()
    {
        foreach (var lib in SteamLibraries())
        {
            var p = Path.Combine(lib, "steamapps", "common", FolderName);
            if (IsValidGameDir(p)) return p;
        }
        return null;
    }

    public static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            try { if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string s) roots.Add(s); } catch { }
            try { if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string s) roots.Add(s); } catch { }
        }
        roots.Add(@"C:\Program Files (x86)\Steam");
        roots.Add(@"C:\Program Files\Steam");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Select(r => r.Replace('/', Path.DirectorySeparatorChar)))
        {
            if (!Directory.Exists(root)) continue;
            if (seen.Add(root)) yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(p)) yield return p;
            }
        }
    }
}
