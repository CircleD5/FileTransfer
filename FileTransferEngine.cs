using System;
using System.Collections.Generic;
using System.IO;

namespace FileTransfer2
{
    public enum TransferAction { Unknown, Copy, Move, Delete }
    public enum TransferMode { Unknown, FolderAsIs, ExtractContents, DeleteAll, DeleteKeepParent, DeleteFilesOnly }
    public enum ProcessResult { Success, CompletedWithWarnings, Skipped_NotFound, Skipped_Conflict }

    public class FileTransferEngine
    {
        private readonly Action<string> _logger;
        private readonly AppConfig _config;

        public FileTransferEngine(Action<string> logger, AppConfig config)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public ProcessResult ProcessRow(TransferAction action, string src, string dest, TransferMode mode, bool addDate, int lineNo)
        {
            src = src.Trim('"');
            dest = dest.Trim('"');

            if (!File.Exists(src) && !Directory.Exists(src))
            {
                _logger(_config.GetMsg("Msg_SkipNotFound", lineNo));
                return ProcessResult.Skipped_NotFound;
            }

            bool isFolder = Directory.Exists(src);

            if (action == TransferAction.Delete)
                return ExecuteDelete(src, isFolder, mode, lineNo);
            else if (isFolder)
                return ExecuteFolderTransfer(action, src, dest, mode, addDate, lineNo);
            else
                return ExecuteFileTransfer(action, src, dest, addDate, lineNo);
        }

        private ProcessResult ExecuteFileTransfer(TransferAction action, string src, string dest, bool addDate, int lineNo)
        {
            string destFile = DetermineDestFilePath(src, dest, addDate);

            if (!addDate && File.Exists(destFile))
                _logger(_config.GetMsg("Msg_Overwrite", lineNo, Path.GetFileName(destFile)));

            try
            {
                if (action == TransferAction.Copy) File.Copy(src, destFile, true);
                else if (action == TransferAction.Move)
                {
                    if (File.Exists(destFile)) File.Delete(destFile);
                    File.Move(src, destFile);
                }
                return ProcessResult.Success;
            }
            catch (Exception ex)
            {
                _logger(_config.GetMsg("Msg_TransferFail", lineNo, Path.GetFileName(src), ex.Message));
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
                _logger(_config.GetMsg("Msg_FolderDate", lineNo, Path.GetFileName(targetDest)));
            }
            else
            {
                targetDest = (mode == TransferMode.ExtractContents) ? destFull : Path.Combine(destFull, srcFolderName);
            }

            if (targetDest.Equals(srcFull, StringComparison.OrdinalIgnoreCase) || targetDest.StartsWith(srcFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                _logger(_config.GetMsg("Msg_Conflict", lineNo));
                return ProcessResult.Skipped_Conflict;
            }

            if (!Directory.Exists(targetDest)) Directory.CreateDirectory(targetDest);

            // フリーズ回避用の安全な探索（フォルダ）
            foreach (string dirPath in SafeEnumerateDirectories(srcFull))
            {
                string relPath = dirPath.Substring(srcFull.Length).TrimStart('\\');
                try { Directory.CreateDirectory(Path.Combine(targetDest, relPath)); } catch { }
            }

            // フリーズ回避用の安全な探索（ファイル）
            foreach (string filePath in SafeEnumerateFiles(srcFull))
            {
                string relPath = filePath.Substring(srcFull.Length).TrimStart('\\');
                string targetFile = Path.Combine(targetDest, relPath);
                bool isFileNamingDateActive = (mode == TransferMode.ExtractContents) && addDate;

                if (!isFileNamingDateActive && File.Exists(targetFile))
                    _logger(_config.GetMsg("Msg_Overwrite", lineNo, Path.GetFileName(targetFile)));

                if (isFileNamingDateActive) targetFile = AppendDateToFileName(targetFile);

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
                    _logger(_config.GetMsg("Msg_TransferFail", lineNo, Path.GetFileName(filePath), ex.Message));
                    warningCount++;
                }
            }

            if (action == TransferAction.Move) warningCount += CleanupEmptyDirectories(srcFull);
            return warningCount > 0 ? ProcessResult.CompletedWithWarnings : ProcessResult.Success;
        }

