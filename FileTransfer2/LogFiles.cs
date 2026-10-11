using System;
using System.Globalization;
using System.IO;

namespace FileTransfer2
{
    /// <summary>
    /// 日付ごとのログファイル（yyyyMMdd.log）の整理。
    /// </summary>
    public static class LogFiles
    {
        /// <summary>
        /// 保存期間を過ぎた日付のログファイルを消し、消した数を返す。
        /// 名前が「yyyyMMdd.log」のファイルだけが対象で、deploy_history.log などほかのファイルは消さない。
        /// </summary>
        /// <param name="retentionDays">何日分残すか。0 なら何も消さない。</param>
        public static int DeleteOld(string logDir, int retentionDays, DateTime today)
        {
            if (retentionDays <= 0 || !Directory.Exists(logDir)) return 0;

            DateTime oldestToKeep = today.Date.AddDays(-(retentionDays - 1)); // 今日を含めて retentionDays 日分残す
            int deleted = 0;
            foreach (string file in Directory.GetFiles(logDir, "*.log"))
            {
                bool isDailyLog = DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyyMMdd",
                                                         CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime logDate);
                if (isDailyLog && logDate < oldestToKeep)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            return deleted;
        }
    }
}
