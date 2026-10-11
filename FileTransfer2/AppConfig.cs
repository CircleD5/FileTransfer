using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace FileTransfer2
{
    /// <summary>
    /// 設定ファイルの不備（ファイルがない・項目が足りない・値が不正）を表す例外。
    /// </summary>
    public class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
    }

    /// <summary>
    /// 設定ファイルを読み込んで保持するクラス。
    /// ・settings.config     … 動作設定（キー=値）
    /// ・messages.json       … 画面やログに出す文言（JSON。中身は Messages クラスを参照）
    /// ・forbidden_paths.txt … 実行を禁止するパスの一覧（1行に1つ）
    /// ・deploy_keep.txt     … 配置のときに本番に残すものの一覧（1行に1つ）
    /// プログラム側に既定値は持たない。ファイルや項目が欠けていたら ConfigException を投げる。
    /// </summary>
    public class AppConfig
    {
        public const string DefaultSettingsFile = "settings.config";

        public int AutoRunIntervalMs { get; private set; }
        public int RowDelayMs { get; private set; }
        public string AutoRunPlanDir { get; private set; }
        public string AutoRunDoneDir { get; private set; }
        public string AutoRunErrorDir { get; private set; }
        public string ProfilesDir { get; private set; }
        public string LogDir { get; private set; }
        public int LogRetentionDays { get; private set; }
        public string MessagesFile { get; private set; }
        public string ForbiddenPathsFile { get; private set; }
        public string BackupDir { get; private set; }
        public string DeployKeepFile { get; private set; }
        /// <summary>起動時に読み込むプロファイルの名前（"" なら読み込まない）</summary>
        public string DefaultProfile { get; private set; }

        /// <summary>画面やログに出す文言</summary>
        public Messages Msg { get; private set; }

        public IReadOnlyList<string> ForbiddenPaths => _forbiddenPaths;
        /// <summary>配置のときに本番フォルダに残すもの（配置先フォルダから見た相対パス）</summary>
        public IReadOnlyList<string> DeployKeepPaths => _deployKeepPaths;

        private readonly List<string> _forbiddenPaths = new List<string>();
        private readonly List<string> _deployKeepPaths = new List<string>();

        // ※ここのエラー文言だけは messages.json に置けない（messages.json 自体が読めない場合があるため）

        /// <param name="settingsPath">動作設定ファイル。テスト用の設定に切り替えるときに別のファイルを渡す。</param>
        public void Load(string settingsPath = DefaultSettingsFile)
        {
            string settingsFullPath = GetFullPath(settingsPath);
            var settings = ReadKeyValueFile(settingsFullPath);
            var errors = new List<string>();

            AutoRunIntervalMs = GetInt(settings, "AutoRunIntervalMs", 1, errors); // タイマーの間隔は 0 にできない
            RowDelayMs = GetInt(settings, "RowDelayMs", 0, errors);
            AutoRunPlanDir = GetString(settings, "AutoRunPlanDir", errors);
            AutoRunDoneDir = GetString(settings, "AutoRunDoneDir", errors);
            AutoRunErrorDir = GetString(settings, "AutoRunErrorDir", errors);
            ProfilesDir = GetString(settings, "ProfilesDir", errors);
            LogDir = GetString(settings, "LogDir", errors);
            LogRetentionDays = GetInt(settings, "LogRetentionDays", 0, errors);
            MessagesFile = GetString(settings, "MessagesFile", errors);
            ForbiddenPathsFile = GetString(settings, "ForbiddenPathsFile", errors);
            BackupDir = GetString(settings, "BackupDir", errors);
            DeployKeepFile = GetString(settings, "DeployKeepFile", errors);
            DefaultProfile = GetOptionalString(settings, "DefaultProfile", errors);

            if (errors.Count > 0)
                throw new ConfigException($"設定ファイルに誤りがあります。\n{settingsFullPath}\n\n{string.Join("\n", errors)}");

            Msg = LoadMessages(GetFullPath(MessagesFile));
            _forbiddenPaths.AddRange(ReadSettingLines(GetFullPath(ForbiddenPathsFile)));
            // 先頭の \ は付けても付けなくても同じ意味にする（「\Logs」も「Logs」も配置先の直下の Logs）
            _deployKeepPaths.AddRange(ReadSettingLines(GetFullPath(DeployKeepFile)).Select(path => path.Trim('\\')));
        }

        /// <summary>
        /// 設定ファイルに書かれた作業用フォルダ（ログ・プロファイル・自動処理）を作る。すでにあれば何もしない。
        /// 書き込み権限がないなどで作れなければ ConfigException（どのフォルダが、なぜ作れないかを書く）。
        /// </summary>
        public void CreateFolders()
        {
            foreach (string dir in new[] { LogDir, ProfilesDir, AutoRunPlanDir, AutoRunDoneDir, AutoRunErrorDir })
            {
                string fullPath = GetFullPath(dir);
                try
                {
                    Directory.CreateDirectory(fullPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    throw new ConfigException($"フォルダを作成できません。\n{fullPath}\n\n{ex.Message}\n\nsettings.config のフォルダの設定と、書き込み権限を確認してください。");
                }
            }
        }

        /// <summary>
        /// messages.json を読み込む。項目の抜け・知らないキー（書き間違い）・JSON の書式誤りは ConfigException。
        /// </summary>
        private static Messages LoadMessages(string path)
        {
            if (!File.Exists(path))
                throw new ConfigException($"設定ファイルが見つかりません。\n{path}");

            Messages messages;
            try
            {
                var jsonSettings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error };
                messages = JsonConvert.DeserializeObject<Messages>(File.ReadAllText(path, Encoding.UTF8), jsonSettings);
            }
            catch (JsonException ex)
            {
                throw new ConfigException($"文言ファイルに誤りがあります。\n{path}\n\n{ex.Message}");
            }
            if (messages == null)
                throw new ConfigException($"文言ファイルが空です。\n{path}");

            // 処理種別・モードごとの文言は、enum の値（Unknown 以外）がすべてそろっているかを確認する
            var missing = MissingKeys("Actions", messages.Actions)
                .Concat(MissingKeys("Modes", messages.Modes))
                .Concat(MissingKeys("SourceHints", messages.SourceHints))
                .Concat(MissingKeys("DestHints", messages.DestHints))
                .ToList();
            if (missing.Count > 0)
                throw new ConfigException($"文言ファイルに足りない項目があります。\n{path}\n\n{string.Join("\n", missing.Select(key => "・" + key))}");

            return messages;
        }

        /// <summary>
        /// enum の値（Unknown 以外）のうち、文言の辞書に書かれていないものを「グループ名.値」の形で返す。
        /// </summary>
        private static IEnumerable<string> MissingKeys<T>(string group, Dictionary<T, string> texts) where T : struct
        {
            return Enum.GetValues(typeof(T)).Cast<T>()
                       .Where(value => value.ToString() != "Unknown" && !texts.ContainsKey(value))
                       .Select(value => $"{group}.{value}");
        }

        /// <summary>
        /// 相対パスをアプリ（exe）のフォルダ基準の絶対パスにする。
        /// </summary>
        public string GetFullPath(string relativeOrAbsolutePath)
        {
            if (Path.IsPathRooted(relativeOrAbsolutePath)) return relativeOrAbsolutePath;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeOrAbsolutePath);
        }

        private static string GetString(Dictionary<string, string> settings, string key, List<string> errors)
        {
            if (settings.TryGetValue(key, out string value) && value.Length > 0) return value;
            errors.Add($"・{key} が書かれていません。");
            return null;
        }

        /// <summary>
        /// 値は空でもよい項目を読む。ただし項目そのもの（「キー=」の行）は必須（書き忘れと「使わない」を区別するため）。
        /// </summary>
        private static string GetOptionalString(Dictionary<string, string> settings, string key, List<string> errors)
        {
            if (settings.TryGetValue(key, out string value)) return value;
            errors.Add($"・{key} が書かれていません。（使わない場合は「{key}=」と値を空にして書いてください）");
            return "";
        }

        private static int GetInt(Dictionary<string, string> settings, string key, int min, List<string> errors)
        {
            string value = GetString(settings, key, errors);
            if (value == null) return 0;
            if (int.TryParse(value, out int number) && number >= min) return number;
            errors.Add($"・{key} は {min} 以上の整数で指定してください。（現在の値：{value}）");
            return 0;
        }

        /// <summary>
        /// 「キー=値」形式のファイルを読む。同じキーが複数あれば後のものを使う。
        /// </summary>
        private static Dictionary<string, string> ReadKeyValueFile(string path)
        {
            var result = new Dictionary<string, string>();
            foreach (string line in ReadSettingLines(path))
            {
                int eqIdx = line.IndexOf('=');
                if (eqIdx > 0)
                    result[line.Substring(0, eqIdx).Trim()] = line.Substring(eqIdx + 1).Trim();
            }
            return result;
        }

        /// <summary>
        /// 空行と「#」で始まるコメント行を除いた行を返す。ファイルがなければ ConfigException。
        /// </summary>
        private static List<string> ReadSettingLines(string path)
        {
            if (!File.Exists(path))
                throw new ConfigException($"設定ファイルが見つかりません。\n{path}");

            var lines = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                lines.Add(trimmed);
            }
            return lines;
        }
    }
}
