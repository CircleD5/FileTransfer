using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileTransfer2
{
    public partial class Form1 : Form
    {
        private readonly AppConfig _config;
        private readonly Messages _msg;   // 画面やログに出す文言（_config.Msg の短縮）
        private readonly FileTransferEngine _engine;
        private Timer _autoRunTimer;

        private bool _isDirty = false;     // 未保存の変更があるか
        private bool _isLoading = false;   // 一覧を読み込み中か（読み込みによる変更は「未保存」にしない）
        private bool _isRunning = false;   // 処理を実行中か（実行中は画面を操作できなくする）
        private string _currentProfile = ""; // 開いているプロファイル名（"" は未選択）
        private bool _isSelectingByCode = false; // プログラムがコンボの選択を変えている最中か（そのときは読み込まない）
        private string _pendingProfile = null;   // 未保存の確認の帯で「破棄して読み込む」を押したときに読み込むプロファイル

        // 処理種別のプルダウンの表示名（messages.json の Actions から作る）
        private Dictionary<TransferAction, string> _actionNames;

        /// <summary>
        /// 画面を作る。文言・一覧の列・イベントを準備し、自動処理・古いログの整理・プロファイルの一覧を用意して、
        /// DefaultProfile が設定されていれば読み込む。
        /// </summary>
        public Form1(AppConfig config)
        {
            InitializeComponent();

            _config = config;
            _msg = config.Msg;
            _engine = new FileTransferEngine(WriteLog, _config, new FileSystem());

            ApplyTexts();
            SetupDataGridView();

            btnExecuteAll.Click += btnExecuteAll_Click;
            btnSave.Click += btnSave_Click;
            btnSaveAs.Click += btnSaveAs_Click;
            btnCreateRestorePlan.Click += btnCreateRestorePlan_Click;
            chkAutoRun.CheckedChanged += chkAutoRun_CheckedChanged;
            cmbProfiles.SelectedIndexChanged += cmbProfiles_SelectedIndexChanged;
            cmbProfiles.MouseWheel += cmbProfiles_MouseWheel;
            btnDiscardAndLoad.Click += btnDiscardAndLoad_Click;
            btnCancelLoad.Click += btnCancelLoad_Click;
            FormClosing += Form1_FormClosing;

            dgvTasks.CellContentClick += dgvTasks_CellContentClick;
            dgvTasks.DefaultValuesNeeded += dgvTasks_DefaultValuesNeeded;
            dgvTasks.CurrentCellDirtyStateChanged += dgvTasks_CurrentCellDirtyStateChanged;
            dgvTasks.CellValueChanged += dgvTasks_CellValueChanged;
            dgvTasks.RowsRemoved += dgvTasks_RowsRemoved;
            dgvTasks.DataError += dgvTasks_DataError;

            InitializeAutoRun();
            DeleteOldLogs();
            RefreshProfileList();
            SetDirty(false);
            LoadDefaultProfile();
        }

        #region 画面の準備

        /// <summary>
        /// ボタンなどの文字を messages.json の文言にする（Designer に書いた文字は、画面を作るときの仮のもの）。
        /// </summary>
        private void ApplyTexts()
        {
            btnExecuteAll.Text = _msg.Ui.BtnExecuteAll;
            btnSave.Text = _msg.Ui.BtnSave;
            btnSaveAs.Text = _msg.Ui.BtnSaveAs;
            btnCreateRestorePlan.Text = _msg.Ui.BtnCreateRestorePlan;
            btnDiscardAndLoad.Text = _msg.Ui.BtnDiscardAndLoad;
            btnCancelLoad.Text = _msg.Ui.BtnCancelLoad;
            chkAutoRun.Text = _msg.Ui.ChkAutoRun;

            _actionNames = TransferTask.Actions.ToDictionary(a => a, a => _msg.Actions[a]);
        }

        /// <summary>
        /// 一覧（DataGridView）の列を作る。列の見出しは messages.json から取る。
        /// </summary>
        private void SetupDataGridView()
        {
            dgvTasks.AutoGenerateColumns = false;
            dgvTasks.Columns.Clear();

            var colAction = new DataGridViewComboBoxColumn { Name = "ActionColumn", HeaderText = _msg.Column.Action, Width = 80 };
            colAction.DataSource = new BindingSource(_actionNames, null);
            colAction.DisplayMember = "Value"; colAction.ValueMember = "Key";
            dgvTasks.Columns.Add(colAction);

            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "SourceColumn", HeaderText = _msg.Column.Source, Width = 250 });
            dgvTasks.Columns.Add(NewButtonColumn("SourceBrowseColumn", _msg.Column.Browse, "…", 40));
            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "DestColumn", HeaderText = _msg.Column.Dest, Width = 250 });
            dgvTasks.Columns.Add(NewButtonColumn("DestBrowseColumn", _msg.Column.Browse, "…", 40));

            var colMode = new DataGridViewComboBoxColumn { Name = "ModeColumn", HeaderText = _msg.Column.Mode, Width = 150 };
            colMode.DisplayMember = "Value"; colMode.ValueMember = "Key";
            dgvTasks.Columns.Add(colMode);

            dgvTasks.Columns.Add(new DataGridViewCheckBoxColumn { Name = "DateColumn", HeaderText = _msg.Column.Date, Width = 70 });
            dgvTasks.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusColumn", HeaderText = _msg.Column.Status, Width = 100, ReadOnly = true });

            dgvTasks.Columns.Add(NewButtonColumn("UpColumn", _msg.Column.Up, "▲", 30));
            dgvTasks.Columns.Add(NewButtonColumn("DownColumn", _msg.Column.Down, "▼", 30));
            dgvTasks.Columns.Add(NewButtonColumn("ExecColumn", _msg.Column.Exec, "▶", 60));
            dgvTasks.Columns.Add(NewButtonColumn("DelColumn", _msg.Column.Delete, "×", 40));
        }

        /// <summary>
        /// ボタンの列を1つ作る（…・▲・▼・▶・× の列）。
        /// </summary>
        private DataGridViewButtonColumn NewButtonColumn(string name, string headerText, string buttonText, int width)
        {
            return new DataGridViewButtonColumn { Name = name, HeaderText = headerText, Text = buttonText, UseColumnTextForButtonValue = true, Width = width };
        }

        #endregion

        #region ログ

        /// <summary>
        /// 予期しない例外をログに出す（画面の処理中に起きた例外を、Program から受け取る）。
        /// </summary>
        public void LogGlobalError(Exception ex)
        {
            WriteLog(_msg.Error.GlobalAlert);
            WriteLog(Messages.Format(_msg.Error.GlobalDetail, ex.Message));
        }

        /// <summary>
        /// 画面のログ欄と、日付ごとのログファイルの両方に書く。別スレッドから呼ばれても大丈夫。
        /// </summary>
        private void WriteLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => WriteLog(message)));
                return;
            }
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            txtLog.AppendText(line + "\r\n");

            try
            {
                string logPath = Path.Combine(_config.GetFullPath(_config.LogDir), $"{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logPath, line + "\r\n", Encoding.UTF8);
            }
            catch
            {
                // ログファイルに書けなくても、本来の処理は止めない（画面のログには出ている）
            }
        }

        /// <summary>
        /// 誰が、どの PC で実行したかをログに残す（リリースの記録として使えるように）。
        /// </summary>
        private void WriteExecutor()
        {
            WriteLog(Messages.Format(_msg.Run.Executor, Environment.UserName, Environment.MachineName));
        }

        /// <summary>
        /// 保存期間を過ぎた日付ごとのログを消す（起動時に1回）。
        /// </summary>
        private void DeleteOldLogs()
        {
            try
            {
                int deleted = LogFiles.DeleteOld(_config.GetFullPath(_config.LogDir), _config.LogRetentionDays, DateTime.Today);
                if (deleted > 0) WriteLog(Messages.Format(_msg.Run.LogsDeleted, _config.LogRetentionDays, deleted));
            }
            catch (Exception ex)
            {
                // 古いログが消せなくても、アプリは使えるので止めない
                WriteLog(Messages.Format(_msg.Run.LogsDeleteFailed, ex.Message));
            }
        }

        #endregion

        #region 一覧（グリッド）の操作

        /// <summary>
        /// 一覧の新規入力用の行に入力し始めたとき、初期値を入れる。
        /// </summary>
        private void dgvTasks_DefaultValuesNeeded(object sender, DataGridViewRowEventArgs e)
        {
            SetDefaultValues(e.Row);
        }

        /// <summary>
        /// 行に初期値（処理種別はコピー、日付付与なし、ステータスは待機中）を入れる。
        /// </summary>
        private void SetDefaultValues(DataGridViewRow row)
        {
            row.Cells["ActionColumn"].Value = TransferAction.Copy;
            row.Cells["DateColumn"].Value = false;
            row.Cells["StatusColumn"].Value = _msg.Status.Wait;
            UpdateRowState(row, TransferAction.Copy);
        }

        /// <summary>
        /// セルを編集している途中に呼ばれる。プルダウンだけは、選んだ時点で値を確定させる。
        /// </summary>
        private void dgvTasks_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // プルダウンは選んだ時点で確定させる（確定しないと CellValueChanged が来ない）
            if (dgvTasks.IsCurrentCellDirty && dgvTasks.CurrentCell is DataGridViewComboBoxCell)
                dgvTasks.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        /// <summary>
        /// セルの値が変わったとき：処理種別なら行の状態（モードの選択肢・入力できるセル）を切り替え、
        /// ステータス以外の変更なら「未保存」にする。
        /// </summary>
        private void dgvTasks_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            string colName = dgvTasks.Columns[e.ColumnIndex].Name;

            // 処理種別が変更された場合は、転送先やモードのUI状態を更新
            if (colName == "ActionColumn" && dgvTasks.Rows[e.RowIndex].Cells["ActionColumn"].Value is TransferAction action)
            {
                UpdateRowState(dgvTasks.Rows[e.RowIndex], action);
            }

            // ステータス列はプログラムが書き換えるだけなので「未保存」にしない
            if (!_isLoading && colName != "StatusColumn")
            {
                SetDirty(true);
            }
        }

        /// <summary>
        /// 行が消されたら「未保存」にする（プロファイルの読み込みで消したときを除く）。
        /// </summary>
        private void dgvTasks_RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e)
        {
            if (!_isLoading) SetDirty(true);
        }

        /// <summary>
        /// 処理種別に合わせて、モードの選択肢・入力できるセル・セルの説明（マウスを置くと出る）を切り替える。
        /// </summary>
        private void UpdateRowState(DataGridViewRow row, TransferAction action)
        {
            var modeCell = (DataGridViewComboBoxCell)row.Cells["ModeColumn"];
            var modeNames = TransferTask.ModesFor(action).ToDictionary(m => m, m => _msg.Modes[m]);
            modeCell.DataSource = new BindingSource(modeNames, null);
            if (!(modeCell.Value is TransferMode mode) || !modeNames.ContainsKey(mode))
                modeCell.Value = modeNames.Keys.First();

            // 使わないセルは入力できないようにして灰色にする（値も消す）
            bool usesDest = action != TransferAction.Delete;
            bool usesDate = action == TransferAction.Copy || action == TransferAction.Move;
            SetCellEnabled(row.Cells["DestColumn"], usesDest, "");
            SetCellEnabled(row.Cells["DateColumn"], usesDate, false);
            SetCellEnabled(modeCell, modeNames.Count > 1, null); // 選択肢が1つしかなければ選ぶ必要がない

            // 処理種別によって転送元・転送先の意味が変わる（例：退避の転送先は「退避先」）ので、説明を出す
            // （処理種別が Unknown のときは説明がないので、空にする）
            if (_msg.SourceHints.ContainsKey(action))
            {
                row.Cells["SourceColumn"].ToolTipText = _msg.SourceHints[action];
                row.Cells["DestColumn"].ToolTipText = _msg.DestHints[action];
            }
            else
            {
                row.Cells["SourceColumn"].ToolTipText = "";
                row.Cells["DestColumn"].ToolTipText = "";
            }
        }

        /// <summary>
        /// セルを入力できる状態・できない状態（灰色）にする。できない状態にするとき、clearValue が null でなければ値をそれにする。
        /// </summary>
        private static void SetCellEnabled(DataGridViewCell cell, bool enabled, object clearValue)
        {
            if (!enabled && clearValue != null) cell.Value = clearValue;
            cell.ReadOnly = !enabled;
            cell.Style.BackColor = enabled ? Color.White : Color.LightGray;

            // プルダウンは、背景色を変えても▼付きのボタンとして描かれ、選べるように見えてしまう。
            // 入力できないときは▼を消して、普通の文字として表示する
            if (cell is DataGridViewComboBoxCell combo)
            {
                combo.DisplayStyle = enabled
                    ? DataGridViewComboBoxDisplayStyle.DropDownButton
                    : DataGridViewComboBoxDisplayStyle.Nothing;
            }
        }

        // 一覧にない値（TSV の不正な値など）は空欄で表示し、実行時に入力エラーとして止める
        private void dgvTasks_DataError(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; }

        /// <summary>
        /// 一覧の中のボタン（…・▲・▼・▶・×）が押されたときの処理。
        /// </summary>
        private async void dgvTasks_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string colName = dgvTasks.Columns[e.ColumnIndex].Name;

            // 参照ボタンは、まだ何も入力していない新しい行でも使えるようにする
            if (colName == "SourceBrowseColumn") { BrowseFolder(e.RowIndex, "SourceColumn"); return; }
            if (colName == "DestBrowseColumn") { BrowseFolder(e.RowIndex, "DestColumn"); return; }

            if (dgvTasks.Rows[e.RowIndex].IsNewRow) return;

            if (colName == "DelColumn")
            {
                dgvTasks.Rows.RemoveAt(e.RowIndex);
                SetDirty(true);
            }
            else if (colName == "UpColumn" || colName == "DownColumn")
            {
                dgvTasks.EndEdit();
                if (colName == "UpColumn") MoveRow(e.RowIndex, -1); // ▲：1つ上へ
                else MoveRow(e.RowIndex, 1);                         // ▼：1つ下へ
                SetDirty(true);
            }
            else if (colName == "ExecColumn")
            {
                await ExecuteRowAsync(dgvTasks.Rows[e.RowIndex]);
            }
        }

        /// <summary>
        /// 「…」ボタン：フォルダを選ぶ画面を開き、選んだフォルダをセルに入れる。
        /// 新規入力用の行なら、行を足してから入れる。削除の行の転送先のように入力できないセルでは何もしない。
        /// </summary>
        private void BrowseFolder(int rowIndex, string targetColumn)
        {
            DataGridViewRow row = dgvTasks.Rows[rowIndex];
            if (row.Cells[targetColumn].ReadOnly) return; // 削除の行の転送先など

            using (var dialog = new FolderBrowserDialog { Description = _msg.Ui.FolderDialogDesc })
            {
                // 今セルに入っているフォルダがあれば、選ぶ画面をそこから開く（空のセルなら何もしない）
                object value = row.Cells[targetColumn].Value;
                if (value != null)
                {
                    string current = value.ToString().Trim('"');
                    if (Directory.Exists(current)) dialog.SelectedPath = current;
                }

                if (dialog.ShowDialog() != DialogResult.OK) return;

                if (row.IsNewRow)
                {
                    row = dgvTasks.Rows[dgvTasks.Rows.Add()];
                    SetDefaultValues(row);
                }
                row.Cells[targetColumn].Value = dialog.SelectedPath;
            }
        }

        /// <summary>
        /// 行を1つ上（direction が -1）または下（+1）へ動かす。端の行や、新規入力用の行とは入れ替えない。
        /// </summary>
        private void MoveRow(int rowIndex, int direction)
        {
            int targetIndex = rowIndex + direction;
            int lastIndex = dgvTasks.Rows.Count - 2; // 一番下は新規入力用の行なので除く
            if (targetIndex < 0 || targetIndex > lastIndex) return;

            DataGridViewRow row = dgvTasks.Rows[rowIndex];
            dgvTasks.Rows.RemoveAt(rowIndex);
            dgvTasks.Rows.Insert(targetIndex, row);
            dgvTasks.ClearSelection();
            dgvTasks.CurrentCell = dgvTasks.Rows[targetIndex].Cells[0];
            dgvTasks.Rows[targetIndex].Selected = true;
        }

        /// <summary>
        /// 編集中のセルの値を確定させる（保存や実行の前に呼ぶ）。
        /// </summary>
        private void CommitGridEdit()
        {
            if (dgvTasks.IsCurrentCellDirty) dgvTasks.CommitEdit(DataGridViewDataErrorContexts.Commit);
            dgvTasks.EndEdit();
        }

        /// <summary>
        /// 一覧の1行を TransferTask にする。
        /// </summary>
        private TransferTask ReadTask(DataGridViewRow row)
        {
            var task = new TransferTask();

            // 一覧にない値（TSV の不正な値など）は Unknown として扱う（実行すると入力エラーになる）
            object action = row.Cells["ActionColumn"].Value;
            if (action is TransferAction) task.Action = (TransferAction)action;
            else task.Action = TransferAction.Unknown;

            object mode = row.Cells["ModeColumn"].Value;
            if (mode is TransferMode) task.Mode = (TransferMode)mode;
            else task.Mode = TransferMode.Unknown;

            // 空のセル（値が null）は "" として扱う
            object source = row.Cells["SourceColumn"].Value;
            if (source != null) task.Source = source.ToString();
            else task.Source = "";

            object dest = row.Cells["DestColumn"].Value;
            if (dest != null) task.Destination = dest.ToString();
            else task.Destination = "";

            // 日付付与のセルが空なら「付けない」
            object addDate = row.Cells["DateColumn"].Value;
            if (addDate != null) task.AddDate = Convert.ToBoolean(addDate);
            else task.AddDate = false;

            return task;
        }

        /// <summary>
        /// 一覧の行のうち、一番下の新規入力用の行を除いたものを返す。
        /// </summary>
        private List<DataGridViewRow> GetTaskRows()
        {
            return dgvTasks.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).ToList();
        }

        /// <summary>
        /// TransferTask の一覧を、画面の一覧に入れ直す。読み込みによる変更は「未保存」にしない。
        /// </summary>
        private void LoadRows(List<TransferTask> tasks)
        {
            _isLoading = true;
            try
            {
                dgvTasks.Rows.Clear();
                foreach (TransferTask task in tasks)
                {
                    DataGridViewRow row = dgvTasks.Rows[dgvTasks.Rows.Add()];
                    row.Cells["ActionColumn"].Value = task.Action;
                    row.Cells["SourceColumn"].Value = task.Source;
                    row.Cells["DestColumn"].Value = task.Destination;
                    UpdateRowState(row, task.Action);
                    row.Cells["ModeColumn"].Value = task.Mode;
                    row.Cells["DateColumn"].Value = task.AddDate;
                    row.Cells["StatusColumn"].Value = _msg.Status.Wait;
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// 行のステータス列に文字を入れる。
        /// </summary>
        private void SetStatus(DataGridViewRow row, string status)
        {
            row.Cells["StatusColumn"].Value = status;
        }

        #endregion

        #region 画面の状態

        /// <summary>
        /// 未保存かどうかを記録し、タイトルに反映する。未保存でなくなったら、確認の帯も消す。
        /// </summary>
        private void SetDirty(bool isDirty)
        {
            _isDirty = isDirty;
            if (!isDirty) HideUnsavedBar(); // 保存や読み込みで失う変更がなくなったら、確認の帯も消す
            string profileName = _currentProfile == "" ? _msg.Ui.ProfileUnselected : _currentProfile;
            string dirtyMark = _isDirty ? _msg.Ui.DirtyMark : "";
            Text = Messages.Format(_msg.Ui.TitleFormat, profileName, dirtyMark);
        }

        /// <summary>
        /// 実行中は、一覧・ボタン・プロファイル切替・自動処理をすべて操作できなくする。
        /// </summary>
        private void SetRunning(bool isRunning)
        {
            _isRunning = isRunning;
            dgvTasks.Enabled = !isRunning;
            btnExecuteAll.Enabled = !isRunning;
            btnSave.Enabled = !isRunning;
            btnSaveAs.Enabled = !isRunning;
            btnCreateRestorePlan.Enabled = !isRunning;
            cmbProfiles.Enabled = !isRunning;
            pnlUnsaved.Enabled = !isRunning;
            chkAutoRun.Enabled = !isRunning;
        }

        /// <summary>
        /// 未保存の変更があれば、破棄してよいか確認する。続けてよければ true。
        /// </summary>
        private bool ConfirmDiscardChanges()
        {
            if (!_isDirty) return true;
            return MessageBox.Show(_msg.Profile.UnsavedWarn, _msg.Profile.UnsavedTitle,
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        /// <summary>
        /// アプリを閉じようとしたとき：実行中なら閉じさせない。未保存の変更があれば確認する。
        /// </summary>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isRunning)
            {
                MessageBox.Show(_msg.Ui.RunningCannotClose, _msg.Ui.RunningTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                e.Cancel = true;
                return;
            }
            if (!ConfirmDiscardChanges()) e.Cancel = true;
        }

        #endregion

        #region プロファイル

        /// <summary>
        /// プロファイル名から、TSV ファイルのパスを作る。
        /// </summary>
        private string GetProfilePath(string profileName)
        {
            return Path.Combine(_config.GetFullPath(_config.ProfilesDir), $"{profileName}.tsv");
        }

        /// <summary>
        /// Profiles フォルダの中身でコンボの一覧を作り直し、今開いているプロファイルを選んだ状態にする。
        /// </summary>
        private void RefreshProfileList()
        {
            // 作り直しの途中でも選択変更のイベントが起きるので、その間は読み込みをしない
            _isSelectingByCode = true;
            try
            {
                cmbProfiles.Items.Clear();
                cmbProfiles.Items.Add(_msg.Ui.ProfileUnselected);
                foreach (string file in Directory.GetFiles(_config.GetFullPath(_config.ProfilesDir), "*.tsv"))
                    cmbProfiles.Items.Add(Path.GetFileNameWithoutExtension(file));
            }
            finally
            {
                _isSelectingByCode = false;
            }
            SelectProfileInCombo(_currentProfile);
        }

        /// <summary>
        /// コンボで指定した名前を選ぶだけ（読み込みはしない）。""（未選択）や一覧にない名前なら「未選択」を選ぶ。
        /// </summary>
        private void SelectProfileInCombo(string profileName)
        {
            _isSelectingByCode = true;
            try { cmbProfiles.SelectedIndex = Math.Max(0, cmbProfiles.Items.IndexOf(profileName)); }
            finally { _isSelectingByCode = false; }
        }

        /// <summary>
        /// コンボでプロファイルを選んだら、すぐ読み込む。
        /// 未保存の変更があるときは、選択を元に戻して一覧の上に確認の帯を出す（変更が勝手に消えないように）。
        /// 確認を別ウィンドウ（メッセージボックス）にしないのは、矢印キーで選び直すたびに何度も答える必要が出ないようにするため。
        /// </summary>
        private void cmbProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isSelectingByCode) return;

            // 一覧の先頭は「未選択」。未選択は "" として扱う
            string selected = "";
            if (cmbProfiles.SelectedIndex > 0) selected = cmbProfiles.SelectedItem.ToString();

            if (_isDirty)
            {
                SelectProfileInCombo(_currentProfile);
                ShowUnsavedBar(selected);
                return;
            }
            LoadProfile(selected);
        }

        /// <summary>
        /// コンボの上でホイールを回しても選択を変えない（一覧をスクロールするつもりで、プロファイルが切り替わる事故を防ぐ）。
        /// </summary>
        private void cmbProfiles_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        }

        /// <summary>
        /// 未保存の確認の帯を出す。「破棄して読み込む」で読み込むプロファイルを覚えておく。
        /// </summary>
        private void ShowUnsavedBar(string profileToLoad)
        {
            _pendingProfile = profileToLoad;
            string name = profileToLoad == "" ? _msg.Ui.ProfileUnselected : profileToLoad;
            lblUnsaved.Text = Messages.Format(_msg.Ui.UnsavedBar, name);
            pnlUnsaved.Visible = true;
        }

        /// <summary>
        /// 未保存の確認の帯を隠す。
        /// </summary>
        private void HideUnsavedBar()
        {
            pnlUnsaved.Visible = false;
            _pendingProfile = null;
        }

        /// <summary>
        /// 確認の帯の「破棄して読み込む」：変更を捨てて、選ぼうとしたプロファイルを読み込む。
        /// </summary>
        private void btnDiscardAndLoad_Click(object sender, EventArgs e)
        {
            string profileToLoad = _pendingProfile;
            HideUnsavedBar();
            if (profileToLoad != null) LoadProfile(profileToLoad);
        }

        /// <summary>
        /// 確認の帯の「キャンセル」：帯を消すだけ（一覧も変更もそのまま）。
        /// </summary>
        private void btnCancelLoad_Click(object sender, EventArgs e)
        {
            HideUnsavedBar();
        }

        /// <summary>
        /// 起動時に、settings.config の DefaultProfile に書かれたプロファイルを読み込む（読み込むだけで、実行はしない）。
        /// 見つからなくてもアプリは使えるので、起動は止めずにログで知らせる。
        /// </summary>
        private void LoadDefaultProfile()
        {
            string name = _config.DefaultProfile;
            if (name == "") return;

            if (!cmbProfiles.Items.Contains(name))
            {
                WriteLog(Messages.Format(_msg.Profile.DefaultNotFound, name));
                return;
            }
            LoadProfile(name);
        }

        /// <summary>
        /// プロファイルを一覧に読み込む。""（未選択）なら一覧を空にする（新しく作り始めるとき用）。
        /// </summary>
        private void LoadProfile(string profileName)
        {
            try
            {
                if (profileName == "")
                {
                    // 「未選択」は一覧を空にする（新しく作り始めるとき用）
                    LoadRows(new List<TransferTask>());
                    WriteLog(_msg.Profile.Cleared);
                }
                else
                {
                    LoadRows(TaskFile.Load(GetProfilePath(profileName)));
                    WriteLog(Messages.Format(_msg.Profile.Loaded, profileName));
                }
                _currentProfile = profileName;
                SetDirty(false);
            }
            catch (Exception ex)
            {
                WriteLog(Messages.Format(_msg.Profile.LoadFailed, ex.Message));
            }
            // 読み込めなかったときも、コンボは今開いているプロファイルに合わせる
            SelectProfileInCombo(_currentProfile);
        }

        /// <summary>
        /// 「上書き保存」：確認してから、今開いているプロファイルに保存する。未選択なら「名前を付けて保存」と同じ。
        /// </summary>
        private void btnSave_Click(object sender, EventArgs e)
        {
            // まだプロファイルを開いていなければ「名前を付けて保存」と同じ動きにする
            if (_currentProfile == "")
            {
                SaveAs();
                return;
            }

            if (MessageBox.Show(Messages.Format(_msg.Profile.OverwriteConfirm, _currentProfile), _msg.Profile.OverwriteTitle,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            if (SaveProfile(GetProfilePath(_currentProfile))) SetDirty(false);
        }

        /// <summary>
        /// 「名前を付けて保存」ボタン。
        /// </summary>
        private void btnSaveAs_Click(object sender, EventArgs e)
        {
            SaveAs();
        }

        /// <summary>
        /// 保存ダイアログで名前を決めて保存し、コンボをそのプロファイルに合わせる。
        /// </summary>
        private void SaveAs()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = _msg.Ui.SaveDialogFilter,
                Title = _msg.Ui.SaveDialogTitle,
                InitialDirectory = _config.GetFullPath(_config.ProfilesDir),
                DefaultExt = "tsv",
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (!SaveProfile(dialog.FileName)) return;

                // Profiles フォルダの外に保存した場合は、コンボには出ないので「未選択」扱いになる
                string savedName = Path.GetFileNameWithoutExtension(dialog.FileName);
                if (cmbProfiles.Items.Contains(savedName)) _currentProfile = savedName;
                else _currentProfile = "";
                SelectProfileInCombo(_currentProfile);
                SetDirty(false);
            }
        }

        /// <summary>
        /// 一覧の内容をファイルに保存する。保存できたら true。
        /// </summary>
        private bool SaveProfile(string filePath)
        {
            CommitGridEdit();
            try
            {
                TaskFile.Save(filePath, GetTaskRows().Select(ReadTask));
                WriteLog(Messages.Format(_msg.Profile.SaveSuccess, Path.GetFileName(filePath)));
            }
            catch (Exception ex)
            {
                ShowSaveError(Messages.Format(_msg.Profile.SaveError, ex.Message));
                return false;
            }

            // 復元用の計画は自動では作り直さないので、古いままになっているかもしれないことを知らせる
            string restorePath = RestorePlan.PathFor(filePath, _msg);
            if (File.Exists(restorePath))
                WriteLog(Messages.Format(_msg.Profile.RestorePlanMayBeStale, Path.GetFileName(restorePath)));

            RefreshProfileList();
            return true;
        }

        /// <summary>
        /// 保存の失敗を、ログとメッセージボックスで知らせる。
        /// </summary>
        private void ShowSaveError(string message)
        {
            WriteLog(message);
            MessageBox.Show(message, _msg.Profile.SaveErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// 「復元用を作成」：今開いているプロファイル（保存済みの内容）から、元に戻すための計画（〇〇_復元.tsv）を作る。
        /// 退避・配置だけでできたプロファイルからだけ作れる。
        /// </summary>
        private void btnCreateRestorePlan_Click(object sender, EventArgs e)
        {
            CommitGridEdit();
            // 画面の内容と保存した内容が違うと、復元用がどちらに対応するのか分からなくなるので、保存済みのときだけ作る
            if (_currentProfile == "" || _isDirty)
            {
                ShowRestorePlanResult(_msg.Profile.RestorePlanNeedSave, MessageBoxIcon.Warning);
                return;
            }

            string profilePath = GetProfilePath(_currentProfile);
            string restorePath = RestorePlan.PathFor(profilePath, _msg);
            try
            {
                List<TransferTask> restoreTasks = RestorePlan.Create(TaskFile.Load(profilePath), _config);
                if (restoreTasks == null)
                {
                    ShowRestorePlanResult(_msg.Profile.RestorePlanNotCreated, MessageBoxIcon.Warning);
                    return;
                }
                TaskFile.Save(restorePath, restoreTasks);
            }
            catch (Exception ex)
            {
                ShowRestorePlanResult(Messages.Format(_msg.Profile.RestorePlanSaveError, ex.Message), MessageBoxIcon.Error);
                return;
            }

            RefreshProfileList();
            ShowRestorePlanResult(Messages.Format(_msg.Profile.RestorePlanSaved, Path.GetFileName(restorePath)), MessageBoxIcon.Information);
        }

        /// <summary>
        /// 「復元用を作成」の結果を、ログとメッセージボックスで知らせる。
        /// </summary>
        private void ShowRestorePlanResult(string message, MessageBoxIcon icon)
        {
            WriteLog(message);
            MessageBox.Show(message, _msg.Ui.RestorePlanTitle, MessageBoxButtons.OK, icon);
        }

        #endregion

        #region 実行

        /// <summary>
        /// 「すべて実行」：一覧の行を上から順に実行する。実行中は画面を操作できなくする。
        /// </summary>
        private async void btnExecuteAll_Click(object sender, EventArgs e)
        {
            CommitGridEdit();
            List<DataGridViewRow> rows = GetTaskRows();
            foreach (DataGridViewRow row in rows) SetStatus(row, _msg.Status.Wait);

            SetRunning(true);
            try { await RunPlanAsync(rows.Select(ReadTask).ToList(), 1, (index, status) => SetStatus(rows[index], status)); }
            finally { SetRunning(false); }
        }

        /// <summary>
        /// 一覧の1行だけを実行する（行の「個別実行」ボタン）。
        /// </summary>
        private async Task ExecuteRowAsync(DataGridViewRow row)
        {
            CommitGridEdit();
            SetRunning(true);
            try { await RunPlanAsync(new[] { ReadTask(row) }, row.Index + 1, (index, status) => SetStatus(row, status)); }
            finally { SetRunning(false); }
        }

        /// <summary>
        /// 処理を上から順に実行する。入力エラーや失敗があれば、そこで止める。全部成功したら true。
        /// 一覧からの実行・個別実行・自動処理で共通。
        /// </summary>
        /// <param name="firstLineNo">1件目の行番号（ログの「n行目」に使う）</param>
        /// <param name="setStatus">各行の状態（処理中・完了など）を受け取る処理。画面に出さないなら null。</param>
        private async Task<bool> RunPlanAsync(IList<TransferTask> tasks, int firstLineNo, Action<int, string> setStatus)
        {
            WriteLog(_msg.Run.RunStart);
            WriteExecutor();

            // setStatus が null のとき（自動処理）は、行の状態を画面に出さない
            bool showStatus = setStatus != null;

            bool succeeded = true;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (showStatus) setStatus(i, _msg.Status.Proc);

                succeeded = await RunTaskAsync(tasks[i], firstLineNo + i);

                if (showStatus)
                {
                    if (succeeded) setStatus(i, _msg.Status.Done);
                    else setStatus(i, _msg.Status.Err);
                }

                if (!succeeded)
                {
                    // 失敗したら、残りの行は「未実行」にして止める
                    if (showStatus)
                    {
                        for (int j = i + 1; j < tasks.Count; j++) setStatus(j, _msg.Status.Unexec);
                    }
                    WriteLog(_msg.Run.RowAbort);
                    break;
                }
                if (_config.RowDelayMs > 0 && i < tasks.Count - 1) await Task.Delay(_config.RowDelayMs);
            }

            WriteLog(_msg.Run.RunEnd);
            return succeeded;
        }

        /// <summary>
        /// 処理1件を実行する。成功したら true。失敗の内容はログに出す。
        /// </summary>
        private async Task<bool> RunTaskAsync(TransferTask task, int lineNo)
        {
            string inputError = task.Validate(_config);
            if (inputError != null)
            {
                WriteLog(Messages.Format(_msg.Run.InputError, lineNo, inputError));
                return false;
            }

            string actionName = _actionNames[task.Action];
            if (task.Action == TransferAction.Delete)
            {
                // 削除は転送先を使わないので、転送元だけ出す
                WriteLog(Messages.Format(_msg.Run.RowStart, lineNo, actionName, task.Source));
            }
            else
            {
                // それ以外は「転送元 → 転送先」の形で出す（復元などで、どこへ戻すのかが分かるように）
                WriteLog(Messages.Format(_msg.Run.RowStartToDest, lineNo, actionName, task.Source, task.Destination));
            }
            try
            {
                // ファイル操作は時間がかかることがあるので、画面が固まらないよう別スレッドで行う
                await Task.Run(() => _engine.ProcessRow(task.Action, task.Source, task.Destination, task.Mode, task.AddDate, lineNo));
                WriteLog(Messages.Format(_msg.Run.RowSuccess, lineNo));
                return true;
            }
            catch (Exception ex)
            {
                WriteLog(Messages.Format(_msg.Run.RowFailed, lineNo, ex.Message));
                return false;
            }
        }

        #endregion

        #region 自動処理（AutoRun）

        /// <summary>
        /// 自動処理のタイマーを準備する（「自動処理」にチェックが入るまでは動かさない）。
        /// </summary>
        private void InitializeAutoRun()
        {
            _autoRunTimer = new Timer { Interval = _config.AutoRunIntervalMs };
            _autoRunTimer.Tick += AutoRunTimer_Tick;
            WriteLog(_msg.AutoRun.Ready);
        }

        /// <summary>
        /// 「自動処理」のチェックに合わせて、タイマーを動かす・止める。
        /// </summary>
        private void chkAutoRun_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoRun.Checked)
            {
                _autoRunTimer.Start();
                WriteLog(Messages.Format(_msg.AutoRun.Start, _config.AutoRunPlanDir, _config.AutoRunIntervalMs));
            }
            else
            {
                _autoRunTimer.Stop();
                WriteLog(_msg.AutoRun.Stop);
            }
        }

        /// <summary>
        /// Plan フォルダの計画ファイル（TSV）を1つずつ実行する。
        /// 一覧（グリッド）は使わないので、画面で編集中の内容には影響しない。
        /// </summary>
        private async void AutoRunTimer_Tick(object sender, EventArgs e)
        {
            if (_isRunning) return; // 手動で実行中なら、次の機会に回す

            _autoRunTimer.Stop();
            try
            {
                foreach (string planPath in Directory.GetFiles(_config.GetFullPath(_config.AutoRunPlanDir), "*.tsv"))
                {
                    if (!chkAutoRun.Checked) break;

                    if (IsFileLocked(planPath))
                    {
                        WriteLog(Messages.Format(_msg.AutoRun.SkipLock, Path.GetFileName(planPath)));
                        continue;
                    }

                    WriteLog(Messages.Format(_msg.AutoRun.Detect, Path.GetFileName(planPath)));
                    bool succeeded;
                    SetRunning(true);
                    try { succeeded = await RunPlanFileAsync(planPath); }
                    finally { SetRunning(false); }

                    MovePlanFile(planPath, succeeded);
                }
            }
            catch (Exception ex)
            {
                WriteLog(Messages.Format(_msg.AutoRun.Error, ex.Message));
            }
            finally
            {
                if (chkAutoRun.Checked) _autoRunTimer.Start();
            }
        }

        /// <summary>
        /// 自動処理：計画ファイル1つを読み込んで実行する。読み込めなかったとき・失敗したときは false。
        /// </summary>
        private async Task<bool> RunPlanFileAsync(string planPath)
        {
            List<TransferTask> tasks;
            try
            {
                tasks = TaskFile.Load(planPath);
            }
            catch (Exception ex)
            {
                WriteLog(Messages.Format(_msg.AutoRun.Error, ex.Message));
                return false;
            }
            // 自動処理は一覧を使わないので、行の状態は画面に出さない
            return await RunPlanAsync(tasks, 1, null);
        }

        /// <summary>
        /// 実行し終えた計画ファイルを、成功なら Done、失敗なら Error フォルダへ移す。
        /// </summary>
        private void MovePlanFile(string planPath, bool succeeded)
        {
            string fileName = Path.GetFileName(planPath);

            if (succeeded && TryMoveFile(planPath, _config.AutoRunDoneDir))
            {
                WriteLog(Messages.Format(_msg.AutoRun.Done, fileName));
                return;
            }
            if (TryMoveFile(planPath, _config.AutoRunErrorDir))
            {
                WriteLog(Messages.Format(_msg.AutoRun.Failed, fileName));
                return;
            }

            // Plan フォルダに残ったままだと同じ計画を何度も実行してしまうため、自動処理を止める
            WriteLog(Messages.Format(_msg.AutoRun.Stopped, fileName));
            chkAutoRun.Checked = false;
        }

        /// <summary>
        /// ファイルを指定フォルダへ移す。同じ名前があれば日時（さらに同じなら連番）を付ける。成功したら true。
        /// </summary>
        private bool TryMoveFile(string filePath, string destDirSetting)
        {
            try
            {
                string destDir = _config.GetFullPath(destDirSetting);
                string name = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);
                string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string destPath = Path.Combine(destDir, name + ext);
                for (int counter = 0; File.Exists(destPath); counter++)
                {
                    // plan.tsv → plan_日時.tsv → plan_日時_1.tsv → plan_日時_2.tsv …
                    if (counter == 0) destPath = Path.Combine(destDir, $"{name}_{stamp}{ext}");
                    else destPath = Path.Combine(destDir, $"{name}_{stamp}_{counter}{ext}");
                }
                File.Move(filePath, destPath);
                return true;
            }
            catch (Exception ex)
            {
                WriteLog(Messages.Format(_msg.AutoRun.Error, ex.Message));
                return false;
            }
        }

        /// <summary>
        /// ほかのプログラムがファイルを開いている（まだ書き込み中の）かどうか。開けなければ書き込み中とみなす。
        /// </summary>
        private bool IsFileLocked(string filePath)
        {
            try { using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None)) { return false; } }
            catch (IOException) { return true; }
        }

        #endregion
    }
}
