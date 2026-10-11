using System.Windows;
using Microsoft.Win32;
using TCTV.Core;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Fenster-Einstellungen / Einblende-Optionen und VLC-Einstellungen.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSession _session = App.Session;

    public SettingsWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        LoadValues();
    }

    private void ApplyLocalization()
    {
        Title = Localization.T(0);
        HeaderGeneral.Text = Localization.T(113);
        HeaderVlc.Text = Localization.T(127);
        LabelLanguage.Text = Localization.T(120);
        LabelFadeDelay.Text = Localization.T(301);
        FadeAutoRefreshBox.Content = Localization.T(50);
        FadeAutoRefreshBox.ToolTip = Localization.T(51);
        FirstScreenBox.Content = Localization.T(308);
        LeftEdgeBox.Content = Localization.T(316);
        RecursiveBox.Content = Localization.T(320);
        TransponderBox.Content = Localization.T(33);
        FullscreenStreamsBox.Content = Localization.T(168);
        CloseVlcBox.Content = Localization.T(154);
        SaveButton.Content = Localization.T(302);
        CancelButton.Content = Localization.T(272);
        LabelVlcPath.Text = "VLC Player";
        UpdateCheckBox.Content = Localization.T(326);
        UpdateCheckBox.ToolTip = Localization.T(327);
        CheckUpdatesButton.Content = Localization.T(328);
    }

    /// <summary>Reihenfolge der Themen im Auswahlfeld (Dark orange ist Standard).</summary>
    private static readonly string[] ThemeValues = { "3", "2", "4", "1" };

    private void LoadValues()
    {
        AppSettings settings = _session.Settings;
        VlcOptions vlc = _session.VlcOptions;

        LanguageCombo.SelectedIndex = settings.Language == Core.Language.English ? 1 : 0;
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
            Title = Localization.T(102),
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

        settings.Language = LanguageCombo.SelectedIndex == 1 ? Core.Language.English : Core.Language.Deutsch;
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
            MessageBox.Show(this, Localization.T(234), Localization.T(233), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (update.IsNewer)
        {
            await App.NotifyUpdateAsync(update, this);
        }
        else
        {
            MessageBox.Show(this, Localization.T(329), Localization.T(324), MessageBoxButton.OK, MessageBoxImage.Information);
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
