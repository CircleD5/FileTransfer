using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Xunit;
using Moq;
using FileTransfer2;

namespace UT
{
    public class FileTransferEngineTests : IDisposable
    {
        private readonly TempConfigFiles _configFiles;
        private readonly AppConfig _config;
        private readonly Mock<Action<string>> _mockLogger;

        public FileTransferEngineTests()
        {
            _configFiles = new TempConfigFiles(); // 禁止パスは空の状態
            _config = _configFiles.LoadConfig();
            _mockLogger = new Mock<Action<string>>();
        }

        public void Dispose() => _configFiles.Dispose();

        private FileTransferEngine CreateEngine(IFileSystem fs) => new FileTransferEngine(_mockLogger.Object, _config, fs);

        /// <summary>
        /// 一部だけ偽物にしたファイルシステムを作る（パス操作は本物を使う）。例外を起こしたいテスト用。
        /// </summary>
        private static IFileSystem CreateMockFs(Mock<IFile> file, Mock<IDirectory> dir)
        {
            var fs = new Mock<IFileSystem>();
            fs.Setup(f => f.File).Returns(file.Object);
            fs.Setup(f => f.Directory).Returns(dir.Object);
            fs.Setup(f => f.Path).Returns(new FileSystem().Path);
            return fs.Object;
        }

        #region 1. 共通・ガード処理

        [Fact]
        public void ProcessRow_転送元が存在しない場合_例外になる()
        {
            var engine = CreateEngine(new MockFileSystem());

            Assert.Throws<TransferException>(() =>
                engine.ProcessRow(TransferAction.Copy, @"C:\not_exist.txt", @"D:\dest", TransferMode.FolderAsIs, false, 1));
        }

        [Fact]
        public void ProcessRow_転送先が空欄の場合_例外になる()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\src.txt", new MockFileData("data") } });
            var engine = CreateEngine(mockFs);

            Assert.Throws<TransferException>(() =>
                engine.ProcessRow(TransferAction.Copy, @"C:\src.txt", "", TransferMode.FolderAsIs, false, 1));
        }

        [Fact]
        public void ProcessRow_パスの前後のダブルクォーテーションは無視される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\src.txt", new MockFileData("data") } });
            var engine = CreateEngine(mockFs);

            engine.ProcessRow(TransferAction.Copy, "\"C:\\src.txt\"", "\"D:\\DestDir\\\"", TransferMode.FolderAsIs, false, 1);

