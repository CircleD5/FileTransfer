using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FileTransfer2
{
    public enum TransferAction { Unknown, Copy, Move, Delete, Deploy, Backup, Restore }
    public enum TransferMode { Unknown, FolderAsIs, ExtractContents, DeleteAll, DeleteKeepParent, DeleteFilesOnly, DeployReplace, BackupCopy, RestoreReplace }

    /// <summary>
    /// 「この行は実行できない」とエンジンが判断したときの例外。
    /// Message は messages.json の文言なので、そのまま画面に表示してよい。
    /// </summary>
    public class TransferException : Exception
    {
        /// <summary>
        /// 「実行できない」理由の文言を持つ例外を作る。
        /// </summary>
        public TransferException(string message) : base(message) { }
    }

    /// <summary>
    /// 1行分のファイル操作を行うクラス。
    /// ・コピー・移動・削除（テストデータの入れ替えなど、何度でも上書きする操作）
    /// ・配置・退避・復元（リリース用。退避を残し、何度実行しても結果が同じになる操作）
    /// 失敗したらその場で例外を投げる。続けるか止めるかは呼び出し側が決める。
    /// </summary>
    public class FileTransferEngine
    {
        private readonly Action<string> _logger;
        private readonly AppConfig _config;
        private readonly Messages _msg;
        private readonly IFileSystem _fs; // テストでは偽物のファイルシステムに差し替える

        // 今実行している行の番号と、その行で避けた「触らない場所」（ログを場所ごとに1回だけ出すため）。ProcessRow の最初で元に戻す
        private int _lineNo;
        private readonly HashSet<string> _avoided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ログの出力先・設定・ファイルシステムを受け取る。
        /// テストでは fileSystem に偽物（MockFileSystem）を渡し、本物のファイルに触れずに確かめる。
        /// </summary>
        public FileTransferEngine(Action<string> logger, AppConfig config, IFileSystem fileSystem)
        {
            // どれも必須。渡し忘れ（null）は、使う前にここで分かりやすいエラーにする
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));

            _logger = logger;
            _config = config;
            _msg = config.Msg;
            _fs = fileSystem;
        }

        /// <summary>
        /// 1行分の処理を実行する。転送元・転送先を絶対パスにし、転送元があるかを確かめてから、
        /// 処理種別ごとの処理に振り分ける。失敗したら例外を投げる（TransferException の文言は、そのまま画面に出せる）。
        /// 触らない場所（forbidden_paths.txt）は、どの処理でもエラーにせず避ける。転送元・転送先そのものが
        /// 触らない場所なら、何もせずに終わる（ログには出る）。
        /// </summary>
        public void ProcessRow(TransferAction action, string src, string dest, TransferMode mode, bool addDate, int lineNo)
        {
            _lineNo = lineNo;
            _avoided.Clear();

            string srcPath = ResolvePath(src);
            if (IsAvoided(srcPath)) return;

            bool isFolder = _fs.Directory.Exists(srcPath);
            if (!isFolder && !_fs.File.Exists(srcPath))
                throw new TransferException(Messages.Format(_msg.Transfer.SourceNotFound, srcPath));

            if (action == TransferAction.Delete)
            {
                if (isFolder) DeleteFolder(srcPath, mode);
                else ForceDeleteFile(srcPath);
                return;
            }

            string destPath = ResolvePath(dest);
            if (IsAvoided(destPath)) return;

            switch (action)
            {
                case TransferAction.Deploy:
                case TransferAction.Backup:
                case TransferAction.Restore:
                    // リリース用の処理は、フォルダ単位で扱う
                    if (!isFolder) throw new TransferException(_msg.Transfer.SourceMustBeFolder);
                    if (action == TransferAction.Deploy) Deploy(srcPath, destPath, lineNo);
                    else if (action == TransferAction.Backup) Backup(srcPath, destPath, lineNo);
                    else Restore(srcPath, destPath, lineNo);
                    break;
                default:
                    if (isFolder) TransferFolder(action, srcPath, destPath, mode, addDate, lineNo);
                    else TransferFile(action, srcPath, destPath, addDate, lineNo);
                    break;
            }
        }

        #region リリース（配置・退避・復元）
        //
        // 3つとも「退避先のフォルダ\対象フォルダのパスから作ったハッシュ（12文字）」に退避を置く（例：D:\App → 3f9c2e1b7d04）。
        // 同じパスからは必ず同じ名前になるので、配置や退避で作った退避は、復元でそのまま戻せる。
        // 名前の長さが対象フォルダのパスの長さに関係なく一定なので、深いフォルダでもパスが長くなりすぎない。
        //
        //   退避先\3f9c2e1b7d04\               … 退避（リリース前の状態）。一度作ったら上書きしない
        //   退避先\3f9c2e1b7d04.path           … どのフォルダの退避か（元のパス。人が見るためと、名前がぶつかっていないかの確認用）
        //   退避先\3f9c2e1b7d04.done           … 配置の適用済みの印
        //   退避先\3f9c2e1b7d04.restored       … 復元済みの印
        //   退避先\3f9c2e1b7d04.rolledback_日時\ … 復元する直前の状態（リリース後に増えたデータを取り出せるように）
        //
        // 本番に手を付けるのは、必ず退避（復元なら .rolledback）を作り終えてから。
        // どこで止まっても退避はそろっているので、原因を取り除いて再実行すれば、正しい状態にできる。
        // 触らない場所（forbidden_paths.txt）は、3つとも、退避にも入れず、消しも置きもしない。

        /// <summary>
        /// 配置：本番フォルダを退避してから、新版で入れ替える。deploy_keep.txt に書いたものは入れ替えず、その場に残す。
        /// 何度実行しても結果が同じになる（冪等）ように、次の順で行う。
        ///   1. 適用済みの印があれば何もしない
        ///   2. 本番フォルダを退避へコピーする（触らない場所は除く。すでに退避があれば、旧版を守るため退避し直さない）
        ///   3. 消す予定のファイルを開いてみて、使用中や権限がないものがないかを確かめる
        ///   4. 残すもの以外を消して、新版を置く（残すものの場所には置かない）
        ///   5. 適用済みの印を書く
        /// 転送元は「…\リリース番号\配置するフォルダ」の形にする（例：Release\v1.2.0\App）。親フォルダ名がリリース番号になる。
        /// 退避は BackupDir\リリース番号 に置く。
        /// </summary>
        private void Deploy(string srcPath, string destPath, int lineNo)
        {
            string releaseId = ReleaseIdOf(srcPath);
            if (string.IsNullOrEmpty(releaseId))
                throw new TransferException(_msg.Transfer.DeployNoReleaseId);

            string backupDir = SnapshotPath(DeployBackupRoot(_config, releaseId), destPath);
            string doneMark = backupDir + DoneMarkSuffix;

            // 配置先を退避すると、その中にある転送元や退避先まで一緒に退避されたり消えたりしてしまうため止める
            if (IsSamePath(srcPath, destPath) || IsInside(srcPath, destPath) || IsInside(destPath, srcPath) || IsInside(backupDir, destPath))
                throw new TransferException(_msg.Transfer.Conflict);

            // 別のフォルダの退避と名前がぶつかっていないか（ぶつかっていると、印も別のフォルダのものになるので、最初に確かめる）
            _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(backupDir));
            CheckAndRecordSource(backupDir, destPath);

            // 1. 適用済みなら何もしない（2回目以降の実行で、本番やその後のログなどを書き換えないため）
            if (_fs.File.Exists(doneMark))
            {
                _logger(Messages.Format(_msg.Transfer.DeployAlreadyDone, lineNo, releaseId, destPath));
                return;
            }

            // 残すもの：deploy_keep.txt のうち、この配置先の中にあるものだけを使う（配置先そのものや、外を指すものは無視）
            List<string> keeps = _config.DeployKeepPaths.Where(keep => IsInside(keep, destPath)).ToList();
            foreach (string keep in keeps)
            {
                _logger(Messages.Format(_msg.Transfer.DeployKept, lineNo, keep));
            }

            // 配置先の中の各パス（相対パス）を、退避するか・入れ替えるかで分ける
            Func<string, bool> isBackedUp = relative => !IsAvoided(_fs.Path.Combine(destPath, relative));
            Func<string, bool> isReplaced = relative =>
            {
                string path = _fs.Path.Combine(destPath, relative);
                return !IsAvoided(path) && !IsUnderAny(path, keeps);
            };

            // 今の本番（退避される）と新しい版（あとで復元するときに、戻す直前の状態として退避される）の両方を確かめる。
            // 配置のときに通っても、復元のときに長さで止まると切り戻せなくなるため、ここで止める
            CheckPathLength(backupDir, isBackedUp, destPath, srcPath);

            // 2. 退避（コピー。本番はまだ変えない）
            if (!_fs.Directory.Exists(backupDir))
            {
                if (_fs.Directory.Exists(destPath))
                    CopyFolderSafely(destPath, backupDir + ".tmp", backupDir, isBackedUp);
                else
                    _fs.Directory.CreateDirectory(backupDir); // 初めての配置：退避するものはないが、退避済みの印として空のフォルダを作る
                _logger(Messages.Format(_msg.Transfer.DeployBackedUp, lineNo, backupDir));
            }

            // 新しい版を置くもの：入れ替えるものと、本番になかった残すもの（初めての配置など）。本番にあるかは、置く前に決めておく
            List<string> existingKeeps = keeps.Where(keep => _fs.Directory.Exists(keep) || _fs.File.Exists(keep)).ToList();
            Func<string, bool> isPlaced = relative =>
            {
                string path = _fs.Path.Combine(destPath, relative);
                return !IsAvoided(path) && !IsUnderAny(path, existingKeeps);
            };

            if (_fs.Directory.Exists(destPath))
            {
                // 3. 消す予定のファイルが、使用中や権限がないために消せない、ということがないか（退避はできているので、止まっても本番は無事）
                CheckCanModify(destPath, isReplaced);
                // 4. 残すもの以外を消す。前回が途中で止まっていた場合も、ここで置きかけのものが消える
                DeleteContents(destPath, isReplaced);
            }
            CopyFolder(srcPath, destPath, isPlaced);

            // 5. 適用済みの印（中身は人が見るためのメモ）と、配置の履歴（いつ・何を・どこへ・誰が）
            string record = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss}\t{releaseId}\t{srcPath}\t{destPath}\t{Environment.UserName}@{Environment.MachineName}";
            _fs.File.WriteAllText(doneMark, record);
            DeleteFileIfExists(backupDir + RestoredMarkSuffix); // 一度復元した後で配置し直した場合、もう一度復元できるようにする
            AppendDeployHistory(record);
        }

        /// <summary>
        /// 退避：フォルダの今の状態を、退避先へまるごとコピーする（元のフォルダはそのまま残す）。
        /// すでに退避があれば何もしない（2回実行しても、最初の退避＝リリース前の状態を上書きしない）。
        /// </summary>
        /// <param name="backupRoot">退避先のフォルダ（例：D:\ReleaseBackup\v1.2.0）</param>
        private void Backup(string srcPath, string backupRoot, int lineNo)
        {
            // 退避先が退避するフォルダの中にあると、コピーが終わらなくなるため止める
            if (IsSamePath(backupRoot, srcPath) || IsInside(backupRoot, srcPath))
                throw new TransferException(_msg.Transfer.Conflict);

            string snapshot = SnapshotPath(backupRoot, srcPath);
            _fs.Directory.CreateDirectory(backupRoot);
            CheckAndRecordSource(snapshot, srcPath);
            if (_fs.Directory.Exists(snapshot))
            {
                _logger(Messages.Format(_msg.Transfer.BackupExists, lineNo, snapshot));
                return;
            }

            // 触らない場所は、退避にも入れない
            Func<string, bool> isBackedUp = relative => !IsAvoided(_fs.Path.Combine(srcPath, relative));
            CheckPathLength(snapshot, isBackedUp, srcPath);
            CopyFolderSafely(srcPath, snapshot + ".tmp", snapshot, isBackedUp);
            _logger(Messages.Format(_msg.Transfer.BackupSaved, lineNo, snapshot));
        }

        /// <summary>
        /// 復元：退避（退避や配置で作ったもの）を、元の場所へ戻す。触らない場所（forbidden_paths.txt）には触らない。
        ///   1. 復元済みの印があれば何もしない
        ///   2. 今の状態を「〇〇.rolledback_日時」にコピーする（リリース後に増えたデータを取り出せるように。本番はまだ変えない）
        ///   3. 消す予定のファイルを開いてみて、使用中や権限がないものがないかを確かめる
        ///   4. 今の中身を消して、退避の中身をコピーして戻す（退避そのものは残るので、何度でも戻せる）
        ///   5. 配置の適用済みの印を消し、復元済みの印を書く
        /// </summary>
        /// <param name="backupRoot">退避先のフォルダ（退避の転送先、または配置なら BackupDir\リリース番号）</param>
        private void Restore(string backupRoot, string targetPath, int lineNo)
        {
            // 戻す先の中に退避先があると、今の状態を退避するときに退避先ごと動いてしまうため止める
            if (IsSamePath(backupRoot, targetPath) || IsInside(backupRoot, targetPath))
                throw new TransferException(_msg.Transfer.Conflict);

            string snapshot = SnapshotPath(backupRoot, targetPath);
            if (!_fs.Directory.Exists(snapshot))
                throw new TransferException(Messages.Format(_msg.Transfer.RestoreNoBackup, snapshot));
            CheckAndRecordSource(snapshot, targetPath);

            string restoredMark = snapshot + RestoredMarkSuffix;
            if (_fs.File.Exists(restoredMark))
            {
                _logger(Messages.Format(_msg.Transfer.RestoreAlreadyDone, lineNo, targetPath));
                return;
            }

            // 触らない場所は、戻す直前の状態の退避にも入れず、消しも戻しもしない（今の forbidden_paths.txt で決める）
            Func<string, bool> isHandled = relative => !IsAvoided(_fs.Path.Combine(targetPath, relative));

            // 今の戻す先（リリース後に深いフォルダが増えていることがある）を、戻す直前の状態として退避できるか
            CheckPathLength(snapshot, isHandled, targetPath);

            if (_fs.Directory.Exists(targetPath))
            {
                // 2. 〇〇.rolledback_日時（同じ秒に2回復元しても名前がぶつからないよう、必要なら連番を付ける）
                string rolledBack = AppendDateToFolderName(snapshot + ".rolledback");
                CopyFolderSafely(targetPath, snapshot + ".rolledback.tmp", rolledBack, isHandled);
                _logger(Messages.Format(_msg.Transfer.RestoreRolledBack, lineNo, rolledBack));

                // 3. 消せないファイルがないか（.rolledback はできているので、止まっても本番は無事）
                CheckCanModify(targetPath, isHandled);
                // 4. 今の中身を消す
                DeleteContents(targetPath, isHandled);
            }
            CopyFolder(snapshot, targetPath, isHandled);

            DeleteFileIfExists(snapshot + DoneMarkSuffix); // 配置し直せるように、適用済みの印を消す
            _fs.File.WriteAllText(restoredMark, $"{DateTime.Now:yyyy/MM/dd HH:mm:ss}\t{snapshot}\t{targetPath}\t{Environment.UserName}@{Environment.MachineName}");
            _logger(Messages.Format(_msg.Transfer.RestoreDone, lineNo, targetPath));
        }

        private const string DoneMarkSuffix = ".done";
        private const string RestoredMarkSuffix = ".restored";
        private const string SourcePathSuffix = ".path";

        // Windows のパスの長さの上限（長いパスを有効にしていない、ふつうの設定の場合）
        private const int MaxFilePathLength = 259;   // ファイル
        private const int MaxFolderPathLength = 247; // フォルダ（中に短い名前のファイルを置ける余地を残す決まり）
        // 退避の名前に付く、いちばん長いもの（復元のときの「〇〇.rolledback_日時_連番」。連番は2桁まで見込む）
        private const string LongestSuffix = ".rolledback_yyyyMMddHHmmss_99";
        public const string DeployHistoryFile = "deploy_history.log";

        /// <summary>
        /// 配置の転送元（…\リリース番号\配置するフォルダ）から、リリース番号（親フォルダの名前）を取り出す。
        /// </summary>
        public static string ReleaseIdOf(string sourceFullPath)
        {
            string parent = Path.GetDirectoryName(sourceFullPath.TrimEnd('\\'));
            if (parent == null) return ""; // ドライブ直下（C:\ など）には親フォルダがない
            return Path.GetFileName(parent);
        }

        /// <summary>
        /// 配置の退避先のフォルダ（BackupDir\リリース番号）。復元用の計画を作るときにも使う。
        /// </summary>
        public static string DeployBackupRoot(AppConfig config, string releaseId)
        {
            return Path.Combine(Path.GetFullPath(config.GetFullPath(config.BackupDir)), releaseId);
        }

        /// <summary>
        /// 退避先のフォルダの中で、対象フォルダの退避を置く場所。
        /// </summary>
        private string SnapshotPath(string backupRoot, string targetPath)
        {
            return _fs.Path.Combine(backupRoot, SnapshotName(targetPath));
        }

        /// <summary>
        /// 対象フォルダのパスから、退避の名前（SHA-256 の先頭12文字）を作る。
        /// 同じパスからは必ず同じ名前になる（大文字・小文字と、末尾の \ の違いは同じパスとみなす）。
        /// </summary>
        public static string SnapshotName(string targetFullPath)
        {
            string normalized = targetFullPath.TrimEnd('\\').ToUpperInvariant();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToLowerInvariant();
            }
        }

        /// <summary>
        /// 退避の隣に「〇〇.path」を書き、どのフォルダの退避かを残す。
        /// すでにあって別のフォルダが書かれていたら、名前がぶつかっているので止める（別のフォルダの退避で上書き・復元しないため）。
        /// </summary>
        private void CheckAndRecordSource(string snapshot, string targetPath)
        {
            string pathFile = snapshot + SourcePathSuffix;
            if (!_fs.File.Exists(pathFile))
            {
                _fs.File.WriteAllText(pathFile, targetPath);
                return;
            }

            string recorded = _fs.File.ReadAllText(pathFile).Trim();
            if (!IsSamePath(recorded, targetPath))
                throw new TransferException(Messages.Format(_msg.Transfer.SnapshotNameConflict, pathFile, recorded, targetPath));
        }

        /// <summary>
        /// 退避や復元で作るパスが、Windows のパスの長さの上限に収まるかを、作り始める前に確かめる。
        /// folders の中身を、退避の名前でいちばん長くなるもの（〇〇.rolledback_日時_連番）の下に置いたとして計算する。
        /// 上限を超えるものがあれば、いちばん長いパスと、何文字超えるかを書いて止める。
        /// </summary>
        /// <param name="snapshot">退避のフォルダ（退避先\ハッシュ）</param>
        /// <param name="include">退避されるものか（相対パスを受け取る）。「退避しない」ものは計算に入れない</param>
        /// <param name="folders">退避されるフォルダ（ないものは飛ばす。初めての配置など）</param>
        private void CheckPathLength(string snapshot, Func<string, bool> include, params string[] folders)
        {
            string longestRoot = snapshot + LongestSuffix;

            // 作ることになるパスと、それぞれの上限（退避のフォルダ自体も含める）
            var planned = new List<(string Path, int Limit)> { (longestRoot, MaxFolderPathLength) };
            foreach (string folder in folders)
            {
                if (!_fs.Directory.Exists(folder)) continue;
                foreach (string dir in _fs.Directory.GetDirectories(folder, "*", SearchOption.AllDirectories))
                {
                    string relative = GetRelativePath(folder, dir);
                    if (include(relative)) planned.Add((longestRoot + "\\" + relative, MaxFolderPathLength));
                }
                foreach (string file in _fs.Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string relative = GetRelativePath(folder, file);
                    if (include(relative)) planned.Add((longestRoot + "\\" + relative, MaxFilePathLength));
                }
            }

            // いちばん上限を超えているもの（超えていなければ、いちばん余裕がないもの）
            var worst = planned.OrderByDescending(p => p.Path.Length - p.Limit).First();
            int over = worst.Path.Length - worst.Limit;
            if (over > 0)
                throw new TransferException(Messages.Format(_msg.Transfer.PathTooLong, worst.Limit, over, worst.Path));
        }

        /// <summary>
        /// 配置の履歴を Logs\deploy_history.log に1行追記する。日付ごとのログと違い、古くなっても消さない。
        /// </summary>
        private void AppendDeployHistory(string record)
        {
            string logDir = ResolvePath(_config.LogDir);
            _fs.Directory.CreateDirectory(logDir);
            _fs.File.AppendAllText(_fs.Path.Combine(logDir, DeployHistoryFile), record + Environment.NewLine);
        }

        /// <summary>
        /// path が触らない場所（forbidden_paths.txt に書いた場所か、その中）なら true。どの処理でも、true のものには触らない。
        /// 今の行で初めて当たった触らない場所は、ログに1行出す（ファイルごとに出すと、ログがとても長くなるため）。
        /// </summary>
        private bool IsAvoided(string path)
        {
            foreach (string forbidden in _config.ForbiddenPaths)
            {
                if (IsSamePath(path, forbidden) || IsInside(path, forbidden))
                {
                    if (_avoided.Add(forbidden)) _logger(Messages.Format(_msg.Transfer.Avoided, _lineNo, forbidden));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// path が folders のどれかか、その中にあれば true。
        /// </summary>
        private static bool IsUnderAny(string path, IEnumerable<string> folders)
        {
            return folders.Any(folder => IsSamePath(path, folder) || IsInside(path, folder));
        }

        /// <summary>
        /// from の中身（include が true のもの）を、いったん tempDir にコピーし、最後までコピーできてから to という名前に変える。
        /// 途中で止まっても正式な名前のフォルダはできていないので、再実行したときに「退避済み」と誤解することがない。
        /// 再実行したときに前回の tempDir が残っていれば、消してからコピーし直す。
        /// </summary>
        private void CopyFolderSafely(string from, string tempDir, string to, Func<string, bool> include)
        {
            if (_fs.Directory.Exists(tempDir)) DeleteFolderTree(tempDir); // 前回のコピーが途中で止まっていた
            CopyFolder(from, tempDir, include);
            _fs.Directory.Move(tempDir, to); // 同じフォルダの中なので名前の変更だけで済む
        }

        /// <summary>
        /// from の中身のうち、include が true のもの（相対パスで判断する）を、to へコピーする（同じ名前のファイルは上書き）。
        /// </summary>
        private void CopyFolder(string from, string to, Func<string, bool> include)
        {
            _fs.Directory.CreateDirectory(to);
            foreach (string dir in _fs.Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                string relative = GetRelativePath(from, dir);
                if (include(relative)) _fs.Directory.CreateDirectory(_fs.Path.Combine(to, relative));
            }
            foreach (string file in _fs.Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string relative = GetRelativePath(from, file);
                if (!include(relative)) continue;
                string destFile = _fs.Path.Combine(to, relative);
                // 含めないフォルダの中に、含めるものがある場合（近い項目の種類が違う）に備えて、親フォルダを作っておく
                _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(destFile));
                _fs.File.Copy(file, destFile, true);
            }
        }

        /// <summary>
        /// folder の中身のうち、remove が true のもの（相対パスで判断する）を消す。folder 自体は残す。
        /// フォルダは、中が空になったものだけを消す（残すものが入っているフォルダは消さない）。
        /// </summary>
        private void DeleteContents(string folder, Func<string, bool> remove)
        {
            foreach (string file in _fs.Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (remove(GetRelativePath(folder, file))) ForceDeleteFile(file);
            }
            // 深いフォルダから順に消す（親より先に子を空にするため）
            foreach (string dir in _fs.Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(dir => dir.Length))
            {
                bool isEmpty = !_fs.Directory.EnumerateFileSystemEntries(dir).Any();
                if (isEmpty && remove(GetRelativePath(folder, dir))) _fs.Directory.Delete(dir);
            }
        }

        /// <summary>
        /// 消す予定のファイル（remove が true のもの）を、実際に消す前にすべて開いてみる。中身も更新日時も変えない。
        /// ほかのプログラムが使用中のものや、書き換える権限がないものがあれば、どのファイルか・なぜかを書いて止める。
        /// （開けるかどうかは、アクセス権を読んで判断するより、実際に開いてみる方が簡単で正確）
        /// </summary>
        private void CheckCanModify(string folder, Func<string, bool> remove)
        {
            foreach (string file in _fs.Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!remove(GetRelativePath(folder, file))) continue;

                // 読み取り専用のファイルは、消すときに属性を外すので、ほかのプログラムが使っていないかだけを確かめる
                FileAccess access = FileAccess.ReadWrite;
                if (_fs.File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)) access = FileAccess.Read;
                try
                {
                    using (_fs.File.Open(file, FileMode.Open, access, FileShare.None)) { }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new TransferException(Messages.Format(_msg.Transfer.CannotModify, file, ex.Message));
                }
            }
        }

        /// <summary>
        /// ファイルがあれば消す（なければ何もしない）。
        /// </summary>
        private void DeleteFileIfExists(string path)
        {
            if (_fs.File.Exists(path)) _fs.File.Delete(path);
        }

        #endregion

        #region コピー・移動

        /// <summary>
        /// ファイル1つをコピー・移動する。転送先の書き方（フォルダか、ファイル名か）の解釈は DetermineDestFilePath で決める。
        /// </summary>
        private void TransferFile(TransferAction action, string srcPath, string destPath, bool addDate, int lineNo)
        {
            string destFile = DetermineDestFilePath(srcPath, destPath, addDate);

            // 同じファイルに移動すると「上書きのための削除」で元ファイルが消えてしまうため止める
            if (IsSamePath(srcPath, destFile))
                throw new TransferException(_msg.Transfer.SamePath);
            if (IsAvoided(destFile)) return;

            if (_fs.File.Exists(destFile))
                _logger(Messages.Format(_msg.Transfer.Overwrite, lineNo, _fs.Path.GetFileName(destFile)));

            CopyOrMoveFile(action, srcPath, destFile);
        }

        /// <summary>
        /// フォルダをコピー・移動する。
        /// 「フォルダごと転送」なら転送先の中にフォルダを作り、「親フォルダを含めずに転送」なら中身だけを転送先に入れる。
        /// 移動のときは、最後に空になった転送元のフォルダを消す。
        /// </summary>
        private void TransferFolder(TransferAction action, string srcPath, string destPath, TransferMode mode, bool addDate, int lineNo)
        {
            string targetDir;
            if (mode == TransferMode.ExtractContents)
            {
                targetDir = destPath;
            }
            else
            {
                targetDir = _fs.Path.Combine(destPath, _fs.Path.GetFileName(srcPath.TrimEnd('\\')));
                if (addDate)
                {
                    targetDir = AppendDateToFolderName(targetDir);
                    _logger(Messages.Format(_msg.Transfer.FolderDate, lineNo, _fs.Path.GetFileName(targetDir)));
                }
            }

            // 自分自身の中へコピーすると終わらなくなるため止める
            if (IsSamePath(targetDir, srcPath) || IsInside(targetDir, srcPath))
                throw new TransferException(_msg.Transfer.Conflict);
            if (IsAvoided(targetDir)) return;

            // 「親フォルダを含めずに転送」のときだけ、日付はファイル名に付ける
            bool addDateToFiles = mode == TransferMode.ExtractContents && addDate;

            _fs.Directory.CreateDirectory(targetDir);
            foreach (string dir in _fs.Directory.GetDirectories(srcPath, "*", SearchOption.AllDirectories))
            {
                string targetSubDir = _fs.Path.Combine(targetDir, GetRelativePath(srcPath, dir));
                // 触らない場所は、転送元・転送先のどちらにあっても飛ばす
                if (IsAvoided(dir) || IsAvoided(targetSubDir)) continue;
                _fs.Directory.CreateDirectory(targetSubDir);
            }

            foreach (string file in _fs.Directory.GetFiles(srcPath, "*", SearchOption.AllDirectories))
            {
                string targetFile = _fs.Path.Combine(targetDir, GetRelativePath(srcPath, file));
                if (IsAvoided(file) || IsAvoided(targetFile)) continue;

                if (addDateToFiles)
                    targetFile = AppendDateToFileName(targetFile);
                else if (_fs.File.Exists(targetFile))
                    _logger(Messages.Format(_msg.Transfer.Overwrite, lineNo, _fs.Path.GetFileName(targetFile)));

                // 触らない場所の中のフォルダを飛ばしたときに備えて、親フォルダを作っておく
                _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(targetFile));
                CopyOrMoveFile(action, file, targetFile);
            }

            // 移動の場合、空になった転送元のフォルダを消す。
            // 触らない場所のために残ったファイルがあれば、そのフォルダ（と転送元のフォルダ）は残る
            if (action == TransferAction.Move) DeleteEmptyFolders(srcPath);
        }

        /// <summary>
        /// folder の中の空のフォルダを深い順に消し、最後に folder 自体も空なら消す。触らない場所は消さない。
        /// </summary>
        private void DeleteEmptyFolders(string folder)
        {
            foreach (string dir in _fs.Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(dir => dir.Length))
            {
                if (!IsAvoided(dir) && !_fs.Directory.EnumerateFileSystemEntries(dir).Any()) _fs.Directory.Delete(dir);
            }
            if (!_fs.Directory.EnumerateFileSystemEntries(folder).Any()) _fs.Directory.Delete(folder);
        }

        /// <summary>
        /// ファイル1つをコピーまたは移動する。転送先に同じ名前があれば上書きする（移動は、先に転送先を消してから移す）。
        /// </summary>
        private void CopyOrMoveFile(TransferAction action, string from, string to)
        {
            if (action == TransferAction.Copy)
            {
                _fs.File.Copy(from, to, true);
            }
            else
            {
                if (_fs.File.Exists(to)) _fs.File.Delete(to);
                _fs.File.Move(from, to);
            }
        }

        /// <summary>
        /// ファイルの転送先のパスを決める。
        /// 転送先が既存のフォルダか \ で終わるなら、その中の同じ名前。それ以外は、転送先そのものをファイル名として使う。
        /// 日付付与なら、名前に日時を付ける。
        /// </summary>
        private string DetermineDestFilePath(string srcFile, string destPath, bool addDate)
        {
            string targetPath;
            // 転送先が既存のフォルダ、または \ で終わっていれば「フォルダの中へ」入れる
            if (_fs.Directory.Exists(destPath) || destPath.EndsWith("\\"))
            {
                _fs.Directory.CreateDirectory(destPath);
                targetPath = _fs.Path.Combine(destPath, _fs.Path.GetFileName(srcFile));
            }
            else
            {
                // それ以外は「このファイル名で保存」とみなす
                string parentDir = _fs.Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parentDir)) _fs.Directory.CreateDirectory(parentDir);
                targetPath = destPath;
            }
            if (addDate) return AppendDateToFileName(targetPath);
            return targetPath;
        }

        #endregion

        #region 削除

        /// <summary>
        /// フォルダを、モード（丸ごと削除・親フォルダのみ残す・ファイルのみ削除）に合わせて消す。
        /// 触らない場所は残す（その場所が入っているフォルダも、空にならないので残る）。
        /// </summary>
        private void DeleteFolder(string folderPath, TransferMode mode)
        {
            Func<string, bool> isDeleted = relative => !IsAvoided(_fs.Path.Combine(folderPath, relative));
            switch (mode)
            {
                case TransferMode.DeleteAll:
                    DeleteContents(folderPath, isDeleted);
                    if (!_fs.Directory.EnumerateFileSystemEntries(folderPath).Any()) _fs.Directory.Delete(folderPath);
                    break;
                case TransferMode.DeleteKeepParent:
                    DeleteContents(folderPath, isDeleted);
                    break;
                case TransferMode.DeleteFilesOnly:
                    foreach (string file in _fs.Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                    {
                        if (isDeleted(GetRelativePath(folderPath, file))) ForceDeleteFile(file);
                    }
                    break;
                default:
                    throw new TransferException(_msg.Input.ModeInvalid);
            }
        }

        /// <summary>
        /// フォルダを中身ごと消す（読み取り専用のファイルも消す）。
        /// </summary>
        private void DeleteFolderTree(string folderPath)
        {
            DeleteAllFiles(folderPath);
            DeleteSubFolders(folderPath);
            _fs.Directory.Delete(folderPath);
        }

        /// <summary>
        /// フォルダの中のファイルを、下の階層まで全部消す（フォルダは残す）。
        /// </summary>
        private void DeleteAllFiles(string folderPath)
        {
            foreach (string file in _fs.Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                ForceDeleteFile(file);
            }
        }

        /// <summary>
        /// 配下のフォルダを深い順に消す。空のフォルダしか消さないので、ファイルが残っていれば例外になる。
        /// </summary>
        private void DeleteSubFolders(string folderPath)
        {
            var subFolders = _fs.Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories)
                                          .OrderByDescending(dir => dir.Length);
            foreach (string dir in subFolders)
            {
                _fs.Directory.Delete(dir);
            }
        }

        /// <summary>
        /// ファイルを消す。読み取り専用なら、その属性を外してから消す。
        /// </summary>
        private void ForceDeleteFile(string filePath)
        {
            var attrs = _fs.File.GetAttributes(filePath);
            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                _fs.File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
            _fs.File.Delete(filePath);
        }

        #endregion

        #region パスの扱い

        /// <summary>
        /// 入力されたパスを絶対パスにする。前後の " は取り除き、相対パスはアプリのフォルダ基準で解釈する。
        /// </summary>
        private string ResolvePath(string path)
        {
            if (path == null) path = "";
            string trimmed = path.Trim().Trim('"');
            if (trimmed.Length == 0)
                throw new TransferException(_msg.Transfer.PathEmpty);
            return _fs.Path.GetFullPath(_config.GetFullPath(trimmed));
        }

        /// <summary>
        /// 2つのパスが同じ場所を指しているか（大文字・小文字と、末尾の \ は区別しない）。
        /// </summary>
        private static bool IsSamePath(string a, string b)
        {
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// path が folder の中（下の階層）にあるか。folder 自身は含まない。
        /// </summary>
        private static bool IsInside(string path, string folder)
        {
            return path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// root から見た相対パスを返す（例：root が C:\A、fullPath が C:\A\B\c.txt なら B\c.txt）。
        /// </summary>
        private static string GetRelativePath(string root, string fullPath)
        {
            return fullPath.Substring(root.TrimEnd('\\').Length).TrimStart('\\');
        }

        /// <summary>
        /// ファイル名の後ろに日時を付けたパスを返す（例：a.txt → a_20261011153000.txt）。
        /// 同じ名前がすでにあれば _1、_2 … と連番を付ける。
        /// </summary>
        private string AppendDateToFileName(string filePath)
        {
            string dir = _fs.Path.GetDirectoryName(filePath);
            if (dir == null) dir = ""; // ドライブ直下などで親フォルダがないとき
            string name = _fs.Path.GetFileNameWithoutExtension(filePath);
            string ext = _fs.Path.GetExtension(filePath);
            string dateStr = DateTime.Now.ToString("yyyyMMddHHmmss");
            string newPath = _fs.Path.Combine(dir, $"{name}_{dateStr}{ext}");
            int counter = 1;
            while (_fs.File.Exists(newPath))
            {
                newPath = _fs.Path.Combine(dir, $"{name}_{dateStr}_{counter}{ext}");
                counter++;
            }
            return newPath;
        }

        /// <summary>
        /// フォルダ名の後ろに日時を付けたパスを返す（例：Data → Data_20261011153000）。
        /// 同じ名前がすでにあれば _1、_2 … と連番を付ける。
        /// </summary>
        private string AppendDateToFolderName(string dirPath)
        {
            string parent = _fs.Path.GetDirectoryName(dirPath);
            if (parent == null) parent = ""; // ドライブ直下などで親フォルダがないとき
            string name = _fs.Path.GetFileName(dirPath);
            string dateStr = DateTime.Now.ToString("yyyyMMddHHmmss");
            string newPath = _fs.Path.Combine(parent, $"{name}_{dateStr}");
            int counter = 1;
            while (_fs.Directory.Exists(newPath))
            {
                newPath = _fs.Path.Combine(parent, $"{name}_{dateStr}_{counter}");
                counter++;
            }
            return newPath;
        }

        #endregion
    }
}
