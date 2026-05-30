using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileTransfer2
{
    public partial class Form1 : Form
    {
        #region 0. 定数・フィールド・データバインディング用辞書

        private readonly string AutoRunPlanDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoRun", "Plan");
        private readonly string AutoRunDoneDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoRun", "Done");
        private readonly string ProfilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");

        private Timer autoRunTimer;
        private FileTransferEngine transferEngine;

        // 【UI表示用】EnumをKeyとして画面表示文字列を提供する辞書（データバインド用）
        private static readonly Dictionary<TransferAction, string> ActionBindSource = new Dictionary<TransferAction, string>
        {
            { TransferAction.Copy, "コピー" },
            { TransferAction.Move, "移動" },
            { TransferAction.Delete, "削除" }
        };

        private static readonly Dictionary<TransferMode, string> TransferModeBindSource = new Dictionary<TransferMode, string>
        {
            { TransferMode.FolderAsIs, "フォルダごと転送" },
            { TransferMode.ExtractContents, "中身を展開して転送" }
        };

        private static readonly Dictionary<TransferMode, string> DeleteModeBindSource = new Dictionary<TransferMode, string>
        {
            { TransferMode.DeleteAll, "丸ごと削除" },
            { TransferMode.DeleteKeepParent, "親フォルダのみ残す" },
            { TransferMode.DeleteFilesOnly, "ファイルのみ削除" }
        };

        private static readonly Dictionary<TransferMode, string> AllModeDisplayDict = new Dictionary<TransferMode, string>
        {
            { TransferMode.FolderAsIs, "フォルダごと転送" },
            { TransferMode.ExtractContents, "中身を展開して転送" },
            { TransferMode.DeleteAll, "丸ごと削除" },
            { TransferMode.DeleteKeepParent, "親フォルダのみ残す" },
            { TransferMode.DeleteFilesOnly, "ファイルのみ削除" }
        };

        // 【ファイル読込用】TSVの文字列からEnumへ逆変換する辞書（後方互換性含む）
        private static readonly Dictionary<string, TransferMode> ModeParseDict = new Dictionary<string, TransferMode>
        {
            { "フォルダごと転送", TransferMode.FolderAsIs },
            { "中身を展開して転送", TransferMode.ExtractContents },
            { "丸ごと削除", TransferMode.DeleteAll },
            { "親フォルダのみ残す", TransferMode.DeleteKeepParent },
            { "ファイルのみ削除", TransferMode.DeleteFilesOnly },
            { "---", TransferMode.FolderAsIs },
            { "階層維持", TransferMode.FolderAsIs },
            { "階層無視", TransferMode.ExtractContents },
            { "中身のみ削除", TransferMode.DeleteKeepParent }
        };

        #endregion

        #region 1. 初期化処理

        public Form1()
        {
            InitializeComponent();
            SetupDataGridView();

            transferEngine = new FileTransferEngine(WriteLog);

            btnExecuteAll.Click += btnExecuteAll_Click;
            btnSave.Click += btnSave_Click;
            btnLoad.Click += btnLoad_Click;
            chkAutoRun.CheckedChanged += chkAutoRun_CheckedChanged;
            cmbProfiles.SelectedIndexChanged += cmbProfiles_SelectedIndexChanged;

            dgvTasks.CellContentClick += dgvTasks_CellContentClick;
            dgvTasks.DefaultValuesNeeded += dgvTasks_DefaultValuesNeeded;
            dgvTasks.CurrentCellDirtyStateChanged += dgvTasks_CurrentCellDirtyStateChanged;
            dgvTasks.CellValueChanged += dgvTasks_CellValueChanged;
            dgvTasks.DataError += dgvTasks_DataError; // データバインド切替時の内部エラーを握り潰すため必須

            InitializeAutoRun();
            InitializeProfiles();
        }

        public void LogGlobalError(Exception ex)
        {
            WriteLog($"[システムエラー] 予期せぬ例外を捕捉し、強制終了を防止しました。");
            WriteLog($"詳細: {ex.Message}");
        }

        private void WriteLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => WriteLog(message)));
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }

        #endregion

        #region 2. UI連動 (DataGridView制御 - Enumバインディング)

        private void SetupDataGridView()
        {
            dgvTasks.AutoGenerateColumns = false;
            dgvTasks.Columns.Clear();

            // Action列：DataSourceに辞書をバインド。実態(Value)はEnum、表示(Display)は文字列となる。
            var colAction = new DataGridViewComboBoxColumn { Name = "ActionColumn", HeaderText = "処理種別", Width = 80 };
            colAction.DataSource = new BindingSource(ActionBindSource, null);
            colAction.DisplayMember = "Value";
            colAction.ValueMember = "Key";
            dgvTasks.Columns.Add(colAction);

            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceColumn", HeaderText = "転送元", Width = 200 });
            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "DestColumn", HeaderText = "転送先", Width = 200 });

            // Mode列：各セルごとにDataSourceを切り替えるため、列定義ではメンバー指定のみ行う。
            var colMode = new DataGridViewComboBoxColumn { Name = "ModeColumn", HeaderText = "モード", Width = 150 };
            colMode.DisplayMember = "Value";
            colMode.ValueMember = "Key";
            dgvTasks.Columns.Add(colMode);

            dgvTasks.Columns.Add(new DataGridViewCheckBoxColumn { Name = "DateColumn", HeaderText = "日付付与", Width = 70 });
            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusColumn", HeaderText = "ステータス", Width = 100, ReadOnly = true });
            dgvTasks.Columns.Add(new DataGridViewButtonColumn { Name = "ExecColumn", HeaderText = "個別実行", Text = "▶ 実行", UseColumnTextForButtonValue = true, Width = 80 });
            dgvTasks.Columns.Add(new DataGridViewButtonColumn { Name = "DelColumn", HeaderText = "行削除", Text = "×", UseColumnTextForButtonValue = true, Width = 60 });
        }

        private void dgvTasks_DefaultValuesNeeded(object sender, DataGridViewRowEventArgs e)
        {
            // 初期値をEnumとして直接設定
            e.Row.Cells["ActionColumn"].Value = TransferAction.Copy;
            e.Row.Cells["DateColumn"].Value = false;
            e.Row.Cells["StatusColumn"].Value = "待機中";

            UpdateRowState(e.Row, TransferAction.Copy);
        }

        private void dgvTasks_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dgvTasks.IsCurrentCellDirty && dgvTasks.CurrentCell is DataGridViewComboBoxCell)
                dgvTasks.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void dgvTasks_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (dgvTasks.Columns[e.ColumnIndex].Name == "ActionColumn")
            {
                if (dgvTasks.Rows[e.RowIndex].Cells["ActionColumn"].Value is TransferAction action)
                {
                    UpdateRowState(dgvTasks.Rows[e.RowIndex], action);
                }
            }
        }

        // セル単位でのDataSource切り替えと、UIロック状態の制御
        private void UpdateRowState(DataGridViewRow row, TransferAction action)
        {
            var destCell = row.Cells["DestColumn"];
            var modeCell = (DataGridViewComboBoxCell)row.Cells["ModeColumn"];

            if (action == TransferAction.Delete)
            {
                modeCell.DataSource = new BindingSource(DeleteModeBindSource, null);
                if (modeCell.Value == null || !DeleteModeBindSource.ContainsKey((TransferMode)modeCell.Value))
                {
                    modeCell.Value = TransferMode.DeleteAll;
                }
                destCell.Value = "";
                destCell.ReadOnly = true;
                destCell.Style.BackColor = Color.LightGray;
            }
            else
            {
                modeCell.DataSource = new BindingSource(TransferModeBindSource, null);
                if (modeCell.Value == null || !TransferModeBindSource.ContainsKey((TransferMode)modeCell.Value))
                {
                    modeCell.Value = TransferMode.FolderAsIs;
                }
                destCell.ReadOnly = false;
                destCell.Style.BackColor = Color.White;
            }
        }

        private void dgvTasks_DataError(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; }

        #endregion

        #region 3. プロファイル管理 (TSV設定の保存・読込)

        private void InitializeProfiles()
        {
            if (!Directory.Exists(ProfilesDir)) Directory.CreateDirectory(ProfilesDir);
            RefreshProfileList();
        }

        private void RefreshProfileList()
        {
            cmbProfiles.SelectedIndexChanged -= cmbProfiles_SelectedIndexChanged;
            cmbProfiles.Items.Clear();
            cmbProfiles.Items.Add("未選択");

            foreach (string file in Directory.GetFiles(ProfilesDir, "*.tsv"))
            {
                cmbProfiles.Items.Add(Path.GetFileNameWithoutExtension(file));
            }

            cmbProfiles.SelectedIndex = 0;
            cmbProfiles.SelectedIndexChanged += cmbProfiles_SelectedIndexChanged;
        }

        private void cmbProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbProfiles.SelectedIndex <= 0) return;

            string profileName = cmbProfiles.SelectedItem.ToString();
            string filePath = Path.Combine(ProfilesDir, $"{profileName}.tsv");

            if (File.Exists(filePath))
            {
                try
                {
                    LoadTsv(filePath);
                    WriteLog($"設定パターン [{profileName}] に切り替えました。");
                }
                catch (Exception ex) { WriteLog($"切替失敗: {ex.Message}"); }
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog { Filter = "TSVファイル (*.tsv)|*.tsv", Title = "設定保存", InitialDirectory = ProfilesDir, DefaultExt = "tsv" })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                    {
                        foreach (DataGridViewRow row in dgvTasks.Rows)
                        {
                            if (row.IsNewRow) continue;

                            // 画面のセルはEnumを持っているので、保存時は文字列に翻訳する
                            TransferAction action = row.Cells["ActionColumn"].Value is TransferAction a ? a : TransferAction.Unknown;
                            TransferMode mode = row.Cells["ModeColumn"].Value is TransferMode m ? m : TransferMode.Unknown;

                            string actionStr = ActionBindSource.ContainsKey(action) ? ActionBindSource[action] : "";
                            string modeStr = AllModeDisplayDict.ContainsKey(mode) ? AllModeDisplayDict[mode] : "";
                            string src = row.Cells["SourceColumn"].Value?.ToString() ?? "";
                            string dest = row.Cells["DestColumn"].Value?.ToString() ?? "";
                            string addDate = Convert.ToBoolean(row.Cells["DateColumn"].Value ?? false).ToString();

                            sw.WriteLine($"{actionStr}\t{src}\t{dest}\t{modeStr}\t{addDate}");
                        }
                    }
                    WriteLog($"設定を保存しました: {Path.GetFileName(sfd.FileName)}");

                    RefreshProfileList();
                    SetComboSelectionWithoutEvent(Path.GetFileNameWithoutExtension(sfd.FileName));
                }
                catch (Exception ex) { WriteLog($"保存エラー: {ex.Message}"); }
            }
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "TSVファイル (*.tsv)|*.tsv", Title = "設定読込", InitialDirectory = ProfilesDir })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    LoadTsv(ofd.FileName);
                    WriteLog($"設定を読み込みました: {Path.GetFileName(ofd.FileName)}");

                    string loadedName = Path.GetFileNameWithoutExtension(ofd.FileName);
                    if (Path.GetDirectoryName(ofd.FileName) == ProfilesDir && cmbProfiles.Items.Contains(loadedName))
                    {
                        SetComboSelectionWithoutEvent(loadedName);
                    }
                    else
                    {
                        SetComboSelectionWithoutEvent("未選択");
                    }
                }
                catch (Exception ex) { WriteLog($"読込エラー: {ex.Message}"); }
            }
        }

        private void SetComboSelectionWithoutEvent(string itemName)
        {
            cmbProfiles.SelectedIndexChanged -= cmbProfiles_SelectedIndexChanged;
            cmbProfiles.SelectedItem = cmbProfiles.Items.Contains(itemName) ? itemName : cmbProfiles.Items[0];
            cmbProfiles.SelectedIndexChanged += cmbProfiles_SelectedIndexChanged;
        }

        private void LoadTsv(string filePath)
        {
            dgvTasks.Rows.Clear();
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = line.Split('\t');
                if (parts.Length >= 5)
                {
                    int rowIndex = dgvTasks.Rows.Add();
                    DataGridViewRow row = dgvTasks.Rows[rowIndex];

                    TransferAction loadedAction = ParseAction(parts[0]);
                    row.Cells["ActionColumn"].Value = loadedAction;
                    row.Cells["SourceColumn"].Value = parts[1];
                    row.Cells["DestColumn"].Value = parts[2];

                    // Actionに応じたDataSourceの適用（ModeのValueをセットする前に必須）
                    UpdateRowState(row, loadedAction);

                    TransferMode loadedMode = ModeParseDict.ContainsKey(parts[3]) ? ModeParseDict[parts[3]] : TransferMode.FolderAsIs;
                    row.Cells["ModeColumn"].Value = loadedMode;

                    row.Cells["DateColumn"].Value = bool.TryParse(parts[4], out bool isDate) && isDate;
                    row.Cells["StatusColumn"].Value = "待機中";
                }
            }
        }

        private TransferAction ParseAction(string actionStr)
        {
            switch (actionStr)
            {
                case "コピー": return TransferAction.Copy;
                case "移動": return TransferAction.Move;
                case "削除": return TransferAction.Delete;
                default: return TransferAction.Unknown;
            }
        }

        #endregion

        #region 4. AutoRun (自動実行) 監視機能

        private void InitializeAutoRun()
        {
            if (!Directory.Exists(AutoRunPlanDir)) Directory.CreateDirectory(AutoRunPlanDir);
            if (!Directory.Exists(AutoRunDoneDir)) Directory.CreateDirectory(AutoRunDoneDir);

            autoRunTimer = new Timer { Interval = 30000 };
            autoRunTimer.Tick += AutoRunTimer_Tick;
            WriteLog("システム: AutoRunの待機準備が完了しました。(監視はOFFです)");
        }

        private void chkAutoRun_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoRun.Checked)
            {
                autoRunTimer.Start();
                WriteLog("システム: AutoRun/Plan フォルダの監視を開始しました。(30秒間隔)");
            }
            else
            {
                autoRunTimer.Stop();
                WriteLog("システム: AutoRun/Plan フォルダの監視を停止しました。");
            }
        }

        private async void AutoRunTimer_Tick(object sender, EventArgs e)
        {
            autoRunTimer.Stop();

            try
            {
                foreach (string filePath in Directory.GetFiles(AutoRunPlanDir, "*.tsv"))
                {
                    if (IsFileLocked(filePath))
                    {
                        WriteLog($"自動実行: {Path.GetFileName(filePath)} は書き込み中のためスキップします。");
                        continue;
                    }

                    WriteLog($"自動実行: {Path.GetFileName(filePath)} の設定を検出しました。実行を開始します。");
                    LoadTsv(filePath);
                    await ExecuteAllTasksAsync();

                    MoveFileToDone(filePath);
                }
            }
            catch (Exception ex) { WriteLog($"自動実行エラー: {ex.Message}"); }
            finally { if (chkAutoRun.Checked) autoRunTimer.Start(); }
        }

        private bool IsFileLocked(string filePath)
        {
            try
            {
                using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None)) { return false; }
            }
            catch (IOException) { return true; }
        }

        private void MoveFileToDone(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string donePath = Path.Combine(AutoRunDoneDir, fileName);

            if (File.Exists(donePath))
            {
                string name = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                donePath = Path.Combine(AutoRunDoneDir, $"{name}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
            }
            File.Move(filePath, donePath);
            WriteLog($"自動実行: 完了した設定ファイルを Done フォルダへ移動しました。");
        }

        #endregion

        #region 5. 実行処理 (コアへの委譲)

        private async void btnExecuteAll_Click(object sender, EventArgs e)
        {
            if (dgvTasks.IsCurrentCellDirty) dgvTasks.CommitEdit(DataGridViewDataErrorContexts.Commit);
            dgvTasks.EndEdit();
            await ExecuteAllTasksAsync();
        }

        private async Task ExecuteAllTasksAsync()
        {
            btnExecuteAll.Enabled = false;
            WriteLog("=== 一括処理を開始します ===");

            foreach (DataGridViewRow row in dgvTasks.Rows)
            {
                if (!row.IsNewRow) row.Cells["StatusColumn"].Value = "待機中";
            }

            for (int i = 0; i < dgvTasks.Rows.Count; i++)
            {
                DataGridViewRow row = dgvTasks.Rows[i];
                if (row.IsNewRow) continue;

                if (!TryExtractRowData(row, out var action, out var src, out var dest, out var mode, out var addDate))
                    continue;

                row.Cells["StatusColumn"].Value = "処理中...";
                string actionDisp = ActionBindSource.ContainsKey(action) ? ActionBindSource[action] : "不明な処理";
                WriteLog($"[{i + 1}行目] {actionDisp}開始: {src}");

                try
                {
                    ProcessResult result = await Task.Run(() => transferEngine.ProcessRow(action, src, dest, mode, addDate, i + 1));
                    row.Cells["StatusColumn"].Value = (result == ProcessResult.Success) ? "完了" : "スキップ";
                    if (result == ProcessResult.Success) WriteLog($"[{i + 1}行目] 正常完了");
                }
                catch (Exception ex)
                {
                    row.Cells["StatusColumn"].Value = "エラー";
                    WriteLog($"[{i + 1}行目] 致命的なエラー: {ex.Message}");
                    WriteLog("安全のため、後続の処理をすべて中断しました。");
                    CancelRemainingTasks(i + 1);
                    break;
                }
            }

            WriteLog("=== 一括処理が終了しました ===");
            btnExecuteAll.Enabled = true;
        }

        private async void dgvTasks_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || dgvTasks.Rows[e.RowIndex].IsNewRow) return;

            string colName = dgvTasks.Columns[e.ColumnIndex].Name;

            if (colName == "DelColumn")
            {
                dgvTasks.Rows.RemoveAt(e.RowIndex);
                return;
            }

            if (colName == "ExecColumn")
            {
                if (dgvTasks.IsCurrentCellDirty) dgvTasks.CommitEdit(DataGridViewDataErrorContexts.Commit);
                dgvTasks.EndEdit();

                DataGridViewRow row = dgvTasks.Rows[e.RowIndex];

                if (!TryExtractRowData(row, out var action, out var src, out var dest, out var mode, out var addDate))
                {
                    WriteLog($"[{e.RowIndex + 1}行目] 入力エラー: 処理種別と転送元は必須入力です。");
                    return;
                }

                row.Cells["StatusColumn"].Value = "処理中...";
                WriteLog($"[{e.RowIndex + 1}行目] 個別実行を開始: {src}");

                try
                {
                    ProcessResult result = await Task.Run(() => transferEngine.ProcessRow(action, src, dest, mode, addDate, e.RowIndex + 1));
                    row.Cells["StatusColumn"].Value = (result == ProcessResult.Success) ? "完了" : "スキップ";
                    if (result == ProcessResult.Success) WriteLog($"[{e.RowIndex + 1}行目] 個別実行が正常完了");
                }
                catch (Exception ex)
                {
                    row.Cells["StatusColumn"].Value = "エラー";
                    WriteLog($"[{e.RowIndex + 1}行目] エラー: {ex.Message}");
                }
            }
        }

        // --- 実行補助メソッド ---

        // パース処理を排除し、セルのValueから直接Enumを抽出
        private bool TryExtractRowData(DataGridViewRow row, out TransferAction action, out string src, out string dest, out TransferMode mode, out bool addDate)
        {
            action = row.Cells["ActionColumn"].Value is TransferAction a ? a : TransferAction.Unknown;
            mode = row.Cells["ModeColumn"].Value is TransferMode m ? m : TransferMode.Unknown;
            src = row.Cells["SourceColumn"].Value?.ToString() ?? "";
            dest = row.Cells["DestColumn"].Value?.ToString() ?? "";
            addDate = Convert.ToBoolean(row.Cells["DateColumn"].Value ?? false);

            if (action == TransferAction.Unknown || string.IsNullOrWhiteSpace(src)) return false;

            return true;
        }

        private void CancelRemainingTasks(int startIndex)
        {
            for (int j = startIndex; j < dgvTasks.Rows.Count; j++)
            {
                DataGridViewRow nextRow = dgvTasks.Rows[j];
                // Enumを直接評価するため、nullチェックを行う
                if (!nextRow.IsNewRow && nextRow.Cells["ActionColumn"].Value != null)
                {
                    nextRow.Cells["StatusColumn"].Value = "未実行";
                }
            }
        }

        #endregion
    }
}