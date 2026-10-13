using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using SDModLoader.Core;

namespace SDModLoader.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) => { await _vm.InitAsync(); ApplyGameIcon(); };
    }

    /// <summary>Иконка окна/панели задач — из установленной игры (сама иконка в программу не вшита).</summary>
    void ApplyGameIcon()
    {
        if (!_vm.HasGame) return;
        var exe = _vm.LaunchExePath;
        if (exe != null && IconHelper.FromExe(exe) is { } icon) Icon = icon;
    }

    // ---------- Язык ----------
    async void OnSwitchLanguage(object s, RoutedEventArgs e) => await _vm.SwitchLanguageAsync();

    // ---------- Игра и FileRedirector ----------
    void OnBrowseGame(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("PickFolderTitle") };
        if (dlg.ShowDialog(this) == true) _ = SetGameDirAsync(dlg.FolderName);
    }

    void OnAutoDetect(object s, RoutedEventArgs e)
    {
        var p = GameLocator.Find();
        if (p == null) { MessageBox.Show(Loc.T("SteamNotFound"), Loc.T("NotFoundTitle")); return; }
        _ = SetGameDirAsync(p);
    }

    async void OnGameDirEdited(object s, RoutedEventArgs e) { await _vm.CheckRedirectorAsync(); ApplyGameIcon(); }

    async Task SetGameDirAsync(string path)
    {
        if (!GameLocator.IsValidGameDir(path))
        {
            MessageBox.Show(Loc.T("BadFolderText"), Loc.T("BadFolderTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _vm.GameDir = path;
        await _vm.CheckRedirectorAsync();
        ApplyGameIcon();
    }

    async void OnInstallRedirector(object s, RoutedEventArgs e)
    {
        var ok = MessageBox.Show(Loc.T("RdInstallText", RedirectorService.RepoUrl),
            Loc.T("RdInstallTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok == MessageBoxResult.Yes) await _vm.InstallRedirectorAsync();
    }

    async void OnRecheck(object s, RoutedEventArgs e) => await _vm.CheckRedirectorAsync(refresh: true);
    void OnOpenRedirectorRepo(object s, RoutedEventArgs e) => Open(RedirectorService.RepoUrl);

    // ---------- Запуск игры ----------
    void OnPlay(object s, RoutedEventArgs e)
    {
        if (!_vm.HasGame) { MessageBox.Show(Loc.T("NoGameText"), Loc.T("NoGameTitle")); return; }
        if (!_vm.RedirectorOk &&
            MessageBox.Show(Loc.T("RdWarnLaunch"), "FileRedirector", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var exe = _vm.LaunchExePath;
        if (exe == null)
        {
            var dlg = new OpenFileDialog { Title = Loc.T("PickExeTitle"), Filter = Loc.T("ExeFilter"), InitialDirectory = _vm.GameDir };
            if (dlg.ShowDialog(this) != true) return;
            exe = dlg.FileName;
            _vm.SetLaunchExe(exe);
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
            _vm.Status = Loc.T("Launched");
        }
        catch (Exception ex)
        {
            _vm.SetLaunchExe(null); // при следующем запуске определим заново
            MessageBox.Show(ex.Message, Loc.T("LaunchFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Стрелка рядом с «Запустить»: выбор exe (например, sdhdship_debug.exe для разработчиков модов).</summary>
    void OnPlayMenu(object s, RoutedEventArgs e)
    {
        if (!_vm.HasGame) return;
        var current = _vm.LaunchExePath;
        var explicitPick = _vm.Config.GameExe != null;
        var menu = new ContextMenu { PlacementTarget = (UIElement)s, Placement = PlacementMode.Top };

        var auto = new MenuItem { Header = Loc.T("ExeAuto"), IsCheckable = true, IsChecked = !explicitPick };
        auto.Click += (_, _) => _vm.SetLaunchExe(null);
        menu.Items.Add(auto);
        menu.Items.Add(new Separator());

        foreach (var exe in GameLocator.ListExecutables(_vm.GameDir!))
        {
            var path = exe;
            var item = new MenuItem
            {
                Header = Path.GetFileName(path),
                IsCheckable = true,
                IsChecked = explicitPick && string.Equals(current, path, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => _vm.SetLaunchExe(path);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var other = new MenuItem { Header = Loc.T("ExeBrowse") };
        other.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Title = Loc.T("PickExeTitle"), Filter = Loc.T("ExeFilter"), InitialDirectory = _vm.GameDir };
            if (dlg.ShowDialog(this) == true) _vm.SetLaunchExe(dlg.FileName);
        };
        menu.Items.Add(other);
        menu.IsOpen = true;
    }

    // ---------- Моды ----------
    async void OnToggleClick(object s, RoutedEventArgs e)
    {
        var vm = (ModItemVM)((CheckBox)s).DataContext;
        var want = vm.IsEnabled; // TwoWay-привязка уже применила клик
        if (!_vm.HasGame)
        {
            MessageBox.Show(Loc.T("NoGameText"), Loc.T("NoGameTitle"));
            vm.IsEnabled = _vm.Installer.IsEnabled(vm.Id);
            return;
        }
        if (want && !_vm.RedirectorOk &&
            MessageBox.Show(Loc.T("RdWarnEnable"), "FileRedirector", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            vm.IsEnabled = false;
            return;
        }

        var r = await _vm.SetEnabledAsync(vm, want);
        if (!r.Ok)
        {
            var list = string.Join("\n", r.Conflicts.Take(8).Select(c => $"  {c.RelPath}  ←  {c.OwnerModId}"));
            var more = r.Conflicts.Count > 8 ? Loc.T("ConflictMore", r.Conflicts.Count - 8) : "";
            if (MessageBox.Show(Loc.T("ConflictText", vm.Name, list, more), Loc.T("ConflictTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                await _vm.SetEnabledAsync(vm, true, overrideConflicts: true);
        }
    }

    async void OnAddMod(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.T("PickMod"), Multiselect = true, Filter = Loc.T("ModFilter") };
        if (dlg.ShowDialog(this) == true) await ImportAsync(dlg.FileNames);
    }

    async void OnDrop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await ImportAsync(files.Where(f => f.EndsWith(".sdmod", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));
    }

    async Task ImportAsync(IEnumerable<string> files)
    {
        foreach (var f in files)
        {
            try
            {
                ImportResult r;
                try { r = await _vm.ImportAsync(f, replace: false); }
                catch (ModExistsException ex)
                {
                    if (MessageBox.Show(Loc.T("ExistsText", ex.Message), Loc.T("ExistsTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) continue;
                    r = await _vm.ImportAsync(f, replace: true);
                }
                _vm.Status = Loc.T("Added", r.Entry.Manifest.Name);
                if (r.Warnings.Count > 0)
                    MessageBox.Show(string.Join("\n• ", r.Warnings.Prepend(Loc.T("Warning"))), r.Entry.Manifest.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{Path.GetFileName(f)}:\n{ex.Message}", Loc.T("ImportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    void OnCreateMod(object s, RoutedEventArgs e) => new CreateModWindow { Owner = this }.ShowDialog();

    void OnExport(object s, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } m) return;
        var dlg = new SaveFileDialog { FileName = m.Id + ModPackage.Extension, Filter = Loc.T("SdmodFilter") };
        if (dlg.ShowDialog(this) != true) return;
        try { _vm.Library.Export(m.Id, dlg.FileName); _vm.Status = Loc.T("Exported", dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    void OnDelete(object s, RoutedEventArgs e) => DeleteMod(_vm.Selected);

    // Правый клик по моду: выбираем его и показываем контекстное меню
    void OnItemRightClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is ModItemVM vm) _vm.Selected = vm;
    }

    void OnDeleteMenu(object s, RoutedEventArgs e) =>
        DeleteMod((s as FrameworkElement)?.DataContext as ModItemVM ?? _vm.Selected);

    void OnOpenModFolder(object s, RoutedEventArgs e)
    {
        if (((s as FrameworkElement)?.DataContext as ModItemVM ?? _vm.Selected) is { } m) Open(m.Entry.Directory);
    }

    void DeleteMod(ModItemVM? m)
    {
        if (m == null) return;
        if (MessageBox.Show(Loc.T("DeleteText", m.Name), Loc.T("DeleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { _vm.Delete(m); _vm.Status = Loc.T("Deleted", m.Name); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    async void OnDisableAll(object s, RoutedEventArgs e) => await _vm.DisableAllAsync();
    void OnOpenGame(object s, RoutedEventArgs e) { if (_vm.HasGame) Open(_vm.GameDir!); }
    void OnOpenMods(object s, RoutedEventArgs e) => Open(_vm.Paths.ModsDir);
    static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
