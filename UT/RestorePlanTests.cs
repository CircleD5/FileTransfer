using System;
using System.Collections.Generic;
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
        public void 復元用の計画の保存先_名前の後ろに印が付く()
        {
            Assert.Equal(@"C:\Profiles\v1.2.0_復元.tsv", RestorePlan.PathFor(@"C:\Profiles\v1.2.0.tsv", _config.Msg));
        }
    }
}
