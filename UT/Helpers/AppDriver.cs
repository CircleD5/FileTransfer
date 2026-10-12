using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using FileTransfer2;

namespace UT
{
    /// <summary>
    /// UI テストからアプリを操作するためのクラス。画面の操作はすべてここに集め、テスト側には書かない。
    ///
    /// ・キーボードやマウスは使わず、UI Automation で値の設定やボタンの実行をする
    ///   （テスト中もほかの作業はできる。ただし、テストが開くアプリの画面に触ると、テストの操作と食い違って失敗することがある）
    /// ・設定ファイル・プロファイル・自動処理・ログのフォルダは一時フォルダに作る（本番のフォルダを汚さない）
    /// ・列は見出しの文言で探し、文言は messages.json から取る（列の追加や文言の変更でテストが壊れない）
    /// ・待ち合わせは「条件がそろうまで待つ」で行い、決め打ちの Sleep はしない
    /// </summary>
    public class AppDriver : IDisposable
    {
        // テストの DLL（UT\bin\Debug）から見た、アプリの exe の場所
        private static readonly string AppPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\FileTransfer2\bin\Debug\FileTransfer2.exe"));
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly TempConfigFiles _files;
        private readonly UIA3Automation _automation;
        private readonly Application _app;
        private readonly Window _window;

        /// <summary>アプリが使っている設定（フォルダの場所や文言）</summary>
        public AppConfig Config { get; }
        /// <summary>アプリが使っている文言（列名やステータスなど、テストで比べる文字に使う）</summary>
        public Messages Msg => Config.Msg;
        /// <summary>テストで使うファイルを置くフォルダ（終了時に消える）</summary>
        public string WorkDir { get; }

        /// <param name="setup">アプリを起動する前に、設定を書き換えたりファイルを置いたりする処理（起動時の動きを確かめるテスト用）</param>
        public AppDriver(Action<TempConfigFiles, Dictionary<string, string>> setup = null)
        {
            _files = new TempConfigFiles();
            var settings = _files.ValidSettings();
            settings["AutoRunIntervalMs"] = "500"; // 自動処理のテストを早く終わらせる
            if (setup != null) setup(_files, settings);
            _files.WriteSettings(settings);
            Config = _files.LoadConfig();
            WorkDir = Path.Combine(_files.Dir, "Work");
            Directory.CreateDirectory(WorkDir);

            _automation = new UIA3Automation();
            _app = Application.Launch(AppPath, $"\"{_files.SettingsPath}\"");
            _window = _app.GetMainWindow(_automation, Timeout);
        }

        #region 画面全体

        /// <summary>画面のタイトル（開いているプロファイル名と未保存の印が出る）</summary>
        public string Title => _window.Title;
        /// <summary>
        /// ログの全文。画面のログ欄と同じ内容が、日付ごとのログファイルにすべて書かれているので、そこから読む。
        /// （画面のログ欄は UI Automation では約4,000文字で切れて読めなくなるため。ScreenLog を参照）
        /// </summary>
        public string Log => ReadLogFiles();

        /// <summary>
        /// 画面のログ欄の文字。UI Automation の制限で、約4,000文字を超えると途中までしか読めない。
        /// 画面に出ていることを確かめたいときだけ使い、ふだんは Log（ログファイルから読む）を使う。
        /// </summary>
        public string ScreenLog => GetById("txtLog").Patterns.Value.Pattern.Value.Value;
        /// <summary>「すべて実行」が押せるか（実行中は押せない）</summary>
        public bool IsExecuteAllEnabled => GetById("btnExecuteAll").IsEnabled;

        /// <summary>
        /// 「すべて実行」を押し、一括処理が終わるまで待つ。
        /// </summary>
        public void ExecuteAll()
        {
            int endCount = CountInLog(Msg.Run.RunEnd);
            Invoke(GetById("btnExecuteAll"));
            WaitUntil(() => CountInLog(Msg.Run.RunEnd) > endCount, "実行が終わりませんでした。");
            // 実行が終わった後の片付け（復元用の計画の保存など）まで終わると、ボタンが押せるようになる
            WaitUntil(() => IsExecuteAllEnabled, "実行の後の片付けが終わりませんでした。");
        }

