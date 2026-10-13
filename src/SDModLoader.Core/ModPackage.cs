using System.IO.Compression;
using System.Text.Json;

namespace SDModLoader.Core;

public sealed record PayloadFile(string EntryName, string Rel);

public sealed class PackageInfo
{
    public required ModManifest Manifest { get; init; }
    public required List<PayloadFile> Files { get; init; }
    public string? PreviewEntry { get; init; }
    public List<string> Warnings { get; init; } = new();
    public bool HadManifest { get; init; }
}

/// <summary>Чтение, проверка и упаковка файлов формата .sdmod (zip + mod.json).</summary>
public static class ModPackage
{
    public const string Extension = ".sdmod";
    static readonly string[] PreviewNames = { "preview.png", "preview.jpg", "preview.jpeg" };
    static readonly string[] RootNames = { PathUtil.RedirectorRoot };
    /// <summary>Папка с файлами персонажей. Допускаем и частую опечатку Character_New.</summary>
    static readonly string[] CharNames = { "Characters_New", "Character_New" };
    static readonly string[] JunkNames = { ".ds_store", "thumbs.db", "desktop.ini" };

    static bool IsJunk(string raw) =>
        raw.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
        JunkNames.Contains(raw[(raw.LastIndexOf('/') + 1)..].ToLowerInvariant());

    static bool IsServiceName(string lower) =>
        lower == "mod.json" || PreviewNames.Contains(lower) ||
        lower.StartsWith("readme") || lower.StartsWith("license") || lower.StartsWith("changelog");

    static string Raw(ZipArchiveEntry e) => e.FullName.Replace('\\', '/');

    public static PackageInfo Inspect(ZipArchive zip, string fallbackName) => Inspect(new ZipFileReader(zip), fallbackName);

    public static PackageInfo Inspect(ZipFileReader zip, string fallbackName)
    {
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var warnings = new List<string>();

        // 1. Ищем mod.json в корне или в единственной вложенной папке.
        var mfEntry = files
            .Where(e => e.Name.Equals("mod.json", StringComparison.OrdinalIgnoreCase) && Raw(e).Count(c => c == '/') <= 1)
            .OrderBy(e => Raw(e).Count(c => c == '/')).FirstOrDefault();

        string baseDir = mfEntry == null ? "" : Raw(mfEntry)[..^"mod.json".Length];
        ModManifest manifest;
        if (mfEntry != null)
        {
            var bytes = zip.ReadAllBytes(mfEntry);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) bytes = bytes[3..]; // BOM
            try { manifest = JsonSerializer.Deserialize<ModManifest>(bytes, Json.Options) ?? new ModManifest(); }
            catch (JsonException ex) { throw new InvalidDataException(CoreText.T("ManifestBroken", ex.Message)); }
            if (manifest.FormatVersion > 1)
                throw new InvalidDataException(CoreText.T("FormatNewer"));
            if (string.IsNullOrWhiteSpace(manifest.Id)) manifest.Id = PathUtil.Slugify(manifest.Name);
        }
        else
        {
            manifest = new ModManifest { Name = fallbackName, Id = PathUtil.Slugify(fallbackName) };
            warnings.Add(CoreText.T("NoManifest"));
        }
        manifest.Id = manifest.Id.Trim().ToLowerInvariant();
        if (!PathUtil.IsValidId(manifest.Id))
            throw new InvalidDataException(CoreText.T("BadId", manifest.Id));
        if (string.IsNullOrWhiteSpace(manifest.Name)) manifest.Name = manifest.Id;

