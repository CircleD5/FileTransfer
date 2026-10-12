using System.IO;
using Xunit;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// 「中止」と進み具合の表示のテスト。
    /// 行と行の間で止めることと、2回押したり、止めている途中で実行を押したりしても壊れないことを確かめる。
    /// 中止を押す時間を作るため、行と行の間の待ち時間（RowDelayMs）を長くしたアプリで確かめる。
    /// </summary>
    [Collection("UI Tests")]
    public class StopTests
    {
        /// <summary>
        /// 行と行の間に3秒待つアプリを起動する（1行目が終わってから、2行目が始まるまでに「中止」を押せるように）。
        /// </summary>
        private static AppDriver StartSlowApp() => new AppDriver((files, settings) => settings["RowDelayMs"] = "3000");

        /// <summary>
        /// in フォルダにファイルを作り、それを out フォルダへコピーする行を、count 行並べる。
        /// </summary>
        private static void SetCopyRows(AppDriver app, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string src = Path.Combine(app.WorkDir, $@"in\{i}.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(src));
                File.WriteAllText(src, "test");
                app.SetRow(i, TransferAction.Copy, src, Path.Combine(app.WorkDir, @"out\"));
            }
        }

        private static bool Copied(AppDriver app, int i) => File.Exists(Path.Combine(app.WorkDir, $@"out\{i}.txt"));

        [Fact]
        public void 中止_今の行が終わったら止まり_残りは未実行になる_実行中は進み具合が出る()
        {
            using (var app = StartSlowApp())
            {
                SetCopyRows(app, 3);
                Assert.False(app.IsStopEnabled); // 実行していないときは押せない

                app.StartExecuteAll();
                app.WaitUntil(() => app.GetStatus(0) == app.Msg.Status.Done, "1行目が終わりませんでした。");
                Assert.Equal(Messages.Format(app.Msg.Ui.ProgressFormat, 1, 3), app.Progress);
                Assert.True(app.IsStopEnabled);
                Assert.False(app.IsExecuteAllEnabled);

                app.Stop();
                app.WaitForRunEnd(1);

                Assert.Equal(app.Msg.Status.Done, app.GetStatus(0));
                Assert.Equal(app.Msg.Status.Unexec, app.GetStatus(1));
                Assert.Equal(app.Msg.Status.Unexec, app.GetStatus(2));
                Assert.True(Copied(app, 0));
                Assert.False(Copied(app, 1));
                Assert.Contains(Messages.Format(app.Msg.Run.Stopped, 2), app.Log);
                // 終わったら、進み具合の表示は消え、「中止」は押せなくなり、また実行できるようになる
                Assert.Equal("", app.Progress);
                Assert.False(app.IsStopEnabled);
                Assert.True(app.IsExecuteAllEnabled);
            }
        }

        [Fact]
        public void 中止を2回押しても_止めている途中で実行を押しても_何も起きず_止まった後はもう一度実行できる()
        {
            using (var app = StartSlowApp())
            {
                SetCopyRows(app, 3);
                app.StartExecuteAll();
                app.WaitUntil(() => app.GetStatus(0) == app.Msg.Status.Done, "1行目が終わりませんでした。");

                app.Stop();
                app.Stop();            // 2回目は何もしない
                app.StartExecuteAll(); // 止めている途中（まだ実行中）なので、何もしない
                app.WaitForRunEnd(1);

                Assert.Equal(1, app.CountLog(app.Msg.Run.StopRequested));
                Assert.Equal(1, app.CountLog(app.Msg.Run.RunStart));
                Assert.Equal(app.Msg.Status.Unexec, app.GetStatus(1));

                // 止まった後は、もう一度最初から実行できる
                app.StartExecuteAll();
                app.WaitForRunEnd(2);

                Assert.Equal(2, app.CountLog(app.Msg.Run.RunStart));
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(app.Msg.Status.Done, app.GetStatus(i));
                    Assert.True(Copied(app, i));
                }
            }
        }

        [Fact]
        public void 実行中にすべて実行をもう一度押しても_2つ目の実行は始まらない()
        {
            // 行の間に待ち時間があるので、2回目のクリックは必ず1回目の実行中に届く
            // （待ち時間がないと、2回目が届く前に1回目が終わってしまい、正しく2回実行される）
            using (var app = StartSlowApp())
            {
                SetCopyRows(app, 2);

                app.StartExecuteAll();
                app.StartExecuteAll();
                app.WaitForRunEnd(1);

                Assert.Equal(1, app.CountLog(app.Msg.Run.RunStart));
                Assert.Equal(app.Msg.Status.Done, app.GetStatus(1));
                Assert.True(app.IsExecuteAllEnabled);
                Assert.Equal(1, app.CountLog(app.Msg.Run.RunStart)); // 終わった後に、残っていたクリックで始まったりもしない
            }
        }

        [Fact]
        public void 自動処理の実行中に中止すると_計画ファイルはErrorへ移り_自動処理が止まる()
        {
            using (var app = StartSlowApp())
            {
                string src1 = Path.Combine(app.WorkDir, @"in\1.txt");
                string src2 = Path.Combine(app.WorkDir, @"in\2.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(src1));
                File.WriteAllText(src1, "1");
                File.WriteAllText(src2, "2");
                string dest = Path.Combine(app.WorkDir, @"out\");
                TaskFile.Save(Path.Combine(app.Config.AutoRunPlanDir, "plan.tsv"), new[]
                {
                    new TransferTask { Action = TransferAction.Copy, Source = src1, Destination = dest, Mode = TransferMode.FolderAsIs },
                    new TransferTask { Action = TransferAction.Copy, Source = src2, Destination = dest, Mode = TransferMode.FolderAsIs },
                });

                app.SetAutoRun(true);
                app.WaitUntil(() => app.Log.Contains(Messages.Format(app.Msg.Run.RowSuccess, 1)), "1行目が終わりませんでした。");
                app.Stop();

                app.WaitUntil(() => File.Exists(Path.Combine(app.Config.AutoRunErrorDir, "plan.tsv")), "計画ファイルが Error フォルダへ移りませんでした。");
                app.WaitUntil(() => !app.IsAutoRunOn, "自動処理が止まりませんでした。");
                Assert.Contains(Messages.Format(app.Msg.Run.Stopped, 2), app.Log);
                Assert.False(File.Exists(Path.Combine(app.WorkDir, @"out\2.txt")));
            }
        }
    }
}
