using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Moq;
using Xunit;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// 「配置」（退避して入れ替え）のテスト。特に、何度実行しても結果が同じになる（冪等）ことを確かめる。
    /// </summary>
    public class DeployTests : IDisposable
    {
        private const string Release = @"C:\Release\v1.2.0\App";   // 転送元（リリース番号 v1.2.0）
        private const string Target = @"D:\App";                   // 配置先（本番）

        private readonly TempConfigFiles _configFiles = new TempConfigFiles();
        private readonly AppConfig _config;
        private readonly string _backup;   // この配置の退避先
        private readonly MockFileSystem _fs;
        private readonly FileTransferEngine _engine;

        public DeployTests()
        {
            _configFiles.WriteDeployKeep("# 残すもの", "settings.config", @"\Logs");
            _config = _configFiles.LoadConfig();
            _backup = Path.Combine(_config.BackupDir, "v1.2.0", "D_App");

            _fs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                // 新版
                { $@"{Release}\App.exe", new MockFileData("new exe") },
                { $@"{Release}\settings.config", new MockFileData("初期設定") },
                // 本番（旧版）
                { $@"{Target}\App.exe", new MockFileData("old exe") },
                { $@"{Target}\old.dll", new MockFileData("新版にはないファイル") },
                { $@"{Target}\settings.config", new MockFileData("本番の設定") },
                { $@"{Target}\Logs\a.log", new MockFileData("過去のログ") },
            });
            _engine = new FileTransferEngine(new Mock<Action<string>>().Object, _config, _fs);
        }

        public void Dispose() => _configFiles.Dispose();

        private void Deploy() => _engine.ProcessRow(TransferAction.Deploy, Release, Target, TransferMode.DeployReplace, false, 1);

        [Fact]
        public void 配置_本番が新版に入れ替わり_残すものは本番のまま()
        {
            Deploy();

            Assert.Equal("new exe", _fs.File.ReadAllText($@"{Target}\App.exe"));
            Assert.False(_fs.File.Exists($@"{Target}\old.dll"));                              // 完全な入れ替え
            Assert.Equal("本番の設定", _fs.File.ReadAllText($@"{Target}\settings.config"));    // 残すもの（ファイル）
            Assert.Equal("過去のログ", _fs.File.ReadAllText($@"{Target}\Logs\a.log"));          // 残すもの（フォルダ）
        }

        [Fact]
        public void 配置_旧版はまるごと退避され_適用済みの印が付く()
        {
            Deploy();

            Assert.Equal("old exe", _fs.File.ReadAllText($@"{_backup}\App.exe"));
            Assert.True(_fs.File.Exists($@"{_backup}\old.dll"));
            Assert.True(_fs.File.Exists(_backup + ".done"));
        }

        [Fact]
        public void 配置_履歴に1行だけ記録され_2回目は記録されない()
        {
            string history = Path.Combine(_config.LogDir, FileTransferEngine.DeployHistoryFile);

            Deploy();
            Deploy();

            string[] lines = _fs.File.ReadAllLines(history);
            Assert.Single(lines);
            Assert.Contains("v1.2.0", lines[0]);
            Assert.Contains(Target, lines[0]);
            Assert.Contains(Environment.UserName, lines[0]);
        }

        [Fact]
        public void 配置_2回実行しても_2回目は何もしない()
        {
            Deploy();
            // 配置の後に、本番でログが増えた
            _fs.File.WriteAllText($@"{Target}\Logs\b.log", "配置後のログ");

            Deploy();

            Assert.Equal("配置後のログ", _fs.File.ReadAllText($@"{Target}\Logs\b.log"));   // 退避から戻し直されて消えたりしない
            Assert.Equal("old exe", _fs.File.ReadAllText($@"{_backup}\App.exe"));            // 退避が新版で上書きされない
        }

        [Fact]
        public void 配置_途中で止まった後の再実行_旧版の退避は守られ_最初から置き直す()
        {
            // 前回、退避まで済んで新版を置く途中で止まった状態を作る
            foreach (string file in _fs.Directory.GetFiles(Target, "*", SearchOption.AllDirectories))
                _fs.AddFile(_backup + file.Substring(Target.Length), new MockFileData(_fs.File.ReadAllText(file)));
            _fs.Directory.Delete(Target, true);
            _fs.AddFile($@"{Target}\half.tmp", new MockFileData("置きかけ"));

            Deploy();

            Assert.False(_fs.File.Exists($@"{Target}\half.tmp"));
            Assert.Equal("new exe", _fs.File.ReadAllText($@"{Target}\App.exe"));
            Assert.Equal("本番の設定", _fs.File.ReadAllText($@"{Target}\settings.config"));
            Assert.Equal("old exe", _fs.File.ReadAllText($@"{_backup}\App.exe"));
        }

        [Fact]
        public void 配置_別のドライブへの退避コピーが途中で止まっていた場合_やり直して本番は消えない()
        {
            // 前回、退避先へのコピー（.tmp）の途中で止まった状態。正式な退避フォルダはまだない
            _fs.AddFile($@"{_backup}.tmp\App.exe", new MockFileData("コピーしかけ"));

            Deploy();

            Assert.False(_fs.Directory.Exists(_backup + ".tmp"));
            Assert.Equal("old exe", _fs.File.ReadAllText($@"{_backup}\App.exe"));
            Assert.True(_fs.File.Exists($@"{_backup}\old.dll"));
            Assert.Equal("new exe", _fs.File.ReadAllText($@"{Target}\App.exe"));
        }

        [Fact]
        public void 配置_退避先と同じドライブの場合_移動で退避される()
        {
            // 退避先（一時フォルダ）と同じドライブに配置先を作る
            string drive = Path.GetPathRoot(_config.BackupDir);
            string target = Path.Combine(drive, @"Prod\App");
            _fs.AddFile(Path.Combine(target, "App.exe"), new MockFileData("old exe"));

            _engine.ProcessRow(TransferAction.Deploy, Release, target, TransferMode.DeployReplace, false, 1);

            string backup = Path.Combine(_config.BackupDir, "v1.2.0", target.Replace(":", "").Replace('\\', '_'));
            Assert.Equal("old exe", _fs.File.ReadAllText(Path.Combine(backup, "App.exe")));
            Assert.Equal("new exe", _fs.File.ReadAllText(Path.Combine(target, "App.exe")));
        }

        [Fact]
        public void 配置_初めての配置_新版が置かれる()
        {
            _fs.Directory.Delete(Target, true);

            Deploy();

            Assert.Equal("new exe", _fs.File.ReadAllText($@"{Target}\App.exe"));
            Assert.Equal("初期設定", _fs.File.ReadAllText($@"{Target}\settings.config"));   // 本番になかったので新版のもの
            Assert.True(_fs.File.Exists(_backup + ".done"));
        }

        [Theory]
        [InlineData(@"C:\App", @"D:\App")]                        // 親フォルダがドライブ直下で、リリース番号がない
        [InlineData(@"C:\Release\v1.2.0\App\App.exe", @"D:\App")] // 転送元がファイル
        [InlineData(@"D:\App\Release\v1\App", @"D:\App")]          // 転送元が配置先の中にある（退避すると一緒に動いてしまう）
        public void 配置_転送元が正しくない場合_何もせずエラーになる(string source, string target)
        {
            _fs.AddFile(@"C:\App\x.txt", new MockFileData("x"));
            _fs.AddFile(@"D:\App\Release\v1\App\x.txt", new MockFileData("x"));

            Assert.Throws<TransferException>(() =>
                _engine.ProcessRow(TransferAction.Deploy, source, target, TransferMode.DeployReplace, false, 1));

            Assert.Equal("old exe", _fs.File.ReadAllText($@"{Target}\App.exe"));
        }
    }
}
