namespace FileTransfer2
{
    partial class Form1
    {
        /// <summary>
        /// 必要なデザイナー変数です。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 使用中のリソースをすべてクリーンアップします。
        /// </summary>
        /// <param name="disposing">マネージド リソースを破棄する場合は true を指定し、その他の場合は false を指定します。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows フォーム デザイナーで生成されたコード

        /// <summary>
        /// デザイナー サポートに必要なメソッドです。このメソッドの内容を
        /// コード エディターで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            this.btnExecuteAll = new System.Windows.Forms.Button();
            this.dgvTasks = new System.Windows.Forms.DataGridView();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnSaveAs = new System.Windows.Forms.Button();
            this.btnCreateRestorePlan = new System.Windows.Forms.Button();
            this.chkAutoRun = new System.Windows.Forms.CheckBox();
            this.cmbProfiles = new System.Windows.Forms.ComboBox();
            this.pnlUnsaved = new System.Windows.Forms.Panel();
            this.lblUnsaved = new System.Windows.Forms.Label();
            this.btnDiscardAndLoad = new System.Windows.Forms.Button();
            this.btnCancelLoad = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTasks)).BeginInit();
            this.pnlUnsaved.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnExecuteAll
            // 
            this.btnExecuteAll.Location = new System.Drawing.Point(12, 20);
            this.btnExecuteAll.Name = "btnExecuteAll";
            this.btnExecuteAll.Size = new System.Drawing.Size(86, 24);
            this.btnExecuteAll.TabIndex = 0;
            this.btnExecuteAll.Text = "すべて実行";
            this.btnExecuteAll.UseVisualStyleBackColor = true;
            // 
            // dgvTasks
            // 
            this.dgvTasks.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvTasks.Location = new System.Drawing.Point(12, 84);
            this.dgvTasks.Name = "dgvTasks";
            this.dgvTasks.RowTemplate.Height = 21;
            this.dgvTasks.Size = new System.Drawing.Size(1244, 280);
            this.dgvTasks.TabIndex = 1;
            // 
            // txtLog
            // 
            this.txtLog.Location = new System.Drawing.Point(12, 372);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(1244, 150);
            this.txtLog.TabIndex = 2;
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(110, 20);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(86, 24);
            this.btnSave.TabIndex = 3;
            this.btnSave.Text = "上書き保存";
            this.btnSave.UseVisualStyleBackColor = true;
            // 
            // btnSaveAs
            // 
            this.btnSaveAs.Location = new System.Drawing.Point(202, 20);
            this.btnSaveAs.Name = "btnSaveAs";
            this.btnSaveAs.Size = new System.Drawing.Size(110, 24);
            this.btnSaveAs.TabIndex = 4;
            this.btnSaveAs.Text = "名前を付けて保存";
            this.btnSaveAs.UseVisualStyleBackColor = true;
            //
            // btnCreateRestorePlan
            //
            this.btnCreateRestorePlan.Location = new System.Drawing.Point(318, 20);
            this.btnCreateRestorePlan.Name = "btnCreateRestorePlan";
            this.btnCreateRestorePlan.Size = new System.Drawing.Size(100, 24);
            this.btnCreateRestorePlan.TabIndex = 8;
            this.btnCreateRestorePlan.Text = "復元用を作成";
            this.btnCreateRestorePlan.UseVisualStyleBackColor = true;
            //
            // chkAutoRun
            //
            this.chkAutoRun.AutoSize = true;
            this.chkAutoRun.Location = new System.Drawing.Point(440, 24);
            this.chkAutoRun.Name = "chkAutoRun";
            this.chkAutoRun.Size = new System.Drawing.Size(72, 16);
            this.chkAutoRun.TabIndex = 5;
            this.chkAutoRun.Text = "自動処理";
            this.chkAutoRun.UseVisualStyleBackColor = true;
            // 
            // cmbProfiles
            // 
            this.cmbProfiles.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbProfiles.FormattingEnabled = true;
            this.cmbProfiles.Location = new System.Drawing.Point(530, 22);
            this.cmbProfiles.Name = "cmbProfiles";
            this.cmbProfiles.Size = new System.Drawing.Size(200, 20);
            this.cmbProfiles.TabIndex = 6;
            //
            // pnlUnsaved（未保存のままプロファイルを切り替えようとしたときに出す帯。ふだんは隠しておく）
            //
            this.pnlUnsaved.BackColor = System.Drawing.Color.LightYellow;
            this.pnlUnsaved.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlUnsaved.Controls.Add(this.btnCancelLoad);
            this.pnlUnsaved.Controls.Add(this.btnDiscardAndLoad);
            this.pnlUnsaved.Controls.Add(this.lblUnsaved);
            this.pnlUnsaved.Location = new System.Drawing.Point(12, 50);
            this.pnlUnsaved.Name = "pnlUnsaved";
            this.pnlUnsaved.Size = new System.Drawing.Size(1244, 30);
            this.pnlUnsaved.TabIndex = 7;
            this.pnlUnsaved.Visible = false;
            //
            // lblUnsaved
            //
            this.lblUnsaved.AutoSize = true;
            this.lblUnsaved.Location = new System.Drawing.Point(8, 8);
            this.lblUnsaved.Name = "lblUnsaved";
            this.lblUnsaved.Size = new System.Drawing.Size(0, 12);
            this.lblUnsaved.TabIndex = 0;
            //
            // btnDiscardAndLoad
            //
            this.btnDiscardAndLoad.Location = new System.Drawing.Point(1006, 2);
            this.btnDiscardAndLoad.Name = "btnDiscardAndLoad";
            this.btnDiscardAndLoad.Size = new System.Drawing.Size(130, 24);
            this.btnDiscardAndLoad.TabIndex = 1;
            this.btnDiscardAndLoad.Text = "破棄して読み込む";
            this.btnDiscardAndLoad.UseVisualStyleBackColor = true;
            //
            // btnCancelLoad
            //
            this.btnCancelLoad.Location = new System.Drawing.Point(1142, 2);
            this.btnCancelLoad.Name = "btnCancelLoad";
            this.btnCancelLoad.Size = new System.Drawing.Size(90, 24);
            this.btnCancelLoad.TabIndex = 2;
            this.btnCancelLoad.Text = "キャンセル";
            this.btnCancelLoad.UseVisualStyleBackColor = true;
            //
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1282, 537);
            this.Controls.Add(this.pnlUnsaved);
            this.Controls.Add(this.cmbProfiles);
            this.Controls.Add(this.chkAutoRun);
            this.Controls.Add(this.btnCreateRestorePlan);
            this.Controls.Add(this.btnSaveAs);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.dgvTasks);
            this.Controls.Add(this.btnExecuteAll);
            this.Name = "Form1";
            this.Text = "Form1";
            ((System.ComponentModel.ISupportInitialize)(this.dgvTasks)).EndInit();
            this.pnlUnsaved.ResumeLayout(false);
            this.pnlUnsaved.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnExecuteAll;
        private System.Windows.Forms.DataGridView dgvTasks;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnSaveAs;
        private System.Windows.Forms.Button btnCreateRestorePlan;
        private System.Windows.Forms.CheckBox chkAutoRun;
        private System.Windows.Forms.ComboBox cmbProfiles;
        private System.Windows.Forms.Panel pnlUnsaved;
        private System.Windows.Forms.Label lblUnsaved;
        private System.Windows.Forms.Button btnDiscardAndLoad;
        private System.Windows.Forms.Button btnCancelLoad;
    }
}

