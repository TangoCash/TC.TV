using System.IO;

namespace TCTV.Core;

/// <summary>
/// Verzeichnisstruktur. Im Gegensatz zum Original schreibt die Portierung nichts
/// mehr neben die EXE, sondern in das Benutzerprofil:
///
///   %APPDATA%\TC.TV\TC.TV_Workdir\TC.TV.ini       (allgemeine Einstellungen)
///   %APPDATA%\TC.TV\TC.TV_Workdir\Box100.conf3     (Empfängerprofile)
///   %APPDATA%\TC.TV\TC.TV_Workdir\Logos\           (Senderlogos)
///   %APPDATA%\TC.TV\TC.TV_Workdir\VLCPortable\
///   %APPDATA%\TC.TV\TC.TV_Workdir\Temp\            (Arbeitsdateien, wird beim Start geleert)
///
/// Die Wurzel lässt sich verschieben - für portable Installationen und Tests -
/// über den Schalter <c>--datadir &lt;Ordner&gt;</c> oder die Umgebungsvariable
/// <c>TCTV_DATADIR</c>. Der Schalter hat Vorrang vor der Umgebungsvariablen;
/// die vor der Umbenennung verwendete Variable <c>STV_DATADIR</c> gilt weiter.
/// </summary>
public static class AppPaths
{
    /// <summary>Umgebungsvariable, die die Datenwurzel verschiebt.</summary>
    public const string DataRootEnvironmentVariable = "TCTV_DATADIR";

    private const string WorkDirName = "TC.TV_Workdir";
    private const string TempDirName = "Temp";
    private const string LogosDirName = "Logos";
    private const string IniFileName = "TC.TV.ini";
    private const string VlcPortableDirName = "VLCPortable";

    /// <summary>Name der Umgebungsvariablen vor der Umbenennung (wird weiter akzeptiert).</summary>
    private const string LegacyDataRootEnvironmentVariable = "STV_DATADIR";

    /// <summary>Arbeitsordner der früheren Fassung "S.TV" (wird bei der Übernahme erkannt).</summary>
    private static readonly string[] LegacyWorkDirNames = { "S.TV_Workdir" };

    /// <summary>Datenwurzel der früheren Fassung "S.TV" im Benutzerprofil.</summary>
    private static readonly string[] LegacyDataRootNames = { "S.TV" };

    /// <summary>INI-Name der früheren Fassung "S.TV" (wird bei der Übernahme erkannt).</summary>
    private static readonly string[] LegacyIniFileNames = { "S.TV.ini" };

    /// <summary>Ordner der EXE - nur noch für die Übernahme alter Datenbestände.</summary>
    public static string ProgramDir { get; } = ResolveProgramDir();

    /// <summary>Pendant zu AutoIts @AppDataDir (Roaming).</summary>
    public static string AppDataDir { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Vorgabe für die Datenwurzel: %APPDATA%\TC.TV.</summary>
    public static string DefaultDataRoot => Path.Combine(AppDataDir, "TC.TV");

    /// <summary>
    /// Wurzel der Benutzerdaten. Vor Initialize() gilt bereits die Vorgabe,
    /// damit die Reihenfolge der statischen Initialisierung keine Rolle spielt.
    /// </summary>
    public static string DataRoot => _dataRoot ?? DefaultDataRoot;

    public static string WorkDir { get; private set; } = string.Empty;

    public static string TempDir { get; private set; } = string.Empty;

    public static string LogosDir { get; private set; } = string.Empty;

    public static string IniPath { get; private set; } = string.Empty;

    public static string VlcPortableDir { get; private set; } = string.Empty;

    /// <summary>true, wenn die Wurzel per --datadir/TCTV_DATADIR verschoben wurde.</summary>
    public static bool IsCustomDataRoot { get; private set; }

    /// <summary>
    /// Ordner, aus dem beim ersten Start ein alter Datenbestand neben der EXE
    /// übernommen wurde (sonst null).
    /// </summary>
    public static string? MigratedFrom { get; private set; }

    private static string? _dataRoot;

    /// <summary>Legt die Arbeitsordner an. Entspricht _xl()/DirCreate() des Originals.</summary>
    /// <param name="dataRoot">Ziel aus dem Schalter <c>--datadir</c> (optional).</param>
    /// <returns>false, wenn das Arbeitsverzeichnis nicht erstellt werden konnte.</returns>
    public static bool Initialize(string? dataRoot = null)
    {
        string? requested = string.IsNullOrWhiteSpace(dataRoot)
            ? Environment.GetEnvironmentVariable(DataRootEnvironmentVariable) ??
              Environment.GetEnvironmentVariable(LegacyDataRootEnvironmentVariable)
            : dataRoot;

        IsCustomDataRoot = !string.IsNullOrWhiteSpace(requested);
        _dataRoot = IsCustomDataRoot
            ? Path.GetFullPath(requested!.Trim().Trim('"'))
            : DefaultDataRoot;

        WorkDir = Path.Combine(DataRoot, WorkDirName);
        TempDir = Path.Combine(WorkDir, TempDirName);
        LogosDir = Path.Combine(WorkDir, LogosDirName);
        IniPath = Path.Combine(WorkDir, IniFileName);
        VlcPortableDir = Path.Combine(WorkDir, VlcPortableDirName);

        try
        {
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, recursive: true);
            }

            Directory.CreateDirectory(LogosDir);
            Directory.CreateDirectory(TempDir);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        // Eine verschobene Wurzel (portabel/Test) bleibt unangetastet.
        if (!IsCustomDataRoot)
        {
            MigrateLegacyWorkDir();
        }

        return true;
    }

