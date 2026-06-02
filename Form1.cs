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

        private bool _isDirty = false;
        private bool _isLoading = false;
        private string _currentProfile = "未選択";

        private static readonly Dictionary<TransferAction, string> ActionBindSource = new Dictionary<TransferAction, string>
        {
            { TransferAction.Copy, "コピー" },
            { TransferAction.Move, "移動" },
            { TransferAction.Delete, "削除" }
        };

        private static readonly Dictionary<TransferMode, string> TransferModeBindSource = new Dictionary<TransferMode, string>
        {
            { TransferMode.FolderAsIs, "フォルダごと転送" },
            { TransferMode.ExtractContents, "親フォルダを含めずに転送" }
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
            { TransferMode.ExtractContents, "親フォルダを含めずに転送" },
            { TransferMode.DeleteAll, "丸ごと削除" },
            { TransferMode.DeleteKeepParent, "親フォルダのみ残す" },
            { TransferMode.DeleteFilesOnly, "ファイルのみ削除" }
        };

        private static readonly Dictionary<string, TransferMode> ModeParseDict = new Dictionary<string, TransferMode>
        {
            { "フォルダごと転送", TransferMode.FolderAsIs },
            { "親フォルダを含めずに転送", TransferMode.ExtractContents },
            { "丸ごと削除", TransferMode.DeleteAll },
            { "親フォルダのみ残す", TransferMode.DeleteKeepParent },
            { "ファイルのみ削除", TransferMode.DeleteFilesOnly },
            { "中身を展開して転送", TransferMode.ExtractContents }, // 後方互換
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

            chkAutoRun.CheckedChanged += chkAutoRun_CheckedChanged;
            cmbProfiles.SelectedIndexChanged += cmbProfiles_SelectedIndexChanged;

            dgvTasks.CellContentClick += dgvTasks_CellContentClick;
            dgvTasks.DefaultValuesNeeded += dgvTasks_DefaultValuesNeeded;
            dgvTasks.CurrentCellDirtyStateChanged += dgvTasks_CurrentCellDirtyStateChanged;
            dgvTasks.CellValueChanged += dgvTasks_CellValueChanged;
            dgvTasks.RowsRemoved += dgvTasks_RowsRemoved;
            dgvTasks.DataError += dgvTasks_DataError;

            InitializeAutoRun();
            InitializeProfiles();
            SetDirty(false);
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

        private void SetDirty(bool isDirty)
        {
            _isDirty = isDirty;
            string dirtyMark = _isDirty ? " (*未保存)" : "";
            this.Text = $"ファイル転送ツール - [{_currentProfile}]{dirtyMark}";
        }

        #endregion

        #region 2. UI連動 (DataGridView制御)

        private void SetupDataGridView()
        {
            dgvTasks.AutoGenerateColumns = false;
            dgvTasks.Columns.Clear();

            var colAction = new DataGridViewComboBoxColumn { Name = "ActionColumn", HeaderText = "処理種別", Width = 80 };
            colAction.DataSource = new BindingSource(ActionBindSource, null);
            colAction.DisplayMember = "Value";
            colAction.ValueMember = "Key";
            dgvTasks.Columns.Add(colAction);

            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceColumn", HeaderText = "転送元", Width = 200 });
            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "DestColumn", HeaderText = "転送先", Width = 200 });

            var colMode = new DataGridViewComboBoxColumn { Name = "ModeColumn", HeaderText = "モード", Width = 150 };
            colMode.DisplayMember = "Value";
            colMode.ValueMember = "Key";
            dgvTasks.Columns.Add(colMode);

            dgvTasks.Columns.Add(new DataGridViewCheckBoxColumn { Name = "DateColumn", HeaderText = "日付付与", Width = 70 });
            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusColumn", HeaderText = "ステータス", Width = 100, ReadOnly = true });

            // 行操作ボタン列
            dgvTasks.Columns.Add(new DataGridViewButtonColumn { Name = "UpColumn", HeaderText = "上", Text = "▲", UseColumnTextForButtonValue = true, Width = 30 });
            dgvTasks.Columns.Add(new DataGridViewButtonColumn { Name = "DownColumn", HeaderText = "下", Text = "▼", UseColumnTextForButtonValue = true, Width = 30 });
            dgvTasks.Columns.Add(new DataGridViewButtonColumn { Name = "ExecColumn", HeaderText = "個別実行", Text = "▶", UseColumnTextForButtonValue = true, Width = 50 });
            dgvTasks.Columns.Add(new DataGridViewButtonColumn { Name = "DelColumn", HeaderText = "削除", Text = "×", UseColumnTextForButtonValue = true, Width = 40 });
        }

        private void dgvTasks_DefaultValuesNeeded(object sender, DataGridViewRowEventArgs e)
        {
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

            if (!_isLoading) SetDirty(true);
        }

        private void dgvTasks_RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e)
        {
            if (!_isLoading) SetDirty(true);
        }

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
            if (cmbProfiles.SelectedIndex <= 0)
            {
                _currentProfile = "未選択";
                SetDirty(false);
                return;
            }

            if (_isDirty)
            {
                var result = MessageBox.Show("変更が保存されていません。破棄して別のプロファイルを読み込みますか？", "未保存の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.No)
                {
                    SetComboSelectionWithoutEvent(_currentProfile);
                    return;
                }
            }

            string profileName = cmbProfiles.SelectedItem.ToString();
            string filePath = Path.Combine(ProfilesDir, $"{profileName}.tsv");

            if (File.Exists(filePath))
            {
                try
                {
                    LoadTsv(filePath);
                    WriteLog($"設定パターン [{profileName}] に切り替えました。");
                    _currentProfile = profileName;
                    SetDirty(false);
                }
                catch (Exception ex) { WriteLog($"切替失敗: {ex.Message}"); }
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (cmbProfiles.SelectedIndex > 0)
            {
                string profileName = cmbProfiles.SelectedItem.ToString();
                var result = MessageBox.Show($"プロファイル '{profileName}' を上書き保存しますか？", "上書き保存の確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    string filePath = Path.Combine(ProfilesDir, $"{profileName}.tsv");
                    SaveTsvLogic(filePath);
                    _currentProfile = profileName;
                    SetDirty(false);
                }
            }
            else
            {
                using (SaveFileDialog sfd = new SaveFileDialog { Filter = "TSVファイル (*.tsv)|*.tsv", Title = "名前を付けて保存", InitialDirectory = ProfilesDir, DefaultExt = "tsv" })
                {
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        SaveTsvLogic(sfd.FileName);
                        RefreshProfileList();

                        string savedName = Path.GetFileNameWithoutExtension(sfd.FileName);
                        SetComboSelectionWithoutEvent(savedName);
                        _currentProfile = savedName;
                        SetDirty(false);
                    }
                }
            }
        }

        private void SaveTsvLogic(string filePath)
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    foreach (DataGridViewRow row in dgvTasks.Rows)
                    {
                        if (row.IsNewRow) continue;

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
                WriteLog($"設定を保存しました: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                WriteLog($"保存エラー: {ex.Message}");
                MessageBox.Show($"保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _isLoading = true;
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

                    UpdateRowState(row, loadedAction);

                    TransferMode loadedMode = ModeParseDict.ContainsKey(parts[3]) ? ModeParseDict[parts[3]] : TransferMode.FolderAsIs;
                    row.Cells["ModeColumn"].Value = loadedMode;

                    row.Cells["DateColumn"].Value = bool.TryParse(parts[4], out bool isDate) && isDate;
                    row.Cells["StatusColumn"].Value = "待機中";
                }
            }
            _isLoading = false;
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
                    _currentProfile = "自動実行中";
                    SetDirty(false);

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

        #region 5. 実行処理・行操作

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

                    if (result == ProcessResult.Success)
                    {
                        row.Cells["StatusColumn"].Value = "完了";
                        WriteLog($"[{i + 1}行目] 正常完了");
                    }
                    else if (result == ProcessResult.CompletedWithWarnings)
                    {
                        row.Cells["StatusColumn"].Value = "完了(一部ｽｷｯﾌﾟ)";
                        WriteLog($"[{i + 1}行目] 完了しましたが、一部のファイルがスキップされました。");
                    }
                    else
                    {
                        row.Cells["StatusColumn"].Value = "スキップ";
                    }
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
                SetDirty(true);
                return;
            }

            if (colName == "UpColumn" || colName == "DownColumn")
            {
                dgvTasks.EndEdit();
                int direction = (colName == "UpColumn") ? -1 : 1;
                MoveRow(e.RowIndex, direction);
                SetDirty(true);
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

                    if (result == ProcessResult.Success)
                    {
                        row.Cells["StatusColumn"].Value = "完了";
                        WriteLog($"[{e.RowIndex + 1}行目] 個別実行が正常完了");
                    }
                    else if (result == ProcessResult.CompletedWithWarnings)
                    {
                        row.Cells["StatusColumn"].Value = "完了(一部ｽｷｯﾌﾟ)";
                        WriteLog($"[{e.RowIndex + 1}行目] 完了しましたが、一部のファイルがスキップされました。");
                    }
                    else
                    {
                        row.Cells["StatusColumn"].Value = "スキップ";
                    }
                }
                catch (Exception ex)
                {
                    row.Cells["StatusColumn"].Value = "エラー";
                    WriteLog($"[{e.RowIndex + 1}行目] エラー: {ex.Message}");
                }
            }
        }

        private void MoveRow(int rowIndex, int direction)
        {
            if (rowIndex < 0 || rowIndex >= dgvTasks.Rows.Count - 1) return;

            int targetIndex = rowIndex + direction;
            if (targetIndex < 0 || targetIndex >= dgvTasks.Rows.Count - 1) return;

            DataGridViewRow row = dgvTasks.Rows[rowIndex];
            dgvTasks.Rows.RemoveAt(rowIndex);
            dgvTasks.Rows.Insert(targetIndex, row);

            dgvTasks.ClearSelection();
            dgvTasks.CurrentCell = dgvTasks.Rows[targetIndex].Cells[0];
            dgvTasks.Rows[targetIndex].Selected = true;
        }

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
                if (!nextRow.IsNewRow && nextRow.Cells["ActionColumn"].Value != null)
                {
                    nextRow.Cells["StatusColumn"].Value = "未実行";
                }
            }
        }

        #endregion
    }
}