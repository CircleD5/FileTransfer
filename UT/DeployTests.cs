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
    /// 「配置」（退避して入れ替え）と、その取り消し（復元）のテスト。
    /// 何度実行しても結果が同じになる（冪等）こと、残すもの（deploy_keep.txt）と触らない場所（forbidden_paths.txt）の扱い、
    /// 途中で止まったときに安全なことを確かめる。
    /// </summary>
    public class DeployTests : IDisposable
    {
        private const string Release = @"C:\Release\v1.2.0\App";   // 転送元（リリース番号 v1.2.0）
        private const string Target = @"D:\App";                   // 配置先（本番）

        /// <summary>残すもの（その場に残し、退避もする）：本番の設定とログ</summary>
        private static readonly string[] DefaultKeep = { "# 残すもの", Target + @"\settings.config", Target + @"\Logs" };
        /// <summary>触らない場所（退避もしない）：重いランタイム</summary>
        private static readonly string[] DefaultForbidden = { "# 触らない場所", Target + @"\Runtime" };

        private readonly TempConfigFiles _configFiles = new TempConfigFiles();
        private readonly MockFileSystem _fs;
        private AppConfig _config;
        private FileTransferEngine _engine;
        private readonly string _backup;   // この配置の退避

        public DeployTests()
        {
            _fs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                // 新版
                { $@"{Release}\App.exe", new MockFileData("new exe") },
                { $@"{Release}\settings.config", new MockFileData("初期設定") },
                { $@"{Release}\Runtime\big.bin", new MockFileData("新版のランタイム") },
                // 本番（旧版）
                { $@"{Target}\App.exe", new MockFileData("old exe") },
                { $@"{Target}\old.dll", new MockFileData("新版にはないファイル") },
                { $@"{Target}\settings.config", new MockFileData("本番の設定") },
                { $@"{Target}\Logs\a.log", new MockFileData("過去のログ") },
                { $@"{Target}\Runtime\big.bin", new MockFileData("本番のランタイム") },
            });
            UseLists(DefaultKeep, DefaultForbidden);
            _backup = Path.Combine(_config.BackupDir, "v1.2.0", FileTransferEngine.SnapshotName(Target));
        }

        public void Dispose() => _configFiles.Dispose();

        /// <summary>
        /// deploy_keep.txt と forbidden_paths.txt を書き換えて、設定とエンジンを作り直す。
        /// </summary>
        private void UseLists(string[] keepLines, string[] forbiddenLines)
        {
            _configFiles.WriteDeployKeep(keepLines);
            _configFiles.WriteForbiddenPaths(forbiddenLines);
            _config = _configFiles.LoadConfig();
            _engine = new FileTransferEngine(new Mock<Action<string>>().Object, _config, _fs);
        }

        private void Deploy() => _engine.ProcessRow(TransferAction.Deploy, Release, Target, TransferMode.DeployReplace, false, 1);
        private void Undo() => _engine.ProcessRow(TransferAction.Restore, FileTransferEngine.DeployBackupRoot(_config, "v1.2.0"), Target, TransferMode.RestoreReplace, false, 1);
        private string Read(string path) => _fs.File.ReadAllText(path);

        #region 入れ替えと残すもの

        [Fact]
        public void 配置_本番が新版に入れ替わり_残すものは本番のまま()
        {
            Deploy();

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
            Assert.False(_fs.File.Exists($@"{Target}\old.dll"));                // 一覧にないものは完全に入れ替わる
            Assert.Equal("本番の設定", Read($@"{Target}\settings.config"));    // 残す（ファイル）
            Assert.Equal("過去のログ", Read($@"{Target}\Logs\a.log"));          // 残す（フォルダ）
            Assert.Equal("本番のランタイム", Read($@"{Target}\Runtime\big.bin")); // 触らない場所（新版のものは置かない）
        }

        [Fact]
        public void 配置_残すものは動かさない()
        {
            // 退避からコピーで戻すと別のデータになる（アクセス権や作成日時が変わる）。同じデータのままであることを確かめる
            MockFileData settings = _fs.GetFile($@"{Target}\settings.config");
            MockFileData log = _fs.GetFile($@"{Target}\Logs\a.log");
            MockFileData runtime = _fs.GetFile($@"{Target}\Runtime\big.bin");

            Deploy();

            Assert.Same(settings, _fs.GetFile($@"{Target}\settings.config"));
            Assert.Same(log, _fs.GetFile($@"{Target}\Logs\a.log"));
            Assert.Same(runtime, _fs.GetFile($@"{Target}\Runtime\big.bin"));
        }

        [Fact]
        public void 配置_退避には_触らない場所以外がすべてコピーされる()
        {
            Deploy();

            Assert.Equal("old exe", Read($@"{_backup}\App.exe"));
            Assert.True(_fs.File.Exists($@"{_backup}\old.dll"));
            Assert.Equal("本番の設定", Read($@"{_backup}\settings.config")); // 残すもの
            Assert.Equal("過去のログ", Read($@"{_backup}\Logs\a.log"));
            Assert.False(_fs.Directory.Exists($@"{_backup}\Runtime"));         // 触らない場所
            Assert.True(_fs.File.Exists(_backup + ".done"));
        }

        [Fact]
        public void 配置_残すフォルダが新しい版にもある場合_本番のものだけになる()
        {
            _fs.AddFile($@"{Release}\Logs\default.log", new MockFileData("新しい版の初期ログ"));

            Deploy();

            Assert.Equal("過去のログ", Read($@"{Target}\Logs\a.log"));
            Assert.False(_fs.File.Exists($@"{Target}\Logs\default.log")); // 残すフォルダの中には、新しい版のものを置かない
        }

        [Fact]
        public void 配置_残すものが本番になかった場合_新しい版のものが置かれる()
        {
            _fs.File.Delete($@"{Target}\settings.config");

            Deploy();

            Assert.Equal("初期設定", Read($@"{Target}\settings.config"));
        }

        [Fact]
        public void 配置_近い項目の種類が使われる()
        {
            // Data は残す。その中の Archive は触らない（退避もしない）
            _fs.AddFile($@"{Target}\Data\a.txt", new MockFileData("データ"));
            _fs.AddFile($@"{Target}\Data\Archive\old.zip", new MockFileData("重い過去分"));
            UseLists(new[] { Target + @"\Data" }, new[] { Target + @"\Data\Archive" });

            Deploy();

            Assert.Equal("データ", Read($@"{_backup}\Data\a.txt"));
            Assert.False(_fs.Directory.Exists($@"{_backup}\Data\Archive"));
            Assert.Equal("重い過去分", Read($@"{Target}\Data\Archive\old.zip"));
            Assert.Equal("データ", Read($@"{Target}\Data\a.txt"));
        }

        [Fact]
        public void 配置_配置先の外を指す項目と_配置先そのものを指す項目は無視される()
        {
            UseLists(new[] { @"D:\Other\Data", Target }, new string[0]);

            Deploy();

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));      // 配置先そのものを「残す」扱いにはしない
            Assert.Equal("初期設定", Read($@"{Target}\settings.config"));
        }

        #endregion

        #region 何度実行しても・途中で止まっても

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

            Assert.Equal("配置後のログ", Read($@"{Target}\Logs\b.log"));
            Assert.Equal("old exe", Read($@"{_backup}\App.exe")); // 退避が新版で上書きされない
        }

        [Fact]
        public void 配置_新版を置く途中で止まった後の再実行_旧版の退避は守られ_置き直す()
        {
            // 前回、退避まで済んで、新版を置く途中で止まった状態を作る（残すものはその場にある）
            _fs.AddFile($@"{_backup}\App.exe", new MockFileData("old exe"));
            _fs.AddFile($@"{_backup}\old.dll", new MockFileData("新版にはないファイル"));
            _fs.File.Delete($@"{Target}\App.exe");
            _fs.AddFile($@"{Target}\half.tmp", new MockFileData("置きかけ"));

            Deploy();

            Assert.False(_fs.File.Exists($@"{Target}\half.tmp"));
            Assert.False(_fs.File.Exists($@"{Target}\old.dll"));
            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
            Assert.Equal("本番の設定", Read($@"{Target}\settings.config"));
            Assert.Equal("old exe", Read($@"{_backup}\App.exe"));
        }

        [Fact]
        public void 配置_退避のコピーが途中で止まっていた場合_やり直して本番は消えない()
        {
            // 前回、退避のコピー（.tmp）の途中で止まった状態。正式な退避フォルダはまだない
            _fs.AddFile($@"{_backup}.tmp\App.exe", new MockFileData("コピーしかけ"));

            Deploy();

            Assert.False(_fs.Directory.Exists(_backup + ".tmp"));
            Assert.Equal("old exe", Read($@"{_backup}\App.exe"));
            Assert.True(_fs.File.Exists($@"{_backup}\old.dll"));
            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
        }

        [Fact]
        public void 配置_使用中のファイルがあると_本番を変える前に止まり_原因を取り除けば再実行できる()
        {
            // 業務アプリが old.dll を開いたまま（読むことはできるが、書き換え・削除はできない）
            MockFileData locked = _fs.GetFile($@"{Target}\old.dll");
            locked.AllowedFileShare = FileShare.Read;

            var ex = Assert.Throws<TransferException>(() => Deploy());

            // 文言の1行目（「変更できないファイルがあるため…」）と、どのファイルかが出る
            Assert.StartsWith(_config.Msg.Transfer.CannotModify.Split('\n')[0], ex.Message);
            Assert.Contains($@"{Target}\old.dll", ex.Message);
            Assert.Equal("old exe", Read($@"{_backup}\App.exe")); // 退避はできている
            Assert.Equal("old exe", Read($@"{Target}\App.exe"));  // 本番は変わっていない
            Assert.False(_fs.File.Exists(_backup + ".done"));

            // 業務アプリを止めてから、もう一度実行する
            locked.AllowedFileShare = FileShare.ReadWrite | FileShare.Delete;
            Deploy();

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
            Assert.False(_fs.File.Exists($@"{Target}\old.dll"));
        }

        [Fact]
        public void 配置_初めての配置_新版が置かれる()
        {
            _fs.Directory.Delete(Target, true);

            Deploy();

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
            Assert.Equal("初期設定", Read($@"{Target}\settings.config"));       // 本番になかったので新版のもの
            Assert.False(_fs.Directory.Exists($@"{Target}\Runtime"));         // 触らない場所には、なかったとしても置かない
            Assert.True(_fs.File.Exists(_backup + ".done"));
        }

        #endregion

        #region 配置の取り消し（復元）

        [Fact]
        public void 取り消し_旧版と退避したものは配置前に戻り_触らない場所はそのまま()
        {
            Deploy();
            // リリース後に、ログが増え、ランタイムも更新された
            _fs.AddFile($@"{Target}\Logs\after.log", new MockFileData("リリース後のログ"));
            _fs.File.WriteAllText($@"{Target}\Runtime\big.bin", "リリース後に更新したランタイム");

            Undo();

            Assert.Equal("old exe", Read($@"{Target}\App.exe"));
            Assert.Equal("新版にはないファイル", Read($@"{Target}\old.dll"));
            Assert.False(_fs.File.Exists($@"{Target}\Logs\after.log"));                     // 退避したものは配置前の状態に戻る
            Assert.Equal("リリース後に更新したランタイム", Read($@"{Target}\Runtime\big.bin")); // 触らない場所はそのまま
            // 戻す直前の状態は rolledback に残る（触らない場所は入らない）
            string rolledBack = _fs.Directory.GetDirectories(Path.GetDirectoryName(_backup), Path.GetFileName(_backup) + ".rolledback_*").Single();
            Assert.Equal("リリース後のログ", Read($@"{rolledBack}\Logs\after.log"));
            Assert.False(_fs.Directory.Exists($@"{rolledBack}\Runtime"));
        }

        [Fact]
        public void 取り消し_使用中のファイルがあると_本番を変える前に止まる()
        {
            Deploy();
            MockFileData locked = _fs.GetFile($@"{Target}\App.exe");
            locked.AllowedFileShare = FileShare.Read;

            Assert.Throws<TransferException>(() => Undo());

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
            // 戻す直前の状態の退避は、先にできている
            Assert.Single(_fs.Directory.GetDirectories(Path.GetDirectoryName(_backup), Path.GetFileName(_backup) + ".rolledback_*"));

            locked.AllowedFileShare = FileShare.ReadWrite | FileShare.Delete;
            Undo();

            Assert.Equal("old exe", Read($@"{Target}\App.exe"));
        }

        [Fact]
        public void 取り消し_戻す直前の状態のコピーが途中で止まっていた場合_やり直す()
        {
            Deploy();
            _fs.AddFile($@"{_backup}.rolledback.tmp\App.exe", new MockFileData("コピーしかけ"));

            Undo();

            Assert.False(_fs.Directory.Exists(_backup + ".rolledback.tmp"));
            Assert.Equal("old exe", Read($@"{Target}\App.exe"));
        }

        #endregion

        #region 入力の誤り・パスの長さ

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

            Assert.Equal("old exe", Read($@"{Target}\App.exe"));
        }

        [Fact]
        public void 配置_新版に長すぎるパスがある場合_エラーになり本番はそのまま()
        {
            // 新版は、あとで復元するときに「戻す直前の状態」として退避される。そのときに長さで止まると切り戻せないので、配置の時点で止める
            string longestRoot = _backup + ".rolledback_yyyyMMddHHmmss_99";
            string relative = new string('a', 100) + "\\" + new string('b', 260 - (longestRoot.Length + 1) - 101);
            _fs.AddFile(Path.Combine(Release, relative), new MockFileData("深いファイル"));

            var ex = Assert.Throws<TransferException>(() => Deploy());

            Assert.Equal(Messages.Format(_config.Msg.Transfer.PathTooLong, 259, 1, longestRoot + "\\" + relative), ex.Message);
            Assert.Equal("old exe", Read($@"{Target}\App.exe"));
            Assert.False(_fs.Directory.Exists(_backup));
        }

        [Fact]
        public void 配置_触らない場所はパスの長さの計算に入れない()
        {
            // 触らない Runtime の中に深いパスがあっても、退避されないので問題にしない
            string longestRoot = _backup + ".rolledback_yyyyMMddHHmmss_99";
            string relative = @"Runtime\" + new string('a', 100) + "\\" + new string('b', 300 - (longestRoot.Length + 1) - 109);
            _fs.AddFile(Path.Combine(Target, relative), new MockFileData("深いファイル"));

            Deploy();

            Assert.Equal("new exe", Read($@"{Target}\App.exe"));
        }

        #endregion
    }
}