        /// <summary>
        /// 「すべて実行」を押すだけで、終わるのを待たない（実行中の画面や「中止」を確かめるとき）。
        /// 終わるのを待つときは WaitForRunEnd を使う。
        /// </summary>
        public void StartExecuteAll() => Invoke(GetById("btnExecuteAll"));

        /// <summary>
        /// ログの「実行が終わりました」が count 回になるまで待つ（StartExecuteAll で始めた実行の終わりを待つ）。
        /// </summary>
        public void WaitForRunEnd(int count)
        {
            WaitUntil(() => CountInLog(Msg.Run.RunEnd) >= count, "実行が終わりませんでした。");
            WaitUntil(() => IsExecuteAllEnabled, "実行の後の片付けが終わりませんでした。");
        }

        /// <summary>「中止」を押す。</summary>
        public void Stop() => Invoke(GetById("btnStop"));
        /// <summary>「中止」が押せるか（実行中だけ押せる）</summary>
        public bool IsStopEnabled => GetById("btnStop").IsEnabled;
        /// <summary>進み具合の表示（「実行中：n / 全件」。実行していないときは空）</summary>
        public string Progress => GetById("lblProgress").Name;
        /// <summary>ログに text が何回出ているか</summary>
        public int CountLog(string text) => CountInLog(text);

        /// <summary>
        /// 「名前を付けて保存」で、指定したパスに保存する。
        /// </summary>
        public void SaveAs(string filePath)
        {
            Invoke(GetById("btnSaveAs"));
            Window dialog = WaitForDialog();
            // ファイル名の入力欄（1001）と「保存」ボタン（1）は Windows 標準ダイアログの決まった ID
            dialog.FindFirstDescendant(cf => cf.ByAutomationId("1001")).Patterns.Value.Pattern.SetValue(filePath);
            Invoke(dialog.FindFirstDescendant(cf => cf.ByAutomationId("1").And(cf.ByControlType(ControlType.Button))));
            WaitUntil(() => _window.ModalWindows.Length == 0, "保存ダイアログが閉じませんでした。");
        }

        /// <summary>
        /// プロファイルをコンボで選ぶ（アプリは選んだらすぐ読み込む）。
        /// 未保存の変更があるときは、読み込まれずに確認の帯が出る（IsUnsavedBarVisible で確かめ、DiscardAndLoad / CancelLoad で答える）。
        /// </summary>
        public void LoadProfile(string name)
        {
            // 選んでもダイアログは出ないので、UI Automation の普通の操作で選んでよい。
            // ただし WinForms のコンボは、ドロップダウンを開いている間しか項目が見えず、
            // FlaUI の ComboBox.Select() では選べないため、「開く → 項目を選ぶ → 閉じる」を自分で行う。
            var combo = GetById("cmbProfiles");
            combo.Patterns.ExpandCollapse.Pattern.Expand();
            var item = Retry.WhileNull(() => combo.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName(name))), Timeout).Result;
            if (item == null) throw new InvalidOperationException($"プロファイル「{name}」が一覧にありません。");
            item.Patterns.SelectionItem.Pattern.Select();
            combo.Patterns.ExpandCollapse.Pattern.Collapse();

