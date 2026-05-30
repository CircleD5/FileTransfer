using System;
using System.IO;

namespace FileTransfer2
{
    public enum TransferAction
    {
        Unknown,
        Copy,
        Move,
        Delete
    }

    public enum TransferMode
    {
        Unknown,
        FolderAsIs,
        ExtractContents,
        DeleteAll,
        DeleteKeepParent,
        DeleteFilesOnly
    }

    public enum ProcessResult
    {
        Success,
        Skipped_NotFound,
        Skipped_Conflict
    }

    public class FileTransferEngine
    {
        private readonly Action<string> _logger;

        public FileTransferEngine(Action<string> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ProcessResult ProcessRow(TransferAction action, string src, string dest, TransferMode mode, bool addDate, int lineNo)
        {
            src = src.Trim('"');
            dest = dest.Trim('"');

            if (!File.Exists(src) && !Directory.Exists(src))
            {
                _logger($"[{lineNo}行目] スキップ: 転送元が存在しません (処理済または対象なし)。");
                return ProcessResult.Skipped_NotFound;
            }

            bool isFolder = Directory.Exists(src);

            switch (action)
            {
                case TransferAction.Delete:
                    ExecuteDelete(src, isFolder, mode, lineNo);
                    break;

                case var _ when isFolder:
                    return ExecuteFolderTransfer(action, src, dest, mode, addDate, lineNo);

                default:
                    ExecuteFileTransfer(action, src, dest, addDate, lineNo);
                    break;
            }

            return ProcessResult.Success;
        }

        private void ExecuteFileTransfer(TransferAction action, string src, string dest, bool addDate, int lineNo)
        {
            string destFile = DetermineDestFilePath(src, dest, addDate);

            if (!addDate && File.Exists(destFile))
            {
                _logger($"[{lineNo}行目] [上書き実行] 既存ファイルを上書きします: {destFile}");
            }

            switch (action)
            {
                case TransferAction.Copy:
                    File.Copy(src, destFile, true);
                    break;

                case TransferAction.Move:
                    if (File.Exists(destFile)) File.Delete(destFile);
                    File.Move(src, destFile);
                    break;
            }
        }

        private ProcessResult ExecuteFolderTransfer(TransferAction action, string src, string dest, TransferMode mode, bool addDate, int lineNo)
        {
            string srcFull = Path.GetFullPath(src).TrimEnd('\\');
            string destFull = Path.GetFullPath(dest).TrimEnd('\\');
            string srcFolderName = new DirectoryInfo(srcFull).Name;

            string targetDest = (mode == TransferMode.ExtractContents)
                ? destFull
                : Path.Combine(destFull, srcFolderName);

            if (targetDest.Equals(srcFull, StringComparison.OrdinalIgnoreCase) ||
                targetDest.StartsWith(srcFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                _logger($"[{lineNo}行目] エラー: 転送先パスが転送元と同一、または転送元内に含まれています。操作をスキップします。");
                return ProcessResult.Skipped_Conflict;
            }

            if (!Directory.Exists(targetDest)) Directory.CreateDirectory(targetDest);

            foreach (string dirPath in Directory.GetDirectories(srcFull, "*", SearchOption.AllDirectories))
            {
                string relPath = dirPath.Substring(srcFull.Length).TrimStart('\\');
                Directory.CreateDirectory(Path.Combine(targetDest, relPath));
            }

            foreach (string filePath in Directory.GetFiles(srcFull, "*", SearchOption.AllDirectories))
            {
                string relPath = filePath.Substring(srcFull.Length).TrimStart('\\');
                string targetFile = Path.Combine(targetDest, relPath);

                if (!addDate && File.Exists(targetFile))
                {
                    _logger($"[{lineNo}行目] [上書き実行] {Path.GetFileName(targetFile)}");
                }

                if (addDate) targetFile = AppendDateToFileName(targetFile);
                File.Copy(filePath, targetFile, true);
            }

            if (action == TransferAction.Move) Directory.Delete(src, true);

            return ProcessResult.Success;
        }

        private void ExecuteDelete(string src, bool isFolder, TransferMode mode, int lineNo)
        {
            if (!isFolder)
            {
                File.Delete(src);
                return;
            }

            switch (mode)
            {
                case TransferMode.DeleteAll:
                    Directory.Delete(src, true);
                    break;

                case TransferMode.DeleteKeepParent:
                    DirectoryInfo di = new DirectoryInfo(src);
                    foreach (FileInfo file in di.GetFiles()) file.Delete();
                    foreach (DirectoryInfo dir in di.GetDirectories()) dir.Delete(true);
                    break;

                case TransferMode.DeleteFilesOnly:
                    foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                    break;
            }
        }

        private string DetermineDestFilePath(string srcFile, string destPath, bool addDate)
        {
            string targetPath;
            if (Directory.Exists(destPath) || destPath.EndsWith("\\"))
            {
                if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);
                targetPath = Path.Combine(destPath, Path.GetFileName(srcFile));
            }
            else
            {
                string parentDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);
                targetPath = destPath;
            }

            if (addDate) targetPath = AppendDateToFileName(targetPath);
            return targetPath;
        }

        private string AppendDateToFileName(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(filePath);
            string ext = Path.GetExtension(filePath);
            string dateStr = DateTime.Now.ToString("yyyyMMddHHmmss");

            string newPath = Path.Combine(dir, $"{name}_{dateStr}{ext}");

            int counter = 1;
            while (File.Exists(newPath))
            {
                newPath = Path.Combine(dir, $"{name}_{dateStr}_{counter}{ext}");
                counter++;
            }
            return newPath;
        }
    }
}