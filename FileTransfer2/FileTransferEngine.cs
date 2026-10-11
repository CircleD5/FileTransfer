using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;

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

        /// <summary>
        /// ログの出力先・設定・ファイルシステムを受け取る。
        /// テストでは fileSystem に偽物（MockFileSystem）を渡し、本物のファイルに触れずに確かめる。
        /// </summary>
        public FileTransferEngine(Action<string> logger, AppConfig config, IFileSystem fileSystem)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _msg = config.Msg;
            _fs = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        /// <summary>
        /// 1行分の処理を実行する。転送元・転送先を絶対パスにし、禁止パスと転送元があるかを確かめてから、
        /// 処理種別ごとの処理に振り分ける。失敗したら例外を投げる（TransferException の文言は、そのまま画面に出せる）。
        /// </summary>
        public void ProcessRow(TransferAction action, string src, string dest, TransferMode mode, bool addDate, int lineNo)
        {
            string srcPath = ResolvePath(src);
            CheckNotForbidden(srcPath);

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
            CheckNotForbidden(destPath);

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
        // 3つとも「退避先のフォルダ\対象フォルダのパスを1つの名前にしたもの」に退避を置く（例：D:\App → D_App）。
        // 名前の付け方が同じなので、配置や退避で作った退避は、復元でそのまま戻せる。
        //
        //   退避先\D_App\               … 退避（リリース前の状態）。一度作ったら上書きしない
        //   退避先\D_App.done           … 配置の適用済みの印
        //   退避先\D_App.restored       … 復元済みの印
        //   退避先\D_App.rolledback_日時\ … 復元する直前の状態（リリース後に増えたデータを取り出せるように）

        /// <summary>
        /// 配置：本番フォルダを退避してから、新版で完全に入れ替える。
        /// 何度実行しても結果が同じになる（冪等）ように、次の順で行う。
        ///   1. 適用済みの印があれば何もしない
        ///   2. 本番フォルダをまるごと退避する（すでに退避があれば、旧版を守るため退避し直さない）
        ///   3. 新版を置く
        ///   4. deploy_keep.txt に書いたものだけ、退避から戻す
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

            // 配置先を退避（移動）すると、その中にある転送元や退避先まで一緒に動いてしまうため止める
            if (IsSamePath(srcPath, destPath) || IsInside(srcPath, destPath) || IsInside(destPath, srcPath) || IsInside(backupDir, destPath))
                throw new TransferException(_msg.Transfer.Conflict);

            // 1. 適用済みなら何もしない（2回目以降の実行で、本番やその後のログなどを書き換えないため）
            if (_fs.File.Exists(doneMark))
            {
                _logger(Messages.Format(_msg.Transfer.DeployAlreadyDone, lineNo, releaseId, destPath));
                return;
            }

            // 2. 退避
            if (!_fs.Directory.Exists(backupDir))
            {
                _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(backupDir));
                if (_fs.Directory.Exists(destPath))
                    MoveFolderSafely(destPath, backupDir);
                else
                    _fs.Directory.CreateDirectory(backupDir); // 初めての配置：退避するものはないが、退避済みの印として空のフォルダを作る
                _logger(Messages.Format(_msg.Transfer.DeployBackedUp, lineNo, backupDir));
            }
            else if (_fs.Directory.Exists(destPath))
            {
                // 前回が途中で止まっていた（または「退避」で先に退避を作ってあった）場合：
                // 旧版は退避にあるので、今の配置先を消して最初から置き直す
                DeleteFolderTree(destPath);
            }

            // 3. 新版を置く
            CopyFolder(srcPath, destPath);

            // 4. 残すものを退避から戻す（本番にあったものだけ。退避は旧版のままなので、何度戻しても同じ結果になる）
            foreach (string keep in _config.DeployKeepPaths)
            {
                string from = _fs.Path.Combine(backupDir, keep);
                string to = _fs.Path.Combine(destPath, keep);
                if (_fs.Directory.Exists(from))
                {
                    if (_fs.Directory.Exists(to)) DeleteFolderTree(to);
                    CopyFolder(from, to);
                }
                else if (_fs.File.Exists(from))
                {
                    _fs.Directory.CreateDirectory(_fs.Path.GetDirectoryName(to));
                    _fs.File.Copy(from, to, true);
                }
                else continue;
                _logger(Messages.Format(_msg.Transfer.DeployKept, lineNo, keep));
            }

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
            if (_fs.Directory.Exists(snapshot))
            {
                _logger(Messages.Format(_msg.Transfer.BackupExists, lineNo, snapshot));
                return;
            }

            _fs.Directory.CreateDirectory(backupRoot);
            CopyFolderSafely(srcPath, snapshot);
            _logger(Messages.Format(_msg.Transfer.BackupSaved, lineNo, snapshot));
        }

        /// <summary>
        /// 復元：退避（退避や配置で作ったもの）を、元の場所へ戻す。
        ///   1. 復元済みの印があれば何もしない
        ///   2. 今の状態を「〇〇.rolledback_日時」に退避する（リリース後に増えたデータを取り出せるように）
        ///   3. 退避を元の場所へコピーする（退避そのものは残るので、何度でも戻せる）
        ///   4. 配置の適用済みの印を消し、復元済みの印を書く
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

            string restoredMark = snapshot + RestoredMarkSuffix;
            if (_fs.File.Exists(restoredMark))
            {
                _logger(Messages.Format(_msg.Transfer.RestoreAlreadyDone, lineNo, targetPath));
                return;
            }

            if (_fs.Directory.Exists(targetPath))
            {
                // 〇〇.rolledback_日時（同じ秒に2回復元しても名前がぶつからないよう、必要なら連番を付ける）
                string rolledBack = AppendDateToFolderName(snapshot + ".rolledback");
                MoveFolderSafely(targetPath, rolledBack);
                _logger(Messages.Format(_msg.Transfer.RestoreRolledBack, lineNo, rolledBack));
            }

            CopyFolder(snapshot, targetPath);

            DeleteFileIfExists(snapshot + DoneMarkSuffix); // 配置し直せるように、適用済みの印を消す
            _fs.File.WriteAllText(restoredMark, $"{DateTime.Now:yyyy/MM/dd HH:mm:ss}\t{snapshot}\t{targetPath}\t{Environment.UserName}@{Environment.MachineName}");
            _logger(Messages.Format(_msg.Transfer.RestoreDone, lineNo, targetPath));
        }

        private const string DoneMarkSuffix = ".done";
        private const string RestoredMarkSuffix = ".restored";
        public const string DeployHistoryFile = "deploy_history.log";

        /// <summary>
        /// 配置の転送元（…\リリース番号\配置するフォルダ）から、リリース番号（親フォルダの名前）を取り出す。
        /// </summary>
        public static string ReleaseIdOf(string sourceFullPath)
        {
            return Path.GetFileName(Path.GetDirectoryName(sourceFullPath.TrimEnd('\\')) ?? "");
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
            return _fs.Path.Combine(backupRoot, ToFolderName(targetPath));
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
        /// フォルダをまるごと別の場所へ移す。
        /// 同じドライブなら「移動」（一瞬で終わり、途中で止まることがない）。
        /// 別のドライブは移動できないので、CopyFolderSafely でコピーしてから元を消す。
        /// </summary>
        private void MoveFolderSafely(string from, string to)
        {
            bool sameDrive = string.Equals(_fs.Path.GetPathRoot(from), _fs.Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase);
            if (sameDrive)
            {
                _fs.Directory.Move(from, to);
                return;
            }
            CopyFolderSafely(from, to);
            DeleteFolderTree(from);
        }

        /// <summary>
        /// フォルダをまるごとコピーする。いったん「〇〇.tmp」という名前でコピーし、最後までコピーできてから正式な名前に変える。
        /// 途中で止まっても正式な名前のフォルダはできていないので、再実行したときに「退避済み」と誤解することがない。
        /// </summary>
        private void CopyFolderSafely(string from, string to)
        {
            string tempDir = to + ".tmp";
            if (_fs.Directory.Exists(tempDir)) DeleteFolderTree(tempDir); // 前回のコピーが途中で止まっていた
            CopyFolder(from, tempDir);
            _fs.Directory.Move(tempDir, to); // 同じフォルダの中なので名前の変更だけで済む
        }

        /// <summary>
        /// パスをフォルダ名に使える1つの名前にする（例：C:\App → C_App、\\server\share\App → server_share_App）。
        /// </summary>
        private static string ToFolderName(string path)
        {
            return path.TrimEnd('\\').Replace(":", "").Replace('\\', '_').TrimStart('_');
        }

        /// <summary>
        /// フォルダの中身を、別のフォルダへコピーする（同じ名前のファイルは上書き）。
        /// </summary>
        private void CopyFolder(string from, string to)
        {
            _fs.Directory.CreateDirectory(to);
            foreach (string dir in _fs.Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            {
                _fs.Directory.CreateDirectory(_fs.Path.Combine(to, GetRelativePath(from, dir)));
            }
            foreach (string file in _fs.Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                _fs.File.Copy(file, _fs.Path.Combine(to, GetRelativePath(from, file)), true);
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

            // 「親フォルダを含めずに転送」のときだけ、日付はファイル名に付ける
            bool addDateToFiles = mode == TransferMode.ExtractContents && addDate;

            _fs.Directory.CreateDirectory(targetDir);
            foreach (string dir in _fs.Directory.GetDirectories(srcPath, "*", SearchOption.AllDirectories))
            {
                _fs.Directory.CreateDirectory(_fs.Path.Combine(targetDir, GetRelativePath(srcPath, dir)));
            }

            foreach (string file in _fs.Directory.GetFiles(srcPath, "*", SearchOption.AllDirectories))
            {
                string targetFile = _fs.Path.Combine(targetDir, GetRelativePath(srcPath, file));
                if (addDateToFiles)
                    targetFile = AppendDateToFileName(targetFile);
                else if (_fs.File.Exists(targetFile))
                    _logger(Messages.Format(_msg.Transfer.Overwrite, lineNo, _fs.Path.GetFileName(targetFile)));

                CopyOrMoveFile(action, file, targetFile);
            }

            // 移動の場合、中身はすべて移し終えているので、空になった転送元フォルダを消す
            if (action == TransferAction.Move)
            {
                DeleteSubFolders(srcPath);
                _fs.Directory.Delete(srcPath);
            }
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
            return addDate ? AppendDateToFileName(targetPath) : targetPath;
        }

        #endregion

        #region 削除

        /// <summary>
        /// フォルダを、モード（丸ごと削除・親フォルダのみ残す・ファイルのみ削除）に合わせて消す。
        /// </summary>
        private void DeleteFolder(string folderPath, TransferMode mode)
        {
            switch (mode)
            {
                case TransferMode.DeleteAll:
                    DeleteFolderTree(folderPath);
                    break;
                case TransferMode.DeleteKeepParent:
                    DeleteAllFiles(folderPath);
                    DeleteSubFolders(folderPath);
                    break;
                case TransferMode.DeleteFilesOnly:
                    DeleteAllFiles(folderPath);
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
            string trimmed = (path ?? "").Trim().Trim('"');
            if (trimmed.Length == 0)
                throw new TransferException(_msg.Transfer.PathEmpty);
            return _fs.Path.GetFullPath(_config.GetFullPath(trimmed));
        }

        /// <summary>
        /// forbidden_paths.txt に書かれたパスと完全一致したら止める。
        /// </summary>
        private void CheckNotForbidden(string fullPath)
        {
            foreach (string forbidden in _config.ForbiddenPaths)
            {
                if (IsSamePath(fullPath, ResolvePath(forbidden)))
                    throw new TransferException(Messages.Format(_msg.Transfer.ForbiddenPath, fullPath));
            }
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
            string dir = _fs.Path.GetDirectoryName(filePath) ?? "";
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
            string parent = _fs.Path.GetDirectoryName(dirPath) ?? "";
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
