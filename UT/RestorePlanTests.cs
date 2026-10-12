using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FileTransfer2;

namespace UT
{
    public class RestorePlanTests : IDisposable
    {
        private readonly TempConfigFiles _configFiles = new TempConfigFiles();
        private readonly AppConfig _config;

        public RestorePlanTests()
        {
            _config = _configFiles.LoadConfig();
        }

        public void Dispose() => _configFiles.Dispose();

        private static TransferTask Task(TransferAction action, string src, string dest)
        {
            var modes = TransferTask.ModesFor(action);
            return new TransferTask { Action = action, Source = src, Destination = dest, Mode = modes[0] };
        }

        [Fact]
        public void 退避と配置の計画から_逆の順番で復元の計画が作られる()
        {
            var plan = new List<TransferTask>
            {
                Task(TransferAction.Backup, @"D:\BizAppData", @"D:\ReleaseBackup\v1.2.0"),
                Task(TransferAction.Deploy, @"C:\Release\v1.2.0\App", @"D:\App"),
            };

            var restore = RestorePlan.Create(plan, _config);

            Assert.Equal(2, restore.Count);
            // 1行目：配置の取り消し（後の行から戻す）
            Assert.Equal(TransferAction.Restore, restore[0].Action);
            Assert.Equal(FileTransferEngine.DeployBackupRoot(_config, "v1.2.0"), restore[0].Source);
            Assert.Equal(@"D:\App", restore[0].Destination);
            // 2行目：退避の取り消し（転送元と転送先が入れ替わる）
            Assert.Equal(@"D:\ReleaseBackup\v1.2.0", restore[1].Source);
            Assert.Equal(@"D:\BizAppData", restore[1].Destination);
            // 作った計画は、そのまま実行できる正しい内容
            Assert.All(restore, task => Assert.Null(task.Validate(_config)));
        }

        [Fact]
        public void 戻せない処理が混ざっている場合_作らない()
        {
            var plan = new List<TransferTask>
            {
                Task(TransferAction.Backup, @"D:\BizAppData", @"D:\ReleaseBackup\v1.2.0"),
                Task(TransferAction.Copy, @"D:\a.txt", @"D:\out"),
            };

            Assert.Null(RestorePlan.Create(plan, _config));
        }

        [Fact]
        public void 入力エラーがある場合や行がない場合_作らない()
        {
            Assert.Null(RestorePlan.Create(new List<TransferTask> { Task(TransferAction.Backup, @"D:\BizAppData", "") }, _config));
            Assert.Null(RestorePlan.Create(new List<TransferTask> { Task(TransferAction.Deploy, @"C:\App", @"D:\App") }, _config)); // リリース番号がない
            Assert.Null(RestorePlan.Create(new List<TransferTask>(), _config));
        }

        [Fact]
        public void 実行した行から作る場合_戻す元の退避がある行だけになる()
        {
            var executed = new List<TransferTask>
            {
                Task(TransferAction.Backup, @"D:\BizAppData", @"D:\ReleaseBackup\v1.2.0"),
                Task(TransferAction.Deploy, @"C:\Release\v1.2.0\App", @"D:\App"), // 退避のコピーの途中で止まった（退避がない）
            };
            string dataSnapshot = Path.Combine(@"D:\ReleaseBackup\v1.2.0", FileTransferEngine.SnapshotName(@"D:\BizAppData"));

            var restore = RestorePlan.CreateFromExecuted(executed, _config, path => path == dataSnapshot);

            // 配置の行は入らない（入れると、復元のときにその行のエラーで止まり、業務データを戻せなくなる）
            var only = Assert.Single(restore);
            Assert.Equal(@"D:\BizAppData", only.Destination);
        }

        [Fact]
        public void 実行した行から作る場合_戻すものがなければ作らない()
        {
            var executed = new List<TransferTask> { Task(TransferAction.Backup, @"D:\BizAppData", @"D:\ReleaseBackup\v1.2.0") };

            Assert.Null(RestorePlan.CreateFromExecuted(executed, _config, path => false));
            Assert.Null(RestorePlan.CreateFromExecuted(new List<TransferTask> { Task(TransferAction.Copy, @"D:\a.txt", @"D:\out") }, _config, path => true));
        }

        [Fact]
        public void 実行した内容から作る復元用の保存先_プロファイル名と日時が付く()
        {
            string path = RestorePlan.PathForExecuted(@"C:\Profiles", "Release", _config.Msg, new DateTime(2026, 10, 13, 15, 30, 0));

            Assert.Equal(@"C:\Profiles\Release_復元_20261013153000.tsv", path);
        }
    }
}
