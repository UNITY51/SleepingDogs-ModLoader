namespace SDModLoader.Core;

/// <summary>Сообщения ядра (ошибки, предупреждения) на двух языках. Язык выставляет приложение.</summary>
public static class CoreText
{
    /// <summary>"en" (по умолчанию) или "ru".</summary>
    public static string Language { get; set; } = "en";

    public static string T(string key, params object[] args)
    {
        var s = Table.TryGetValue(key, out var v) ? (Language == "ru" ? v.Ru : v.En) : key;
        return args.Length == 0 ? s : string.Format(s, args);
    }

    static readonly Dictionary<string, (string Ru, string En)> Table = new()
    {
        ["GameDirMissing"] = ("Папка игры не найдена. Укажите путь к игре.", "Game folder not found. Select the game folder."),
        ["ModExists"] = ("Мод «{0}» ({1}) уже есть в библиотеке.", "Mod “{0}” ({1}) is already in the library."),
        ["ModNotInLibrary"] = ("Мод не найден в библиотеке.", "The mod was not found in the library."),
        ["ManifestBroken"] = ("mod.json повреждён: {0}", "mod.json is corrupted: {0}"),
        ["FormatNewer"] = ("Мод создан для более новой версии формата. Обновите ModLoader.", "This mod uses a newer format version. Please update ModLoader."),
        ["NoManifest"] = ("mod.json не найден — метаданные созданы автоматически.", "mod.json not found — metadata was generated automatically."),
        ["BadId"] = ("Некорректный id мода «{0}» (разрешены a-z, 0-9, . _ -).", "Invalid mod id “{0}” (allowed: a-z, 0-9, . _ -)."),
        ["IgnoredOutsideRd"] = ("{0} файл(ов) вне папки RedirectorData проигнорировано.", "{0} file(s) outside the RedirectorData folder were ignored."),
        ["IgnoredOutsideChars"] = ("{0} файл(ов) вне папки Characters_New проигнорировано.", "{0} file(s) outside the Characters_New folder were ignored."),
        ["WrapperDropped"] = ("Общая папка «{0}» отброшена: файлы помещены в {1}.", "The common folder “{0}” was dropped: files were placed in {1}."),
        ["DuplicatePaths"] = ("В архиве есть файлы с одинаковыми путями.", "The archive contains files with identical paths."),
        ["NoFiles"] = ("В архиве нет файлов мода.", "The archive contains no mod files."),
        ["NoTempPair"] = ("Для {0} нет парного .temp.bin (FileRedirector требует его, пусть даже пустым).", "{0} has no matching .temp.bin (FileRedirector requires it, even if empty)."),
        ["BadIdShort"] = ("Некорректный id мода.", "Invalid mod id."),
        ["NothingToPack"] = ("В папке нет файлов для упаковки.", "The folder has no files to pack."),
        ["EmptyPath"] = ("Пустой путь в архиве.", "Empty path in the archive."),
        ["BadPath"] = ("Недопустимый путь в архиве: {0}", "Invalid path in the archive: {0}"),
        ["BadFileName"] = ("Недопустимое имя файла в архиве: {0}", "Invalid file name in the archive: {0}"),
        ["EscapesFolder"] = ("Путь выходит за пределы папки: {0}", "Path escapes the target folder: {0}"),
        ["RdInstalled"] = ("FileRedirector установлен.", "FileRedirector is installed."),
        ["RdMissing"] = ("FileRedirector не установлен (не найдено файлов: {0}).", "FileRedirector is not installed (missing files: {0})."),
        ["RdProbably"] = ("FileRedirector, вероятно, установлен (нет интернета для точной проверки).", "FileRedirector is probably installed (no internet for an exact check)."),
        ["RdNotFound"] = ("FileRedirector не найден.", "FileRedirector not found."),
        ["NoWriteAccess"] = ("Нет прав на запись в папку игры. Запустите ModLoader от имени администратора или переместите игру из Program Files.",
                             "No write access to the game folder. Run ModLoader as administrator or move the game out of Program Files."),
    };
}
