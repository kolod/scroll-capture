using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ScrollCapture.App.Settings
{
    /// <summary>
    /// Settings in <c>ScrollCapture.ini</c> next to the executable (no registry). The file is only
    /// written when a setting changed; if the folder is read-only, settings live in memory.
    /// </summary>
    internal sealed class PortableSettings
    {
        public const string FileName = "ScrollCapture.ini";

        private readonly string _path;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _dirty;

        private PortableSettings(string path)
        {
            _path = path;
        }

        /// <summary>Language code, or empty to follow the OS language.</summary>
        public string Language
        {
            get => Get("language", string.Empty);
            set => Set("language", value ?? string.Empty);
        }

        public string ScrollMethod
        {
            get => Get("scrollMethod", string.Empty);
            set => Set("scrollMethod", value ?? string.Empty);
        }

        public string CaptureMethod
        {
            get => Get("captureMethod", string.Empty);
            set => Set("captureMethod", value ?? string.Empty);
        }

        public int DelayMs
        {
            get => GetInt("delayMs", 250);
            set => SetInt("delayMs", value);
        }

        public bool StartFromTop
        {
            get => GetBool("startFromTop", true);
            set => SetBool("startFromTop", value);
        }

        public bool AskWhereToSave
        {
            get => GetBool("askWhereToSave", true);
            set => SetBool("askWhereToSave", value);
        }

        public bool TrimBackground
        {
            get => GetBool("trimBackground", true);
            set => SetBool("trimBackground", value);
        }

        public bool FitWidth
        {
            get => GetBool("fitWidth", true);
            set => SetBool("fitWidth", value);
        }

        public string LastFolder
        {
            get => Get("lastFolder", string.Empty);
            set => Set("lastFolder", value ?? string.Empty);
        }

        /// <summary>1-based index into the save dialog filter (PNG, JPEG, BMP, TIFF).</summary>
        public int LastFormat
        {
            get => GetInt("lastFormat", 1);
            set => SetInt("lastFormat", value);
        }

        public static PortableSettings Load()
        {
            var settings = new PortableSettings(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName));
            try
            {
                if (File.Exists(settings._path))
                {
                    foreach (var line in File.ReadAllLines(settings._path, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0 && !line.TrimStart().StartsWith(";", StringComparison.Ordinal))
                        {
                            settings._values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Unreadable settings: use defaults.
            }

            return settings;
        }

        public void SaveIfChanged()
        {
            if (!_dirty)
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.Append("; ScrollCapture settings\n");
                foreach (var pair in _values)
                {
                    sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
                }

                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
                _dirty = false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Read-only location (e.g. Program Files): keep settings for this session only.
            }
        }

        private string Get(string key, string fallback)
        {
            return _values.TryGetValue(key, out var value) ? value : fallback;
        }

        private void Set(string key, string value)
        {
            if (!_values.TryGetValue(key, out var old) || old != value)
            {
                _values[key] = value;
                _dirty = true;
            }
        }

        private bool GetBool(string key, bool fallback)
        {
            return _values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private void SetBool(string key, bool value)
        {
            Set(key, value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant());
        }

        private int GetInt(string key, int fallback)
        {
            return _values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        }

        private void SetInt(string key, int value)
        {
            Set(key, value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