            // 確認の帯が出るか、タイトルが選んだプロファイル名に変わるまで待つ
            string expectedTitle = Messages.Format(Msg.Ui.TitleFormat, name, "").TrimEnd();
            WaitUntil(() => IsUnsavedBarVisible || Title.StartsWith(expectedTitle), $"プロファイル「{name}」を読み込めませんでした。");
        }

        /// <summary>未保存の確認の帯が出ているか（隠れているときは画面の要素として見つからない）</summary>
        public bool IsUnsavedBarVisible => FindById("lblUnsaved") != null;

        /// <summary>未保存の確認の帯の文言</summary>
        public string UnsavedBarText => GetById("lblUnsaved").Name;

        /// <summary>確認の帯の「破棄して読み込む」を押し、帯が消えるまで待つ。</summary>
        public void DiscardAndLoad()
        {
            Invoke(GetById("btnDiscardAndLoad"));
            WaitUntil(() => !IsUnsavedBarVisible, "確認の帯が消えませんでした。");
        }

        /// <summary>確認の帯の「キャンセル」を押し、帯が消えるまで待つ。</summary>
        public void CancelLoad()
        {
            Invoke(GetById("btnCancelLoad"));
            WaitUntil(() => !IsUnsavedBarVisible, "確認の帯が消えませんでした。");
        }

        /// <summary>「自動処理」にチェックが入っているか</summary>
        public bool IsAutoRunOn => GetById("chkAutoRun").Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On;

        /// <summary>
        /// 「自動処理」のチェックを入れる（on が true）・外す。
        /// </summary>
        public void SetAutoRun(bool on)
        {
            var check = GetById("chkAutoRun");
            WaitUntil(() => check.IsEnabled, "自動処理のチェックボックスが操作できません。");
            var toggle = check.Patterns.Toggle.Pattern;
            if ((toggle.ToggleState.Value == ToggleState.On) != on) toggle.Toggle();
        }

        /// <summary>
        /// 「上書き保存」を押し、上書きの確認に「はい」で答える。
        /// </summary>
        public void Save()
        {
            Invoke(GetById("btnSave"));
            AnswerDialog(yes: true);
        }

        /// <summary>
        /// 表示中の「OK」だけのメッセージボックスを閉じる。
        /// </summary>
        public void CloseDialog()
        {
            Window dialog = WaitForDialog();
            // 「OK」だけのメッセージボックスでは、OK ボタンの ID は 2（Windows 標準の決まり）
            Invoke(dialog.FindFirstDescendant(cf => cf.ByAutomationId("2")));
            WaitUntil(() => _window.ModalWindows.Length == 0, "ダイアログが閉じませんでした。");
        }

        /// <summary>
        /// 表示中のダイアログ（メッセージボックス）の本文を返す。
        /// </summary>
        public string DialogText()
        {
            return string.Join("\n", WaitForDialog().FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(e => e.Name));
        }

        /// <summary>
        /// 表示中のダイアログのボタンを押す。yes = true で「はい」、false で「いいえ」。
        /// </summary>
        public void AnswerDialog(bool yes)
        {
            Window dialog = WaitForDialog();
            // 「はい」（6）と「いいえ」（7）は Windows 標準メッセージボックスの決まった ID
            string buttonId = yes ? "6" : "7";
            Invoke(dialog.FindFirstDescendant(cf => cf.ByAutomationId(buttonId)));
            WaitUntil(() => _window.ModalWindows.Length == 0, "ダイアログが閉じませんでした。");
        }

        #endregion

        #region 一覧（グリッド）

        /// <summary>
        /// 一覧の行数（一番下の新規入力用の行は数えない）。
        /// </summary>
        public int RowCount => GetGridRows().Length - 2; // 見出しの行と新規入力用の行を除く

        /// <summary>
        /// 1行分を入力する。row は 0 から数える。新規入力用の行に入力すると、その下に次の新規入力用の行ができる。
        /// </summary>
        public void SetRow(int row, TransferAction action, string source, string dest = null, TransferMode? mode = null)
        {
            SetText(row, Msg.Column.Source, source);
            SetCombo(row, Msg.Column.Action, Msg.Actions[action]);
            if (dest != null) SetText(row, Msg.Column.Dest, dest);
            if (mode != null) SetCombo(row, Msg.Column.Mode, Msg.Modes[mode.Value]);
        }

        /// <summary>
        /// テキストのセルに入力する。
        /// </summary>
        public void SetText(int row, string column, string text)
        {
            // セルを編集状態にしてから、表示された入力欄に値を入れる（人が入力したときと同じ流れになる）
            GetCell(row, column).Patterns.LegacyIAccessible.Pattern.DoDefaultAction();
            var editor = Retry.WhileNull(() => GetCell(row, column).FindFirstChild(cf => cf.ByControlType(ControlType.Edit)), Timeout).Result;
            if (editor == null) throw new InvalidOperationException($"{row}行目の「{column}」が編集できません。");
            editor.Patterns.Value.Pattern.SetValue(text);
            EndEdit(row);
        }

        /// <summary>
        /// プルダウンのセルで、表示名を指定して選ぶ。
        /// </summary>
        public void SetCombo(int row, string column, string displayText)
        {
            GetCell(row, column).Patterns.Value.Pattern.SetValue(displayText);
        }

        public string GetText(int row, string column) => GetCell(row, column).Patterns.Value.Pattern.Value.Value;
        public string GetStatus(int row) => GetText(row, Msg.Column.Status);
        public bool IsReadOnly(int row, string column) => GetCell(row, column).Patterns.Value.Pattern.IsReadOnly.Value;

        /// <summary>
        /// 行の中のボタン（個別実行・削除・上・下）を押す。
        /// </summary>
        public void ClickRowButton(int row, string column) => Invoke(GetCell(row, column));

        /// <summary>
        /// 行の「個別実行」を押し、結果（完了・エラー）が出るまで待つ。
        /// </summary>
        public void ExecuteRow(int row)
        {
            ClickRowButton(row, Msg.Column.Exec);
            WaitUntil(() => IsExecuteAllEnabled && GetStatus(row) != Msg.Status.Proc && GetStatus(row) != Msg.Status.Wait, "個別実行が終わりませんでした。");
        }

        #endregion

        #region 調査用

        /// <summary>
        /// 画面の要素ツリー（UI Automation からどう見えているか）を文字列にする。テストがうまく動かないときの調査用。
        ///
        /// 1行に1要素で「種類 Name="表示名" Id="AutomationId" [使える操作(パターン)]」を、親子関係を字下げで表す。
        ///   例）ComboBox Name="A" Id="cmbProfiles" [ExpandCollapse,Value,LegacyIAccessible]
        ///       → Selection が無いので「選択中の項目」は Value で読む必要がある、と分かる
        ///
        /// 使い方：調べたい場所に一時的に次の1行を入れてテストを実行し、出力されたファイルを見る。
        ///   File.WriteAllText(@"C:\temp\tree.txt", _app.DumpTree());
        /// 一部だけ見たいときは、その要素を渡す（例：ドロップダウンを開いた状態のコンボだけ）。
        ///   File.WriteAllText(@"C:\temp\combo.txt", _app.DumpTree("cmbProfiles"));
        /// ※ 画面全体だと一覧の行数に比例して時間がかかる。調査が終わったら呼び出しは消しておく。
        /// </summary>
        /// <param name="automationId">この ID の要素から下だけを書き出す。省略すると画面全体。</param>
        public string DumpTree(string automationId = null)
        {
            // ID を省略したときは、画面全体を書き出す
            AutomationElement root;
            if (automationId == null) root = _window;
            else root = GetById(automationId);
            var sb = new StringBuilder();
            DumpElement(root, 0, sb);
            return sb.ToString();
        }

        /// <summary>
        /// 要素1つを1行に書き出し、子の要素も字下げして書き出す（DumpTree から呼ぶ）。
        /// </summary>
        private static void DumpElement(AutomationElement element, int depth, StringBuilder sb)
        {
            // 種類や名前を持たない要素もあるので、取れない値は空として書き出す（調査用なので途中で止めない）
            var props = element.Properties;
            string patterns;
            try { patterns = string.Join(",", element.GetSupportedPatterns().Select(p => p.Name)); }
            catch (Exception) { patterns = "?"; }
            sb.AppendLine($"{new string(' ', depth * 2)}{props.ControlType.ValueOrDefault} Name=\"{props.Name.ValueOrDefault}\" Id=\"{props.AutomationId.ValueOrDefault}\" [{patterns}]");
            foreach (var child in element.FindAllChildren())
            {
                DumpElement(child, depth + 1, sb);
            }
        }

        #endregion

        #region 共通の部品

        /// <summary>
        /// 条件がそろうまで待つ。時間内にそろわなければテスト失敗にする。
        /// </summary>
        public void WaitUntil(Func<bool> condition, string failMessage)
        {
            bool ok = Retry.WhileFalse(() =>
            {
                try { return condition(); }
                catch (Exception) { return false; } // 画面の更新中で要素が取れないときは、もう一度試す
            }, Timeout).Result;
            if (!ok) throw new TimeoutException(failMessage);
        }

        /// <summary>
        /// AutomationId（WinForms ではコントロールの Name）で要素を探す。見つかるまで少し待ち、見つからなければ例外。
        /// </summary>
        private AutomationElement GetById(string automationId)
        {
            var element = Retry.WhileNull(() => _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), Timeout).Result;
            if (element == null) throw new InvalidOperationException($"画面に「{automationId}」が見つかりません。");
            return element;
        }

        /// <summary>
        /// 今この瞬間に画面にある要素を探す（待たない）。見つからなければ null。
        /// </summary>
        private AutomationElement FindById(string automationId) => _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

        private AutomationElement GetGrid() => GetById("dgvTasks");

        /// <summary>
        /// 一覧の行を「見出しの行, 1行目, 2行目, …, 新規入力用の行」の順で返す。
        /// 行が多くて画面に入りきらないと、スクロールバーも一覧の子要素として先頭に入ってくるので、それを除く。
        /// （画面の外の行も要素としては全部あるので、スクロールしなくても読み書きできる）
        /// </summary>
        private AutomationElement[] GetGridRows()
        {
            // 一覧の行は ControlType を持たない（そのまま読むと例外になる）ので、取れないときは既定値として扱う
            return GetGrid().FindAllChildren()
                            .Where(child => child.Properties.ControlType.ValueOrDefault != ControlType.ScrollBar)
                            .ToArray();
        }

        /// <summary>
        /// セルを探す。グリッドの子要素は「見出しの行, 1行目, 2行目, …」の順に並び、
        /// 各行の子要素は「行見出し, 1列目, 2列目, …」の順に並んでいる。
        /// </summary>
        private AutomationElement GetCell(int row, string column)
        {
            var cell = Retry.WhileNull(() =>
            {
                var rows = GetGridRows();
                if (rows.Length <= row + 1) return null; // その行がまだない

                int col = Array.FindIndex(rows[0].FindAllChildren(), header => header.Name == column);
                var cells = rows[row + 1].FindAllChildren();
                if (col < 0 || col >= cells.Length) return null; // その列が見つからない

                return cells[col];
            }, Timeout).Result;
            if (cell == null) throw new InvalidOperationException($"{row}行目の「{column}」セルが見つかりません。");
            return cell;
        }

        /// <summary>
        /// 編集中のセルを確定する（同じ行の読み取り専用セルへ移動させる）。
        /// </summary>
        private void EndEdit(int row)
        {
            GetCell(row, Msg.Column.Status).Patterns.LegacyIAccessible.Pattern.DoDefaultAction();
        }

        /// <summary>
        /// ボタンを押す。
        /// UI Automation の Invoke は、押した結果ダイアログが開くと閉じるまで戻らず、その間ほかの操作もできなくなる。
        /// そのため、ウィンドウを持つボタンは「クリックしたという通知（BM_CLICK）」を投げるだけにする。
        /// 一覧の中のボタンのようにウィンドウを持たないものは Invoke で押す。
        /// </summary>
        private static void Invoke(AutomationElement element)
        {
            IntPtr hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd != IntPtr.Zero)
                NativeMethods.PostMessage(hwnd, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            else
                element.Patterns.Invoke.Pattern.Invoke();
        }

        /// <summary>
        /// 【学習用・今は未使用】コンボの選択変更をきっかけにダイアログが開く画面を操作するための部品。
        ///
        /// このアプリは「コンボで選ぶ → ボタンで確定」の作りなので、今は使っていない。
        /// 選択変更で即ダイアログが出る画面ができた場合は、FlaUI の Select() だとダイアログが閉じるまで
        /// 戻らなくなる（Invoke と同じ問題）ため、この方法で「選択が変わった」という通知を投げるだけにする。
        ///
        /// 仕組み：
        ///   1. CB_SETCURSEL で選択位置だけを変える（これだけではアプリの SelectedIndexChanged は起きない）
        ///   2. 親ウィンドウへ WM_COMMAND（CBN_SELCHANGE）を投げ、人が選んだときと同じ通知を届ける
        ///
        /// 注意（実際にハマった点）：
        ///   WM_COMMAND の wParam は「上位16ビット＝通知の種類、下位16ビット＝コントロールID」を1つの数値に詰めたもの。
        ///   WinForms はコントロールIDにウィンドウハンドルの値（例：0x3A0F30）を使っていて16ビットに収まらないため、
        ///   そのまま (CBN_SELCHANGE << 16) | id とすると、はみ出した部分が上位16ビットに混ざって通知の種類が
        ///   1 ではなく 0x3B（59）になり、アプリに無視される（エラーも出ない）。
        ///   → id を & 0xFFFF で下位16ビットに切り詰めてから合成する。
        ///   （WinForms は通知元を lParam のハンドルで判断するので、ID を切り詰めても問題ない）
        /// </summary>
        public static void SelectComboItemWithNotification(AutomationElement combo, string itemText)
        {
            IntPtr hwnd = combo.Properties.NativeWindowHandle.Value;
            int index = (int)NativeMethods.SendMessage(hwnd, NativeMethods.CB_FINDSTRINGEXACT, new IntPtr(-1), itemText);
            if (index < 0) throw new InvalidOperationException($"「{itemText}」が一覧にありません。");

            NativeMethods.SendMessage(hwnd, NativeMethods.CB_SETCURSEL, new IntPtr(index), null);
            IntPtr wParam = new IntPtr((NativeMethods.CBN_SELCHANGE << 16) | (NativeMethods.GetDlgCtrlID(hwnd) & 0xFFFF));
            NativeMethods.PostMessage(NativeMethods.GetParent(hwnd), NativeMethods.WM_COMMAND, wParam, hwnd);
        }

        private static class NativeMethods
        {
            public const uint BM_CLICK = 0x00F5;
            public const uint WM_COMMAND = 0x0111;
            public const uint CB_SETCURSEL = 0x014E;
            public const uint CB_FINDSTRINGEXACT = 0x0158;
            public const int CBN_SELCHANGE = 1;

            [DllImport("user32.dll")]
            public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

            [DllImport("user32.dll")]
            public static extern int GetDlgCtrlID(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern IntPtr GetParent(IntPtr hWnd);
        }

        /// <summary>
        /// メッセージボックスなどのダイアログが出るまで待ち、それを返す。
        /// </summary>
        private Window WaitForDialog()
        {
            var dialog = Retry.WhileNull(() => _window.ModalWindows.FirstOrDefault(), Timeout, ignoreException: true).Result;
            if (dialog == null) throw new TimeoutException("ダイアログが表示されませんでした。");
            return dialog;
        }

        private int CountInLog(string text) => Regex.Matches(Log, Regex.Escape(text)).Count;

        /// <summary>
        /// 日付ごとのログファイル（yyyyMMdd.log）を古い順に読んでつなげる（テストが日付をまたいでも全部読めるように）。
        /// アプリが書き込んでいる最中でも読めるよう、ほかのプログラムとの共有を許可して開く。
        /// </summary>
        private string ReadLogFiles()
        {
            if (!Directory.Exists(Config.LogDir)) return "";

            var sb = new StringBuilder();
            foreach (string file in Directory.GetFiles(Config.LogDir, "????????.log").OrderBy(name => name))
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    sb.Append(reader.ReadToEnd());
                }
            }
            return sb.ToString();
        }

        #endregion

        /// <summary>
        /// テストの後始末：アプリを強制終了し、一時フォルダを消す。
        /// </summary>
        public void Dispose()
        {
            // 未保存の確認などで終了が止まらないよう、テストの後始末では強制終了する
            try
            {
                using (var process = Process.GetProcessById(_app.ProcessId))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (ArgumentException) { } // すでに終了している
            _automation.Dispose();
            _files.Dispose();
        }
    }
}
