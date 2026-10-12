using System.Configuration;

namespace FileTransfer2
{
    /// <summary>
    /// settings.config の &lt;fileTransfer&gt; 要素。.NET 標準の設定セクション（System.Configuration）を使う。
    ///
    /// 必須かどうかと値の範囲は、プロパティに付けた属性で宣言する。チェックは .NET がしてくれるので、自前では書かない。
    /// ・IsRequired = true        … 書かれていなければエラー
    /// ・IntegerValidator         … 数字でない・範囲外ならエラー
    /// ・StringValidator          … 空ならエラー
    /// ・ここにない名前（書き間違い）もエラー
    /// 誤りがあると ConfigurationErrorsException（ファイル名と行番号つき）になる。誤りは最初の1つだけが報告される。
    ///
    /// ※DefaultValue について：.NET は既定値もチェックにかけるため、チェックを付けた項目には、
    ///   チェックを通る形だけの既定値を書いてある（書かないと、起動するたびにエラーになる）。
    ///   すべて必須（IsRequired = true）なので、この既定値が実際に使われることはない。
    /// </summary>
    public class SettingsSection : ConfigurationSection
    {
        /// <summary>settings.config の中での要素名</summary>
        public const string Name = "fileTransfer";

        /// <summary>自動処理で Plan フォルダを確認する間隔（ミリ秒）。タイマーの間隔は 0 にできないので 1 以上</summary>
        [ConfigurationProperty("AutoRunIntervalMs", IsRequired = true, DefaultValue = 1)]
        [IntegerValidator(MinValue = 1)]
        public int AutoRunIntervalMs => (int)this["AutoRunIntervalMs"];

        /// <summary>行と行の間の待ち時間（ミリ秒）</summary>
        [ConfigurationProperty("RowDelayMs", IsRequired = true)]
        [IntegerValidator(MinValue = 0)]
        public int RowDelayMs => (int)this["RowDelayMs"];

        /// <summary>自動処理の計画ファイル（TSV）を置くフォルダ</summary>
        [ConfigurationProperty("AutoRunPlanDir", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string AutoRunPlanDir => (string)this["AutoRunPlanDir"];

        /// <summary>自動処理で成功した計画ファイルを移すフォルダ</summary>
        [ConfigurationProperty("AutoRunDoneDir", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string AutoRunDoneDir => (string)this["AutoRunDoneDir"];

        /// <summary>自動処理で失敗した計画ファイルを移すフォルダ</summary>
        [ConfigurationProperty("AutoRunErrorDir", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string AutoRunErrorDir => (string)this["AutoRunErrorDir"];

        /// <summary>プロファイル（TSV）の保存先</summary>
        [ConfigurationProperty("ProfilesDir", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string ProfilesDir => (string)this["ProfilesDir"];

        /// <summary>起動時に読み込むプロファイルの名前。空でもよい（"" なら読み込まない）が、項目そのものは必須</summary>
        [ConfigurationProperty("DefaultProfile", IsRequired = true)]
        public string DefaultProfile => (string)this["DefaultProfile"];

        /// <summary>ログ（日付ごとのログと deploy_history.log）の出力先</summary>
        [ConfigurationProperty("LogDir", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string LogDir => (string)this["LogDir"];

        /// <summary>日付ごとのログを残す日数（0 なら消さない）</summary>
        [ConfigurationProperty("LogRetentionDays", IsRequired = true)]
        [IntegerValidator(MinValue = 0)]
        public int LogRetentionDays => (int)this["LogRetentionDays"];

        /// <summary>配置のときに本番フォルダを退避する先</summary>
        [ConfigurationProperty("BackupDir", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string BackupDir => (string)this["BackupDir"];

        /// <summary>配置のときに本番に残すものの一覧ファイルの場所</summary>
        [ConfigurationProperty("DeployKeepFile", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string DeployKeepFile => (string)this["DeployKeepFile"];

        /// <summary>どの処理でも触らない場所の一覧ファイルの場所</summary>
        [ConfigurationProperty("ForbiddenPathsFile", IsRequired = true, DefaultValue = "-")]
        [StringValidator(MinLength = 1)]
        public string ForbiddenPathsFile => (string)this["ForbiddenPathsFile"];
    }
}
