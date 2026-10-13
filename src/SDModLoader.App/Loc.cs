using System.ComponentModel;

namespace SDModLoader.App;

/// <summary>Простая локализация ru/en. В XAML: {Binding [Key], Source={x:Static local:Loc.Instance}, Mode=OneWay}.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    string _lang = "en";
    public string Language
    {
        get => _lang;
        set
        {
            value = value == "en" ? "en" : "ru";
            if (_lang == value) return;
            _lang = value;
            SDModLoader.Core.CoreText.Language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }

    public string this[string key] => Get(key);

    public static string T(string key, params object[] args)
    {
        var s = Instance.Get(key);
        return args.Length == 0 ? s : string.Format(s, args);
    }

    string Get(string key) =>
        Table.TryGetValue(key, out var v) ? (_lang == "en" ? v.En : v.Ru) : key;

    static readonly Dictionary<string, (string Ru, string En)> Table = new()
    {
        ["AppTitle"] = ("Sleeping Dogs ModLoader", "Sleeping Dogs ModLoader"),
        ["LangButton"] = ("English", "Русский"),
        ["GameFolder"] = ("Папка игры:", "Game folder:"),
        ["Browse"] = ("Обзор…", "Browse…"),
        ["FindSteam"] = ("Найти в Steam", "Find in Steam"),
        ["RdTitle"] = ("FileRedirector (обязательный компонент)", "FileRedirector (required component)"),
        ["InstallAuto"] = ("Установить автоматически", "Install automatically"),
        ["Recheck"] = ("Проверить снова", "Recheck"),
        ["SearchTip"] = ("Поиск по названию или автору", "Search by name or author"),
        ["Version"] = ("v", "v"),
        ["FilesCount"] = ("  ·  файлов: ", "  ·  files: "),
        ["AuthorLabel"] = (" · автор: ", " · author: "),
        ["EmptyList"] = ("Модов пока нет.\nПеретащите .sdmod / .zip в окно или нажмите «Добавить мод».",
                         "No mods yet.\nDrag a .sdmod / .zip into the window or click “Add mod”."),
        ["FilesTip"] = ("Файлы мода (относительно папки игры)", "Mod files (relative to the game folder)"),
        ["Export"] = ("Экспорт .sdmod", "Export .sdmod"),
        ["Delete"] = ("Удалить", "Delete"),
        ["AddMod"] = ("＋ Добавить мод", "＋ Add mod"),
        ["CreateMod"] = ("Создать мод из папки…", "Create mod from folder…"),
        ["DisableAll"] = ("Выключить все", "Disable all"),
        ["BtnGameFolder"] = ("Папка игры", "Game folder"),
        ["BtnModsFolder"] = ("Папка модов", "Mods folder"),
        ["OpenModFolder"] = ("Открыть папку мода", "Open mod folder"),
        ["ExeAuto"] = ("Авто (по умолчанию)", "Auto (default)"),
        ["ExeBrowse"] = ("Другой exe…", "Other exe…"),
        ["ExeChoose"] = ("Выбрать exe для запуска (например, sdhdship_debug.exe)", "Choose the executable to launch (e.g. sdhdship_debug.exe)"),
        ["LaunchWith"] = ("Запуск: {0}", "Launch: {0}"),
        ["Play"] = ("▶  Запустить игру", "▶  Launch game"),

        // Статусы
        ["Ready"] = ("Готово.", "Ready."),
        ["Done"] = ("Готово.", "Done."),
        ["RdChecking"] = ("Проверка FileRedirector…", "Checking FileRedirector…"),
        ["RdNoGame"] = ("Папка игры не выбрана.", "Game folder is not selected."),
        ["RdInstalled"] = ("FileRedirector установлен.", "FileRedirector is installed."),
        ["RdProbably"] = ("FileRedirector, вероятно, установлен (нет интернета для точной проверки).",
                          "FileRedirector is probably installed (no internet for an exact check)."),
        ["RdMissing"] = ("FileRedirector не установлен (не найдено файлов: {0}).", "FileRedirector is not installed (missing files: {0})."),
        ["RdNotFound"] = ("FileRedirector не найден.", "FileRedirector not found."),
        ["RdError"] = ("Ошибка проверки: {0}", "Check failed: {0}"),
        ["RdInstalling"] = ("Скачивание и установка FileRedirector…", "Downloading and installing FileRedirector…"),
        ["RdInstalledDone"] = ("FileRedirector установлен.", "FileRedirector installed."),
        ["Enabling"] = ("Включение: {0}", "Enabling: {0}"),
        ["Disabling"] = ("Выключение: {0}", "Disabling: {0}"),
        ["Enabled"] = ("Включён: {0}", "Enabled: {0}"),
        ["Disabled"] = ("Выключен: {0}", "Disabled: {0}"),
        ["DisablingAll"] = ("Выключение всех модов…", "Disabling all mods…"),
        ["AllDisabled"] = ("Все моды выключены.", "All mods disabled."),
        ["ErrorStatus"] = ("Ошибка: {0}", "Error: {0}"),
        ["BrokenMods"] = ("Файлы некоторых включённых модов отсутствуют: {0}. Переключите их для восстановления.",
                          "Files of some enabled mods are missing: {0}. Toggle them to restore."),
        ["Added"] = ("Добавлен: {0}", "Added: {0}"),
        ["Deleted"] = ("Удалён: {0}", "Deleted: {0}"),
        ["Exported"] = ("Экспортировано: {0}", "Exported: {0}"),
        ["Launched"] = ("Игра запущена.", "Game launched."),

        // Диалоги
        ["Error"] = ("Ошибка", "Error"),
        ["Warning"] = ("Предупреждения:", "Warnings:"),
        ["NoGameTitle"] = ("Нет папки игры", "No game folder"),
        ["NoGameText"] = ("Сначала укажите папку игры.", "Select the game folder first."),
        ["NotFoundTitle"] = ("Не найдено", "Not found"),
        ["SteamNotFound"] = ("Игра не найдена в Steam. Укажите папку вручную.", "The game was not found in Steam. Select the folder manually."),
        ["BadFolderTitle"] = ("Неверная папка", "Invalid folder"),
        ["BadFolderText"] = ("В выбранной папке нет exe-файла игры.", "The selected folder has no game .exe file."),
        ["PickFolderTitle"] = ("Выберите папку SleepingDogsDefinitiveEdition", "Select the SleepingDogsDefinitiveEdition folder"),
        ["RdInstallTitle"] = ("Установка FileRedirector", "Installing FileRedirector"),
        ["RdInstallText"] = ("Будет скачан FileRedirector с GitHub ({0}) и распакован в папку игры.\n\nВажно: для совместимости может требоваться определённая версия exe игры — см. README организации SDmodding.\n\nПродолжить?",
                             "FileRedirector will be downloaded from GitHub ({0}) and extracted into the game folder.\n\nNote: compatibility may require a specific game executable version — see the SDmodding organization README.\n\nContinue?"),
        ["RdWarnEnable"] = ("FileRedirector не установлен — мод не будет работать. Включить всё равно?",
                            "FileRedirector is not installed — the mod will not work. Enable anyway?"),
        ["RdWarnLaunch"] = ("FileRedirector не установлен — моды не будут работать. Запустить игру всё равно?",
                            "FileRedirector is not installed — mods will not work. Launch the game anyway?"),
        ["ConflictTitle"] = ("Конфликт модов", "Mod conflict"),
        ["ConflictText"] = ("Мод «{0}» перезаписывает файлы других включённых модов:\n\n{1}{2}\n\nПерезаписать? (при выключении мода файлы вернутся.)",
                            "Mod “{0}” overwrites files of other enabled mods:\n\n{1}{2}\n\nOverwrite? (files are restored when the mod is disabled.)"),
        ["ConflictMore"] = ("\n  … и ещё {0}", "\n  … and {0} more"),
        ["PickMod"] = ("Выберите мод", "Select a mod"),
        ["ModFilter"] = ("Моды (*.sdmod;*.zip)|*.sdmod;*.zip|Все файлы|*.*", "Mods (*.sdmod;*.zip)|*.sdmod;*.zip|All files|*.*"),
        ["ExistsTitle"] = ("Мод уже добавлен", "Mod already added"),
        ["ExistsText"] = ("{0}\nЗаменить новой версией?", "{0}\nReplace with the new version?"),
        ["ImportFailed"] = ("Не удалось импортировать", "Import failed"),
        ["SdmodFilter"] = ("Sleeping Dogs Mod (*.sdmod)|*.sdmod", "Sleeping Dogs Mod (*.sdmod)|*.sdmod"),
        ["DeleteTitle"] = ("Удаление", "Delete"),
        ["DeleteText"] = ("Удалить «{0}» из библиотеки? Если мод включён, он будет выключен.", "Delete “{0}” from the library? If the mod is enabled it will be disabled first."),
        ["PickExeTitle"] = ("Выберите exe-файл игры", "Select the game executable"),
        ["ExeFilter"] = ("Программы (*.exe)|*.exe", "Executables (*.exe)|*.exe"),
        ["LaunchFailed"] = ("Не удалось запустить игру", "Failed to launch the game"),

        // Окно создания мода
        ["CM_Title"] = ("Создать .sdmod", "Create .sdmod"),
        ["CM_Source"] = ("Папка с файлами мода", "Folder with mod files"),
        ["CM_Hint"] = ("Если в папке есть RedirectorData — берётся она целиком. Иначе всё содержимое попадёт в RedirectorData\\Characters_New.",
                       "If the folder contains RedirectorData it is used as is. Otherwise everything goes to RedirectorData\\Characters_New."),
        ["CM_Name"] = ("Название", "Name"),
        ["CM_Id"] = ("ID (a-z, 0-9, . _ -)", "ID (a-z, 0-9, . _ -)"),
        ["CM_Version"] = ("Версия", "Version"),
        ["CM_Author"] = ("Автор", "Author"),
        ["CM_Desc"] = ("Описание", "Description"),
        ["CM_Create"] = ("Создать", "Create"),
        ["CM_Cancel"] = ("Отмена", "Cancel"),
        ["CM_PickFolder"] = ("Папка с файлами мода", "Folder with mod files"),
        ["CM_NoFolder"] = ("Укажите существующую папку с файлами.", "Select an existing folder with files."),
        ["CM_NoName"] = ("Укажите название мода.", "Enter the mod name."),
        ["CM_BadId"] = ("Некорректный ID: допустимы a-z, 0-9, точка, дефис, подчёркивание.", "Invalid ID: only a-z, 0-9, dot, dash and underscore are allowed."),
        ["CM_Done"] = ("Готово: {0}", "Done: {0}"),
        ["CM_DoneTitle"] = ("Мод создан", "Mod created"),
    };
}
