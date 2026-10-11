using System;
using System.IO;
using Xunit;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// アプリを起動して、画面から実行した結果（ファイルがどうなったか）を確かめるテスト。
    /// 画面の操作は AppDriver にまとめてあり、ここには「何を確かめたいか」だけを書く。
    /// </summary>
    [Collection("UI Tests")]
    public class E2ETests : IDisposable
    {
        private readonly AppDriver _app = new AppDriver();

        public void Dispose() => _app.Dispose();

        /// <summary>作業フォルダにファイルを作り、そのパスを返す。</summary>
        private string CreateFile(string relativePath, string content = "test")
        {
            string path = WorkPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }

        private string WorkPath(string relativePath) => Path.Combine(_app.WorkDir, relativePath);

        #region 一覧からの実行

        [Fact]
        public void ファイルコピー_転送先にコピーされる()
        {
            string src = CreateFile(@"in\copy.txt");

            _app.SetRow(0, TransferAction.Copy, src, WorkPath(@"out\"));
            _app.ExecuteAll();

            Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(0));
            Assert.True(File.Exists(WorkPath(@"out\copy.txt")));
            Assert.True(File.Exists(src));
            // どこからどこへ実行したかが分かる
            Assert.Contains(Messages.Format(_app.Msg.Run.RowStartToDest, 1, _app.Msg.Actions[TransferAction.Copy], src, WorkPath(@"out\")), _app.Log);
            // 誰がどの PC で実行したかが、画面とログファイルの両方に残る
            string executor = Messages.Format(_app.Msg.Run.Executor, Environment.UserName, Environment.MachineName);
            Assert.Contains(executor, _app.Log);
            string logFile = Path.Combine(_app.Config.LogDir, $"{DateTime.Now:yyyyMMdd}.log");
            Assert.Contains(executor, File.ReadAllText(logFile));
        }

        [Fact]
        public void ファイル移動_転送先へ移動し元ファイルは消える()
        {
            string src = CreateFile(@"in\move.txt");

            _app.SetRow(0, TransferAction.Move, src, WorkPath(@"out\"));
            _app.ExecuteAll();

            Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(0));
            Assert.True(File.Exists(WorkPath(@"out\move.txt")));
            Assert.False(File.Exists(src));
        }

        [Fact]
        public void ファイル削除_削除される()
        {
            string src = CreateFile(@"in\delete.txt");

            _app.SetRow(0, TransferAction.Delete, src);
            _app.ExecuteAll();

            Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(0));
            Assert.False(File.Exists(src));
        }

        [Fact]
        public void 失敗した行で止まり_後続の行は実行されない()
        {
            string src = CreateFile(@"in\next.txt");

            _app.SetRow(0, TransferAction.Copy, WorkPath(@"in\not_exist.txt"), WorkPath(@"out\"));
            _app.SetRow(1, TransferAction.Copy, src, WorkPath(@"out\"));
            _app.ExecuteAll();

            Assert.Equal(_app.Msg.Status.Err, _app.GetStatus(0));
            Assert.Equal(_app.Msg.Status.Unexec, _app.GetStatus(1));
            Assert.False(File.Exists(WorkPath(@"out\next.txt")));
        }

        [Fact]
        public void 個別実行_その行だけ実行される()
        {
            string src1 = CreateFile(@"in\first.txt");
            string src2 = CreateFile(@"in\second.txt");

            _app.SetRow(0, TransferAction.Copy, src1, WorkPath(@"out\"));
            _app.SetRow(1, TransferAction.Copy, src2, WorkPath(@"out\"));
            _app.ExecuteRow(1);

            Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(1));
            Assert.True(File.Exists(WorkPath(@"out\second.txt")));
            Assert.False(File.Exists(WorkPath(@"out\first.txt")));
        }

        [Fact]
        public void 配置_2回実行しても旧版の退避が守られる()
        {
            string release = Path.GetDirectoryName(CreateFile(@"Release\v1.0.0\App\App.exe", "new"));
            string target = Path.GetDirectoryName(CreateFile(@"Prod\App\App.exe", "old"));

            _app.SetRow(0, TransferAction.Deploy, release, target);
            _app.ExecuteAll();
            _app.ExecuteAll(); // 誤って2回実行した

            Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(0));
            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "App.exe")));
            string backup = Path.Combine(_app.Config.BackupDir, "v1.0.0", target.Replace(":", "").Replace('\\', '_'));
            Assert.Equal("old", File.ReadAllText(Path.Combine(backup, "App.exe")));
            Assert.Contains(Messages.Format(_app.Msg.Transfer.DeployAlreadyDone, 1, "v1.0.0", target), _app.Log);
        }

        [Fact]
        public void リリースと切り戻し_ボタンで復元用を作り_それを実行すると元に戻る()
        {
            string data = Path.GetDirectoryName(CreateFile(@"BizAppData\data.txt", "リリース前"));
            string backupRoot = WorkPath(@"ReleaseBackup\v1.0.0");
            string release = Path.GetDirectoryName(CreateFile(@"Release\v1.0.0\App\App.exe", "new"));
            string target = Path.GetDirectoryName(CreateFile(@"Prod\App\App.exe", "old"));
            string restoreName = "Release" + _app.Msg.Profile.RestorePlanSuffix;
            string restorePath = Path.Combine(_app.Config.ProfilesDir, restoreName + ".tsv");

            // リリース用の計画：業務データを退避 → アプリを配置。保存しただけでは復元用は作られない
            _app.SetRow(0, TransferAction.Backup, data, backupRoot);
            _app.SetRow(1, TransferAction.Deploy, release, target);
            _app.SaveAs(Path.Combine(_app.Config.ProfilesDir, "Release.tsv"));
            Assert.False(File.Exists(restorePath));

            // 「復元用を作成」で作る
            string result = _app.CreateRestorePlan();
            Assert.Contains(Messages.Format(_app.Msg.Profile.RestorePlanSaved, restoreName + ".tsv"), result);
            Assert.True(File.Exists(restorePath));

            // リリースする。その後、業務データが変わった
            _app.ExecuteAll();
            Assert.Equal("new", File.ReadAllText(Path.Combine(target, "App.exe")));
            File.WriteAllText(Path.Combine(data, "data.txt"), "リリース後");

            // 作った復元用の計画を読み込んで実行すると、リリース前に戻る
            _app.LoadProfile("Release" + _app.Msg.Profile.RestorePlanSuffix);
            _app.ExecuteAll();

            Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(0));
            Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(1));
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "App.exe")));
            Assert.Equal("リリース前", File.ReadAllText(Path.Combine(data, "data.txt")));
        }

        #endregion

        #region プロファイル

        [Fact]
        public void プロファイル_保存して読み込むと入力内容が戻る()
        {
            string src = WorkPath(@"in\profile.txt");
            string profilePath = Path.Combine(_app.Config.ProfilesDir, "E2ETest.tsv");

            _app.SetRow(0, TransferAction.Move, src, WorkPath(@"out\"), TransferMode.ExtractContents);
            _app.SaveAs(profilePath);
            Assert.True(File.Exists(profilePath));

            // 一度「未選択」に戻して一覧を空にしてから、保存したプロファイルを読み込む
            _app.LoadProfile(_app.Msg.Ui.ProfileUnselected);
            Assert.Equal(0, _app.RowCount);
            _app.LoadProfile("E2ETest");

            Assert.Equal(_app.Msg.Actions[TransferAction.Move], _app.GetText(0, _app.Msg.Column.Action));
            Assert.Equal(src, _app.GetText(0, _app.Msg.Column.Source));
            Assert.Equal(_app.Msg.Modes[TransferMode.ExtractContents], _app.GetText(0, _app.Msg.Column.Mode));
        }

        #endregion

        #region 自動処理

        /// <summary>Plan フォルダに計画ファイルを置き、そのファイル名を返す。</summary>
        private string CreatePlan(params TransferTask[] tasks)
        {
            string fileName = "plan.tsv";
            TaskFile.Save(Path.Combine(_app.Config.AutoRunPlanDir, fileName), tasks);
            return fileName;
        }

        [Fact]
        public void 自動処理_成功した計画ファイルはDoneへ移る()
        {
            string src = CreateFile(@"in\auto.txt");
            string plan = CreatePlan(new TransferTask { Action = TransferAction.Copy, Source = src, Destination = WorkPath(@"out\"), Mode = TransferMode.FolderAsIs });

            _app.SetAutoRun(true);

            _app.WaitUntil(() => File.Exists(Path.Combine(_app.Config.AutoRunDoneDir, plan)), "計画ファイルが Done フォルダへ移りませんでした。");
            Assert.True(File.Exists(WorkPath(@"out\auto.txt")));
        }

        [Fact]
        public void 自動処理_ファイルがロックされていると失敗し_計画ファイルはErrorへ退避される()
        {
            string src = CreateFile(@"in\lock.txt");
            string plan = CreatePlan(new TransferTask { Action = TransferAction.Copy, Source = src, Destination = WorkPath(@"out\"), Mode = TransferMode.FolderAsIs });

            // テスト側でファイルを排他ロックしておく
            using (File.Open(src, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                _app.SetAutoRun(true);

                _app.WaitUntil(() => File.Exists(Path.Combine(_app.Config.AutoRunErrorDir, plan)), "計画ファイルが Error フォルダへ退避されませんでした。");
            }

            // Plan に残っていると何度も実行されてしまう
            Assert.False(File.Exists(Path.Combine(_app.Config.AutoRunPlanDir, plan)));
            Assert.False(File.Exists(WorkPath(@"out\lock.txt")));
        }

        #endregion
    }
}
