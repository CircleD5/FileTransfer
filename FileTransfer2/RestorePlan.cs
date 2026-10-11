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
        /// 復元用の計画の保存先（例：Profiles\v1.2.0.tsv → Profiles\v1.2.0_復元.tsv）。
        /// </summary>
        public static string PathFor(string planPath, Messages msg)
        {
            string name = Path.GetFileNameWithoutExtension(planPath) + msg.Profile.RestorePlanSuffix + Path.GetExtension(planPath);
            string folder = Path.GetDirectoryName(planPath);
            if (folder == null) folder = ""; // ドライブ直下などで親フォルダがないとき
            return Path.Combine(folder, name);
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
