using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;
using FileTransfer2;

namespace UT
{
    public class AppConfigTests
    {
        #region settings.config / forbidden_paths.txt

        [Fact]
        public void 正しい設定ファイル_値が読み込まれる()
        {
            using (var temp = new TempConfigFiles())
            {
                temp.WriteForbiddenPaths("# コメント行は無視", "", @"C:\Windows");

                var config = temp.LoadConfig();

                Assert.Equal(30000, config.AutoRunIntervalMs);
                Assert.Equal(0, config.RowDelayMs);
                Assert.Equal(Path.Combine(temp.Dir, @"AutoRun\Plan"), config.AutoRunPlanDir);
                Assert.Equal(Path.Combine(temp.Dir, "Profiles"), config.ProfilesDir);
                Assert.Equal(new[] { @"C:\Windows" }, config.ForbiddenPaths);
            }
        }

        [Fact]
        public void 設定ファイルがない場合_エラーになる()
        {
            var ex = Assert.Throws<ConfigException>(() => new AppConfig().Load(@"C:\not_exist\settings.config"));
            Assert.Contains(@"C:\not_exist\settings.config", ex.Message);
        }

        [Fact]
        public void 項目が足りない場合_足りない項目がすべてエラーに書かれる()
        {
            using (var temp = new TempConfigFiles())
            {
                var settings = temp.ValidSettings();
                settings.Remove("ProfilesDir");
                settings["LogDir"] = "";
                temp.WriteSettings(settings);

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());

                Assert.Contains("ProfilesDir", ex.Message);
                Assert.Contains("LogDir", ex.Message);
            }
        }

        [Fact]
        public void 値が空でよい項目_空なら空として読まれ_行がなければエラーになる()
        {
            using (var temp = new TempConfigFiles())
            {
                Assert.Equal("", temp.LoadConfig().DefaultProfile);

                var settings = temp.ValidSettings();
                settings.Remove("DefaultProfile");
                temp.WriteSettings(settings);

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());
                Assert.Contains("DefaultProfile=", ex.Message);
            }
        }

        [Fact]
        public void 数値の項目がない場合_エラーになる()
        {
            using (var temp = new TempConfigFiles())
            {
                var settings = temp.ValidSettings();
                settings.Remove("RowDelayMs");
                temp.WriteSettings(settings);

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());

                Assert.Contains("RowDelayMs", ex.Message);
            }
        }

        [Fact]
        public void パスの解釈_相対パスはexeのフォルダ基準で_絶対パスはそのまま()
        {
            var config = new AppConfig();

            Assert.Equal(Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, @"Logs\a.log"), config.GetFullPath(@"Logs\a.log"));
            Assert.Equal(@"D:\Data\a.log", config.GetFullPath(@"D:\Data\a.log"));
        }

        [Theory]
        [InlineData("AutoRunIntervalMs", "abc")]
        [InlineData("AutoRunIntervalMs", "0")]   // タイマーの間隔は 1 以上
        [InlineData("RowDelayMs", "-1")]
        [InlineData("LogRetentionDays", "-1")]
        public void 数値が不正な場合_エラーになる(string key, string value)
        {
            using (var temp = new TempConfigFiles())
            {
                var settings = temp.ValidSettings();
                settings[key] = value;
                temp.WriteSettings(settings);

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());

                Assert.Contains(key, ex.Message);
            }
        }

        [Theory]
        [InlineData("forbidden_paths.txt")]
        [InlineData("messages.json")]
        [InlineData("deploy_keep.txt")]
        public void 参照先のファイルがない場合_エラーになる(string fileName)
        {
            using (var temp = new TempConfigFiles())
            {
                File.Delete(Path.Combine(temp.Dir, fileName));

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());

                Assert.Contains(fileName, ex.Message);
            }
        }

        [Fact]
        public void 作業用フォルダ_まとめて作られる()
        {
            using (var temp = new TempConfigFiles())
            {
                var config = temp.LoadConfig();

                config.CreateFolders();

                Assert.True(Directory.Exists(config.LogDir));
                Assert.True(Directory.Exists(config.ProfilesDir));
                Assert.True(Directory.Exists(config.AutoRunErrorDir));
            }
        }

        [Fact]
        public void 作業用フォルダ_作れない場合_フォルダ名入りのエラーになる()
        {
            using (var temp = new TempConfigFiles())
            {
                // 同じ名前のファイルがあるとフォルダは作れない
                string blocked = Path.Combine(temp.Dir, "blocked");
                File.WriteAllText(blocked, "");
                var settings = temp.ValidSettings();
                settings["LogDir"] = Path.Combine(blocked, "Logs");
                temp.WriteSettings(settings);
                var config = temp.LoadConfig();

                var ex = Assert.Throws<ConfigException>(() => config.CreateFolders());

                Assert.Contains(Path.Combine(blocked, "Logs"), ex.Message);
            }
        }

        #endregion

        #region messages.json

        [Fact]
        public void 文言ファイル_読み込まれる()
        {
            using (var temp = new TempConfigFiles())
            {
                var config = temp.LoadConfig();

                Assert.Equal("完了", config.Msg.Status.Done);
                Assert.Equal("コピー", config.Msg.Actions[TransferAction.Copy]);
            }
        }

        [Theory]
        [InlineData("Status", "Done")]          // グループの中の項目が足りない
        [InlineData("Run", null)]               // グループごと足りない
        [InlineData("Actions", "Move")]         // 処理種別の表示名が足りない
        [InlineData("Modes", "DeleteFilesOnly")] // モードの表示名が足りない
        [InlineData("DestHints", "Restore")]     // セルの説明が足りない
        public void 文言ファイル_項目が足りない場合_エラーになる(string group, string item)
        {
            using (var temp = new TempConfigFiles())
            {
                temp.EditMessages(json =>
                {
                    if (item == null) json.Remove(group);
                    else ((JObject)json[group]).Remove(item);
                });

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());

                Assert.Contains(item ?? group, ex.Message);
            }
        }

        [Fact]
        public void 文言ファイル_知らない項目がある場合_書き間違いとしてエラーになる()
        {
            using (var temp = new TempConfigFiles())
            {
                temp.EditMessages(json => ((JObject)json["Status"]).Add("Dnoe", "完了"));

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());

                Assert.Contains("Dnoe", ex.Message);
            }
        }

        [Fact]
        public void 文言ファイル_空の場合_エラーになる()
        {
            using (var temp = new TempConfigFiles())
            {
                File.WriteAllText(temp.MessagesPath, "");

                var ex = Assert.Throws<ConfigException>(() => temp.LoadConfig());

                Assert.Contains("空", ex.Message);
            }
        }

        [Fact]
        public void 文言ファイル_JSONの書式が壊れている場合_エラーになる()
        {
            using (var temp = new TempConfigFiles())
            {
                File.WriteAllText(temp.MessagesPath, "{ \"Ui\": ");

                Assert.Throws<ConfigException>(() => temp.LoadConfig());
            }
        }

        [Theory]
        [InlineData("[{0}行目] 失敗: {1}", "[3行目] 失敗: 理由")]
        [InlineData("[{0}行目] 失敗: {5}", "[{0}行目] 失敗: {5}")] // {} の番号を間違えても落ちず、文言をそのまま返す
        public void 文言の埋め込み(string template, string expected)
        {
            Assert.Equal(expected, Messages.Format(template, 3, "理由"));
        }

        #endregion
    }
}
