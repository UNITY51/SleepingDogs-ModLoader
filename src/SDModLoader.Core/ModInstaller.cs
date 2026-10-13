namespace SDModLoader.Core;

/// <summary>
/// Включает/выключает моды: копирует файлы в папку игры и ведёт учёт владельцев файлов.
/// Перезаписанные файлы (оригинальные или от другого мода) уходят в Data/Backups и возвращаются при выключении.
/// </summary>
public sealed class ModInstaller
{
    readonly AppPaths _paths;
    readonly Func<string?> _gameDir;
    public AppState State { get; }

    public ModInstaller(AppPaths paths, AppState state, Func<string?> gameDir)
    {
        _paths = paths;
        _gameDir = gameDir;
        State = state;
        // System.Text.Json создаёт словарь без нашего comparer — пересоздаём.
        State.Owners = new Dictionary<string, string>(State.Owners, StringComparer.OrdinalIgnoreCase);
    }

    string RequireGameDir()
    {
        var gd = _gameDir();
        if (string.IsNullOrEmpty(gd) || !Directory.Exists(gd))
            throw new DirectoryNotFoundException(CoreText.T("GameDirMissing"));
        return gd;
    }

    public bool IsEnabled(string modId) => State.EnabledMods.Contains(modId, StringComparer.OrdinalIgnoreCase);
    void Save() => Json.Save(_paths.StateFile, State);

    public EnableResult Enable(ModEntry mod, bool overrideConflicts = false)
    {
        if (IsEnabled(mod.Id)) return new EnableResult();
        var gameDir = RequireGameDir();

        var conflicts = mod.Files
            .Where(f => State.Owners.TryGetValue(f, out var o) && !o.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))
            .Select(f => new Conflict(f, State.Owners[f])).ToList();
        if (conflicts.Count > 0 && !overrideConflicts) return new EnableResult { Conflicts = conflicts };

        try
        {
            foreach (var rel in mod.Files)
            {
                var src = PathUtil.Combine(mod.FilesRoot, rel);
                var dest = PathUtil.Combine(gameDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                State.Owners.TryGetValue(rel, out var prior);
                if (File.Exists(dest))
                {
                    var bkRel = mod.Id + "/" + rel;
                    var bk = PathUtil.Combine(_paths.BackupsDir, bkRel);
                    Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
                    File.Move(dest, bk, true);
                    State.Backups.Add(new BackupRecord { ModId = mod.Id, RelPath = rel, PriorOwner = prior, BackupFile = bkRel });
                }
                File.Copy(src, dest, true);
                State.Owners[rel] = mod.Id;
            }
            State.EnabledMods.Add(mod.Id);
            Save();
        }
        catch
        {
            try { Disable(mod.Id); } catch { /* откат best-effort */ }
            throw;
        }
        return new EnableResult();
    }

    public void Disable(string modId)
    {
        var gd = _gameDir();
        var gameDirOk = !string.IsNullOrEmpty(gd) && Directory.Exists(gd);
        var rdRoot = gameDirOk ? Path.Combine(gd!, PathUtil.RedirectorRoot) : "";

        BackupRecord? Rec(string mod, string rel) => State.Backups.FirstOrDefault(r =>
            r.ModId.Equals(mod, StringComparison.OrdinalIgnoreCase) && r.RelPath.Equals(rel, StringComparison.OrdinalIgnoreCase));

        // 1. Файлы, которые сейчас принадлежат этому моду: удаляем и возвращаем то, что лежало до него.
        var owned = State.Owners.Where(kv => kv.Value.Equals(modId, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
        foreach (var rel in owned)
        {
            string? dest = gameDirOk ? PathUtil.Combine(gd!, rel) : null;
            if (dest != null && File.Exists(dest)) File.Delete(dest);
            var own = Rec(modId, rel);
            if (own != null)
            {
                var bk = PathUtil.Combine(_paths.BackupsDir, own.BackupFile);
                if (dest != null && File.Exists(bk))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Move(bk, dest, true);
                }
                if (own.PriorOwner != null) State.Owners[rel] = own.PriorOwner; else State.Owners.Remove(rel);
                State.Backups.Remove(own);
            }
            else State.Owners.Remove(rel);
            if (dest != null) PathUtil.PruneEmptyDirs(Path.GetDirectoryName(dest), rdRoot);
        }

        // 2. Файлы этого мода, которые позже перекрыл другой мод X: X хранит в бэкапе наш файл — заменяем его тем, что было до нас.
        foreach (var rec in State.Backups.Where(r => r.PriorOwner != null && r.PriorOwner.Equals(modId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var own = Rec(modId, rec.RelPath);
            var recBk = PathUtil.Combine(_paths.BackupsDir, rec.BackupFile);
            if (own != null)
            {
                var ownBk = PathUtil.Combine(_paths.BackupsDir, own.BackupFile);
                if (File.Exists(ownBk)) File.Move(ownBk, recBk, true);
                rec.PriorOwner = own.PriorOwner;
                State.Backups.Remove(own);
            }
            else
            {
                if (File.Exists(recBk)) File.Delete(recBk);
                State.Backups.Remove(rec);
            }
        }

        State.EnabledMods.RemoveAll(m => m.Equals(modId, StringComparison.OrdinalIgnoreCase));
        var bkDir = Path.Combine(_paths.BackupsDir, modId);
        if (Directory.Exists(bkDir)) { PruneAll(bkDir); }
        Save();
    }

    static void PruneAll(string dir)
    {
        foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
            if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    public void DisableAll()
    {
        foreach (var id in State.EnabledMods.ToList().AsEnumerable().Reverse()) Disable(id);
    }

    /// <summary>Проверяет, что файлы включённых модов на месте. Возвращает id повреждённых модов.</summary>
    public List<string> Verify(IEnumerable<ModEntry> library)
    {
        var gd = _gameDir();
        var bad = new List<string>();
        if (string.IsNullOrEmpty(gd)) return bad;
        foreach (var m in library.Where(m => IsEnabled(m.Id)))
            if (m.Files.Any(f => State.Owners.TryGetValue(f, out var o) && o.Equals(m.Id, StringComparison.OrdinalIgnoreCase)
                                 && !File.Exists(PathUtil.Combine(gd, f))))
                bad.Add(m.Id);
        return bad;
    }
}
