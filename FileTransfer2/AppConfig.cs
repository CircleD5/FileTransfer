using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FileTransfer2
{
    /// <summary>
    /// 設定ファイルの不備（ファイルがない・項目が足りない・値が不正）を表す例外。
    /// </summary>
    public class ConfigException : Exception
    {
        /// <summary>
        /// 設定エラーの例外を作る。message は、そのまま画面に表示する文言。
        /// </summary>
        public ConfigException(string message) : base(message) { }
    }

    /// <summary>
    /// 設定ファイルを読み込んで保持するクラス。
    /// ・messages.json       … 画面やログに出す文言（JSON。中身は Messages クラスを参照）。settings.config と同じフォルダに置く
    /// ・settings.config     … 動作設定（XML。.NET 標準の設定ファイル。中身は SettingsSection クラスを参照）と、log4net の設定
    /// ・forbidden_paths.txt … どの処理でも触らない場所の一覧（1行に1つの絶対パス）
    /// ・deploy_keep.txt     … 配置のときに本番に残し、退避もするものの一覧（1行に1つの絶対パス）
    /// プログラム側に既定値は持たない。ファイルや項目が欠けていたら ConfigException を投げる。
    /// </summary>
    public class AppConfig
    {
        public const string DefaultSettingsFile = "settings.config";
        public const string MessagesFileName = "messages.json";

        /// <summary>読み込んだ settings.config の絶対パス（log4net の設定もこのファイルから読む）</summary>
        public string SettingsFullPath { get; private set; }

        /// <summary>自動処理で Plan フォルダを確認する間隔（ミリ秒）</summary>
        public int AutoRunIntervalMs { get; private set; }
        /// <summary>行と行の間の待ち時間（ミリ秒）</summary>
        public int RowDelayMs { get; private set; }
        /// <summary>自動処理の計画ファイル（TSV）を置くフォルダ</summary>
        public string AutoRunPlanDir { get; private set; }
        /// <summary>自動処理で成功した計画ファイルを移すフォルダ</summary>
        public string AutoRunDoneDir { get; private set; }
        /// <summary>自動処理で失敗した計画ファイルを移すフォルダ</summary>
        public string AutoRunErrorDir { get; private set; }
        /// <summary>プロファイル（TSV）の保存先</summary>
        public string ProfilesDir { get; private set; }
        /// <summary>ログ（日付ごとのログと deploy_history.log）の出力先</summary>
        public string LogDir { get; private set; }
        /// <summary>日付ごとのログを残す日数（0 なら消さない）</summary>
        public int LogRetentionDays { get; private set; }
        /// <summary>どの処理でも触らない場所の一覧ファイルの場所</summary>
        public string ForbiddenPathsFile { get; private set; }
        /// <summary>配置のときに本番フォルダを退避する先（この下にリリース番号のフォルダができる）</summary>
        public string BackupDir { get; private set; }
        /// <summary>配置のときに本番に残すものの一覧ファイルの場所</summary>
        public string DeployKeepFile { get; private set; }
        /// <summary>起動時に読み込むプロファイルの名前（"" なら読み込まない）</summary>
        public string DefaultProfile { get; private set; }

        /// <summary>画面やログに出す文言</summary>
        public Messages Msg { get; private set; }

        /// <summary>どの処理でも触らない場所（絶対パス。その中のものも触らない）</summary>
        public IReadOnlyList<string> ForbiddenPaths => _forbiddenPaths;
        /// <summary>配置のときに本番フォルダに残し、退避もするもの（絶対パス。使うのは、配置先の中にあるものだけ）</summary>
        public IReadOnlyList<string> DeployKeepPaths => _deployKeepPaths;

        private readonly List<string> _forbiddenPaths = new List<string>();
        private readonly List<string> _deployKeepPaths = new List<string>();

        /// <summary>
        /// 設定ファイル一式を読み込む。
        /// 文言（messages.json）を最初に読む。設定ファイルの誤りを知らせる文言も messages.json に書いてあるため。
        /// </summary>
        /// <param name="settingsPath">動作設定ファイル。テスト用の設定に切り替えるときに別のファイルを渡す。</param>
        public void Load(string settingsPath = DefaultSettingsFile)
        {
            SettingsFullPath = GetFullPath(settingsPath);
            Msg = LoadMessages(Path.Combine(Path.GetDirectoryName(SettingsFullPath), MessagesFileName));

            LoadSettings(SettingsFullPath);
            _forbiddenPaths.AddRange(ReadPathList(GetFullPath(ForbiddenPathsFile)));
            _deployKeepPaths.AddRange(ReadPathList(GetFullPath(DeployKeepFile)));
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
                    throw new ConfigException(Messages.Format(Msg.Config.FolderCreateFailed, fullPath, ex.Message));
                }
            }
        }

        /// <summary>
        /// messages.json を読み込む。ファイルがない・JSON の書式誤り・項目の抜け・知らないキー（書き間違い）は ConfigException。
        /// </summary>
        private static Messages LoadMessages(string path)
        {
            Messages messages;
            try
            {
                var serializer = JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                // JObject.Parse を通すのは、空のファイルもエラーにするため（DeserializeObject だと、空のときは null が返るだけ）
                messages = JObject.Parse(File.ReadAllText(path, Encoding.UTF8)).ToObject<Messages>(serializer);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                // 文言そのものが読めないので、.NET や Newtonsoft.Json のエラー文をそのまま出す
                throw new ConfigException(path + "\n\n" + ex.Message);
            }

            // 処理種別・モードごとの文言は、enum の値（Unknown 以外）がすべてそろっているかを確認する
            var missing = MissingKeys("Actions", messages.Actions)
                .Concat(MissingKeys("Modes", messages.Modes))
                .Concat(MissingKeys("SourceHints", messages.SourceHints))
                .Concat(MissingKeys("DestHints", messages.DestHints))
                .ToList();
            if (missing.Count > 0)
                throw new ConfigException(Messages.Format(messages.Config.MessagesMissingKeys, path, string.Join("\n", missing.Select(key => "・" + key))));

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
        /// settings.config の &lt;fileTransfer&gt; を、.NET 標準の仕組み（System.Configuration）で読み、各プロパティに入れる。
        /// 項目の抜けや値の誤りは .NET がチェックする（チェックの内容は SettingsSection クラスに宣言してある）。
        /// </summary>
        private void LoadSettings(string path)
        {
            // OpenMappedExeConfiguration は、ファイルがなくてもエラーにならず空の設定を返すので、先に確かめる
            if (!File.Exists(path))
                throw new ConfigException(Messages.Format(Msg.Config.FileNotFound, path));

            try
            {
                // exe の名前で決まる App.config（FileTransfer2.exe.config）ではなく、指定したファイルを読む
                var fileMap = new ExeConfigurationFileMap { ExeConfigFilename = path };
                Configuration configuration = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
                var settings = configuration.GetSection(SettingsSection.Name) as SettingsSection;

                // <fileTransfer> がない、または <configSections> での宣言が抜けている
                if (settings == null)
                    throw new ConfigException(Messages.Format(Msg.Config.SectionMissing, path, SettingsSection.Name));

                // 値の誤り（範囲外など）は、値を取り出したときに分かることがあるので、取り出しも try の中で行う
                AutoRunIntervalMs = settings.AutoRunIntervalMs;
                RowDelayMs = settings.RowDelayMs;
                AutoRunPlanDir = settings.AutoRunPlanDir;
                AutoRunDoneDir = settings.AutoRunDoneDir;
                AutoRunErrorDir = settings.AutoRunErrorDir;
                ProfilesDir = settings.ProfilesDir;
                LogDir = settings.LogDir;
                LogRetentionDays = settings.LogRetentionDays;
                ForbiddenPathsFile = settings.ForbiddenPathsFile;
                BackupDir = settings.BackupDir;
                DeployKeepFile = settings.DeployKeepFile;
                DefaultProfile = settings.DefaultProfile;
            }
            catch (ConfigurationErrorsException ex)
            {
                throw new ConfigException(Messages.Format(Msg.Config.SettingsInvalid, path, ex.Message));
            }
        }

        /// <summary>
        /// 相対パスをアプリ（exe）のフォルダ基準の絶対パスにする。
        /// </summary>
        public string GetFullPath(string relativeOrAbsolutePath)
        {
            if (Path.IsPathRooted(relativeOrAbsolutePath)) return relativeOrAbsolutePath;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeOrAbsolutePath);
        }

        /// <summary>
        /// 1行に1つの絶対パスの一覧（forbidden_paths.txt / deploy_keep.txt）を読む。空行と「#」で始まる行は飛ばす。
        /// 返すパスは、実際に指す場所にそろえ、末尾の \ を取り除いたもの。
        /// 相対パスやパスに使えない文字があれば、行番号を書いて ConfigException
        /// （黙って無視すると、守ったつもり・残したつもりのものが消える事故になるため）。
        /// </summary>
        private List<string> ReadPathList(string path)
        {
            if (!File.Exists(path))
                throw new ConfigException(Messages.Format(Msg.Config.FileNotFound, path));

            var paths = new List<string>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                // エクスプローラーの「パスのコピー」で付く前後の " は取り除く
                string line = lines[i].Trim().Trim('"');
                if (line.Length == 0 || line.StartsWith("#")) continue;

                string fullPath = null;
                if (IsFullyQualified(line))
                {
                    // 「..」などを含むパスを、実際に指す場所にそろえる（パスに使えない文字があれば誤り）
                    try { fullPath = Path.GetFullPath(line).TrimEnd('\\'); }
                    catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { }
                }
                if (fullPath == null)
                    throw new ConfigException(Messages.Format(Msg.Config.PathListInvalid, path, i + 1, lines[i]));

                paths.Add(fullPath);
            }
            return paths;
        }

        /// <summary>
        /// 「C:\…」か「\\サーバー\…」の形の絶対パスか。
        /// （「\Logs」や「C:Logs」は、どこを指すかが実行するときの状態で変わるので、絶対パスとみなさない）
        /// </summary>
        private static bool IsFullyQualified(string path)
        {
            if (path.StartsWith(@"\\")) return true;
            return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        }
    }
}
