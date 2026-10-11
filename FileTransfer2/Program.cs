using System;
using System.IO;
using System.Windows.Forms;
using log4net;
using log4net.Config;

namespace FileTransfer2
{
    internal static class Program
    {
        /// <summary>メイン画面（予期しない例外をログに出すときに使う）</summary>
        public static Form1 MainFormInstance { get; private set; }
        private static AppConfig _config;
        /// <summary>ログファイルへの書き込み（log4net）</summary>
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Program));

        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// 引数に設定ファイルのパスを渡すと、settings.config の代わりにそれを読み込みます（テスト用の設定など）。
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += Application_ThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            _config = new AppConfig();
            try
            {
                // 起動時の引数で設定ファイルを渡されたら、それを使う（テスト用の設定など）。なければ settings.config
                string settingsPath = AppConfig.DefaultSettingsFile;
                if (args.Length > 0) settingsPath = args[0];
                _config.Load(settingsPath);
                _config.CreateFolders();
            }
            catch (ConfigException ex)
            {
                // 設定が正しくないまま動かすと危険なので、起動しない
                MessageBox.Show(ex.Message, ConfigErrorTitle(), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // log4net の設定は settings.config の <log4net> から読む。
            // 出力先のフォルダは LogDir の値を使う（<log4net> の中の %property{LogDir} に入る）
            GlobalContext.Properties["LogDir"] = _config.GetFullPath(_config.LogDir);
            XmlConfigurator.Configure(new FileInfo(_config.SettingsFullPath));

            MainFormInstance = new Form1(_config);
            Application.Run(MainFormInstance);
        }

        /// <summary>
        /// 画面の処理中に起きた例外。ここで捕まえればアプリは動き続けられる。
        /// </summary>
        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            if (MainFormInstance != null && !MainFormInstance.IsDisposed)
                MainFormInstance.LogGlobalError(e.Exception);
            else
                ShowFatalError(e.Exception);
        }

        /// <summary>
        /// 画面以外のスレッドで起きた例外。この後アプリは終了してしまうため、知らせることしかできない。
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex) ShowFatalError(ex);
        }

        /// <summary>
        /// 設定エラーのメッセージボックスの題名。
        /// 文言（messages.json）が読めなかったときは、それも使えないので、アプリの名前にする。
        /// </summary>
        private static string ConfigErrorTitle()
        {
            if (_config.Msg != null) return _config.Msg.Config.Title;
            return Application.ProductName;
        }

        /// <summary>
        /// 続けられない例外を、ログファイルに残し、メッセージボックスで知らせる。
        /// </summary>
        private static void ShowFatalError(Exception ex)
        {
            // log4net を設定する前に起きた場合は、何も書かれない（エラーにもならない）
            Logger.Fatal(ex.Message, ex);

            string message;
            string title;
            if (_config != null && _config.Msg != null)
            {
                message = Messages.Format(_config.Msg.Error.Fatal, ex.Message);
                title = _config.Msg.Error.FatalTitle;
            }
            else
            {
                // 文言（messages.json）を読み込む前に起きた場合は、文言が使えないので、エラー文とアプリの名前だけを出す
                message = ex.Message;
                title = Application.ProductName;
            }
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
