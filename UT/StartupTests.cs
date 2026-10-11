using System.IO;
using Xunit;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// 起動時の動きのテスト。設定を書き換えてからアプリを起動するため、テストごとに AppDriver を作る。
    /// </summary>
    [Collection("UI Tests")]
    public class StartupTests
    {
        [Fact]
        public void DefaultProfileを指定すると_起動時に読み込まれる_実行はされない()
        {
            using (var app = new AppDriver((files, settings) =>
            {
                string profilesDir = settings["ProfilesDir"];
                Directory.CreateDirectory(profilesDir);
                TaskFile.Save(Path.Combine(profilesDir, "Release.tsv"), new[]
                {
                    new TransferTask { Action = TransferAction.Copy, Source = @"C:\Src", Destination = @"D:\Dest", Mode = TransferMode.FolderAsIs },
                });
                settings["DefaultProfile"] = "Release";
            }))
            {
                app.WaitUntil(() => app.Title.Contains("Release"), "起動時にプロファイルが読み込まれませんでした。");
                Assert.Equal(1, app.RowCount);
                Assert.Equal(@"C:\Src", app.GetText(0, app.Msg.Column.Source));
                Assert.Equal(app.Msg.Status.Wait, app.GetStatus(0)); // 読み込むだけで実行はしない
            }
        }

        [Fact]
        public void DefaultProfileが見つからない場合_未選択で起動しログで知らせる()
        {
            using (var app = new AppDriver((files, settings) => settings["DefaultProfile"] = "NotExist"))
            {
                app.WaitUntil(() => app.Log.Contains("NotExist"), "見つからないことがログに出ませんでした。");
                Assert.Contains(Messages.Format(app.Msg.Profile.DefaultNotFound, "NotExist"), app.Log);
                Assert.Contains(app.Msg.Ui.ProfileUnselected, app.Title);
            }
        }
    }
}
