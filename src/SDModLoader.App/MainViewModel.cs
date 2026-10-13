using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using SDModLoader.Core;

namespace SDModLoader.App;

public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ModItemVM : Notifier
{
    bool _enabled;
    public ModEntry Entry { get; }
    public ModItemVM(ModEntry e, bool enabled) { Entry = e; _enabled = enabled; }

    public string Id => Entry.Id;
    public string Name => Entry.Manifest.Name;
    public string Version => Entry.Manifest.Version;
    public string Author => string.IsNullOrWhiteSpace(Entry.Manifest.Author) ? "—" : Entry.Manifest.Author;
    public string Description => Entry.Manifest.Description;
    public string Category => Entry.Manifest.Category ?? "";
    public int FileCount => Entry.Files.Count;
    public string FilesText => string.Join(Environment.NewLine, Entry.Files);
    public bool IsEnabled { get => _enabled; set => Set(ref _enabled, value); }

    public BitmapImage? Preview
    {
        get
        {
            if (Entry.PreviewPath == null) return null;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad; // не держим файл открытым
                img.UriSource = new Uri(Entry.PreviewPath);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
    }
}

public sealed class MainViewModel : Notifier
{
    public AppPaths Paths { get; } = AppPaths.ForPortable();
    public AppConfig Config { get; }
    public ModLibrary Library { get; }
    public ModInstaller Installer { get; }
    public RedirectorService Redirector { get; }
    public ObservableCollection<ModItemVM> Mods { get; } = new();
    public ICollectionView ModsView { get; }

    string? _gameDir, _status = Loc.T("Ready"), _search = "";
    string _rdLevel = "warn", _rdMessage = Loc.T("RdChecking");
    bool _busy, _rdOk;
    ModItemVM? _selected;

    public MainViewModel()
    {
        Config = Json.LoadOrNew<AppConfig>(Paths.ConfigFile);
        var state = Json.LoadOrNew<AppState>(Paths.StateFile);
        _gameDir = Config.GameDir;
        Loc.Instance.Language = Config.LanguageChosen ? Config.Language : "en";
        _status = Loc.T("Ready"); _rdMessage = Loc.T("RdChecking");
        Library = new ModLibrary(Paths);
        Installer = new ModInstaller(Paths, state, () => GameDir);
        Redirector = new RedirectorService(Config, SaveConfig);
        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = o => o is ModItemVM m &&
            (string.IsNullOrWhiteSpace(_search) || m.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)
             || m.Author.Contains(_search, StringComparison.CurrentCultureIgnoreCase));
    }

    public void SaveConfig() => Json.Save(Paths.ConfigFile, Config);

    public string? GameDir
    {
        get => _gameDir;
        set { if (Set(ref _gameDir, value)) { Config.GameDir = value; if (Config.GameExe != null && (value == null || !Config.GameExe.StartsWith(value, StringComparison.OrdinalIgnoreCase))) Config.GameExe = null; SaveConfig(); Raise(nameof(HasGame)); Raise(nameof(LaunchExePath)); Raise(nameof(LaunchExeName)); } }
    }
    /// <summary>Exe, который будет запущен: выбранный вручную или определённый автоматически.</summary>
    public string? LaunchExePath =>
        Config.GameExe is { } c && File.Exists(c) ? c : (HasGame ? GameLocator.FindExecutable(GameDir!) : null);
    public string LaunchExeName => LaunchExePath is { } p ? Loc.T("LaunchWith", Path.GetFileName(p)) : "";

    /// <summary>null = автоопределение.</summary>
    public void SetLaunchExe(string? path)
    {
        Config.GameExe = path;
        SaveConfig();
        Raise(nameof(LaunchExePath));
        Raise(nameof(LaunchExeName));
    }

    public bool HasGame => GameLocator.IsValidGameDir(GameDir);
    public string? Status { get => _status; set => Set(ref _status, value); }
    public string Search { get => _search ?? ""; set { if (Set(ref _search, value)) ModsView.Refresh(); } }
    public bool IsBusy { get => _busy; set { Set(ref _busy, value); Raise(nameof(IsIdle)); } }
    public bool IsIdle => !_busy;
    public string RedirectorLevel { get => _rdLevel; set => Set(ref _rdLevel, value); } // ok | warn | bad
    public string RedirectorMessage { get => _rdMessage; set => Set(ref _rdMessage, value); }
    public bool RedirectorOk { get => _rdOk; set { Set(ref _rdOk, value); Raise(nameof(CanInstallRedirector)); } }
    public bool CanInstallRedirector => HasGame && !_rdOk;
    public ModItemVM? Selected { get => _selected; set => Set(ref _selected, value); }

    public async Task InitAsync()
    {
        if (!HasGame) { var found = GameLocator.Find(); if (found != null) GameDir = found; }
        Reload();
        await CheckRedirectorAsync();
        var bad = Installer.Verify(Mods.Select(m => m.Entry));
        if (bad.Count > 0) Status = Loc.T("BrokenMods", string.Join(", ", bad));
    }

    public async Task SwitchLanguageAsync()
    {
        Loc.Instance.Language = Loc.Instance.Language == "ru" ? "en" : "ru";
        Config.Language = Loc.Instance.Language;
        Config.LanguageChosen = true;
        SaveConfig();
        Status = Loc.T("Ready");
        await CheckRedirectorAsync(); // обновляет локализованный текст статуса FileRedirector
    }

    public void Reload()
    {
        var selId = Selected?.Id;
        Mods.Clear();
        foreach (var e in Library.LoadAll()) Mods.Add(new ModItemVM(e, Installer.IsEnabled(e.Id)));
        Selected = Mods.FirstOrDefault(m => m.Id == selId);
    }

    public async Task CheckRedirectorAsync(bool refresh = false)
    {
        Raise(nameof(HasGame));
        if (!HasGame)
        {
            RedirectorLevel = "bad"; RedirectorMessage = Loc.T("RdNoGame"); RedirectorOk = false; return;
        }
        RedirectorLevel = "warn"; RedirectorMessage = Loc.T("RdChecking");
        try
        {
            var st = await Redirector.CheckAsync(GameDir!, refresh);
            RedirectorOk = st.IsOk;
            RedirectorLevel = st.State == RedirectorState.Installed ? "ok" : st.IsOk ? "warn" : "bad";
            RedirectorMessage = st.State switch
            {
                RedirectorState.Installed => Loc.T("RdInstalled"),
                RedirectorState.ProbablyInstalled => Loc.T("RdProbably"),
                _ => st.MissingFiles.Count > 0 ? Loc.T("RdMissing", st.MissingFiles.Count) : Loc.T("RdNotFound"),
            };
        }
        catch (Exception ex) { RedirectorOk = false; RedirectorLevel = "bad"; RedirectorMessage = Loc.T("RdError", ex.Message); }
    }

    public async Task InstallRedirectorAsync()
    {
        await RunBusy(Loc.T("RdInstalling"), async () =>
        {
            await Redirector.InstallAsync(GameDir!);
            await CheckRedirectorAsync();
        }, Loc.T("RdInstalledDone"));
    }

    public async Task RunBusy(string message, Func<Task> work, string? doneMessage = null)
    {
        if (IsBusy) return;
        IsBusy = true; Status = message;
        try { await work(); Status = doneMessage ?? Loc.T("Done"); }
        catch (Exception ex) { Status = Loc.T("ErrorStatus", ex.Message); System.Windows.MessageBox.Show(ex.Message, Loc.T("Error"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
        finally { IsBusy = false; }
    }

    public async Task<EnableResult> SetEnabledAsync(ModItemVM vm, bool enable, bool overrideConflicts = false)
    {
        var result = new EnableResult();
        await RunBusy(Loc.T(enable ? "Enabling" : "Disabling", vm.Name), () => Task.Run(() =>
        {
            if (enable) result = Installer.Enable(vm.Entry, overrideConflicts); else Installer.Disable(vm.Id);
        }));
        vm.IsEnabled = Installer.IsEnabled(vm.Id);
        if (!IsBusy && result.Ok) Status = Loc.T(enable ? "Enabled" : "Disabled", vm.Name);
        return result;
    }

    public async Task DisableAllAsync()
    {
        await RunBusy(Loc.T("DisablingAll"), () => Task.Run(Installer.DisableAll), Loc.T("AllDisabled"));
        foreach (var m in Mods) m.IsEnabled = Installer.IsEnabled(m.Id);
    }

    /// <summary>Импорт в фоне; коллекция Mods обновляется уже в UI-потоке.</summary>
    public async Task<ImportResult> ImportAsync(string path, bool replace)
    {
        var r = await Task.Run(() =>
        {
            if (replace) { var id = Library.PeekId(path); if (id != null && Installer.IsEnabled(id)) Installer.Disable(id); }
            return Library.Import(path, replace);
        });
        Reload();
        Selected = Mods.FirstOrDefault(m => m.Id == r.Entry.Id);
        return r;
    }

    public void Delete(ModItemVM vm)
    {
        if (Installer.IsEnabled(vm.Id)) Installer.Disable(vm.Id);
        Library.Remove(vm.Id);
        Reload();
    }
}
