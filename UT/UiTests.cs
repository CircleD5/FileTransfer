using System;
using Xunit;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// 画面の動き（表示・入力・ボタン）を確かめるテスト。ファイル操作の結果は E2ETests で確かめる。
    /// </summary>
    [Collection("UI Tests")]
    public class UiTests : IDisposable
    {
        private readonly AppDriver _app = new AppDriver();

        public void Dispose() => _app.Dispose();

        [Fact]
        public void 起動直後_未選択で実行ボタンが押せる()
        {
            Assert.Contains(_app.Msg.Ui.ProfileUnselected, _app.Title);
            Assert.DoesNotContain(_app.Msg.Ui.DirtyMark, _app.Title);
            Assert.True(_app.IsExecuteAllEnabled);
            Assert.Equal(0, _app.RowCount);
        }

        [Fact]
        public void 入力すると_新しい行が増えタイトルに未保存の印が付く()
        {
            _app.SetRow(0, TransferAction.Copy, @"C:\TestSource", @"D:\TestDest");

            Assert.Equal(1, _app.RowCount);
            Assert.Equal(@"C:\TestSource", _app.GetText(0, _app.Msg.Column.Source));
            Assert.Equal(@"D:\TestDest", _app.GetText(0, _app.Msg.Column.Dest));
            Assert.Equal(_app.Msg.Status.Wait, _app.GetStatus(0));
            Assert.Contains(_app.Msg.Ui.DirtyMark, _app.Title);
        }

        [Fact]
        public void 処理種別を削除にすると_転送先が入力不可になりモードが削除用になる()
        {
            _app.SetRow(0, TransferAction.Copy, @"C:\TestSource", @"D:\TestDest");

            _app.SetCombo(0, _app.Msg.Column.Action, _app.Msg.Actions[TransferAction.Delete]);

            Assert.True(_app.IsReadOnly(0, _app.Msg.Column.Dest));
            Assert.Equal(_app.Msg.Modes[TransferMode.DeleteAll], _app.GetText(0, _app.Msg.Column.Mode));
        }

        [Theory]
        [InlineData(TransferAction.Copy, true, true, true)]      // 日付付与・モードとも使う
        [InlineData(TransferAction.Delete, false, false, true)]  // 転送先・日付付与は使わない
        [InlineData(TransferAction.Deploy, true, false, false)]  // モードは1つだけ、日付付与は使わない
        [InlineData(TransferAction.Backup, true, false, false)]
        [InlineData(TransferAction.Restore, true, false, false)]
        public void 処理種別に合わせて_使わないセルは入力できなくなる(TransferAction action, bool destEnabled, bool dateEnabled, bool modeEnabled)
        {
            _app.SetRow(0, TransferAction.Copy, @"C:\TestSource", @"D:\TestDest");

            _app.SetCombo(0, _app.Msg.Column.Action, _app.Msg.Actions[action]);

            Assert.Equal(!destEnabled, _app.IsReadOnly(0, _app.Msg.Column.Dest));
            Assert.Equal(!dateEnabled, _app.IsReadOnly(0, _app.Msg.Column.Date));
            Assert.Equal(!modeEnabled, _app.IsReadOnly(0, _app.Msg.Column.Mode));
        }

        [Fact]
        public void 削除ボタン_行が消える()
        {
            _app.SetRow(0, TransferAction.Copy, @"C:\First", @"D:\Dest");
            _app.SetRow(1, TransferAction.Copy, @"C:\Second", @"D:\Dest");

            _app.ClickRowButton(0, _app.Msg.Column.Delete);

            _app.WaitUntil(() => _app.RowCount == 1, "行が削除されませんでした。");
            Assert.Equal(@"C:\Second", _app.GetText(0, _app.Msg.Column.Source));
        }

        [Fact]
        public void 上下ボタン_行が入れ替わる()
        {
            _app.SetRow(0, TransferAction.Copy, @"C:\First", @"D:\Dest");
            _app.SetRow(1, TransferAction.Copy, @"C:\Second", @"D:\Dest");

            _app.ClickRowButton(1, _app.Msg.Column.Up);

            _app.WaitUntil(() => _app.GetText(0, _app.Msg.Column.Source) == @"C:\Second", "行が上へ移動しませんでした。");
            Assert.Equal(@"C:\First", _app.GetText(1, _app.Msg.Column.Source));
        }

        [Fact]
        public void 入力エラーの行_実行するとエラーになりログに理由が出る()
        {
            _app.SetRow(0, TransferAction.Copy, @"C:\TestSource"); // 転送先が空

            _app.ExecuteAll();

            Assert.Equal(_app.Msg.Status.Err, _app.GetStatus(0));
            Assert.Contains(_app.Msg.Input.DestEmpty, _app.Log);
        }

        [Fact]
        public void 自動処理のチェック_監視の開始と停止がログに出る()
        {
            _app.SetAutoRun(true);
            _app.SetAutoRun(false);

            Assert.Contains(Messages.Format(_app.Msg.AutoRun.Start, _app.Config.AutoRunPlanDir, _app.Config.AutoRunIntervalMs), _app.Log);
            Assert.Contains(_app.Msg.AutoRun.Stop, _app.Log);
        }

        [Fact]
        public void 調査用のDumpTree_画面の要素と使える操作が書き出される()
        {
            string tree = _app.DumpTree();
            string combo = _app.DumpTree("cmbProfiles");

            Assert.Contains("Id=\"btnExecuteAll\" [Invoke", tree);
            Assert.StartsWith("ComboBox", combo);
            Assert.Contains("ExpandCollapse", combo);
        }

        /// <summary>
        /// プロファイル「UiTest」を保存して開いた状態にする。editAfterSave なら、さらに編集して未保存にする。
        /// </summary>
        private void OpenUiTestProfile(bool editAfterSave)
        {
            _app.SetRow(0, TransferAction.Copy, @"C:\TestSource", @"D:\TestDest");
            _app.SaveAs(System.IO.Path.Combine(_app.Config.ProfilesDir, "UiTest.tsv"));
            if (!editAfterSave) return;
            _app.SetText(0, _app.Msg.Column.Dest, @"D:\Changed");
            Assert.Contains(_app.Msg.Ui.DirtyMark, _app.Title);
        }

        [Fact]
        public void コンボで選ぶと_すぐ読み込まれる()
        {
            OpenUiTestProfile(editAfterSave: false);

            _app.LoadProfile(_app.Msg.Ui.ProfileUnselected);

            Assert.False(_app.IsUnsavedBarVisible);
            Assert.Equal(0, _app.RowCount);
            Assert.Contains(_app.Msg.Ui.ProfileUnselected, _app.Title);
        }

        [Fact]
        public void 未保存のまま選ぶと_確認の帯が出て一覧は残り_キャンセルで帯が消える()
        {
            OpenUiTestProfile(editAfterSave: true);

            _app.LoadProfile(_app.Msg.Ui.ProfileUnselected);

            // 別ウィンドウではなく、画面の中に確認が出る
            Assert.True(_app.IsUnsavedBarVisible);
            Assert.Contains(Messages.Format(_app.Msg.Ui.UnsavedBar, _app.Msg.Ui.ProfileUnselected), _app.UnsavedBarText);
            Assert.Equal(1, _app.RowCount);
            Assert.Equal(@"D:\Changed", _app.GetText(0, _app.Msg.Column.Dest));
            Assert.Contains("UiTest", _app.Title);

            _app.CancelLoad();

            Assert.Equal(@"D:\Changed", _app.GetText(0, _app.Msg.Column.Dest));
            Assert.Contains(_app.Msg.Ui.DirtyMark, _app.Title);
        }

        [Fact]
        public void 未保存のまま選んで_破棄して読み込むを押すと_読み込まれる()
        {
            OpenUiTestProfile(editAfterSave: true);
            _app.LoadProfile(_app.Msg.Ui.ProfileUnselected);

            _app.DiscardAndLoad();

            Assert.Equal(0, _app.RowCount);
            Assert.Contains(_app.Msg.Ui.ProfileUnselected, _app.Title);
            Assert.DoesNotContain(_app.Msg.Ui.DirtyMark, _app.Title);
        }
    }
}
