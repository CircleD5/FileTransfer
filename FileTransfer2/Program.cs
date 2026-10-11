using System;
using System.Windows.Forms;

namespace FileTransfer2
{
    internal static class Program
    {
        /// <summary>メイン画面（予期しない例外をログに出すときに使う）</summary>
        public static Form1 MainFormInstance { get; private set; }
        private static AppConfig _config;

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
                _config.Load(args.Length > 0 ? args[0] : AppConfig.DefaultSettingsFile);
                _config.CreateFolders();
            }
            catch (ConfigException ex)
            {
                // 設定が正しくないまま動かすと危険なので、起動しない
                MessageBox.Show(ex.Message, "設定エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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
        /// 続けられない例外を、メッセージボックスで知らせる。
        /// </summary>
        private static void ShowFatalError(Exception ex)
        {
            // 設定の読み込み前に起きた場合に備えて、文言は直書きの予備を用意しておく
            string message = _config?.Msg != null ? Messages.Format(_config.Msg.Error.Fatal, ex.Message) : $"致命的なエラーが発生したため、アプリを終了します。\n{ex.Message}";
            string title = _config?.Msg != null ? _config.Msg.Error.FatalTitle : "システムエラー";
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
