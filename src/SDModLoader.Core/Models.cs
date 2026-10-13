namespace SDModLoader.Core;

/// <summary>Содержимое mod.json внутри .sdmod.</summary>
public sealed class ModManifest
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Category { get; set; }
    public string? Homepage { get; set; }
    /// <summary>Имя файла превью внутри архива (необязательно; иначе ищется preview.png/jpg).</summary>
    public string? Preview { get; set; }
}

/// <summary>Мод, импортированный в библиотеку (Data/Mods/&lt;id&gt;).</summary>
public sealed class ModEntry
{
    public required ModManifest Manifest { get; init; }
    public required string Directory { get; init; }
    /// <summary>Пути относительно папки игры, всегда с префиксом RedirectorData/.</summary>
    public required List<string> Files { get; init; }
    public string? PreviewPath { get; init; }
    public string Id => Manifest.Id;
    public string FilesRoot => Path.Combine(Directory, "files");
}

public sealed class AppConfig
{
    public string? GameDir { get; set; }
    /// <summary>Выбранный exe для запуска игры (если автоопределение не сработало).</summary>
    public string? GameExe { get; set; }
    /// <summary>ru | en</summary>
    public string Language { get; set; } = "en";
    /// <summary>true — пользователь сам переключал язык (иначе используется английский по умолчанию).</summary>
    public bool LanguageChosen { get; set; }
    /// <summary>Файлы FileRedirector (относительно папки игры): известны после установки или чтения zip из релиза.</summary>
    public List<string> RedirectorFiles { get; set; } = new();
}

public sealed class BackupRecord
{
    public string ModId { get; set; } = "";
    public string RelPath { get; set; } = "";
    /// <summary>Какой мод владел файлом до нас (null — это оригинальный файл игры/пользователя).</summary>
    public string? PriorOwner { get; set; }
    /// <summary>Путь внутри Data/Backups.</summary>
    public string BackupFile { get; set; } = "";
}

public sealed class AppState
{
    public List<string> EnabledMods { get; set; } = new();
    /// <summary>relPath -> id мода, чей файл сейчас лежит в игре.</summary>
    public Dictionary<string, string> Owners { get; set; } = new();
    public List<BackupRecord> Backups { get; set; } = new();
}

public sealed record Conflict(string RelPath, string OwnerModId);

public sealed class EnableResult
{
    public bool Ok => Conflicts.Count == 0;
    public List<Conflict> Conflicts { get; init; } = new();
}