            Assert.True(mockFs.File.Exists(@"D:\DestDir\src.txt"));
        }

        [Fact]
        public void ProcessRow_禁止パスと完全一致する場合_例外になる()
        {
            using (var temp = new TempConfigFiles())
            {
                temp.WriteForbiddenPaths("# コメント", @"C:\Forbidden\", @"D:\Protected");
                var config = temp.LoadConfig();

                var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
                {
                    { @"C:\Forbidden\file.txt", new MockFileData("data") },
                    { @"C:\Allowed\file.txt", new MockFileData("data") },
                });
                var engine = new FileTransferEngine(_mockLogger.Object, config, mockFs);

                // 転送元が禁止パス（末尾の \ と大文字小文字は区別しない）
                Assert.Throws<TransferException>(() =>
                    engine.ProcessRow(TransferAction.Delete, @"c:\forbidden", "", TransferMode.DeleteAll, false, 1));
                // 転送先が禁止パス
                Assert.Throws<TransferException>(() =>
                    engine.ProcessRow(TransferAction.Copy, @"C:\Allowed", @"D:\Protected\", TransferMode.FolderAsIs, false, 1));
                // 配下のパスは禁止されない
                engine.ProcessRow(TransferAction.Copy, @"C:\Forbidden\file.txt", @"D:\Out\", TransferMode.FolderAsIs, false, 1);
                Assert.True(mockFs.File.Exists(@"D:\Out\file.txt"));
            }
        }

        #endregion

        #region 2. 単一ファイルの転送

        [Fact]
        public void ファイルコピー_成功する()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { @"C:\src.txt", new MockFileData("data") },
                { @"D:\DestDir\", new MockDirectoryData() }
            });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\src.txt", @"D:\DestDir", TransferMode.FolderAsIs, false, 1);

            Assert.True(mockFs.File.Exists(@"D:\DestDir\src.txt"));
            Assert.True(mockFs.File.Exists(@"C:\src.txt"));
        }

        [Fact]
        public void ファイル移動_元ファイルが削除される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\src.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Move, @"C:\src.txt", @"D:\DestDir\", TransferMode.FolderAsIs, false, 1);

            Assert.True(mockFs.File.Exists(@"D:\DestDir\src.txt"));
            Assert.False(mockFs.File.Exists(@"C:\src.txt"));
        }

        [Fact]
        public void ファイル移動_転送先に同じ名前がある場合_上書きされ元ファイルは消える()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { @"C:\src.txt", new MockFileData("new") },
                { @"D:\dest.txt", new MockFileData("old") }
            });

            CreateEngine(mockFs).ProcessRow(TransferAction.Move, @"C:\src.txt", @"D:\dest.txt", TransferMode.FolderAsIs, false, 1);

            Assert.Equal("new", mockFs.File.ReadAllText(@"D:\dest.txt"));
            Assert.False(mockFs.File.Exists(@"C:\src.txt"));
            _mockLogger.Verify(l => l(It.Is<string>(s => s.Contains("dest.txt"))), Times.Once); // 上書きしたことがログに出る
        }

        [Fact]
        public void ファイル移動_転送先が同じファイルの場合_例外になり元ファイルは残る()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\Dir\src.txt", new MockFileData("data") } });

            Assert.Throws<TransferException>(() =>
                CreateEngine(mockFs).ProcessRow(TransferAction.Move, @"C:\Dir\src.txt", @"C:\DIR", TransferMode.FolderAsIs, false, 1));

            Assert.True(mockFs.File.Exists(@"C:\Dir\src.txt"));
        }

        [Fact]
        public void ファイルコピー_日付付与ONの場合_ファイル名に日付が追加される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\src.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\src.txt", @"D:\DestDir\", TransferMode.FolderAsIs, true, 1);

            Assert.False(mockFs.File.Exists(@"D:\DestDir\src.txt"));
            Assert.Matches(@"src_\d{14}\.txt$", Assert.Single(mockFs.Directory.GetFiles(@"D:\DestDir\")));
        }

        [Fact]
        public void ファイルコピー_転送先がファイル名の場合_親フォルダを作ってその名前で保存する()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\src.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\src.txt", @"D:\NewDir\dest.txt", TransferMode.FolderAsIs, false, 1);

            Assert.True(mockFs.File.Exists(@"D:\NewDir\dest.txt"));
        }

        [Fact]
        public void ファイルコピー_上書き時_ログが出力される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { @"C:\src.txt", new MockFileData("new") },
                { @"D:\dest.txt", new MockFileData("old") }
            });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\src.txt", @"D:\dest.txt", TransferMode.FolderAsIs, false, 1);

            Assert.Equal("new", mockFs.File.ReadAllText(@"D:\dest.txt"));
            _mockLogger.Verify(l => l(It.Is<string>(s => s.Contains("dest.txt"))), Times.Once);
        }

        [Fact]
        public void ファイルコピー_失敗した場合_例外がそのまま伝わる()
        {
            var file = new Mock<IFile>();
            file.Setup(f => f.Exists(@"C:\src.txt")).Returns(true);
            file.Setup(f => f.Copy(It.IsAny<string>(), It.IsAny<string>(), true)).Throws(new IOException("Test Error"));
            var engine = CreateEngine(CreateMockFs(file, new Mock<IDirectory>()));

            var ex = Assert.Throws<IOException>(() =>
                engine.ProcessRow(TransferAction.Copy, @"C:\src.txt", @"D:\DestDir\", TransferMode.FolderAsIs, false, 1));
            Assert.Equal("Test Error", ex.Message);
        }

        [Fact]
        public void 日付付与_同名が存在する場合_連番が付与される()
        {
            var file = new Mock<IFile>();
            file.Setup(f => f.Exists(@"C:\src.txt")).Returns(true);
            file.SetupSequence(f => f.Exists(It.IsRegex(@"src_\d{14}\.txt$")))
                .Returns(true)   // 1回目（連番なし）は既にある
                .Returns(false);
            var dir = new Mock<IDirectory>();
            dir.Setup(d => d.Exists(@"D:\dest")).Returns(true);

            CreateEngine(CreateMockFs(file, dir)).ProcessRow(TransferAction.Copy, @"C:\src.txt", @"D:\dest", TransferMode.FolderAsIs, true, 1);

            file.Verify(f => f.Copy(@"C:\src.txt", It.IsRegex(@"src_\d{14}_1\.txt$"), true), Times.Once);
        }

        #endregion

        #region 3. フォルダの転送

        [Fact]
        public void フォルダ転送_転送先が転送元の中の場合_例外になる()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\Folder\file.txt", new MockFileData("data") } });

            Assert.Throws<TransferException>(() =>
                CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\Folder", @"C:\Folder\Sub", TransferMode.FolderAsIs, false, 1));
        }

        [Fact]
        public void フォルダ転送_FolderAsIs_親フォルダごとコピーされる()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\SrcDir\Sub\file.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\SrcDir", @"D:\Dest", TransferMode.FolderAsIs, false, 1);

            Assert.True(mockFs.File.Exists(@"D:\Dest\SrcDir\Sub\file.txt"));
            Assert.True(mockFs.File.Exists(@"C:\SrcDir\Sub\file.txt"));
        }

        [Fact]
        public void フォルダ転送_ExtractContents_親フォルダを含めず中身のみ転送される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\SrcDir\Sub\file.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\SrcDir", @"D:\Dest", TransferMode.ExtractContents, false, 1);

            Assert.True(mockFs.File.Exists(@"D:\Dest\Sub\file.txt"));
            Assert.False(mockFs.Directory.Exists(@"D:\Dest\SrcDir"));
        }

        [Fact]
        public void フォルダ移動_転送後に元のフォルダが削除される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { @"C:\SrcDir\file.txt", new MockFileData("data") },
                { @"C:\SrcDir\Sub\Deep\file2.txt", new MockFileData("data") },
                { @"C:\SrcDir\Empty\", new MockDirectoryData() },
            });

            CreateEngine(mockFs).ProcessRow(TransferAction.Move, @"C:\SrcDir", @"D:\Dest", TransferMode.FolderAsIs, false, 1);

            Assert.True(mockFs.File.Exists(@"D:\Dest\SrcDir\file.txt"));
            Assert.True(mockFs.File.Exists(@"D:\Dest\SrcDir\Sub\Deep\file2.txt"));
            Assert.True(mockFs.Directory.Exists(@"D:\Dest\SrcDir\Empty"));
            Assert.False(mockFs.Directory.Exists(@"C:\SrcDir"));
        }

        [Fact]
        public void フォルダ転送_FolderAsIsで日付付与ONの場合_フォルダ名に日付が付与される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\SrcDir\file.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\SrcDir", @"D:\Dest", TransferMode.FolderAsIs, true, 1);

            string dir = Assert.Single(mockFs.Directory.GetDirectories(@"D:\Dest"));
            Assert.Matches(@"SrcDir_\d{14}$", dir);
            Assert.True(mockFs.File.Exists(Path.Combine(dir, "file.txt")));
        }

        [Fact]
        public void フォルダ転送_ExtractContentsで日付付与ONの場合_ファイル名に日付が付与される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\SrcDir\file.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\SrcDir", @"D:\Dest", TransferMode.ExtractContents, true, 1);

            Assert.Matches(@"file_\d{14}\.txt$", Assert.Single(mockFs.Directory.GetFiles(@"D:\Dest")));
        }

        [Fact]
        public void フォルダ転送_上書き時_ログが出力される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { @"C:\SrcDir\file.txt", new MockFileData("new") },
                { @"D:\DestDir\SrcDir\file.txt", new MockFileData("old") }
            });

            CreateEngine(mockFs).ProcessRow(TransferAction.Copy, @"C:\SrcDir", @"D:\DestDir", TransferMode.FolderAsIs, false, 1);

            Assert.Equal("new", mockFs.File.ReadAllText(@"D:\DestDir\SrcDir\file.txt"));
            _mockLogger.Verify(l => l(It.Is<string>(s => s.Contains("file.txt"))), Times.Once);
        }

        [Fact]
        public void フォルダ転送_途中のファイルで失敗した場合_その場で例外になる()
        {
            var file = new Mock<IFile>();
            file.Setup(f => f.Copy(@"C:\SrcDir\a.txt", It.IsAny<string>(), true)).Throws(new IOException("Copy Error"));
            var dir = new Mock<IDirectory>();
            dir.Setup(d => d.Exists(@"C:\SrcDir")).Returns(true);
            dir.Setup(d => d.GetDirectories(@"C:\SrcDir", "*", SearchOption.AllDirectories)).Returns(new string[0]);
            dir.Setup(d => d.GetFiles(@"C:\SrcDir", "*", SearchOption.AllDirectories)).Returns(new[] { @"C:\SrcDir\a.txt", @"C:\SrcDir\b.txt" });

            Assert.Throws<IOException>(() =>
                CreateEngine(CreateMockFs(file, dir)).ProcessRow(TransferAction.Copy, @"C:\SrcDir", @"D:\DestDir", TransferMode.FolderAsIs, false, 1));

            // 失敗した後のファイルは処理されない
            file.Verify(f => f.Copy(@"C:\SrcDir\b.txt", It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        #endregion

        #region 4. 削除

        [Fact]
        public void 削除_ファイル_削除される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\file.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Delete, @"C:\file.txt", "", TransferMode.DeleteAll, false, 1);

            Assert.False(mockFs.File.Exists(@"C:\file.txt"));
        }

        [Fact]
        public void 削除_読み取り専用ファイル_属性を解除して削除される()
        {
            var readOnlyFile = new MockFileData("data") { Attributes = FileAttributes.ReadOnly };
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\readonly.txt", readOnlyFile } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Delete, @"C:\readonly.txt", "", TransferMode.DeleteAll, false, 1);

            Assert.False(mockFs.File.Exists(@"C:\readonly.txt"));
        }

        [Fact]
        public void 削除_DeleteAll_フォルダごと削除される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\TgtDir\Sub\file.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Delete, @"C:\TgtDir", "", TransferMode.DeleteAll, false, 1);

            Assert.False(mockFs.Directory.Exists(@"C:\TgtDir"));
        }

        [Fact]
        public void 削除_DeleteKeepParent_中身のみ削除され親フォルダは残る()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData>
            {
                { @"C:\TgtDir\file.txt", new MockFileData("data") },
                { @"C:\TgtDir\Sub\file.txt", new MockFileData("data") },
            });

            CreateEngine(mockFs).ProcessRow(TransferAction.Delete, @"C:\TgtDir", "", TransferMode.DeleteKeepParent, false, 1);

            Assert.True(mockFs.Directory.Exists(@"C:\TgtDir"));
            Assert.Empty(mockFs.Directory.GetFileSystemEntries(@"C:\TgtDir"));
        }

        [Fact]
        public void 削除_DeleteFilesOnly_フォルダ構造は残りファイルのみ削除される()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\TgtDir\Sub\file.txt", new MockFileData("data") } });

            CreateEngine(mockFs).ProcessRow(TransferAction.Delete, @"C:\TgtDir", "", TransferMode.DeleteFilesOnly, false, 1);

            Assert.True(mockFs.Directory.Exists(@"C:\TgtDir\Sub"));
            Assert.False(mockFs.File.Exists(@"C:\TgtDir\Sub\file.txt"));
        }

        [Fact]
        public void 削除_フォルダで削除用でないモードの場合_例外になり何も消えない()
        {
            var mockFs = new MockFileSystem(new Dictionary<string, MockFileData> { { @"C:\TgtDir\file.txt", new MockFileData("data") } });

            Assert.Throws<TransferException>(() =>
                CreateEngine(mockFs).ProcessRow(TransferAction.Delete, @"C:\TgtDir", "", TransferMode.FolderAsIs, false, 1));

            Assert.True(mockFs.File.Exists(@"C:\TgtDir\file.txt"));
        }

        [Fact]
        public void 削除_失敗した場合_例外がそのまま伝わる()
        {
            var file = new Mock<IFile>();
            file.Setup(f => f.Exists(@"C:\locked.txt")).Returns(true);
            file.Setup(f => f.GetAttributes(It.IsAny<string>())).Returns(FileAttributes.Normal);
            file.Setup(f => f.Delete(@"C:\locked.txt")).Throws(new IOException("Lock Error"));

            Assert.Throws<IOException>(() =>
                CreateEngine(CreateMockFs(file, new Mock<IDirectory>())).ProcessRow(TransferAction.Delete, @"C:\locked.txt", "", TransferMode.DeleteAll, false, 1));
        }

        #endregion
    }
}
