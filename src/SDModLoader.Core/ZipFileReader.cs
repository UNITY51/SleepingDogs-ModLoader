using System.IO.Compression;

namespace SDModLoader.Core;

/// <summary>
/// Обёртка над ZipArchive: если запись сжата LZMA (её не умеет System.IO.Compression), распаковывает её собственным декодером.
/// </summary>
public sealed class ZipFileReader : IDisposable
{
    readonly Stream? _stream;
    readonly bool _ownsArchive;
    List<RawZipEntry>? _raw;

    public ZipArchive Archive { get; }
    public IReadOnlyList<ZipArchiveEntry> Entries => Archive.Entries;

    /// <summary>Поток должен быть seekable и жить дольше ридера.</summary>
    public ZipFileReader(Stream stream)
    {
        _stream = stream;
        Archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        _ownsArchive = true;
    }

    /// <summary>Без запасного LZMA-декодера (поток недоступен).</summary>
    public ZipFileReader(ZipArchive archive) => Archive = archive;

    public ZipArchiveEntry? GetEntry(string name) => Archive.GetEntry(name);

    RawZipEntry? FindRaw(ZipArchiveEntry e)
    {
        if (_stream == null) return null;
        _raw ??= LzmaZip.ReadCentralDirectory(_stream);
        var idx = Archive.Entries.ToList().IndexOf(e);
        if (_raw.Count == Archive.Entries.Count && idx >= 0) return _raw[idx];
        return _raw.FirstOrDefault(r => r.Name == e.FullName);
    }

    public void ExtractToFile(ZipArchiveEntry e, string dest)
    {
        try { e.ExtractToFile(dest, true); return; }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            var raw = FindRaw(e);
            if (raw == null || raw.Method != LzmaZip.MethodLzma) throw;
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* ignore */ }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dest))!);
            using var fs = File.Create(dest);
            LzmaZip.Extract(_stream!, raw, fs);
        }
    }

    public byte[] ReadAllBytes(ZipArchiveEntry e)
    {
        try
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            var raw = FindRaw(e);
            if (raw == null || raw.Method != LzmaZip.MethodLzma) throw;
            using var ms = new MemoryStream();
            LzmaZip.Extract(_stream!, raw, ms);
            return ms.ToArray();
        }
    }

    public void Dispose() { if (_ownsArchive) Archive.Dispose(); }
}
