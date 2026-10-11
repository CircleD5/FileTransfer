using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FileTransfer2
{
    /// <summary>
    /// 処理内容の TSV ファイル（プロファイル・自動実行の計画ファイル）を読み書きする。
    /// </summary>
    public static class TaskFile
    {
        public static List<TransferTask> Load(string filePath)
        {
            return File.ReadAllLines(filePath, Encoding.UTF8)
                       .Where(line => !string.IsNullOrWhiteSpace(line))
                       .Select(TransferTask.FromTsvLine)
                       .ToList();
        }

        public static void Save(string filePath, IEnumerable<TransferTask> tasks)
        {
            File.WriteAllLines(filePath, tasks.Select(task => task.ToTsvLine()), Encoding.UTF8);
        }
    }
}
