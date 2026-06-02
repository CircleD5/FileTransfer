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
        CompletedWithWarnings,
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

            if (action == TransferAction.Delete)
            {
                return ExecuteDelete(src, isFolder, mode, lineNo);
            }
            else if (isFolder)
            {
                return ExecuteFolderTransfer(action, src, dest, mode, addDate, lineNo);
            }
            else
            {
                return ExecuteFileTransfer(action, src, dest, addDate, lineNo);
            }
        }

        private ProcessResult ExecuteFileTransfer(TransferAction action, string src, string dest, bool addDate, int lineNo)
        {
            string destFile = DetermineDestFilePath(src, dest, addDate);

            if (!addDate && File.Exists(destFile))
            {
                _logger($"[{lineNo}行目] [上書き実行] 既存ファイルを上書きします: {destFile}");
            }

            try
            {
                if (action == TransferAction.Copy)
                {
                    File.Copy(src, destFile, true);
                }
                else if (action == TransferAction.Move)
                {
                    if (File.Exists(destFile)) File.Delete(destFile);
                    File.Move(src, destFile);
                }
                return ProcessResult.Success;
            }
            catch (Exception ex)
            {
                _logger($"[{lineNo}行目] [スキップ] 転送失敗: {Path.GetFileName(src)} - {ex.Message}");
                return ProcessResult.CompletedWithWarnings;
            }
        }

        private ProcessResult ExecuteFolderTransfer(TransferAction action, string src, string dest, TransferMode mode, bool addDate, int lineNo)
        {
            int warningCount = 0;
            string srcFull = Path.GetFullPath(src).TrimEnd('\\');
            string destFull = Path.GetFullPath(dest).TrimEnd('\\');
            string srcFolderName = new DirectoryInfo(srcFull).Name;

            string targetDest;

            if (mode == TransferMode.FolderAsIs && addDate)
            {
                targetDest = AppendDateToFolderName(Path.Combine(destFull, srcFolderName));
                _logger($"[{lineNo}行目] フォルダ名に日付を付与しました: {Path.GetFileName(targetDest)}");
            }
            else
            {
                targetDest = (mode == TransferMode.ExtractContents)
                    ? destFull
                    : Path.Combine(destFull, srcFolderName);
            }

            if (targetDest.Equals(srcFull, StringComparison.OrdinalIgnoreCase) ||
                targetDest.StartsWith(srcFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                _logger($"[{lineNo}行目] エラー: 転送先が転送元と同一、または内包しています。操作をスキップします。");
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
                bool isFileNamingDateActive = (mode == TransferMode.ExtractContents) && addDate;

                if (!isFileNamingDateActive && File.Exists(targetFile))
                {
                    _logger($"[{lineNo}行目] [上書き実行] {Path.GetFileName(targetFile)}");
                }

                if (isFileNamingDateActive)
                {
                    targetFile = AppendDateToFileName(targetFile);
                }

                try
                {
                    if (action == TransferAction.Move)
                    {
                        if (File.Exists(targetFile)) File.Delete(targetFile);
                        File.Move(filePath, targetFile);
                    }
                    else
                    {
                        File.Copy(filePath, targetFile, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger($"[{lineNo}行目] [スキップ] 転送失敗: {Path.GetFileName(filePath)} - {ex.Message}");
                    warningCount++;
                }
            }

            if (action == TransferAction.Move)
            {
                warningCount += CleanupEmptyDirectories(srcFull);
            }

            return warningCount > 0 ? ProcessResult.CompletedWithWarnings : ProcessResult.Success;
        }

        private ProcessResult ExecuteDelete(string src, bool isFolder, TransferMode mode, int lineNo)
        {
            int warningCount = 0;

            if (!isFolder)
            {
                try
                {
                    ForceDeleteFile(src);
                }
                catch (Exception ex)
                {
                    _logger($"[{lineNo}行目] [スキップ] 削除失敗: {Path.GetFileName(src)} - {ex.Message}");
                    return ProcessResult.CompletedWithWarnings;
                }
                return ProcessResult.Success;
            }

            switch (mode)
            {
                case TransferMode.DeleteAll:
                    warningCount += BestEffortDeleteDirectory(src, lineNo);
                    break;

                case TransferMode.DeleteKeepParent:
                    DirectoryInfo di = new DirectoryInfo(src);
                    foreach (FileInfo file in di.GetFiles())
                    {
                        try { ForceDeleteFile(file.FullName); }
                        catch (Exception ex) { _logger($"[{lineNo}行目] [スキップ] 削除失敗: {file.Name} - {ex.Message}"); warningCount++; }
                    }
                    foreach (DirectoryInfo dir in di.GetDirectories())
                    {
                        warningCount += BestEffortDeleteDirectory(dir.FullName, lineNo);
                    }
                    break;

                case TransferMode.DeleteFilesOnly:
                    foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        try { ForceDeleteFile(file); }
                        catch (Exception ex) { _logger($"[{lineNo}行目] [スキップ] 削除失敗: {Path.GetFileName(file)} - {ex.Message}"); warningCount++; }
                    }
                    break;
            }

            return warningCount > 0 ? ProcessResult.CompletedWithWarnings : ProcessResult.Success;
        }

        private void ForceDeleteFile(string filePath)
        {
            var attrs = File.GetAttributes(filePath);
            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
            }
            File.Delete(filePath);
        }

        private int BestEffortDeleteDirectory(string targetDir, int lineNo)
        {
            int warnings = 0;

            foreach (string file in Directory.GetFiles(targetDir))
            {
                try { ForceDeleteFile(file); }
                catch (Exception ex) { _logger($"[{lineNo}行目] [スキップ] 削除失敗: {Path.GetFileName(file)} - {ex.Message}"); warnings++; }
            }

            foreach (string dir in Directory.GetDirectories(targetDir))
            {
                warnings += BestEffortDeleteDirectory(dir, lineNo);
            }

            try { Directory.Delete(targetDir, false); }
            catch (Exception) { warnings++; }

            return warnings;
        }

        private int CleanupEmptyDirectories(string targetDir)
        {
            int warnings = 0;
            foreach (string dir in Directory.GetDirectories(targetDir))
            {
                warnings += CleanupEmptyDirectories(dir);
            }
            try { Directory.Delete(targetDir, false); }
            catch (Exception) { warnings++; }

            return warnings;
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

        private string AppendDateToFolderName(string dirPath)
        {
            string parent = Path.GetDirectoryName(dirPath) ?? "";
            string name = Path.GetFileName(dirPath);
            string dateStr = DateTime.Now.ToString("yyyyMMddHHmmss");

            string newPath = Path.Combine(parent, $"{name}_{dateStr}");
            int counter = 1;
            while (Directory.Exists(newPath))
            {
                newPath = Path.Combine(parent, $"{name}_{dateStr}_{counter}");
                counter++;
            }
            return newPath;
        }
    }
}