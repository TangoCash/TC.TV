using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Fenster-Einstellungen / Einblende-Optionen und VLC-Einstellungen.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSession _session = App.Session;
    private List<string> _languages = new();

    public SettingsWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        LoadValues();
    }

    private void ApplyLocalization()
    {
        Title = Localization.T("settings");
        HeaderGeneral.Text = Localization.T("general_settings");
        HeaderVlc.Text = Localization.T("vlc_settings");
        LabelLanguage.Text = Localization.T("language");
        LabelFadeDelay.Text = Localization.T("fade_delay");
        FadeAutoRefreshBox.Content = Localization.T("epg_auto_update");
        FadeAutoRefreshBox.ToolTip = Localization.T("epg_auto_update_tooltip");
        FirstScreenBox.Content = Localization.T("first_screen");
        LeftEdgeBox.Content = Localization.T("left_edge");
        RecursiveBox.Content = Localization.T("incl_subfolders");
        TransponderBox.Content = Localization.T("mark_transponder_channels");
        FullscreenStreamsBox.Content = Localization.T("streams_fullscreen");
        CloseVlcBox.Content = Localization.T("close_vlc_with_app");
        SaveButton.Content = Localization.T("save");
        CancelButton.Content = Localization.T("cancel");
        LabelVlcPath.Text = "VLC Player";
        LabelTheme.Text = Localization.T("theme");
        ApplyThemeNames();

        VideoOnTopBox.Content = Localization.T("vlc_on_top");
        NotificationBox.Content = Localization.T("vlc_notification");
        FixedSizeBox.Content = Localization.T("vlc_fixed_size");
        MinimizedBox.Content = Localization.T("vlc_minimized");
        TitleShowBox.Content = Localization.T("vlc_no_title");
        MinimalViewBox.Content = Localization.T("vlc_minimal_view");
        DeinterlaceBox.Content = Localization.T("vlc_deinterlace");
        LegacyVlcBox.Content = Localization.T("vlc_legacy");
        KeepVlcBox.Content = Localization.T("vlc_keep_on_zap");

        UpdateCheckBox.Content = Localization.T("update_check");
        UpdateCheckBox.ToolTip = Localization.T("update_check_tooltip");
        CheckUpdatesButton.Content = Localization.T("check_now");
        VersionText.Text = string.Format(Localization.T("version"), UpdateChecker.Display(UpdateChecker.CurrentVersion));
    }

    /// <summary>Reihenfolge der Themen im Auswahlfeld (Dark orange ist Standard).</summary>
    private static readonly string[] ThemeValues = { "3", "2", "4", "1" };

    /// <summary>Übersetzte Beschriftungen der Themen (gleiche Reihenfolge wie ThemeValues).</summary>
    private static readonly string[] ThemeKeys = { "theme_dark_orange", "theme_dark_green", "theme_light", "theme_overlay" };

    /// <summary>Setzt die Beschriftungen der Themen im Auswahlfeld.</summary>
    private void ApplyThemeNames()
    {
        for (int i = 0; i < ThemeCombo.Items.Count && i < ThemeKeys.Length; i++)
        {
            if (ThemeCombo.Items[i] is ComboBoxItem item)
            {
                item.Content = Localization.T(ThemeKeys[i]);
            }
        }
    }

    private void LoadValues()
    {
        AppSettings settings = _session.Settings;
        VlcOptions vlc = _session.VlcOptions;

        _languages = Localization.AvailableLanguages.ToList();
        LanguageCombo.ItemsSource = _languages.Select(DisplayLanguage).ToList();
        int languageIndex = _languages.FindIndex(name => string.Equals(name, settings.Language, StringComparison.OrdinalIgnoreCase));
        LanguageCombo.SelectedIndex = languageIndex >= 0 ? languageIndex : 0;
        int themeIndex = Array.IndexOf(ThemeValues, (settings.ThemeValue ?? string.Empty).Trim());
        ThemeCombo.SelectedIndex = themeIndex < 0 ? 0 : themeIndex;
        VlcPathBox.Text = _session.VlcPath;
        FadeDelayBox.Text = settings.FadeInDelayMs.ToString();

        FadeAutoRefreshBox.IsChecked = settings.FadeInAutoRefresh;
        FirstScreenBox.IsChecked = settings.FadeInFirstScreen;
        LeftEdgeBox.IsChecked = !settings.RightEdge;
        RecursiveBox.IsChecked = settings.RecursiveVideoDisplay;
        TransponderBox.IsChecked = settings.MarkTransponderChannels;
        FullscreenStreamsBox.IsChecked = settings.VideoMode;
        UpdateCheckBox.IsChecked = settings.UpdateCheck;

        VideoOnTopBox.IsChecked = vlc.VideoOnTop;
        NotificationBox.IsChecked = vlc.SuppressNotification;
        FixedSizeBox.IsChecked = vlc.FixedWindowSize;
        MinimizedBox.IsChecked = vlc.StartMinimized;
        TitleShowBox.IsChecked = vlc.NoVideoTitleShow;
        MinimalViewBox.IsChecked = vlc.MinimalView;
        DeinterlaceBox.IsChecked = vlc.Deinterlace;
        LegacyVlcBox.IsChecked = vlc.LegacyVlc;
        KeepVlcBox.IsChecked = vlc.KeepVlcOnZap;
        CloseVlcBox.IsChecked = vlc.CloseVlcWithApp;
    }

    private void OnBrowseVlc(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "VLC Player (vlc.exe)|vlc.exe|Programme (*.exe)|*.exe",
            Title = Localization.T("set_vlc_manually"),
        };

        if (dialog.ShowDialog(this) == true)
        {
            VlcPathBox.Text = dialog.FileName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        AppSettings settings = _session.Settings;
        VlcOptions vlc = _session.VlcOptions;

        int languageIndex = Math.Clamp(LanguageCombo.SelectedIndex, 0, _languages.Count - 1);
        settings.Language = _languages.Count == 0 ? "deutsch" : _languages[languageIndex];
        settings.ThemeValue = ThemeValues[Math.Clamp(ThemeCombo.SelectedIndex, 0, ThemeValues.Length - 1)];
        settings.FadeInDelayMs = int.TryParse(FadeDelayBox.Text, out int delay) ? delay : 0;

        settings.FadeInAutoRefresh = FadeAutoRefreshBox.IsChecked == true;
        settings.FadeInFirstScreen = FirstScreenBox.IsChecked == true;
        settings.RightEdge = LeftEdgeBox.IsChecked != true;
        settings.RecursiveVideoDisplay = RecursiveBox.IsChecked == true;
        settings.MarkTransponderChannels = TransponderBox.IsChecked == true;
        settings.VideoMode = FullscreenStreamsBox.IsChecked == true;
        settings.UpdateCheck = UpdateCheckBox.IsChecked == true;

        vlc.VideoOnTop = VideoOnTopBox.IsChecked == true;
        vlc.SuppressNotification = NotificationBox.IsChecked == true;
        vlc.FixedWindowSize = FixedSizeBox.IsChecked == true;
        vlc.StartMinimized = MinimizedBox.IsChecked == true;
        vlc.NoVideoTitleShow = TitleShowBox.IsChecked == true;
        vlc.MinimalView = MinimalViewBox.IsChecked == true;
        vlc.Deinterlace = DeinterlaceBox.IsChecked == true;
        vlc.LegacyVlc = LegacyVlcBox.IsChecked == true;
        vlc.KeepVlcOnZap = KeepVlcBox.IsChecked == true;
        vlc.CloseVlcWithApp = CloseVlcBox.IsChecked == true;

        settings.Save(_session.Ini);
        vlc.SaveTo(_session.Ini);
        _session.Ini.Write("Benutzerdaten", "VLC Pfad", VlcPathBox.Text.Trim());
        _session.VlcPath = VlcPathBox.Text.Trim();
        _session.Ini.Save();

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Anzeigename einer Sprache.</summary>
    private static string DisplayLanguage(string name)
        => Localization.DisplayName(name);

    /// <summary>Prüft manuell auf Updates und zeigt das Ergebnis.</summary>
    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateInfo? update;
        try
        {
            update = await UpdateChecker.CheckAsync();
        }
        catch (Exception)
        {
            update = null;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }

        if (update is null)
        {
            MessageBox.Show(this, Localization.T("update_failed"), Localization.T("update_error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (update.IsNewer)
        {
            await App.NotifyUpdateAsync(update, this);
        }
        else
        {
            MessageBox.Show(this, Localization.T("no_newer_version"), Localization.T("update_available"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Nur für den Selbsttest: Beschriftung und Tooltip der Option
    /// „EPG-Auto-Aktualisierung“ sowie ihr Zustand.
    /// </summary>
    internal (string Label, string Tip, bool Checked) AutoRefreshInfoForSelfTest
        => (FadeAutoRefreshBox.Content as string ?? string.Empty,
            FadeAutoRefreshBox.ToolTip as string ?? string.Empty,
            FadeAutoRefreshBox.IsChecked == true);
}
