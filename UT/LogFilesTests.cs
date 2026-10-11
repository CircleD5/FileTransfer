using System;
using System.IO;
using Xunit;
using FileTransfer2;

namespace UT
{
    public class LogFilesTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileTransfer2Test_" + Guid.NewGuid().ToString("N"));
        private static readonly DateTime Today = new DateTime(2026, 10, 11);

        public LogFilesTests()
        {
            Directory.CreateDirectory(_dir);
            foreach (string name in new[] { "20261011.log", "20261009.log", "20261008.log", "20250101.log", "deploy_history.log", "memo.log" })
                File.WriteAllText(Path.Combine(_dir, name), "log");
        }

        public void Dispose() => Directory.Delete(_dir, true);

        private bool Exists(string name) => File.Exists(Path.Combine(_dir, name));

        [Fact]
        public void 保存期間を過ぎた日付のログだけ消える()
        {
            // 3日分（10/9〜10/11）残す
            int deleted = LogFiles.DeleteOld(_dir, 3, Today);

            Assert.Equal(2, deleted);
            Assert.True(Exists("20261011.log"));
            Assert.True(Exists("20261009.log"));
            Assert.False(Exists("20261008.log"));
            Assert.False(Exists("20250101.log"));
            Assert.True(Exists("deploy_history.log")); // 日付の名前ではないので消さない
            Assert.True(Exists("memo.log"));
        }

        [Fact]
        public void 保存期間が0なら何も消さない()
        {
            Assert.Equal(0, LogFiles.DeleteOld(_dir, 0, Today));
            Assert.True(Exists("20250101.log"));
        }
    }
}
