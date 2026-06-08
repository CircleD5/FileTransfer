using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileTransfer2
{
    public class AppConfig
    {
        public int AutoRunIntervalMs { get; private set; } = 30000;
        public int RowDelayMs { get; private set; } = 200;
        public string AutoRunPlanDir { get; private set; } = @"AutoRun\Plan";
        public string AutoRunDoneDir { get; private set; } = @"AutoRun\Done";
        public string ProfilesDir { get; private set; } = @"Profiles";

        private Dictionary<string, string> _messages = new Dictionary<string, string>();

        public void Load()
        {
            string configPath = GetFullPath("settings.config");
            string iniPath = GetFullPath("messages.ini");

            if (File.Exists(configPath))
            {
                foreach (string line in File.ReadAllLines(configPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string key = line.Substring(0, eqIdx).Trim();
                        string val = line.Substring(eqIdx + 1).Trim();

                        if (key == "AutoRunIntervalMs" && int.TryParse(val, out int ival)) AutoRunIntervalMs = ival;
                        else if (key == "RowDelayMs" && int.TryParse(val, out int rval)) RowDelayMs = rval;
                        else if (key == "AutoRunPlanDir") AutoRunPlanDir = val;
                        else if (key == "AutoRunDoneDir") AutoRunDoneDir = val;
                        else if (key == "ProfilesDir") ProfilesDir = val;
                    }
                }
            }

            if (File.Exists(iniPath))
            {
                foreach (string line in File.ReadAllLines(iniPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string key = line.Substring(0, eqIdx).Trim();
                        string val = line.Substring(eqIdx + 1).Trim();
                        _messages[key] = val.Replace("\\n", "\n");
                    }
                }
            }
        }

        public string GetMsg(string key, params object[] args)
        {
            if (_messages.TryGetValue(key, out string tmpl))
            {
                try { return string.Format(tmpl, args); } catch { return tmpl; }
            }
            // messages.iniが欠損している場合はキー名と引数をそのまま表示してクラッシュを防ぐ
            return args != null && args.Length > 0 ? $"{key} [{string.Join(", ", args)}]" : key;
        }

        public string GetFullPath(string relativeOrAbsolutePath)
        {
            if (Path.IsPathRooted(relativeOrAbsolutePath)) return relativeOrAbsolutePath;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeOrAbsolutePath);
        }
    }
}