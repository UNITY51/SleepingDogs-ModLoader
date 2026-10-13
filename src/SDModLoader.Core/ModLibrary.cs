using System.IO.Compression;
using System.Text.Json;

namespace SDModLoader.Core;

public sealed class ModExistsException : Exception
{
    public string ModId { get; }
    public ModExistsException(string id, string name) : base(CoreText.T("ModExists", name, id)) => ModId = id;
}

public sealed record ImportResult(ModEntry Entry, List<string> Warnings);

/// <summary>Библиотека модов: Data/Mods/&lt;id&gt;/{mod.json, files/..., preview.*}</summary>
public sealed class ModLibrary
{
    readonly AppPaths _paths;
    public ModLibrary(AppPaths paths) => _paths = paths;

    public List<ModEntry> LoadAll()
    {
        var list = new List<ModEntry>();
        foreach (var dir in Directory.EnumerateDirectories(_paths.ModsDir))
        {
            if (Path.GetFileName(dir).StartsWith('.')) continue;
            var e = TryLoad(dir);
            if (e != null) list.Add(e);
        }
        return list.OrderBy(m => m.Manifest.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public ModEntry? Get(string id) => TryLoad(Path.Combine(_paths.ModsDir, id));

    static ModEntry? TryLoad(string dir)
    {
        var mf = Path.Combine(dir, "mod.json");
        var filesRoot = Path.Combine(dir, "files");
        if (!File.Exists(mf) || !Directory.Exists(filesRoot)) return null;
        ModManifest? m;
        try { m = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(mf), Json.Options); }
        catch (JsonException) { return null; }
        if (m == null || !PathUtil.IsValidId(m.Id)) return null;
        var files = Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(filesRoot, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var preview = new[] { "preview.png", "preview.jpg", "preview.jpeg" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
        return new ModEntry { Manifest = m, Directory = dir, Files = files, PreviewPath = preview };
    }

    /// <summary>Импортирует .sdmod / .zip в библиотеку.</summary>
    public ImportResult Import(string archivePath, bool replace = false)
    {
        using var fs = File.OpenRead(archivePath);
        using var zip = new ZipFileReader(fs);
        var info = ModPackage.Inspect(zip, Path.GetFileNameWithoutExtension(archivePath));
        var target = Path.Combine(_paths.ModsDir, info.Manifest.Id);
        if (Directory.Exists(target) && !replace) throw new ModExistsException(info.Manifest.Id, info.Manifest.Name);

        var tmp = Path.Combine(_paths.ModsDir, ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var filesRoot = Path.Combine(tmp, "files");
            Directory.CreateDirectory(filesRoot);
            foreach (var f in info.Files)
            {
                var dest = PathUtil.Combine(filesRoot, f.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                zip.ExtractToFile(zip.GetEntry(f.EntryName)!, dest);
            }
            Json.Save(Path.Combine(tmp, "mod.json"), info.Manifest);
            if (info.PreviewEntry != null)
                zip.ExtractToFile(zip.GetEntry(info.PreviewEntry)!, Path.Combine(tmp, Path.GetFileName(info.PreviewEntry).ToLowerInvariant()));

            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(tmp, target);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
        return new ImportResult(Get(info.Manifest.Id)!, info.Warnings);
    }

    /// <summary>Читает id мода из архива, не импортируя его (null, если архив нечитаем).</summary>
    public string? PeekId(string archivePath)
    {
        try
        {
            using var fs = File.OpenRead(archivePath);
            using var zip = new ZipFileReader(fs);
            return ModPackage.Inspect(zip, Path.GetFileNameWithoutExtension(archivePath)).Manifest.Id;
        }
        catch { return null; }
    }

    public void Remove(string id)
    {
        var dir = Path.Combine(_paths.ModsDir, id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public void Export(string id, string outFile)
    {
        var e = Get(id) ?? throw new FileNotFoundException(CoreText.T("ModNotInLibrary"));
        ModPackage.Write(outFile, e.Manifest, e.Files.Select(f => (PathUtil.Combine(e.FilesRoot, f), f)), e.PreviewPath);
    }
}