        private ProcessResult ExecuteDelete(string src, bool isFolder, TransferMode mode, int lineNo)
        {
            int warningCount = 0;
            if (!isFolder)
            {
                try { ForceDeleteFile(src); }
                catch (Exception ex)
                {
                    _logger(_config.GetMsg("Msg_DeleteFail", lineNo, Path.GetFileName(src), ex.Message));
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
                    foreach (string file in SafeEnumerateFiles(src, false))
                    {
                        try { ForceDeleteFile(file); }
                        catch (Exception ex) { _logger(_config.GetMsg("Msg_DeleteFail", lineNo, Path.GetFileName(file), ex.Message)); warningCount++; }
                    }
                    foreach (string dir in SafeEnumerateDirectories(src, false))
                    {
                        warningCount += BestEffortDeleteDirectory(dir, lineNo);
                    }
                    break;
                case TransferMode.DeleteFilesOnly:
                    foreach (string file in SafeEnumerateFiles(src))
                    {
                        try { ForceDeleteFile(file); }
                        catch (Exception ex) { _logger(_config.GetMsg("Msg_DeleteFail", lineNo, Path.GetFileName(file), ex.Message)); warningCount++; }
                    }
                    break;
            }
            return warningCount > 0 ? ProcessResult.CompletedWithWarnings : ProcessResult.Success;
        }

        // --- フリーズ回避用 探索ロジック ---
        private IEnumerable<string> SafeEnumerateDirectories(string root, bool recursive = true)
        {
            var queue = new Queue<string>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                string[] dirs = null;
                try { dirs = Directory.GetDirectories(current); } catch { }
                if (dirs != null)
                {
                    foreach (var d in dirs)
                    {
                        if (current != root || recursive) yield return d;
                        if (recursive) queue.Enqueue(d);
                    }
                }
            }
        }

        private IEnumerable<string> SafeEnumerateFiles(string root, bool recursive = true)
        {
            var queue = new Queue<string>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                string[] files = null;
                try { files = Directory.GetFiles(current); } catch { }
                if (files != null)
                {
                    foreach (var f in files) yield return f;
                }
                if (recursive)
                {
                    string[] dirs = null;
                    try { dirs = Directory.GetDirectories(current); } catch { }
                    if (dirs != null)
                    {
                        foreach (var d in dirs) queue.Enqueue(d);
                    }
                }
            }
        }

        private void ForceDeleteFile(string filePath)
        {
            var attrs = File.GetAttributes(filePath);
            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
            File.Delete(filePath);
        }

        private int BestEffortDeleteDirectory(string targetDir, int lineNo)
        {
            int warnings = 0;
            foreach (string file in SafeEnumerateFiles(targetDir, false))
            {
                try { ForceDeleteFile(file); }
                catch (Exception ex) { _logger(_config.GetMsg("Msg_DeleteFail", lineNo, Path.GetFileName(file), ex.Message)); warnings++; }
            }
            foreach (string dir in SafeEnumerateDirectories(targetDir, false))
            {
                warnings += BestEffortDeleteDirectory(dir, lineNo);
            }
            try { Directory.Delete(targetDir, false); } catch { warnings++; }
            return warnings;
        }

        private int CleanupEmptyDirectories(string targetDir)
        {
            int warnings = 0;
            foreach (string dir in SafeEnumerateDirectories(targetDir, false))
            {
                warnings += CleanupEmptyDirectories(dir);
            }
            try { Directory.Delete(targetDir, false); } catch { warnings++; }
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
            return addDate ? AppendDateToFileName(targetPath) : targetPath;
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