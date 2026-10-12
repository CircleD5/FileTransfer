using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileTransfer2
{
    /// <summary>
    /// リリース用の計画（退避・配置だけでできたもの）から、元に戻すための計画（復元）を作る。
    ///
    ///   リリース用                                      復元用（逆の順番）
    ///   1. 退避 D:\BizAppData → D:\ReleaseBackup\v1.2.0     2. 復元 BackupDir\v1.2.0 → D:\App
    ///   2. 配置 Release\v1.2.0\App → D:\App                 1. 復元 D:\ReleaseBackup\v1.2.0 → D:\BizAppData
    ///
    /// コピー・移動・削除は元に戻せないので、1行でも混ざっていたら作らない
    /// （中途半端な復元用の計画があると、「完全に戻せる」と誤解されるため）。
    /// </summary>
    public static class RestorePlan
    {
        /// <summary>
        /// 復元用の計画を作る。作れない（退避・配置以外の行や入力エラーがある、行がない）ときは null。
        /// </summary>
        public static List<TransferTask> Create(IList<TransferTask> tasks, AppConfig config)
        {
            if (tasks.Count == 0) return null;

            var restoreTasks = new List<TransferTask>();
            // 後の行から順に戻す（同じフォルダを複数の行で扱っていても、正しい順番で元に戻るように）
            foreach (TransferTask task in Enumerable.Reverse(tasks))
            {
                bool canRestore = (task.Action == TransferAction.Backup || task.Action == TransferAction.Deploy) && task.Validate(config) == null;
                if (!canRestore) return null;

                if (task.Action == TransferAction.Backup)
                {
                    // 退避：転送先（退避先）から、転送元へ戻す
                    restoreTasks.Add(NewRestore(task.Destination, task.Source));
                }
                else
                {
                    // 配置：BackupDir\リリース番号 から、配置先へ戻す
                    string releaseId = FileTransferEngine.ReleaseIdOf(ToFullPath(task.Source, config));
                    if (string.IsNullOrEmpty(releaseId)) return null;
                    restoreTasks.Add(NewRestore(FileTransferEngine.DeployBackupRoot(config, releaseId), task.Destination));
                }
            }
            return restoreTasks;
        }

        /// <summary>
        /// 実際に実行した行から、復元用の計画を作る（実行した後でプロファイルを編集しても、実行した内容を戻せるように）。
        /// 戻す元の退避がない行は入れない（退避のコピーの途中で止まった行など。本番は変わっていないので戻す必要がなく、
        /// 入れておくと、復元のときにその行のエラーで止まって、ほかの行を戻せなくなるため）。
        /// 作れない（退避・配置以外の行がある、戻すものがない）ときは null。
        /// </summary>
        /// <param name="executedTasks">実行した行（入力エラーで実行しなかった行と、未実行の行は含めない）</param>
        /// <param name="directoryExists">フォルダがあるか（テストでは偽物に差し替える）</param>
        public static List<TransferTask> CreateFromExecuted(IList<TransferTask> executedTasks, AppConfig config, Func<string, bool> directoryExists)
        {
            List<TransferTask> restoreTasks = Create(executedTasks, config);
            if (restoreTasks == null) return null;

            List<TransferTask> withBackup = restoreTasks.Where(task => directoryExists(SnapshotOf(task, config))).ToList();
            if (withBackup.Count == 0) return null;
            return withBackup;
        }

        /// <summary>
        /// 実行した内容から作る復元用の計画の保存先（例：Profiles\Release.tsv → Profiles\Release_復元_20261013153000.tsv）。
        /// 実行するたびに別のファイルにする（前の実行の復元用を上書きしない）。
        /// </summary>
        public static string PathForExecuted(string profilesDir, string profileName, Messages msg, DateTime now)
        {
            return Path.Combine(profilesDir, $"{profileName}{msg.Profile.RestorePlanSuffix}_{now:yyyyMMddHHmmss}.tsv");
        }

        /// <summary>
        /// 復元の行が戻す元にする退避のフォルダ（退避先\対象フォルダのパスから作った名前）。エンジンと同じ場所を指す。
        /// </summary>
        private static string SnapshotOf(TransferTask restoreTask, AppConfig config)
        {
            string target = ToFullPath(restoreTask.Destination, config);
            return Path.Combine(ToFullPath(restoreTask.Source, config), FileTransferEngine.SnapshotName(target));
        }

        /// <summary>
        /// 復元の行を1つ作る。
        /// </summary>
        private static TransferTask NewRestore(string backupRoot, string target)
        {
            return new TransferTask { Action = TransferAction.Restore, Source = backupRoot, Destination = target, Mode = TransferMode.RestoreReplace };
        }

        /// <summary>
        /// 入力されたパス（前後の " や相対パスも可）を絶対パスにする。エンジンと同じ解釈にそろえる。
        /// </summary>
        private static string ToFullPath(string path, AppConfig config)
        {
            return Path.GetFullPath(config.GetFullPath(path.Trim().Trim('"')));
        }
    }
}