        // 2. Делим файлы на служебные и полезную нагрузку.
        string? previewEntry = null;
        var candidates = new List<(ZipArchiveEntry E, string Rel)>();
        foreach (var e in files)
        {
            var raw = Raw(e);
            if (IsJunk(raw)) continue;
            if (!raw.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = PathUtil.NormalizeRelative(raw[baseDir.Length..]);
            if (!rel.Contains('/'))
            {
                var lower = rel.ToLowerInvariant();
                if (lower == "mod.json") continue;
                if ((manifest.Preview != null && lower == manifest.Preview.ToLowerInvariant()) || PreviewNames.Contains(lower))
                { previewEntry ??= e.FullName; continue; }
                if (IsServiceName(lower)) continue;
            }
            candidates.Add((e, rel));
        }

        // 3. Определяем, куда класть файлы. Порядок правил:
        //    а) есть папка RedirectorData      -> всё, что внутри, как есть;
        //    б) есть папка Characters_New      -> файлы внутри в RedirectorData/Characters_New;
        //    в) «голые» файлы                  -> в RedirectorData/Characters_New
        //       (единственная общая папка-обёртка при этом отбрасывается).
        var mapped = new List<PayloadFile>();
        var withRoot = candidates.Where(c => FindSegment(c.Rel, RootNames) >= 0).ToList();
        if (withRoot.Count > 0)
        {
            foreach (var (e, rel) in withRoot)
            {
                var parts = rel.Split('/').Skip(FindSegment(rel, RootNames)).ToArray();
                parts[0] = PathUtil.RedirectorRoot;
                mapped.Add(new PayloadFile(e.FullName, string.Join('/', parts)));
            }
            var ignored = candidates.Count - withRoot.Count;
            if (ignored > 0) warnings.Add(CoreText.T("IgnoredOutsideRd", ignored));
        }
        else
        {
            var withChars = candidates.Where(c => FindSegment(c.Rel, CharNames) >= 0).ToList();
            if (withChars.Count > 0)
            {
                foreach (var (e, rel) in withChars)
                {
                    var inner = rel.Split('/').Skip(FindSegment(rel, CharNames) + 1);
                    mapped.Add(new PayloadFile(e.FullName, PathUtil.DefaultTarget + "/" + string.Join('/', inner)));
                }
                var ignored = candidates.Count - withChars.Count;
                if (ignored > 0) warnings.Add(CoreText.T("IgnoredOutsideChars", ignored));
            }
            else
            {
                var list = candidates.ToList();
                if (list.Count > 0 && list.All(c => c.Rel.Contains('/')) &&
                    list.Select(c => c.Rel.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                {
                    var wrapper = list[0].Rel.Split('/')[0];
                    var stripped = new List<(ZipArchiveEntry E, string Rel)>();
                    foreach (var (e, rel) in list)
                    {
                        var inner = rel[(wrapper.Length + 1)..];
                        if (!inner.Contains('/') && IsServiceName(inner.ToLowerInvariant()))
                        {
                            if (PreviewNames.Contains(inner.ToLowerInvariant())) previewEntry ??= e.FullName;
                            continue;
                        }
                        stripped.Add((e, inner));
                    }
                    list = stripped;
                    warnings.Add(CoreText.T("WrapperDropped", wrapper, PathUtil.DefaultTarget));
                }
                foreach (var (e, rel) in list)
                    mapped.Add(new PayloadFile(e.FullName, PathUtil.DefaultTarget + "/" + rel));
            }
        }

        // 4. Дубликаты (без учёта регистра) — побеждает последний.
        var unique = new Dictionary<string, PayloadFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mapped) unique[m.Rel] = m;
        if (unique.Count != mapped.Count) warnings.Add(CoreText.T("DuplicatePaths"));
        if (unique.Count == 0) throw new InvalidDataException(CoreText.T("NoFiles"));

        // 5. Правило FileRedirector: каждому .perm.bin нужен .temp.bin.
        var set = new HashSet<string>(unique.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var rel in unique.Keys.Where(k => k.EndsWith(".perm.bin", StringComparison.OrdinalIgnoreCase)))
        {
            var temp = rel[..^".perm.bin".Length] + ".temp.bin";
            if (!set.Contains(temp)) warnings.Add(CoreText.T("NoTempPair", rel));
        }

        return new PackageInfo
        {
            Manifest = manifest, Files = unique.Values.OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase).ToList(),
            PreviewEntry = previewEntry, Warnings = warnings, HadManifest = mfEntry != null,
        };
    }

    /// <summary>Индекс первой ПАПКИ (не файла) из names среди первых сегментов пути, иначе -1.</summary>
    static int FindSegment(string rel, string[] names)
    {
        var parts = rel.Split('/');
        for (var i = 0; i < Math.Min(parts.Length - 1, 4); i++)
            if (names.Contains(parts[i], StringComparer.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Собирает .sdmod из папки. Если в папке есть RedirectorData — берётся она, иначе всё содержимое → Characters_New.</summary>
    public static void Pack(string sourceDir, ModManifest manifest, string outFile)
    {
        manifest.Id = manifest.Id.Trim().ToLowerInvariant();
        if (!PathUtil.IsValidId(manifest.Id)) throw new ArgumentException(CoreText.T("BadIdShort"));
        var src = Path.GetFullPath(sourceDir);
        var rdRoot = Path.Combine(src, PathUtil.RedirectorRoot);
        var payload = new List<(string Full, string Rel)>();
        if (Directory.Exists(rdRoot))
        {
            foreach (var f in Directory.EnumerateFiles(rdRoot, "*", SearchOption.AllDirectories))
                payload.Add((f, PathUtil.RedirectorRoot + "/" + Path.GetRelativePath(rdRoot, f).Replace('\\', '/')));
        }
        else if (CharNames.Select(n => Path.Combine(src, n)).FirstOrDefault(Directory.Exists) is { } charsRoot)
        {
            foreach (var f in Directory.EnumerateFiles(charsRoot, "*", SearchOption.AllDirectories))
                payload.Add((f, PathUtil.DefaultTarget + "/" + Path.GetRelativePath(charsRoot, f).Replace('\\', '/')));
        }
        else
        {
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, f).Replace('\\', '/');
                if (rel.Equals("mod.json", StringComparison.OrdinalIgnoreCase) || PreviewNames.Contains(rel.ToLowerInvariant())) continue;
                payload.Add((f, PathUtil.DefaultTarget + "/" + rel));
            }
        }
        if (payload.Count == 0) throw new InvalidOperationException(CoreText.T("NothingToPack"));

        var preview = PreviewNames.Select(n => Path.Combine(src, n)).FirstOrDefault(File.Exists);
        Write(outFile, manifest, payload, preview);
    }

    internal static void Write(string outFile, ModManifest manifest, IEnumerable<(string Full, string Rel)> payload, string? preview)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
        if (File.Exists(outFile)) File.Delete(outFile);
        using var zip = ZipFile.Open(outFile, ZipArchiveMode.Create);
        var mf = zip.CreateEntry("mod.json");
        using (var w = new StreamWriter(mf.Open())) w.Write(JsonSerializer.Serialize(manifest, Json.Options));
        if (preview != null) zip.CreateEntryFromFile(preview, Path.GetFileName(preview).ToLowerInvariant());
        foreach (var (full, rel) in payload) zip.CreateEntryFromFile(full, rel, CompressionLevel.Optimal);
    }
}
