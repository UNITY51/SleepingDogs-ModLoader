using System.Windows;
using Microsoft.Win32;
using SDModLoader.Core;

namespace SDModLoader.App;

public partial class CreateModWindow : Window
{
    bool _idEdited;

    public CreateModWindow()
    {
        InitializeComponent();
        IdBox.TextChanged += (_, _) => { if (IdBox.IsKeyboardFocused) _idEdited = true; };
    }

    void OnNameChanged(object s, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_idEdited) IdBox.Text = PathUtil.Slugify(NameBox.Text);
    }

    void OnBrowse(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("CM_PickFolder") };
        if (dlg.ShowDialog(this) == true) Source.Text = dlg.FolderName;
    }

    void OnCreate(object s, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(Source.Text)) throw new InvalidOperationException(Loc.T("CM_NoFolder"));
            if (string.IsNullOrWhiteSpace(NameBox.Text)) throw new InvalidOperationException(Loc.T("CM_NoName"));
            var id = IdBox.Text.Trim().ToLowerInvariant();
            if (!PathUtil.IsValidId(id)) throw new InvalidOperationException(Loc.T("CM_BadId"));

            var dlg = new SaveFileDialog { FileName = id + ModPackage.Extension, Filter = Loc.T("SdmodFilter") };
            if (dlg.ShowDialog(this) != true) return;

            ModPackage.Pack(Source.Text, new ModManifest
            {
                Id = id, Name = NameBox.Text.Trim(), Version = VersionBox.Text.Trim(),
                Author = AuthorBox.Text.Trim(), Description = DescBox.Text.Trim(),
            }, dlg.FileName);
            MessageBox.Show(Loc.T("CM_Done", dlg.FileName), Loc.T("CM_DoneTitle"));
            Close();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
