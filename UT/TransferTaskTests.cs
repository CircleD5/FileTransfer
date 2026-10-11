using System;
using Xunit;
using FileTransfer2;

namespace UT
{
    public class TransferTaskTests : IDisposable
    {
        private readonly TempConfigFiles _configFiles = new TempConfigFiles();
        private readonly AppConfig _config;

        public TransferTaskTests()
        {
            _config = _configFiles.LoadConfig();
        }

        public void Dispose() => _configFiles.Dispose();

        #region TSV の読み書き

        [Fact]
        public void TSV_書き出して読み戻すと同じ内容になる()
        {
            var task = new TransferTask
            {
                Action = TransferAction.Move,
                Source = @"""C:\src""",
                Destination = @"D:\dest",
                Mode = TransferMode.ExtractContents,
                AddDate = true,
            };

            string line = task.ToTsvLine();
            var loaded = TransferTask.FromTsvLine(line);

            Assert.Equal("Move\t\"C:\\src\"\tD:\\dest\tExtractContents\tTrue", line);
            Assert.Equal(task.Action, loaded.Action);
            Assert.Equal(task.Source, loaded.Source);
            Assert.Equal(task.Destination, loaded.Destination);
            Assert.Equal(task.Mode, loaded.Mode);
            Assert.Equal(task.AddDate, loaded.AddDate);
        }

        [Theory]
        [InlineData("コピー\tC:\\a\tD:\\b\tフォルダごと転送\tFalse")] // 旧形式（日本語名）
        [InlineData("1\tC:\\a\tD:\\b\t1\tFalse")]                    // 数字
        [InlineData("copy\tC:\\a\tD:\\b\tfolderasis\tFalse")]          // 大文字小文字違い
        public void TSV_知らない名前の場合_Unknownになる(string line)
        {
            var task = TransferTask.FromTsvLine(line);

            Assert.Equal(TransferAction.Unknown, task.Action);
            Assert.Equal(TransferMode.Unknown, task.Mode);
        }

        [Fact]
        public void TSV_列が足りない場合_空欄として読む()
        {
            var task = TransferTask.FromTsvLine("Delete\tC:\\a");

            Assert.Equal(TransferAction.Delete, task.Action);
            Assert.Equal("", task.Destination);
            Assert.Equal(TransferMode.Unknown, task.Mode);
            Assert.False(task.AddDate);
        }

        #endregion

        #region 入力チェック

        [Fact]
        public void 入力チェック_正しい内容ならnull()
        {
            Assert.Null(NewTask(TransferAction.Copy, @"C:\a", @"D:\b", TransferMode.FolderAsIs).Validate(_config));
            Assert.Null(NewTask(TransferAction.Delete, @"C:\a", "", TransferMode.DeleteAll).Validate(_config));
        }

        [Fact]
        public void 入力チェック_処理種別が不明ならエラー()
        {
            Assert.NotNull(NewTask(TransferAction.Unknown, @"C:\a", @"D:\b", TransferMode.FolderAsIs).Validate(_config));
        }

        [Fact]
        public void 入力チェック_転送元が空ならエラー()
        {
            Assert.NotNull(NewTask(TransferAction.Copy, " ", @"D:\b", TransferMode.FolderAsIs).Validate(_config));
        }

        [Fact]
        public void 入力チェック_コピーと移動で転送先が空ならエラー()
        {
            Assert.NotNull(NewTask(TransferAction.Copy, @"C:\a", "", TransferMode.FolderAsIs).Validate(_config));
            Assert.NotNull(NewTask(TransferAction.Move, @"C:\a", "", TransferMode.FolderAsIs).Validate(_config));
        }

        [Fact]
        public void 入力チェック_処理種別に合わないモードならエラー()
        {
            Assert.NotNull(NewTask(TransferAction.Delete, @"C:\a", "", TransferMode.FolderAsIs).Validate(_config));
            Assert.NotNull(NewTask(TransferAction.Copy, @"C:\a", @"D:\b", TransferMode.DeleteAll).Validate(_config));
            Assert.NotNull(NewTask(TransferAction.Copy, @"C:\a", @"D:\b", TransferMode.Unknown).Validate(_config));
        }

        [Fact]
        public void 入力チェック_配置()
        {
            Assert.Null(NewTask(TransferAction.Deploy, @"C:\Release\v1\App", @"D:\App", TransferMode.DeployReplace).Validate(_config));
            Assert.NotNull(NewTask(TransferAction.Deploy, @"C:\Release\v1\App", "", TransferMode.DeployReplace).Validate(_config));
            Assert.NotNull(NewTask(TransferAction.Deploy, @"C:\Release\v1\App", @"D:\App", TransferMode.FolderAsIs).Validate(_config));

            var withDate = NewTask(TransferAction.Deploy, @"C:\Release\v1\App", @"D:\App", TransferMode.DeployReplace);
            withDate.AddDate = true;
            Assert.Equal(_config.Msg.Input.DateNotAllowed, withDate.Validate(_config));
        }

        private static TransferTask NewTask(TransferAction action, string src, string dest, TransferMode mode)
        {
            return new TransferTask { Action = action, Source = src, Destination = dest, Mode = mode };
        }

        #endregion
    }
}