    public static string Logos(string fileName) => Path.Combine(LogosDir, fileName);

    /// <summary>
    /// Ordner der EXE. Bei einer Einzeldatei-EXE liefert ProcessPath immer den
    /// echten Ablageort (auch wenn die Laufzeit sonst entpacken würde).
    /// </summary>
    private static string ResolveProgramDir()
    {
        string? path = Environment.ProcessPath;
        string? directory = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);

        return string.IsNullOrEmpty(directory)
            ? AppContext.BaseDirectory.TrimEnd('\\')
            : directory.TrimEnd('\\');
    }

    /// <summary>
    /// Übernimmt einmalig das Arbeitsverzeichnis, das ältere Versionen noch neben
    /// der EXE angelegt haben (TC.TV_Workdir bzw. S.TV_Workdir der früheren
    /// Fassung). Bereits vorhandene eigene Daten werden nie überschrieben, Temp
    /// bleibt aussen vor.
    /// </summary>
    private static void MigrateLegacyWorkDir()
    {
        try
        {
            if (File.Exists(IniPath))
            {
                return;
            }

            foreach (string candidate in CandidateWorkDirs())
            {
                string legacy = candidate;
                if (string.Equals(legacy, WorkDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? legacyIni = FindIni(legacy, includeLegacyNames: true);
                if (legacyIni is null)
                {
                    continue;
                }

                CopyDirectory(legacy, WorkDir);

                // Eine noch "S.TV.ini" heissende Datei auf den neuen Namen umziehen.
                string copied = Path.Combine(WorkDir, Path.GetFileName(legacyIni));
                if (!string.Equals(copied, IniPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(copied) &&
                    !File.Exists(IniPath))
                {
                    File.Move(copied, IniPath);
                }

                MigratedFrom = legacy;
                return;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Mögliche Ablageorte eines übernehmbaren Arbeitsordners – aktueller Name
    /// neben der EXE zuerst, danach die Ablage der früheren Fassung "S.TV"
    /// (neben der EXE und in deren eigenem Ordner unter %APPDATA%).
    /// </summary>
    private static IEnumerable<string> CandidateWorkDirs()
    {
        yield return Path.Combine(ProgramDir, WorkDirName);

        foreach (string name in LegacyWorkDirNames)
        {
            yield return Path.Combine(ProgramDir, name);
        }

        foreach (string root in LegacyDataRootNames)
        {
            string legacyRoot = Path.Combine(AppDataDir, root);
            yield return Path.Combine(legacyRoot, WorkDirName);

            foreach (string name in LegacyWorkDirNames)
            {
                yield return Path.Combine(legacyRoot, name);
            }
        }
    }

    /// <summary>Liefert den Pfad der INI in <paramref name="directory"/> oder null.</summary>
    private static string? FindIni(string directory, bool includeLegacyNames)
    {
        string current = Path.Combine(directory, IniFileName);
        if (File.Exists(current))
        {
            return current;
        }

        if (!includeLegacyNames)
        {
            return null;
        }

        foreach (string name in LegacyIniFileNames)
        {
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            try
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            if (string.Equals(Path.GetFileName(directory), TempDirName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
