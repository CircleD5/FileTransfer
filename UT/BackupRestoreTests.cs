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
        private static readonly string Snapshot = Path.Combine(Root, FileTransferEngine.SnapshotName(Data)); // Data の退避

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
        public void 退避_触らない場所は退避に入らない()
        {
            _configFiles.WriteForbiddenPaths($@"{Data}\sub");
            var engine = new FileTransferEngine(new Mock<Action<string>>().Object, _configFiles.LoadConfig(), _fs);

            engine.ProcessRow(TransferAction.Backup, Data, Root, TransferMode.BackupCopy, false, 1);

            Assert.Equal("リリース前", Read($@"{Snapshot}\data.txt"));
            Assert.False(_fs.Directory.Exists($@"{Snapshot}\sub"));
        }

        [Fact]
        public void 退避_退避先が退避するフォルダの中にある場合_エラーになる()
        {
            Assert.Throws<TransferException>(() =>
                _engine.ProcessRow(TransferAction.Backup, Data, $@"{Data}\backup", TransferMode.BackupCopy, false, 1));
        }

        #endregion

        #region 退避の名前とパスの長さ

        /// <summary>
        /// 事前チェックで使う、いちばん長くなる退避のフォルダ（復元のときに作る「〇〇.rolledback_日時_連番」）。
        /// </summary>
        private static readonly string LongestSnapshot = Snapshot + ".rolledback_yyyyMMddHHmmss_99";

        /// <summary>
        /// LongestSnapshot の下に置いたとき、パス全体がちょうど totalLength 文字になる相対パス（フォルダ1つ＋ファイル）。
        /// </summary>
        private static string RelativePathFor(int totalLength)
        {
            int nameLength = totalLength - (LongestSnapshot.Length + 1) - 101; // 101 ＝ フォルダ名100文字＋ \
            return new string('a', 100) + "\\" + new string('b', nameLength);
        }

        [Fact]
        public void 退避の名前_同じパスなら同じ名前で_大文字小文字と末尾の円記号は区別しない()
        {
            string name = FileTransferEngine.SnapshotName(@"D:\BizAppData");

            Assert.Equal(name, FileTransferEngine.SnapshotName(@"d:\bizappdata\"));
            Assert.NotEqual(name, FileTransferEngine.SnapshotName(@"D:\BizAppData2"));
        }

        [Fact]
        public void 退避の名前_元のパスが長くても12文字()
        {
            string longPath = @"D:\" + string.Join(@"\", Enumerable.Repeat("very_long_folder_name", 10));

            Assert.Equal(12, FileTransferEngine.SnapshotName(longPath).Length);
        }

        [Fact]
        public void 退避_どのフォルダの退避かがpathファイルに残る()
        {
            Backup();

            Assert.Equal(Data, Read(Snapshot + ".path"));
        }

        [Fact]
        public void 退避_名前が別のフォルダの退避とぶつかっている場合_エラーになり何もしない()
        {
            // 同じ名前の退避が、別のフォルダ（D:\Other）のものとして記録されている
            _fs.AddFile(Snapshot + ".path", new MockFileData(@"D:\Other"));

            var ex = Assert.Throws<TransferException>(() => Backup());

            Assert.Equal(Messages.Format(_config.Msg.Transfer.SnapshotNameConflict, Snapshot + ".path", @"D:\Other", Data), ex.Message);
            Assert.False(_fs.Directory.Exists(Snapshot));
        }

        [Fact]
        public void 退避_作るパスがちょうど上限の長さなら実行される()
        {
            string relative = RelativePathFor(259);
            _fs.AddFile(Path.Combine(Data, relative), new MockFileData("深いファイル"));

            Backup();

            Assert.Equal("深いファイル", Read(Path.Combine(Snapshot, relative)));
        }

        [Fact]
        public void 退避_作るパスが上限を超える場合_いちばん長いパスと超える文字数が出て_退避は作られない()
        {
            string relative = RelativePathFor(260);
            _fs.AddFile(Path.Combine(Data, relative), new MockFileData("深いファイル"));

            var ex = Assert.Throws<TransferException>(() => Backup());

            Assert.Equal(Messages.Format(_config.Msg.Transfer.PathTooLong, 259, 1, LongestSnapshot + "\\" + relative), ex.Message);
            Assert.False(_fs.Directory.Exists(Snapshot));
        }

        [Fact]
        public void 退避_作るフォルダのパスが上限を超える場合_エラーになる()
        {
            // フォルダの上限は 247 文字（ファイルより短い）
            string folder = new string('c', 248 - (LongestSnapshot.Length + 1));
            _fs.AddDirectory(Path.Combine(Data, folder));

            var ex = Assert.Throws<TransferException>(() => Backup());

            Assert.Equal(Messages.Format(_config.Msg.Transfer.PathTooLong, 247, 1, LongestSnapshot + "\\" + folder), ex.Message);
        }

        [Fact]
        public void 復元_戻す先に深いパスが増えていて上限を超える場合_エラーになり何も変えない()
        {
            Backup();
            // リリース後に、深いパスのファイルが増えた（戻す直前の状態として退避できない）
            string relative = RelativePathFor(260);
            _fs.AddFile(Path.Combine(Data, relative), new MockFileData("リリース後に増えた"));
            _fs.File.WriteAllText($@"{Data}\data.txt", "リリース後");

            Assert.Throws<TransferException>(() => Restore());

            Assert.Equal("リリース後", Read($@"{Data}\data.txt"));
            Assert.Empty(_fs.Directory.GetDirectories(Root, FileTransferEngine.SnapshotName(Data) + ".rolledback_*"));
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
            string rolledBack = _fs.Directory.GetDirectories(Root, FileTransferEngine.SnapshotName(Data) + ".rolledback_*").Single();
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
            Assert.Single(_fs.Directory.GetDirectories(Root, FileTransferEngine.SnapshotName(Data) + ".rolledback_*"));
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
