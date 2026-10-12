using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FileTransfer2
{
    /// <summary>
    /// 画面やログに出す文言（messages.json の中身）。プロパティ名が messages.json のキーに対応する。
    ///
    /// [JsonObject(ItemRequired = Required.Always)] を付けたクラスは、messages.json に
    /// 1つでも項目が欠けていると読み込み時にエラーになる（起動時に抜けを見つけられる）。
    /// また、プログラムからは _msg.Status.Done のようにプロパティで参照するので、
    /// キー名の打ち間違いはビルド時にエラーになる。
    /// </summary>
    [JsonObject(ItemRequired = Required.Always)]
    public class Messages
    {
        public UiMessages Ui { get; set; }
        public ColumnMessages Column { get; set; }
        public Dictionary<TransferAction, string> Actions { get; set; } // 処理種別の表示名
        public Dictionary<TransferMode, string> Modes { get; set; }     // モードの表示名
        public Dictionary<TransferAction, string> SourceHints { get; set; } // 転送元のセルに出す説明（処理種別ごと）
        public Dictionary<TransferAction, string> DestHints { get; set; }   // 転送先のセルに出す説明（処理種別ごと）
        public StatusMessages Status { get; set; }
        public ProfileMessages Profile { get; set; }
        public AutoRunMessages AutoRun { get; set; }
        public RunMessages Run { get; set; }
        public InputMessages Input { get; set; }
        public TransferMessages Transfer { get; set; }
        public ErrorMessages Error { get; set; }
        public ConfigMessages Config { get; set; } // 設定ファイルの誤りを知らせる文言

        /// <summary>
        /// 文言の {0} {1} に値を埋め込む。
        /// 文言を書き換えたときに {0} の数を間違えても落ちないよう、失敗したら文言をそのまま返す。
        /// </summary>
        public static string Format(string template, params object[] args)
        {
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class UiMessages
    {
        public string TitleFormat { get; set; }
        public string DirtyMark { get; set; }
        public string ProfileUnselected { get; set; }
        public string BtnExecuteAll { get; set; }
        public string BtnStop { get; set; }
        public string ProgressFormat { get; set; }
        public string BtnSave { get; set; }
        public string BtnSaveAs { get; set; }
        public string BtnCreateRestorePlan { get; set; }
        public string RestorePlanTitle { get; set; }
        public string UnsavedBar { get; set; }
        public string BtnDiscardAndLoad { get; set; }
        public string BtnCancelLoad { get; set; }
        public string ChkAutoRun { get; set; }
        public string SaveDialogTitle { get; set; }
        public string SaveDialogFilter { get; set; }
        public string FolderDialogDesc { get; set; }
        public string RunningCannotClose { get; set; }
        public string RunningTitle { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class ColumnMessages
    {
        public string Action { get; set; }
        public string Source { get; set; }
        public string Dest { get; set; }
        public string Browse { get; set; }
        public string Mode { get; set; }
        public string Date { get; set; }
        public string Status { get; set; }
        public string Up { get; set; }
        public string Down { get; set; }
        public string Exec { get; set; }
        public string Delete { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class StatusMessages
    {
        public string Wait { get; set; }
        public string Proc { get; set; }
        public string Done { get; set; }
        public string Err { get; set; }
        public string Unexec { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class ProfileMessages
    {
        public string UnsavedWarn { get; set; }
        public string UnsavedTitle { get; set; }
        public string Loaded { get; set; }
        public string Cleared { get; set; }
        public string LoadFailed { get; set; }
        public string OverwriteConfirm { get; set; }
        public string OverwriteTitle { get; set; }
        public string SaveSuccess { get; set; }
        public string SaveError { get; set; }
        public string SaveErrorTitle { get; set; }
        public string RestorePlanSuffix { get; set; }
        public string RestorePlanSaved { get; set; }
        public string RestorePlanNotCreated { get; set; }
        public string RestorePlanNeedSave { get; set; }
        public string RestorePlanMayBeStale { get; set; }
        public string RestorePlanSaveError { get; set; }
        public string DefaultNotFound { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class AutoRunMessages
    {
        public string Ready { get; set; }
        public string Start { get; set; }
        public string Stop { get; set; }
        public string SkipLock { get; set; }
        public string Detect { get; set; }
        public string Done { get; set; }
        public string Failed { get; set; }
        public string Stopped { get; set; }
        public string Error { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class RunMessages
    {
        public string RunStart { get; set; }
        public string RunEnd { get; set; }
        public string RowStart { get; set; }
        public string RowStartToDest { get; set; }
        public string RowSuccess { get; set; }
        public string RowFailed { get; set; }
        public string RowAbort { get; set; }
        public string StopRequested { get; set; }
        public string Stopped { get; set; }
        public string InputError { get; set; }
        public string Executor { get; set; }
        public string LogsDeleted { get; set; }
        public string LogsDeleteFailed { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class InputMessages
    {
        public string ActionInvalid { get; set; }
        public string SourceEmpty { get; set; }
        public string DestEmpty { get; set; }
        public string ModeInvalid { get; set; }
        public string DateNotAllowed { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class TransferMessages
    {
        public string Overwrite { get; set; }
        public string FolderDate { get; set; }
        public string PathEmpty { get; set; }
        public string SourceNotFound { get; set; }
        public string SamePath { get; set; }
        public string Conflict { get; set; }
        public string Avoided { get; set; }
        public string SourceMustBeFolder { get; set; }
        public string DeployNoReleaseId { get; set; }
        public string DeployAlreadyDone { get; set; }
        public string DeployBackedUp { get; set; }
        public string DeployKept { get; set; }
        public string CannotModify { get; set; }
        public string BackupSaved { get; set; }
        public string BackupExists { get; set; }
        public string RestoreNoBackup { get; set; }
        public string PathTooLong { get; set; }
        public string SnapshotNameConflict { get; set; }
        public string RestoreRolledBack { get; set; }
        public string RestoreDone { get; set; }
        public string RestoreAlreadyDone { get; set; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    public class ErrorMessages
    {
        public string GlobalAlert { get; set; }
        public string GlobalDetail { get; set; }
        public string Fatal { get; set; }
        public string FatalTitle { get; set; }
    }

    /// <summary>
    /// 設定ファイルの誤りを知らせる文言。
    /// ※messages.json 自体が読めないときは、この文言も使えないので、.NET や Newtonsoft.Json のエラー文をそのまま出す。
    /// </summary>
    [JsonObject(ItemRequired = Required.Always)]
    public class ConfigMessages
    {
        public string Title { get; set; }
        public string FileNotFound { get; set; }
        public string SettingsInvalid { get; set; }
        public string SectionMissing { get; set; }
        public string PathListInvalid { get; set; }
        public string MessagesMissingKeys { get; set; }
        public string FolderCreateFailed { get; set; }
    }
}
