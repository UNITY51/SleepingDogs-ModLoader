using System.IO.Compression;

namespace SDModLoader.Core;

public enum RedirectorState { Installed, NotInstalled, ProbablyInstalled }

public sealed record RedirectorStatus(RedirectorState State, string Message, List<string> MissingFiles)
{
    public bool IsOk => State != RedirectorState.NotInstalled;
}

/// <summary>Проверка и установка SDmodding/FileRedirector.</summary>
public sealed class RedirectorService
{
    public const string RepoUrl = "https://github.com/SDmodding/FileRedirector";
    public const string ZipUrl = RepoUrl + "/releases/latest/download/FileRedirector.zip";
    static readonly string[] ProxyNames = { "dinput8.dll", "version.dll", "winmm.dll", "dsound.dll", "xinput1_3.dll", "xinput1_4.dll", "d3d9.dll" };

    readonly AppConfig _cfg;
    readonly Action _saveCfg;
    readonly Func<CancellationToken, Task<byte[]>> _download;

    public RedirectorService(AppConfig cfg, Action saveCfg, Func<CancellationToken, Task<byte[]>>? downloader = null)
    {
        _cfg = cfg;
        _saveCfg = saveCfg;
        _download = downloader ?? DefaultDownload;
    }

    static async Task<byte[]> DefaultDownload(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SDModLoader/1.0");
        return await http.GetByteArrayAsync(ZipUrl, ct);
    }

    static List<string> ListFiles(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        return zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => PathUtil.NormalizeRelative(e.FullName)).ToList();
    }

    /// <param name="refresh">Перечитать список файлов из актуального релиза на GitHub.</param>
    public async Task<RedirectorStatus> CheckAsync(string gameDir, bool refresh = false, CancellationToken ct = default)
    {
        if (_cfg.RedirectorFiles.Count == 0 || refresh)
        {
            try
            {
                _cfg.RedirectorFiles = ListFiles(await _download(ct));
                _saveCfg();
            }
            catch (Exception) when (!ct.IsCancellationRequested) { /* офлайн — используем эвристику */ }
        }

        if (_cfg.RedirectorFiles.Count > 0)
        {
            var missing = _cfg.RedirectorFiles.Where(f => !File.Exists(PathUtil.Combine(gameDir, f))).ToList();
            return missing.Count == 0
                ? new RedirectorStatus(RedirectorState.Installed, CoreText.T("RdInstalled"), missing)
                : new RedirectorStatus(RedirectorState.NotInstalled, CoreText.T("RdMissing", missing.Count), missing);
        }

        var hasFolder = Directory.Exists(Path.Combine(gameDir, PathUtil.RedirectorRoot));
        var hasProxy = Directory.EnumerateFiles(gameDir, "*.asi").Any() ||
                       ProxyNames.Any(n => File.Exists(Path.Combine(gameDir, n)));
        return hasFolder && hasProxy
            ? new RedirectorStatus(RedirectorState.ProbablyInstalled, CoreText.T("RdProbably"), new())
            : new RedirectorStatus(RedirectorState.NotInstalled, CoreText.T("RdNotFound"), new());
    }

    /// <summary>Скачивает FileRedirector.zip и распаковывает в папку игры. Существующие файлы сохраняются как *.sdml.bak.</summary>
    public async Task InstallAsync(string gameDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(gameDir)) throw new DirectoryNotFoundException(CoreText.T("GameDirMissing"));
        var bytes = await _download(ct);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipFileReader(ms);
        var installed = new List<string>();
        try
        {
            foreach (var e in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
            {
                var rel = PathUtil.NormalizeRelative(e.FullName);
                var dest = PathUtil.Combine(gameDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest) && !File.Exists(dest + ".sdml.bak")) File.Copy(dest, dest + ".sdml.bak");
                zip.ExtractToFile(e, dest);
                installed.Add(rel);
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(CoreText.T("NoWriteAccess"));
        }
        Directory.CreateDirectory(Path.Combine(gameDir, PathUtil.RedirectorRoot));
        _cfg.RedirectorFiles = installed;
        _saveCfg();
    }
}
