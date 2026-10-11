using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Moq;
using Xunit;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// 「退避」と「復元」のテスト。何度実行しても結果が同じになる（冪等）ことと、元の状態に戻ることを確かめる。
    /// </summary>
    public class BackupRestoreTests : IDisposable
    {
        private const string Data = @"D:\BizAppData";                         // 業務データ
        private const string Root = @"D:\ReleaseBackup\v1.2.0";               // 退避先
        private const string Snapshot = @"D:\ReleaseBackup\v1.2.0\D_BizAppData"; // Data の退避

        private readonly TempConfigFiles _configFiles = new TempConfigFiles();
        private readonly AppConfig _config;
        private readonly MockFileSystem _fs;
        private readonly FileTransferEngine _engine;

        public BackupRestoreTests()
        {
            _config = _configFiles.LoadConfig();
            _fs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { $@"{Data}\data.txt", new MockFileData("リリース前") },
                { $@"{Data}\sub\a.txt", new MockFileData("a") },
            });
            _engine = new FileTransferEngine(new Mock<Action<string>>().Object, _config, _fs);
        }

        public void Dispose() => _configFiles.Dispose();

        private void Backup() => _engine.ProcessRow(TransferAction.Backup, Data, Root, TransferMode.BackupCopy, false, 1);
        private void Restore() => _engine.ProcessRow(TransferAction.Restore, Root, Data, TransferMode.RestoreReplace, false, 1);
        private string Read(string path) => _fs.File.ReadAllText(path);

        #region 退避

        [Fact]
        public void 退避_まるごとコピーされ_元はそのまま残る()
        {
            Backup();

            Assert.Equal("リリース前", Read($@"{Snapshot}\data.txt"));
            Assert.Equal("a", Read($@"{Snapshot}\sub\a.txt"));
            Assert.Equal("リリース前", Read($@"{Data}\data.txt"));
        }

        [Fact]
        public void 退避_2回実行しても_最初の退避は上書きされない()
        {
            Backup();
            _fs.File.WriteAllText($@"{Data}\data.txt", "リリース後");

            Backup();

            Assert.Equal("リリース前", Read($@"{Snapshot}\data.txt"));
        }

        [Fact]
        public void 退避_コピーが途中で止まっていた場合_やり直して正しい退避になる()
        {
            _fs.AddFile($@"{Snapshot}.tmp\data.txt", new MockFileData("コピーしかけ"));

            Backup();

            Assert.False(_fs.Directory.Exists(Snapshot + ".tmp"));
            Assert.Equal("リリース前", Read($@"{Snapshot}\data.txt"));
        }

        [Fact]
        public void 退避_退避先が退避するフォルダの中にある場合_エラーになる()
        {
            Assert.Throws<TransferException>(() =>
                _engine.ProcessRow(TransferAction.Backup, Data, $@"{Data}\backup", TransferMode.BackupCopy, false, 1));
        }

        #endregion

        #region 復元

        [Fact]
        public void 復元_退避した状態に戻り_戻す前の状態も残る()
        {
            Backup();
            // リリース後にデータが変わった
            _fs.File.WriteAllText($@"{Data}\data.txt", "リリース後");
            _fs.AddFile($@"{Data}\new.txt", new MockFileData("リリース後に増えた"));

            Restore();

            Assert.Equal("リリース前", Read($@"{Data}\data.txt"));
            Assert.False(_fs.File.Exists($@"{Data}\new.txt"));
            // 戻す前の状態（リリース後に増えたデータ）は rolledback に残っている
            string rolledBack = _fs.Directory.GetDirectories(Root, "D_BizAppData.rolledback_*").Single();
            Assert.Equal("リリース後に増えた", Read(Path.Combine(rolledBack, "new.txt")));
        }

        [Fact]
        public void 復元_2回実行しても_2回目は何もしない()
        {
            Backup();
            Restore();
            _fs.AddFile($@"{Data}\after.txt", new MockFileData("復元後に作った"));

            Restore();

            Assert.True(_fs.File.Exists($@"{Data}\after.txt"));
            Assert.Single(_fs.Directory.GetDirectories(Root, "D_BizAppData.rolledback_*"));
        }

        [Fact]
        public void 復元_退避先が戻す先の中にある場合_エラーになり何も変えない()
        {
            // 戻す先の中に退避先があると、今の状態を退避するときに退避先ごと動いてしまう
            _fs.AddDirectory($@"{Data}\backup");

            Assert.Throws<TransferException>(() =>
                _engine.ProcessRow(TransferAction.Restore, $@"{Data}\backup", Data, TransferMode.RestoreReplace, false, 1));

            Assert.Equal("リリース前", Read($@"{Data}\data.txt"));
        }

        [Fact]
        public void 復元_退避がない場合_エラーになり何も変えない()
        {
            Assert.Throws<TransferException>(() => Restore());

            Assert.Equal("リリース前", Read($@"{Data}\data.txt"));
        }

        [Fact]
        public void 配置の取り消し_復元すると元に戻り_もう一度配置も復元もできる()
        {
            const string release = @"C:\Release\v1.2.0\App";
            const string target = @"D:\App";
            _fs.AddFile($@"{release}\App.exe", new MockFileData("new"));
            _fs.AddFile($@"{target}\App.exe", new MockFileData("old"));
            string deployRoot = FileTransferEngine.DeployBackupRoot(_config, "v1.2.0");
            void Deploy() => _engine.ProcessRow(TransferAction.Deploy, release, target, TransferMode.DeployReplace, false, 1);
            void Undo() => _engine.ProcessRow(TransferAction.Restore, deployRoot, target, TransferMode.RestoreReplace, false, 1);

            Deploy();
            Undo();
            Assert.Equal("old", Read($@"{target}\App.exe"));

            // 復元で「適用済み」の印が消えるので、もう一度配置できる
            Deploy();
            Assert.Equal("new", Read($@"{target}\App.exe"));

            // 配置で「復元済み」の印が消えるので、もう一度復元できる
            Undo();
            Assert.Equal("old", Read($@"{target}\App.exe"));
        }

        #endregion
    }
}
