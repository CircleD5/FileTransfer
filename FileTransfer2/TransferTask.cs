using System;
using System.Linq;

namespace FileTransfer2
{
    /// <summary>
    /// 処理1件分の内容。画面の1行、TSV ファイルの1行に対応する。
    /// </summary>
    public class TransferTask
    {
        // 画面のプルダウンに出す選択肢（並び順もこのとおり）
        public static readonly TransferAction[] Actions =
        {
            TransferAction.Copy, TransferAction.Move, TransferAction.Delete,
            TransferAction.Deploy, TransferAction.Backup, TransferAction.Restore,
        };
        public static readonly TransferMode[] TransferModes = { TransferMode.FolderAsIs, TransferMode.ExtractContents };
        public static readonly TransferMode[] DeleteModes = { TransferMode.DeleteAll, TransferMode.DeleteKeepParent, TransferMode.DeleteFilesOnly };

        /// <summary>
        /// 処理種別ごとに選べるモード。
        /// </summary>
        public static TransferMode[] ModesFor(TransferAction action)
        {
            switch (action)
            {
                case TransferAction.Delete: return DeleteModes;
                case TransferAction.Deploy: return new[] { TransferMode.DeployReplace };
                case TransferAction.Backup: return new[] { TransferMode.BackupCopy };
                case TransferAction.Restore: return new[] { TransferMode.RestoreReplace };
                default: return TransferModes;
            }
        }

        /// <summary>
        /// リリース用の処理（配置・退避・復元）かどうか。退避の場所を決まった名前で管理するので、日付付与は使わない。
        /// </summary>
        public static bool IsReleaseAction(TransferAction action)
        {
            return action == TransferAction.Deploy || action == TransferAction.Backup || action == TransferAction.Restore;
        }

        public TransferAction Action { get; set; }
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
        public TransferMode Mode { get; set; }
        public bool AddDate { get; set; }

        /// <summary>
        /// 入力内容に問題があればエラーの文言を、問題なければ null を返す。
        /// </summary>
        public string Validate(AppConfig config)
        {
            if (!Actions.Contains(Action)) return config.Msg.Input.ActionInvalid;
            if (string.IsNullOrWhiteSpace(Source)) return config.Msg.Input.SourceEmpty;
            if (Action != TransferAction.Delete && string.IsNullOrWhiteSpace(Destination)) return config.Msg.Input.DestEmpty;
            if (!ModesFor(Action).Contains(Mode)) return config.Msg.Input.ModeInvalid;
            if (IsReleaseAction(Action) && AddDate) return config.Msg.Input.DateNotAllowed;
            return null;
        }

        /// <summary>
        /// TSV の1行にする。形式：処理種別 TAB 転送元 TAB 転送先 TAB モード TAB 日付付与
        /// 処理種別とモードは enum の名前（Copy, FolderAsIs など）で保存する。
        /// </summary>
        public string ToTsvLine()
        {
            return string.Join("\t", Action, Source, Destination, Mode, AddDate);
        }

        public static TransferTask FromTsvLine(string line)
        {
            string[] cols = line.Split('\t');
            string Col(int index) => index < cols.Length ? cols[index] : "";

            return new TransferTask
            {
                Action = ParseEnum(Col(0), TransferAction.Unknown),
                Source = Col(1),
                Destination = Col(2),
                Mode = ParseEnum(Col(3), TransferMode.Unknown),
                AddDate = bool.TryParse(Col(4), out bool addDate) && addDate,
            };
        }

        /// <summary>
        /// enum の名前と完全に一致したときだけ変換する（"1" のような数字や表記ゆれは Unknown 扱い）。
        /// </summary>
        private static T ParseEnum<T>(string text, T unknown) where T : struct
        {
            return Enum.IsDefined(typeof(T), text) ? (T)Enum.Parse(typeof(T), text) : unknown;
        }
    }
}
