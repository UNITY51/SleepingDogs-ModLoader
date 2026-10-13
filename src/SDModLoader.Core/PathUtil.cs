using System.Text.RegularExpressions;

namespace SDModLoader.Core;

public static class PathUtil
{
    public const string RedirectorRoot = "RedirectorData";
    /// <summary>Куда попадают файлы из «голых» архивов без структуры RedirectorData.</summary>
    public const string DefaultTarget = "RedirectorData/Characters_New";

    /// <summary>Нормализует путь из архива в вид a/b/c. Бросает исключение при zip-slip и прочих небезопасных путях.</summary>
    public static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException(CoreText.T("EmptyPath"));
        var n = path.Replace('\\', '/');
        if (n.StartsWith('/') || n.Contains(':'))
            throw new InvalidDataException(CoreText.T("BadPath", path));
        var parts = new List<string>();
        foreach (var s in n.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (s == ".") continue;
            if (s == "..") throw new InvalidDataException(CoreText.T("BadPath", path));
            if (s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException(CoreText.T("BadFileName", path));
            parts.Add(s);
        }
        if (parts.Count == 0) throw new InvalidDataException(CoreText.T("EmptyPath"));
        return string.Join('/', parts);
    }

    public static string ToNative(string rel) => rel.Replace('/', Path.DirectorySeparatorChar);

    public static string Combine(string root, string rel)
    {
        var full = Path.GetFullPath(Path.Combine(root, ToNative(rel)));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(CoreText.T("EscapesFolder", rel));
        return full;
    }

    public static string Slugify(string s)
    {
        var slug = Regex.Replace(s.Trim().ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-', '.', '_');
        return slug.Length > 64 ? slug[..64] : slug;
    }

    public static bool IsValidId(string id) => Regex.IsMatch(id, @"^[a-z0-9][a-z0-9._-]{0,63}$");

    /// <summary>Удаляет пустые папки снизу вверх, не трогая stopAt.</summary>
    public static void PruneEmptyDirs(string? dir, string stopAt)
    {
        var stop = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
        while (!string.IsNullOrEmpty(dir))
        {
            var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            if (full.Equals(stop, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full)) return;
            if (Directory.EnumerateFileSystemEntries(full).Any()) return;
            Directory.Delete(full);
            dir = Path.GetDirectoryName(full);
        }
    }
}
