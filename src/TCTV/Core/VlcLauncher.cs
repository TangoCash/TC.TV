using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TCTV.Core;

/// <summary>
/// Steuert den externen VLC-Player. Entspricht den ShellExecute-Aufrufen und
/// der Pfaderkennung (VLCPortable, Program Files, Registry) des Originals.
/// </summary>
public static class VlcLauncher
{
    private const int WmChar = 0x0102;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Ermittelt den VLC-Pfad in derselben Reihenfolge wie das Original und
    /// schreibt ihn bei Erfolg in die Ini ("Benutzerdaten"/"VLC Pfad").
    /// </summary>
    public static string ResolvePath(IniFile ini)
    {
        string configured = ini.Read("Benutzerdaten", "VLC Pfad", string.Empty).Trim('"');
        if (IsExecutable(configured))
        {
            return configured;
        }

        ini.DeleteKey("Benutzerdaten", "VLC Pfad");

        string portable = Path.Combine(AppPaths.VlcPortableDir, "VLCPortable.exe");
        if (IsExecutable(portable))
        {
            ini.Write("Benutzerdaten", "VLC Pfad", portable);
            return portable;
        }

        foreach (string candidate in new[]
                 {
                     @"C:\Program Files\VideoLAN\VLC\vlc.exe",
                     @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe",
                 })
        {
            if (IsExecutable(candidate))
            {
                ini.Write("Benutzerdaten", "VLC Pfad", candidate);
                return candidate;
            }
        }

        string? fromRegistry = FromRegistry(RegistryView.Registry64) ?? FromRegistry(RegistryView.Registry32);
        if (fromRegistry is not null)
        {
            ini.Write("Benutzerdaten", "VLC Pfad", fromRegistry);
            return fromRegistry;
        }

        return configured;
    }

    private static string? FromRegistry(RegistryView view)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = baseKey.OpenSubKey(@"Software\VideoLAN\VLC");
            string? dir = key?.GetValue("InstallDir") as string;
            if (string.IsNullOrWhiteSpace(dir))
            {
                return null;
            }

            string exe = Path.Combine(dir.Trim('"'), "vlc.exe");
            return IsExecutable(exe) ? exe : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static bool IsExecutable(string path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <summary>Zeigt einen Stream/eine Aufnahme in VLC an. Liefert false, wenn VLC nicht startbar ist.</summary>
    public static bool Play(string vlcPath, string arguments)
    {
        if (!IsExecutable(vlcPath))
        {
            return false;
        }

        try
        {
            ProcessStartInfo info = new()
            {
                FileName = vlcPath,
                Arguments = arguments,
                UseShellExecute = false,
            };

            Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>Beendet bzw. stoppt die laufende VLC-Wiedergabe (siehe _1aq im Original).</summary>
    public static void StopPlayback(VlcOptions options)
    {
        if (options.LegacyVlc)
        {
            ProcessClose("vlc");
            return;
        }

        IntPtr handle = MainWindowHandle("vlc");
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (options.KeepVlcOnZap)
        {
            // Taste "s" (Stop) an das VLC-Fenster senden.
            PostMessage(handle, WmChar, (IntPtr)'s', IntPtr.Zero);
        }
        else
        {
            ProcessClose("vlc");
        }
    }

    public static void Shutdown()
    {
        ProcessClose("vlc");
    }

    public static bool IsRunning() => Process.GetProcessesByName("vlc").Length > 0;

    private static IntPtr MainWindowHandle(string processName)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
            catch (InvalidOperationException)
            {
                // Prozess bereits beendet.
            }
        }

        return IntPtr.Zero;
    }

    private static void ProcessClose(string processName)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Prozess kann nicht beendet werden.
            }
        }
    }
}
