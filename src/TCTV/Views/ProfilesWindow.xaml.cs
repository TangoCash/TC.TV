using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Profilverwaltung: Neu, Bearbeiten, Löschen und Umschalten (".conf3").</summary>
public partial class ProfilesWindow : Window
{
    private readonly AppSession _session = App.Session;
    private bool _loading;

    public ProfilesWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        LoadProfiles();
    }

    private void ApplyLocalization()
    {
        Title = Localization.T("profiles");
        HeaderProfile.Text = Localization.T("profile_settings");
        LabelWebUser.Text = Localization.T("web_user");
        LabelWebPassword.Text = Localization.T("web_password");
        ExternalBox.Content = Localization.T("external");
        WebAuthBox.Content = Localization.T("web_auth");
        ForceSwitchBox.Content = Localization.T("force_switch");
        FavoritesBox.Content = Localization.T("show_only_favorites");
        MultiTunerBox.Content = Localization.T("multituner");
        NewButton.Content = Localization.T("new");
        DeleteButton.Content = Localization.T("delete");
        ApplyButton.Content = Localization.T("save");
        ActivateButton.Content = Localization.T("switch_profile");
        ExportButton.Content = Localization.T("export");
        ImportButton.Content = Localization.T("import");
        CloseButton.Content = Localization.T("back");

        LabelHost.Text = Localization.T("ip_dyndns");
        LabelWebPort.Text = Localization.T("web_port");
        LabelStreamPort.Text = Localization.T("streaming_port");

        LabelApi.Text = "API";
        ApiBox.ToolTip = Localization.T("api_tooltip");
        ApiBox.Items.Clear();
        ApiBox.Items.Add(BoxApis.DisplayName(BoxApi.Legacy));
        ApiBox.Items.Add(BoxApis.DisplayName(BoxApi.V1));
        ApiBox.SelectedIndex = 0;
    }

    private void LoadProfiles()
    {
        List<string> names = _session.Profiles.GetProfileNames();
        ProfileList.ItemsSource = names;

        string current = _session.Profile.Name;
        ProfileList.SelectedItem = names.Contains(current) ? current : names.FirstOrDefault();
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is not string name)
        {
            return;
        }

        _loading = true;
        Profile profile = _session.Profiles.Load(name);
        HostBox.Text = profile.Host;
        ExternalBox.IsChecked = profile.External;
        WebPortBox.Text = profile.WebPort;
        StreamPortBox.Text = profile.StreamingPort;
        WebAuthBox.IsChecked = profile.WebAuthentication;
        WebUserBox.Text = profile.WebUser;
        WebPasswordBox.Password = profile.WebPassword;
        ForceSwitchBox.IsChecked = profile.ForceSwitch;
        FavoritesBox.IsChecked = profile.FavoritesOnly;
        MultiTunerBox.IsChecked = profile.MultiTuner;
        ApiBox.SelectedIndex = profile.ApiVersion == BoxApi.V1 ? 1 : 0;
        _loading = false;
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        string? name = PromptWindow.Ask(this, Localization.T("new"), Localization.T("profile_name"), _session.Profiles.SuggestName());
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (File.Exists(_session.Profiles.GetPath(name)))
        {
            MessageBox.Show(Localization.T("error"), Localization.T("error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _session.Profiles.Create(name);
        LoadProfiles();
        ProfileList.SelectedItem = name;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not string name)
        {
            return;
        }

        if (string.Equals(name, _session.Profile.Name, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Localization.T("profile_in_use"), Localization.T("error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _session.Profiles.Delete(name);
        LoadProfiles();
    }

    /// <summary>
    /// "Speichern": Eingaben übernehmen, das Profil als aktuelles setzen und das
    /// Fenster schließen – das Hauptfenster lädt danach Profil und Senderliste neu.
    /// </summary>
    private void OnApply(object sender, RoutedEventArgs e) => ApplyAndActivate();

    /// <summary>"Profil wechseln": gleicher Ablauf (speichern und übernehmen).</summary>
    private void OnActivate(object sender, RoutedEventArgs e) => ApplyAndActivate();

    /// <summary>
    /// Übernimmt die Eingaben in die Profildatei, trägt das Profil als aktuelles in
    /// die INI ein (<c>Benutzerdaten\Profil</c>) und schließt das Fenster.
    ///
    /// Entspricht im Original dem Speichern-Zweig des Profilfensters
    /// (<c>IniWrite($em, "Benutzerdaten", "Profil", $nl)</c> sowie
    /// <c>_15t</c>/<c>GUIDelete($mi)</c> beim Klick auf „Speichern“).
    /// </summary>
    /// <returns>false, wenn nichts gespeichert werden konnte (kein Profil gewählt).</returns>
    internal bool ApplyAndActivate()
    {
        string? name = SaveCurrent();
        if (name is null)
        {
            return false;
        }

        _session.Settings.ProfileFileName = name + ProfileStore.Extension;
        _session.Ini.Write("Benutzerdaten", "Profil", _session.Settings.ProfileFileName);
        _session.Ini.Save();

        // Bei modalem Aufruf (ShowDialog) signalisiert DialogResult dem
        // Hauptfenster, dass Profil und Senderliste neu geladen werden müssen.
        if (ComponentDispatcher.IsThreadModal)
        {
            DialogResult = true;
        }
        else
        {
            Close();
        }

        return true;
    }

    /// <summary>Übernimmt die Eingaben in die Profildatei und liefert den Profilnamen.</summary>
    private string? SaveCurrent()
    {
        if (_loading || ProfileList.SelectedItem is not string name)
        {
            return null;
        }

        Profile profile = _session.Profiles.Load(name);
        profile.Host = HostBox.Text.Trim();
        profile.External = ExternalBox.IsChecked == true;
        profile.WebPort = WebPortBox.Text.Trim();
        profile.StreamingPort = StreamPortBox.Text.Trim();
        profile.WebAuthentication = WebAuthBox.IsChecked == true;
        profile.WebUser = WebUserBox.Text.Trim();
        profile.WebPassword = WebPasswordBox.Password;
        profile.ForceSwitch = ForceSwitchBox.IsChecked == true;
        profile.FavoritesOnly = FavoritesBox.IsChecked == true;
        profile.MultiTuner = MultiTunerBox.IsChecked == true;
        profile.ApiVersion = ApiBox.SelectedIndex == 1 ? BoxApi.V1 : BoxApi.Legacy;
        _session.Profiles.Save(profile);
        return name;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Exportiert das markierte Profil als .conf3-Datei.</summary>
    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not string name)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            FileName = name + ProfileStore.Extension,
            Filter = Localization.T("profile_filter") + "|*.conf3",
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                File.Copy(_session.Profiles.GetPath(name), dialog.FileName, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(ex.Message, Localization.T("error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>Importiert eine .conf3-Datei als Profil.</summary>
    private void OnImport(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = Localization.T("profile_filter") + "|*.conf3",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string name = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (name.Length == 0)
        {
            return;
        }

        try
        {
            File.Copy(dialog.FileName, _session.Profiles.GetPath(name), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, Localization.T("error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadProfiles();
    }

    /// <summary>
    /// Nur für den Selbsttest: stellt die API-Auswahl im Fenster ein (entspricht der
    /// Auswahl mit der Maus im Kombinationsfeld).
    /// </summary>
    internal void SelectApiForSelfTest(BoxApi api)
    {
        ApiBox.SelectedIndex = api == BoxApi.V1 ? 1 : 0;
    }
}
