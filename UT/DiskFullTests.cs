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
    /// 配置・退避・復元の途中で、ディスクの空き容量が足りなくなったときのテスト。
    /// どこで止まっても取り返しのつかない状態にならず、容量を空けて再実行するか、復元すれば元に戻せることを確かめる。
    /// </summary>
    public class DiskFullTests : IDisposable
    {
        private const string Release = @"C:\Release\v1.2.0\App";
        private const string Target = @"D:\App";
        private const string Data = @"D:\BizAppData";
        private const string DataBackupRoot = @"D:\ReleaseBackup\v1.2.0";

        private readonly TempConfigFiles _configFiles = new TempConfigFiles();
        private readonly AppConfig _config;
        private readonly MockFileSystem _mock;
        private readonly DiskFullFileSystem _fs;
        private readonly FileTransferEngine _engine;
        private readonly string _backup; // 配置の退避

        public DiskFullTests()
        {
            _configFiles.WriteDeployKeep(Target + @"\settings.config", Target + @"\Logs");
            _config = _configFiles.LoadConfig();
            _mock = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { $@"{Release}\App.exe", new MockFileData("new exe") },
                { $@"{Release}\settings.config", new MockFileData("初期設定") },
                // 本番：入れ替えるもの2つと、残すもの2つ（退避ではこの4つがコピーされる）
                { $@"{Target}\App.exe", new MockFileData("old exe") },
                { $@"{Target}\old.dll", new MockFileData("新版にはないファイル") },
                { $@"{Target}\settings.config", new MockFileData("本番の設定") },
                { $@"{Target}\Logs\a.log", new MockFileData("過去のログ") },
                { $@"{Data}\data.txt", new MockFileData("リリース前") },
                { $@"{Data}\sub\a.txt", new MockFileData("a") },
            });
            _fs = new DiskFullFileSystem(_mock);
            _engine = new FileTransferEngine(new Mock<Action<string>>().Object, _config, _fs);
            _backup = Path.Combine(_config.BackupDir, "v1.2.0", FileTransferEngine.SnapshotName(Target));
        }

        public void Dispose() => _configFiles.Dispose();

        private void Deploy() => _engine.ProcessRow(TransferAction.Deploy, Release, Target, TransferMode.DeployReplace, false, 1);
        private void Undo() => _engine.ProcessRow(TransferAction.Restore, FileTransferEngine.DeployBackupRoot(_config, "v1.2.0"), Target, TransferMode.RestoreReplace, false, 1);
        private string Read(string path) => _mock.File.ReadAllText(path);

        /// <summary>本番が配置の前の状態（旧版）のままか</summary>
        private void AssertOldVersion()
        {
            Assert.Equal("old exe", Read($@"{Target}\App.exe"));
            Assert.Equal("新版にはないファイル", Read($@"{Target}\old.dll"));
            Assert.Equal("本番の設定", Read($@"{Target}\settings.config"));
            Assert.Equal("過去のログ", Read($@"{Target}\Logs\a.log"));
        }

        #region 配置

        [Fact]
        public void 配置_退避のコピー中に容量不足_本番は変わらず_空けて再実行すれば配置できる()
        {
            _fs.FailAfterCopies(2); // 退避の4つのうち、2つ目まででいっぱいになる

            Assert.Throws<IOException>(() => Deploy());

            AssertOldVersion();                                   // 本番は変わっていない
            Assert.False(_mock.Directory.Exists(_backup));        // 正式な退避はできていない（.tmp だけ）
            Assert.False(_mock.File.Exists(_backup + ".done"));

            _fs.FreeUp();
            Deploy();

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
            Assert.False(_mock.Directory.Exists(_backup + ".tmp")); // 途中までのコピーは片付く
            Assert.Equal("old exe", Read($@"{_backup}\App.exe"));
        }

        [Fact]
        public void 配置_新しい版を置く途中で容量不足_退避はそろっていて_空けて再実行すれば配置できる()
        {
            _fs.FailAfterCopies(4); // 退避（4つ）は終わり、新しい版を置くところでいっぱいになる

            Assert.Throws<IOException>(() => Deploy());

            // 本番は入れ替えの途中（旧版は消え、新版はまだない）。ただし退避はそろっていて、残すものもその場にある
            Assert.False(_mock.File.Exists($@"{Target}\App.exe"));
            Assert.Equal("本番の設定", Read($@"{Target}\settings.config"));
            Assert.Equal("old exe", Read($@"{_backup}\App.exe"));
            Assert.Equal("新版にはないファイル", Read($@"{_backup}\old.dll"));
            Assert.False(_mock.File.Exists(_backup + ".done"));

            _fs.FreeUp();
            Deploy();

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
            Assert.False(_mock.File.Exists($@"{Target}\old.dll"));
            Assert.Equal("本番の設定", Read($@"{Target}\settings.config"));
        }

        [Fact]
        public void 配置_退避のコピー中に止まった後に誤って復元しても_戻す元がないエラーになり何も変わらない()
        {
            _fs.FailAfterCopies(2);
            Assert.Throws<IOException>(() => Deploy());
            _fs.FreeUp();

            // 正式な退避はまだない（.tmp だけ）ので、中途半端な退避から戻すことはない
            var ex = Assert.Throws<TransferException>(() => Undo());

            Assert.Equal(Messages.Format(_config.Msg.Transfer.RestoreNoBackup, _backup), ex.Message);
            AssertOldVersion();
        }

        [Fact]
        public void リリース用の計画が退避の途中で止まった後に_復元用の計画を実行しても_何も変わらない()
        {
            // リリース用：1. 業務データを退避 → 2. 配置。1行目の退避のコピー中に容量不足で止まる（2行目は実行されない）
            _fs.FailAfterCopies(1);
            Assert.Throws<IOException>(() => _engine.ProcessRow(TransferAction.Backup, Data, DataBackupRoot, TransferMode.BackupCopy, false, 1));
            _fs.FreeUp();

            // 復元用（逆の順番）：1. 配置を戻す → 2. 業務データを戻す。どちらも戻す元の退避がないので、何も変えずにエラーになる
            Assert.Throws<TransferException>(() => Undo());
            Assert.Throws<TransferException>(() => _engine.ProcessRow(TransferAction.Restore, DataBackupRoot, Data, TransferMode.RestoreReplace, false, 2));

            AssertOldVersion();
            Assert.Equal("リリース前", Read($@"{Data}\data.txt"));
            Assert.Equal("a", Read($@"{Data}\sub\a.txt"));
        }

        [Fact]
        public void 配置_新しい版を置く途中で容量不足_復元すれば配置の前に戻る()
        {
            _fs.FailAfterCopies(4);
            Assert.Throws<IOException>(() => Deploy());

            _fs.FreeUp();
            Undo();

            AssertOldVersion();
        }

        #endregion

        #region 復元

        [Fact]
        public void 復元_戻す直前の状態のコピー中に容量不足_本番は変わらず_空けて再実行すれば戻せる()
        {
            Deploy();
            _mock.AddFile($@"{Target}\Logs\after.log", new MockFileData("リリース後のログ"));
            _fs.FailAfterCopies(1); // 戻す直前の状態（.rolledback）のコピー中にいっぱいになる

            Assert.Throws<IOException>(() => Undo());

            Assert.Equal("new exe", Read($@"{Target}\App.exe")); // 本番は変わっていない
            Assert.Equal("リリース後のログ", Read($@"{Target}\Logs\after.log"));

            _fs.FreeUp();
            Undo();

            AssertOldVersion();
        }

        [Fact]
        public void 復元_戻す途中で容量不足_退避も戻す直前の状態も残っていて_空けて再実行すれば戻せる()
        {
            Deploy();
            _mock.AddFile($@"{Target}\Logs\after.log", new MockFileData("リリース後のログ"));
            _fs.FailAfterCopies(4); // .rolledback（今の4つ）は終わり、退避を戻すところでいっぱいになる

            Assert.Throws<IOException>(() => Undo());

            // 本番は戻す途中。ただし、退避（配置の前の状態）も、戻す直前の状態（リリース後のログを含む）も残っている
            Assert.Equal("old exe", Read($@"{_backup}\App.exe"));
            string rolledBack = _mock.Directory.GetDirectories(Path.GetDirectoryName(_backup), Path.GetFileName(_backup) + ".rolledback_*").Single();
            Assert.Equal("リリース後のログ", Read($@"{rolledBack}\Logs\after.log"));

            _fs.FreeUp();
            Undo();

            AssertOldVersion();
        }

        #endregion

        #region 退避

        [Fact]
        public void 退避_コピー中に容量不足_元は変わらず_空けて再実行すれば退避できる()
        {
            string snapshot = Path.Combine(DataBackupRoot, FileTransferEngine.SnapshotName(Data));
            _fs.FailAfterCopies(1);

            Assert.Throws<IOException>(() => _engine.ProcessRow(TransferAction.Backup, Data, DataBackupRoot, TransferMode.BackupCopy, false, 1));

            Assert.Equal("リリース前", Read($@"{Data}\data.txt"));
            Assert.False(_mock.Directory.Exists(snapshot)); // 「退避済み」と誤解されない

            _fs.FreeUp();
            _engine.ProcessRow(TransferAction.Backup, Data, DataBackupRoot, TransferMode.BackupCopy, false, 1);

            Assert.Equal("リリース前", Read($@"{snapshot}\data.txt"));
            Assert.Equal("a", Read($@"{snapshot}\sub\a.txt"));
        }

        #endregion
    }
}
