using System.IO;

namespace TCTV.Core;

/// <summary>
/// Zentrale Laufzeitdaten des Programms (Ini, Einstellungen, Profile, Client, VLC).
/// Ersetzt die vielen globalen Variablen des AutoIt-Skripts.
/// </summary>
public sealed class AppSession : IDisposable
{
    private AppSession(IniFile ini, AppSettings settings, ProfileStore profiles, Profile profile)
    {
        Ini = ini;
        Settings = settings;
        Profiles = profiles;
        Profile = profile;
        Client = new RecordingServiceClient(profile);
        VlcOptions = VlcOptions.FromIni(ini);
    }

    public IniFile Ini { get; }

    public AppSettings Settings { get; }

    public ProfileStore Profiles { get; }

    public Profile Profile { get; private set; }

    public RecordingServiceClient Client { get; private set; }

    public VlcOptions VlcOptions { get; private set; }

    public Theme Theme => Settings.Theme;

    public string VlcPath { get; set; } = string.Empty;

    public bool IsVlcAvailable => !string.IsNullOrEmpty(VlcPath) && File.Exists(VlcPath);

    public static AppSession Create()
    {
        IniFile ini = new(AppPaths.IniPath);
        AppSettings.EnsureDefaults(ini);
        AppSettings settings = AppSettings.Load(ini);
        ProfileStore profiles = new(AppPaths.WorkDir);
        string profileName = profiles.EnsureCurrent(ini, settings);
        Profile profile = profiles.Load(profileName);
        ini.Save();

        Localization.Current = settings.Language;

        AppSession session = new(ini, settings, profiles, profile)
        {
            VlcPath = VlcLauncher.ResolvePath(ini),
        };
        ini.Save();
        return session;
    }

    /// <summary>Nach einem Profilwechsel oder geänderten Profileinstellungen aufrufen.</summary>
    public void ReloadProfile()
    {
        string name = (Settings.ProfileFileName ?? string.Empty).Trim();
        if (name.EndsWith(ProfileStore.Extension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^ProfileStore.Extension.Length];
        }

        if (name.Length == 0)
        {
            name = Profiles.EnsureCurrent(Ini, Settings);
        }

        Profile = Profiles.Load(name);
        Client.Dispose();
        Client = new RecordingServiceClient(Profile);
    }

    /// <summary>Sprache/Aussehen neu einlesen (nach Änderungen in den Einstellungen).</summary>
    public void ReloadSettings()
    {
        AppSettings loaded = AppSettings.Load(Ini);
        Settings.Language = loaded.Language;
        Settings.ThemeValue = loaded.ThemeValue;
        Settings.RecursiveVideoDisplay = loaded.RecursiveVideoDisplay;
        Settings.FadeInDelayMs = loaded.FadeInDelayMs;
        Settings.FadeInAutoRefresh = loaded.FadeInAutoRefresh;
        Settings.FadeInFirstScreen = loaded.FadeInFirstScreen;
        Settings.RightEdge = loaded.RightEdge;
        Settings.MarkTransponderChannels = loaded.MarkTransponderChannels;
        Settings.VideoMode = loaded.VideoMode;
        Settings.VideoMainDirectories = loaded.VideoMainDirectories;
        Settings.UpdateCheck = loaded.UpdateCheck;
        Settings.AdvancedSettings = loaded.AdvancedSettings;
        Settings.LastBouquetNumber = loaded.LastBouquetNumber;
        Settings.VlcPath = loaded.VlcPath;
        Localization.Current = Settings.Language;
        VlcOptions = VlcOptions.FromIni(Ini);
    }

    /// <summary>Ermittelt den Logopfad eines Senders (kurze ID + ".png").</summary>
    public string GetLogoPath(Channel channel)
        => string.IsNullOrEmpty(channel.ShortId) ? string.Empty : AppPaths.Logos(channel.ShortId + ".png");

    public void Dispose() => Client.Dispose();
}
