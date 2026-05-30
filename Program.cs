using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileTransfer2
{
    internal static class Program
    {
        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// </summary>
        public static Form1 MainFormInstance { get; private set; }
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += Application_ThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            MainFormInstance = new Form1();
            Application.Run(MainFormInstance);
        }
        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleException(ex);
            }
        }

        private static void HandleException(Exception ex)
        {
            if (MainFormInstance != null && !MainFormInstance.IsDisposed)
            {
                MainFormInstance.LogGlobalError(ex);
            }
            else
            {
                MessageBox.Show($"致命的なエラーが発生しました:\n{ex.Message}", "システムエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
