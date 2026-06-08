using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileTransfer2
{
    public class AppConfig
    {
        public int AutoRunIntervalMs { get; private set; } = 30000;
        public int RowDelayMs { get; private set; } = 200;
        public string AutoRunPlanDir { get; private set; } = @"AutoRun\Plan";
        public string AutoRunDoneDir { get; private set; } = @"AutoRun\Done";
        public string ProfilesDir { get; private set; } = @"Profiles";

        private Dictionary<string, string> _messages = new Dictionary<string, string>();

        public void Load(string filePath)
        {
            // デフォルトメッセージの定義
            _messages["Msg_SkipNotFound"] = "[{0}行目] スキップ: 転送元が存在しません。";
            _messages["Msg_Conflict"] = "[{0}行目] エラー: 転送先が転送元と同一か内包しています。";
            _messages["Msg_Overwrite"] = "[{0}行目] [上書き実行] 既存ファイルを上書きします: {1}";
            _messages["Msg_FolderDate"] = "[{0}行目] フォルダ名に日付を付与しました: {1}";
            _messages["Msg_TransferFail"] = "[{0}行目] [スキップ] 転送失敗: {1} - {2}";
            _messages["Msg_DeleteFail"] = "[{0}行目] [スキップ] 削除失敗: {1} - {2}";

            _messages["Msg_SysAutoRunReady"] = "システム: AutoRunの待機準備が完了しました。(監視はOFFです)";
            _messages["Msg_SysAutoRunStart"] = "システム: AutoRun/Plan フォルダの監視を開始しました。({0}ミリ秒間隔)";
            _messages["Msg_SysAutoRunStop"] = "システム: AutoRun/Plan フォルダの監視を停止しました。";
            _messages["Msg_AutoRunSkipLock"] = "自動実行: {0} は書き込み中のためスキップします。";
            _messages["Msg_AutoRunDetect"] = "自動実行: {0} の設定を検出しました。実行を開始します。";
            _messages["Msg_AutoRunDone"] = "自動実行: 完了した設定ファイルを Done フォルダへ移動しました。";
            _messages["Msg_AutoRunError"] = "自動実行エラー: {0}";

            _messages["Msg_ExecAllStart"] = "=== 一括処理を開始します ===";
            _messages["Msg_ExecAllEnd"] = "=== 一括処理が終了しました ===";
            _messages["Msg_RowStart"] = "[{0}行目] {1}開始: {2}";
            _messages["Msg_RowSuccess"] = "[{0}行目] 正常完了";
            _messages["Msg_RowWarning"] = "[{0}行目] 完了しましたが、一部のファイルがスキップされました。";
            _messages["Msg_RowFatal"] = "[{0}行目] 致命的なエラー: {1}";
            _messages["Msg_RowAbort"] = "安全のため、後続の処理をすべて中断しました。";
            _messages["Msg_InputError"] = "[{0}行目] 入力エラー: 処理種別と転送元は必須入力です。";
            _messages["Msg_UnsavedWarn"] = "変更が保存されていません。破棄して別のプロファイルを読み込みますか？";
            _messages["Msg_OverwriteConfirm"] = "プロファイル '{0}' を上書き保存しますか？";
            _messages["Msg_SaveSuccess"] = "設定を保存しました: {0}";
            _messages["Msg_SaveError"] = "保存に失敗しました: {0}";

            if (!File.Exists(filePath))
            {
                SaveDefaults(filePath);
            }
            else
            {
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string key = line.Substring(0, eqIdx).Trim();
                        string val = line.Substring(eqIdx + 1).Trim();

                        if (key == "AutoRunIntervalMs" && int.TryParse(val, out int ival)) AutoRunIntervalMs = ival;
                        else if (key == "RowDelayMs" && int.TryParse(val, out int rval)) RowDelayMs = rval;
                        else if (key == "AutoRunPlanDir") AutoRunPlanDir = val;
                        else if (key == "AutoRunDoneDir") AutoRunDoneDir = val;
                        else if (key == "ProfilesDir") ProfilesDir = val;
                        else if (key.StartsWith("Msg_")) _messages[key] = val.Replace("\\n", "\n");
                    }
                }
            }
        }

        private void SaveDefaults(string filePath)
        {
            using (StreamWriter sw = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                sw.WriteLine("# --- 動作設定 ---");
                sw.WriteLine($"AutoRunIntervalMs={AutoRunIntervalMs}");
                sw.WriteLine($"RowDelayMs={RowDelayMs}");
                sw.WriteLine($"AutoRunPlanDir={AutoRunPlanDir}");
                sw.WriteLine($"AutoRunDoneDir={AutoRunDoneDir}");
                sw.WriteLine($"ProfilesDir={ProfilesDir}");
                sw.WriteLine();
                sw.WriteLine("# --- ログ・UIメッセージ設定 ---");
                foreach (var kvp in _messages)
                {
                    sw.WriteLine($"{kvp.Key}={kvp.Value.Replace("\n", "\\n")}");
                }
            }
        }

        public string GetMsg(string key, params object[] args)
        {
            if (_messages.TryGetValue(key, out string tmpl))
            {
                try { return string.Format(tmpl, args); } catch { return tmpl; }
            }
            return key;
        }

        public string GetFullPath(string relativeOrAbsolutePath)
        {
            if (Path.IsPathRooted(relativeOrAbsolutePath)) return relativeOrAbsolutePath;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeOrAbsolutePath);
        }
    }
}