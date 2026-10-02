using System.IO;
using System.Text;

namespace TCTV.Core;

/// <summary>
/// INI-Datei mit denselben Semantiken wie AutoIts IniRead/IniWrite/IniDelete:
/// Abschnitts- und Schluesselnamen sind unabhängig von Gross-/Kleinschreibung,
/// fehlende Schluessel liefern den Standardwert, Schreibvorgaenge legen Datei,
/// Abschnitt und Schluessel bei Bedarf an. Inhalt wird so weit wie moeglich
/// unveraendert erhalten (Reihenfolge, Kommentare, unbekannte Zeilen).
/// </summary>
public sealed class IniFile
{
    private readonly List<string> _lines = new();
    private readonly string _path;

    public IniFile(string path)
    {
        _path = path;
        Load();
    }

    public string Path => _path;

    public void Load()
    {
        _lines.Clear();
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            // UTF-8 mit Latin-1-Rückfall: so bleiben Umlaute in Schlüsseln auch
            // dann erhalten, wenn die Datei von der AutoIt-Fassung (ANSI) stammt.
            _lines.AddRange(TextEncoding.ReadAllLinesOf(_path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fehlerhafte/unlesbare Datei => wie leere Datei behandeln.
        }
    }

    public void Save()
    {
        string? dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllLines(_path, _lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Liefert (Startzeile, Endzeile exklusiv) des Abschnitts oder (-1, -1).</summary>
    private (int Start, int End) FindSection(string section)
    {
        int start = -1;
        for (int i = 0; i < _lines.Count; i++)
        {
            string line = _lines[i].Trim();
            if (line.Length < 2 || line[0] != '[')
            {
                continue;
            }

            int close = line.IndexOf(']');
            if (close < 0)
            {
                continue;
            }

            string name = line.Substring(1, close - 1).Trim();
            if (start < 0)
            {
                if (string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                {
                    start = i;
                }
            }
            else
            {
                return (start, i);
            }
        }

        return start < 0 ? (-1, -1) : (start, _lines.Count);
    }

    private static (string Key, string Value) SplitKey(string line)
    {
        int eq = line.IndexOf('=');
        if (eq < 0)
        {
            return (line.Trim(), string.Empty);
        }

        return (line.Substring(0, eq).Trim(), line.Substring(eq + 1));
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }

    public string Read(string section, string key, string defaultValue = "")
    {
        (int start, int end) = FindSection(section);
        if (start < 0)
        {
            return defaultValue;
        }

        for (int i = start + 1; i < end; i++)
        {
            string line = _lines[i];
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#')
            {
                continue;
            }

            (string k, string v) = SplitKey(line);
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                return Unquote(v);
            }
        }

        return defaultValue;
    }

    public int ReadInt(string section, string key, int defaultValue = 0)
        => int.TryParse(Read(section, key, string.Empty), out int v) ? v : defaultValue;

    /// <summary>Entspricht AutoIts IniWrite. Bei mehrzeiligen Werten wird nur die erste Zeile geschrieben.</summary>
    public void Write(string section, string key, string value)
    {
        value = value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        (int start, int end) = FindSection(section);

        if (start < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length != 0)
            {
                _lines.Add(string.Empty);
            }

            _lines.Add("[" + section + "]");
            _lines.Add(key + "=" + value);
            return;
        }

        for (int i = start + 1; i < end; i++)
        {
            string trimmed = _lines[i].TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#')
            {
                continue;
            }

            (string k, _) = SplitKey(_lines[i]);
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = key + "=" + value;
                return;
            }
        }

        _lines.Insert(end, key + "=" + value);
    }

    public void DeleteKey(string section, string key)
    {
        (int start, int end) = FindSection(section);
        if (start < 0)
        {
            return;
        }

        for (int i = start + 1; i < end; i++)
        {
            (string k, _) = SplitKey(_lines[i]);
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                _lines.RemoveAt(i);
                return;
            }
        }
    }

    public IReadOnlyList<string> Sections()
    {
        List<string> result = new();
        foreach (string line in _lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '[')
            {
                continue;
            }

            int close = trimmed.IndexOf(']');
            if (close > 0)
            {
                result.Add(trimmed.Substring(1, close - 1));
            }
        }

        return result;
    }
}
