using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Xunit;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// 触らない場所（forbidden_paths.txt）のテスト（コピー・移動・削除）。
    /// 書いた場所とその中は、どの処理でもエラーにせず避けること、避けたことがログに場所ごとに1行出ることを確かめる。
    /// 配置・退避・復元での扱いは DeployTests / BackupRestoreTests で確かめる。
    /// </summary>
    public class AvoidTests : IDisposable
    {
        private readonly TempConfigFiles _configFiles = new TempConfigFiles();
        private readonly List<string> _logs = new List<string>();
        private readonly AppConfig _config;
        private readonly MockFileSystem _fs = new MockFileSystem();
        private readonly FileTransferEngine _engine;

        public AvoidTests()
        {
            // 末尾の \ と大文字・小文字は区別しない
            _configFiles.WriteForbiddenPaths("# 触らない場所", @"c:\src\protected\", @"D:\Out\Src\Keep", @"D:\Work\Keep");
            _config = _configFiles.LoadConfig();
            _engine = new FileTransferEngine(_logs.Add, _config, _fs);

            _fs.AddFile(@"C:\Src\a.txt", new MockFileData("a"));
            _fs.AddFile(@"C:\Src\Protected\b.txt", new MockFileData("b"));
            _fs.AddFile(@"C:\Src\Protected\sub\c.txt", new MockFileData("c"));
            _fs.AddFile(@"C:\Src\Keep\k.txt", new MockFileData("k"));
        }

        public void Dispose() => _configFiles.Dispose();

        private void Run(TransferAction action, string src, string dest, TransferMode mode)
            => _engine.ProcessRow(action, src, dest, mode, false, 1);

        /// <summary>「触らない場所のため避けました」のログが、place について何回出たか</summary>
        private int AvoidedLogCount(string place) => _logs.Count(log => log == Messages.Format(_config.Msg.Transfer.Avoided, 1, place));

        [Fact]
        public void 転送元が触らない場所なら_何もせずエラーにもならず_ログが出る()
        {
            Run(TransferAction.Delete, @"C:\Src\Protected", "", TransferMode.DeleteAll);

            Assert.True(_fs.File.Exists(@"C:\Src\Protected\b.txt"));
            Assert.Equal(1, AvoidedLogCount(@"c:\src\protected")); // ログには一覧に書いたとおりのパスが出る
        }

        [Fact]
        public void 転送先が触らない場所の中なら_何もしない()
        {
            Run(TransferAction.Copy, @"C:\Src\a.txt", @"D:\Out\Src\Keep\sub\", TransferMode.FolderAsIs);

            Assert.False(_fs.Directory.Exists(@"D:\Out\Src\Keep"));
        }

        [Fact]
        public void コピー_転送元の中の触らない場所は避け_ログは場所ごとに1回だけ出る()
        {
            Run(TransferAction.Copy, @"C:\Src", @"D:\Copy\", TransferMode.ExtractContents);

            Assert.True(_fs.File.Exists(@"D:\Copy\a.txt"));
            Assert.False(_fs.Directory.Exists(@"D:\Copy\Protected"));
            Assert.Equal(1, AvoidedLogCount(@"c:\src\protected")); // 中のファイルが2つあっても1回
        }

        [Fact]
        public void コピー_書き込み先が触らない場所なら避ける()
        {
            // C:\Src を D:\Out へフォルダごとコピーすると、C:\Src\Keep は D:\Out\Src\Keep（触らない場所）に入る
            _fs.AddFile(@"D:\Out\Src\Keep\k.txt", new MockFileData("本番のもの"));

            Run(TransferAction.Copy, @"C:\Src", @"D:\Out\", TransferMode.FolderAsIs);

            Assert.True(_fs.File.Exists(@"D:\Out\Src\a.txt"));
            Assert.Equal("本番のもの", _fs.File.ReadAllText(@"D:\Out\Src\Keep\k.txt")); // 上書きされない
        }

        [Fact]
        public void 移動_避けたものは転送元に残り_転送元のフォルダも残る()
        {
            Run(TransferAction.Move, @"C:\Src", @"D:\Moved\", TransferMode.ExtractContents);

            Assert.True(_fs.File.Exists(@"D:\Moved\a.txt"));
            Assert.False(_fs.File.Exists(@"C:\Src\a.txt"));
            Assert.True(_fs.File.Exists(@"C:\Src\Protected\sub\c.txt")); // 避けたものは残る
            Assert.False(_fs.Directory.Exists(@"C:\Src\Keep"));          // 移し終えた空のフォルダは消える
        }

        [Fact]
        public void 移動_避けるものがなければ_今までどおり転送元のフォルダも消える()
        {
            _fs.AddFile(@"C:\Plain\x\y.txt", new MockFileData("y"));

            Run(TransferAction.Move, @"C:\Plain", @"D:\Moved\", TransferMode.FolderAsIs);

            Assert.True(_fs.File.Exists(@"D:\Moved\Plain\x\y.txt"));
            Assert.False(_fs.Directory.Exists(@"C:\Plain"));
        }

        [Theory]
        [InlineData(TransferMode.DeleteAll)]
        [InlineData(TransferMode.DeleteKeepParent)]
        [InlineData(TransferMode.DeleteFilesOnly)]
        public void 削除_触らない場所は残り_ほかは消える(TransferMode mode)
        {
            _fs.AddFile(@"D:\Work\a.txt", new MockFileData("a"));
            _fs.AddFile(@"D:\Work\sub\b.txt", new MockFileData("b"));
            _fs.AddFile(@"D:\Work\Keep\c.txt", new MockFileData("c"));

            Run(TransferAction.Delete, @"D:\Work", "", mode);

            Assert.False(_fs.File.Exists(@"D:\Work\a.txt"));
            Assert.False(_fs.File.Exists(@"D:\Work\sub\b.txt"));
            Assert.True(_fs.File.Exists(@"D:\Work\Keep\c.txt"));
            // 「ファイルのみ削除」以外は、空になったフォルダも消える（触らない場所が入っている D:\Work は残る）
            Assert.Equal(mode == TransferMode.DeleteFilesOnly, _fs.Directory.Exists(@"D:\Work\sub"));
        }

        [Fact]
        public void 削除_触らない場所がなければ_丸ごと削除でフォルダも消える()
        {
            _fs.AddFile(@"D:\Temp\a.txt", new MockFileData("a"));

            Run(TransferAction.Delete, @"D:\Temp", "", TransferMode.DeleteAll);

            Assert.False(_fs.Directory.Exists(@"D:\Temp"));
        }
    }
}
