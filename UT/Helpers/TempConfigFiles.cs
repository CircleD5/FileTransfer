using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaUI.Core.Tools;
using Newtonsoft.Json.Linq;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// テスト用の設定ファイル一式（settings.config / messages.json / forbidden_paths.txt）を一時フォルダに作る。
    /// 中身は正しい状態で作られるので、テストでは必要な部分だけ書き換える。Dispose で一時フォルダごと消える。
    /// </summary>
    public class TempConfigFiles : IDisposable
    {
        public string Dir { get; }
        public string SettingsPath => Path.Combine(Dir, "settings.config");
        public string MessagesPath => Path.Combine(Dir, "messages.json");
        public string ForbiddenPath => Path.Combine(Dir, "forbidden_paths.txt");
        public string DeployKeepPath => Path.Combine(Dir, "deploy_keep.txt");

        public TempConfigFiles()
        {
            Dir = Path.Combine(Path.GetTempPath(), "FileTransfer2Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            WriteSettings(ValidSettings());
            // 文言は本物（アプリと一緒に出力先へコピーされたもの）を使う
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "messages.json"), MessagesPath);
            WriteForbiddenPaths();
            WriteDeployKeep();
        }

        /// <summary>
        /// すべての項目がそろった正しい設定。テストではここから一部を消したり書き換えたりする。
        /// フォルダはすべてこの一時フォルダの中を指すので、アプリを起動しても本番のフォルダは汚れない。
        /// </summary>
        public Dictionary<string, string> ValidSettings()
        {
            return new Dictionary<string, string>
            {
                { "AutoRunIntervalMs", "30000" },
                { "RowDelayMs", "0" },
                { "AutoRunPlanDir", Path.Combine(Dir, @"AutoRun\Plan") },
                { "AutoRunDoneDir", Path.Combine(Dir, @"AutoRun\Done") },
                { "AutoRunErrorDir", Path.Combine(Dir, @"AutoRun\Error") },
                { "ProfilesDir", Path.Combine(Dir, "Profiles") },
                { "DefaultProfile", "" },
                { "LogDir", Path.Combine(Dir, "Logs") },
                { "LogRetentionDays", "30" },
                { "MessagesFile", MessagesPath },
                { "ForbiddenPathsFile", ForbiddenPath },
                { "BackupDir", Path.Combine(Dir, "Backup") },
                { "DeployKeepFile", DeployKeepPath },
            };
        }

        public void WriteSettings(Dictionary<string, string> settings)
        {
            File.WriteAllLines(SettingsPath, settings.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        public void WriteForbiddenPaths(params string[] lines)
        {
            File.WriteAllLines(ForbiddenPath, lines);
        }

        public void WriteDeployKeep(params string[] lines)
        {
            File.WriteAllLines(DeployKeepPath, lines);
        }

        /// <summary>
        /// messages.json を JSON のまま書き換える（例：json => json["Status"]["Done"].Parent.Remove()）。
        /// </summary>
        public void EditMessages(Action<JObject> edit)
        {
            JObject json = JObject.Parse(File.ReadAllText(MessagesPath));
            edit(json);
            File.WriteAllText(MessagesPath, json.ToString());
        }

        public AppConfig LoadConfig()
        {
            var config = new AppConfig();
            config.Load(SettingsPath);
            return config;
        }

        public void Dispose()
        {
            // アプリを終了した直後はログファイルなどがまだ開かれていることがあるので、少し待って再試行する
            Retry.WhileException(() => Directory.Delete(Dir, true), TimeSpan.FromSeconds(5));
        }
    }
}
