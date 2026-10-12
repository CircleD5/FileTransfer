# FileTransfer2 開発者ガイド

コードを読む・直す・機能を足す人向けの説明です。使い方は [利用者ガイド](user-guide.md) を見てください。

## 1. 方針

- **シンプルさを優先する**。教材としても使うので、初心者が読んで分かることを大事にする。
- **本番アプリに近い形にする**。設定や文言はファイルに出し、プログラムを直さずに変えられるようにする。
- **設定に既定値を持たない**。ファイルや項目が欠けていたら、起動時に「設定エラー」で止める。
- **失敗したらその場で止める**。続けるか止めるかの判断は、呼び出し側（画面）でまとめて行う。
- **メソッドとプロパティには、private のものも含めて `/// <summary>` で説明を書く**。何をするかを1〜2行で書き、分かりにくいものには例を付ける。イベント処理には、どのボタンや操作のときに呼ばれるかを書く。
- **条件分岐は if / else を基本にする**。行数を短くするよりも、他の人が理解するまでの時間を短くすることを優先する。
  - 三項演算子（`条件 ? A : B`）は、それで簡潔になるとき（見た目の値を2つから選ぶだけ、など）にだけ使う。長くなるときは、条件・真・偽の3行に分ける。
  - `??` や `?.` は使わず、null のときの扱いは if で書く。null になりうる理由もコメントに書く（例：「ドライブ直下などで親フォルダがないとき」）。

```csharp
// よい例：決まりは if で書く
if (profileName == "")
{
    // 「未選択」は一覧を空にする
    LoadRows(new List<TransferTask>());
}
else
{
    LoadRows(TaskFile.Load(GetProfilePath(profileName)));
}

// よい例：見た目の値を2つから選ぶだけなら、三項演算子でよい
cell.Style.BackColor = enabled ? Color.White : Color.LightGray;

// よい例：長くなるときは3行に分ける
combo.DisplayStyle = enabled
    ? DataGridViewComboBoxDisplayStyle.DropDownButton
    : DataGridViewComboBoxDisplayStyle.Nothing;
```
- これまでの判断の経緯は、プロジェクト直下の `REVIEW.md` に記録してある。

## 2. フォルダ構成

```
FileTransfer2\                 アプリ本体（.NET Framework 4.8 / WinForms）
  Program.cs                   起動。設定の読み込みと、作業フォルダの作成
  AppConfig.cs                 設定ファイルの読み込み（ConfigException）
  SettingsSection.cs           settings.config の項目の宣言（.NET 標準の設定セクション）
  Messages.cs                  文言（messages.json）の型
  Form1.cs / Form1.Designer.cs 画面
  FileTransferEngine.cs        1行分のファイル操作（コピー・移動・削除・配置・退避・復元）
  TransferTask.cs              1行分のデータ、入力チェック、TSV との変換
  TaskFile.cs                  TSV ファイルの読み書き
  RestorePlan.cs               復元用の計画を作る
  LogFiles.cs                  古いログの削除
  settings.config / messages.json / forbidden_paths.txt / deploy_keep.txt
                               設定ファイル（ビルドすると exe の横にコピーされる）
UT\                            テスト（xUnit）
  Helpers\AppDriver.cs         画面操作をまとめたクラス（UI テスト用）
  Helpers\TempConfigFiles.cs   テスト用の設定ファイル一式を一時フォルダに作る
docs\                          ドキュメント
REVIEW.md                      レビューの指摘と判断、変更の記録
```

## 3. クラスの役割と関係

```
Program ──読み込む──▶ AppConfig ──持つ──▶ Messages（文言）
   │
   └─作る─▶ Form1（画面）
              ├─ 一覧の行 ⇄ TransferTask（ReadTask / LoadRows）
              ├─ プロファイル ⇄ TaskFile（TSV）
              ├─「復元用を作成」─▶ RestorePlan（保存済みのプロファイルから復元用の計画を作る）
              └─ 実行 ─▶ FileTransferEngine.ProcessRow（1行ずつ）
```

| クラス | 役割 | 画面に依存するか |
|---|---|---|
| `FileTransferEngine` | ファイル操作。失敗したら例外を投げる | しない（`IFileSystem` を受け取るので、テストで偽物に差し替えられる） |
| `TransferTask` | 1行分のデータ。入力チェック（`Validate`）と TSV 変換 | しない |
| `TaskFile` / `RestorePlan` / `LogFiles` | ファイルの読み書き、計画の生成、ログの整理 | しない |
| `AppConfig` / `Messages` | 設定と文言 | しない |
| `Form1` | 画面の操作と、実行の流れの管理 | する |

画面に依存しない部分は、ユニットテストで直接確かめられる。

## 4. 処理の流れ

### 起動

```
Program.Main
  ├ AppConfig.Load        messages.json → settings.config → forbidden_paths.txt → deploy_keep.txt
  │   （文言を先に読むのは、設定ファイルの誤りを知らせる文言も messages.json にあるため）
  ├ AppConfig.CreateFolders
  │   （どこかで失敗したら ConfigException →「設定エラー」を表示して終了）
  ├ log4net の設定      settings.config の <log4net> を読む。出力先は LogDir
  └ Form1
      ├ 列・文言の設定
      ├ 自動処理の準備、古いログの削除
      ├ プロファイルの一覧を作る
      └ DefaultProfile を読み込む
```

### 実行（一括・個別・自動処理で共通）

```
btnExecuteAll_Click / ExecuteRowAsync / RunPlanFileAsync
  └ RunPlanAsync(計画, 1件目の行番号, 状態を受け取る処理)
      ├ 開始ログ・実行者
      └ 1件ずつ RunTaskAsync
          ├ TransferTask.Validate     入力エラーなら false
          └ Task.Run(engine.ProcessRow)  例外なら false
          → false なら残りを「未実行」にして止める
```

違いは「行の状態を画面に出すかどうか」だけで、`RunPlanAsync` に渡す処理（`setStatus`）で切り替えている。

### プロファイルの選択

```
cmbProfiles_SelectedIndexChanged
  ├ プログラムが選択を変えた（_isSelectingByCode）→ 何もしない
  ├ 未保存の変更あり → 選択を元に戻し、確認の帯（pnlUnsaved）を出す
  └ それ以外 → LoadProfile
```

`SelectedIndexChanged` は、プログラムが `SelectedIndex` を変えたときにも起きる。そのため、コードから選択を変えるときは、必ず `SelectProfileInCombo` を通す（`_isSelectingByCode` を立てる）。

## 5. 文言と設定のルール

- **画面やログの文言はすべて `messages.json`** に置き、`_msg.グループ.名前` で参照する（打ち間違いはビルドエラーになる）。
  - 設定エラーの文言も `messages.json` の `Config` に置く。
  - 例外：`messages.json` 自体が読めないときは、文言が使えないので、.NET や Newtonsoft.Json のエラー文をそのまま出す（コードに日本語の文言は書かない）。
- `{0}` などの埋め込みは `Messages.Format` を使う（番号を間違えても落ちない）。
- `messages.json` は、項目の抜け・知らないキー・書式の誤りがあると起動しない。処理種別やモードごとの文言（`Actions`、`Modes`、`SourceHints`、`DestHints`）は、enum の値がすべてそろっているかも確認する。
- `settings.config` は .NET 標準の設定ファイル（`System.Configuration`）で読む。項目は `SettingsSection` に属性で宣言し、**チェックは .NET に任せる**（自前のチェック処理は書かない）。
  - すべて必須（`IsRequired = true`）。値を空にしてよい項目（`DefaultProfile`）も、項目そのものは必須（書き忘れと「使わない」を区別するため）。
  - 誤りは最初の1つだけが報告される（.NET の仕様）。
  - `IntegerValidator` などのチェックは既定値にもかかるため、チェックを付けた項目には、チェックを通る形だけの `DefaultValue` を書く（必須なので実際には使われない）。
  - 値の誤りは、プロパティを読んだときに例外になることがある。そのため `AppConfig.LoadSettings` では、値の取り出しまでを try の中で行う。
- ログファイルは log4net で書く。書式や出力のしかたは `settings.config` の `<log4net>`、出力先のフォルダは `LogDir`（`%property{LogDir}` で渡す）。
  - `deploy_history.log` は log4net を使わない。配置の記録（データ）なので、`FileTransferEngine` が `IFileSystem` で書く（ユニットテストで偽物のファイルシステムに差し替えられるように）。
  - 古いログの削除（`LogFiles.DeleteOld`）は自前。log4net の日付ごとのファイルは、日数での自動削除ができないため。

## 6. 機能を足すとき

### 処理種別を1つ足す場合のチェックリスト

1. `FileTransferEngine.cs` の `TransferAction`（必要なら `TransferMode`）に値を足す
2. `TransferTask.Actions`（プルダウンの並び）と `ModesFor` に足す。入力チェックが必要なら `Validate` も
3. `messages.json` の `Actions`・`Modes`・`SourceHints`・`DestHints` に文言を足す（足し忘れると起動時にエラーで分かる）
4. `FileTransferEngine.ProcessRow` に処理を足す
5. 画面で使わないセルがあれば、`Form1.UpdateRowState` で灰色にする
6. 元に戻せる処理なら `RestorePlan` に、戻せないなら何もしない（混ざると「復元用を作成」で作れない）
7. テストを書く（ファイル操作は `MockFileSystem`、画面は `AppDriver`）

### 設定項目を1つ足す場合

1. `SettingsSection` にプロパティを足す（`[ConfigurationProperty(..., IsRequired = true)]` と、必要ならチェックの属性）
2. `AppConfig` にプロパティを足し、`LoadSettings` で `SettingsSection` から写す
3. `FileTransfer2\settings.config` の `<fileTransfer>` に足し、下のコメントに説明を書く
4. `UT\Helpers\TempConfigFiles.cs` の `ValidSettings` に足す（足さないとテストが設定エラーになる）

## 7. ビルドとテスト

```bash
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" FileTransfer2.sln -t:Build -p:Configuration=Debug
```

```bash
./packages/xunit.runner.console.2.9.3/tools/net472/xunit.console.exe UT/bin/Debug/UT.dll -noshadow
```

- Visual Studio のテスト エクスプローラーからも実行できる。
- **UI テストはキーボードもマウスも使わない**ので、実行中もほかの作業はできる。ただし、**テストが開くアプリの画面には触らない**こと（クリックすると編集中のセルが確定されるなど、テストの操作と食い違って失敗することがある）。
- テストは一時フォルダで動き、終わると消える（本番の Profiles や AutoRun は使わない）。

### カバレッジ（テストでどこまで通ったか）

プロジェクト直下の `RunCoverage.bat` をダブルクリックする。ビルド → テストを実行しながら計測（OpenCover）→ HTML のレポート作成（ReportGenerator）の順に行い、終わるとブラウザで `CoverageReport\index.html` が開く。

- `RunCoverage.bat /noopen` とすると、ブラウザを開かない。
- テストが失敗すると、メッセージを出して終了コード 1 で止まる（レポートは作らない）。
- UI テストで起動したアプリの中で動いたコード（画面の処理）も、`-mergebyhash` で合算して計測している。
- Visual Studio が自動で作る `Properties`（Resources / Settings。使っていない）は計測しない。
- bat は **UTF-8（BOM なし）・CRLF** で保存する。BOM を付けたり、最初の `chcp 65001` を消したりすると、日本語の Windows で表示が文字化けする。
- `coverage.xml` と `CoverageReport\` は git の管理外（`.gitignore`）。

| テストファイル | 内容 |
|---|---|
| `FileTransferEngineTests` / `DeployTests` / `BackupRestoreTests` | ファイル操作（`MockFileSystem` で、実際のファイルは触らない） |
| `TransferTaskTests` / `RestorePlanTests` / `AppConfigTests` / `LogFilesTests` | データ・計画・設定・ログ |
| `UiTests` / `E2ETests` / `StartupTests` / `StopTests` | アプリを起動して画面から操作する（`StopTests` は「中止」と進み具合） |

## 8. UI テスト（AppDriver）の書き方とハマりどころ

テストには「何を確かめたいか」だけを書き、画面の操作は `AppDriver` に任せる。

```csharp
_app.SetRow(0, TransferAction.Copy, src, WorkPath(@"out\"));
_app.ExecuteAll();
Assert.Equal(_app.Msg.Status.Done, _app.GetStatus(0));
```

`AppDriver` に操作を足すときは、次の点に気をつける（実際にハマった点）。

| 症状・場面 | 原因 | 対処 |
|---|---|---|
| ボタンを押した後、10秒止まって `COMException: 操作がタイムアウト` | FlaUI の `Invoke()` は、押した結果ダイアログが開くと、閉じるまで戻らない | `AppDriver` の `Invoke`（ウィンドウを持つボタンには `BM_CLICK` を投げるだけ）を使う |
| 一覧の新しい行に `SetValue` しても、行が確定しない | WinForms の DataGridView は、値を入れただけでは新しい行として確定しない | セルの既定の動作（`DoDefaultAction`）で編集状態にしてから、出てきた入力欄に値を入れる（`SetText`） |
| コンボの項目が見つからない、選択中の項目が取れない | .NET Framework の WinForms のコンボは、開いている間しか項目が見えず、選択中の項目は Value でしか取れない | 「開く → 項目を選ぶ → 閉じる」を自分で行う（`LoadProfile`） |
| 自分で組み立てた `WM_COMMAND` が無視される | WinForms はコントロール ID にハンドルの値を使い、16ビットに収まらない。そのまま合成すると上位16ビット（通知の種類）を壊す | ID を `& 0xFFFF` で切り詰める（`SelectComboItemWithNotification` のコメント参照。今は学習用で未使用） |
| ツールチップ（`ToolTipText`）を確かめたい | UI Automation から読めない | 自動テストにせず、画面で目視確認する |
| 行が多いと、行数が1つ多く数えられ、列の位置もずれる | 一覧が画面に入りきらないとスクロールバーが出て、それも一覧の子要素として先頭に入ってくる | スクロールバーを除いてから行を数える（`GetGridRows`）。なお、画面の外の行も要素としては全部あるので、スクロールしなくても読み書き・ボタン操作ができる（100行で確認済み） |
| ログが長くなると「実行が終わりませんでした」で失敗する | 画面のログ欄（TextBox）を UI Automation で読むと、4,096文字で切れる | ログはログファイルから読む（`AppDriver.Log`）。画面のログ欄を確かめたいときだけ `ScreenLog` を使う |
| ラベルが「画面に見つかりません」になる | `AutoSize` のラベルは、文字が空のとき幅が 0 になり、UI Automation から見えなくなる | 文字が空になることがあるラベルは、`AutoSize` を外して大きさを決めておく（`lblProgress`） |
| 「2回続けて押しても1回だけ実行される」のテストが、2回実行されて失敗する | 実行がすぐ終わると、2回目のクリックが届く前に1回目が終わり、正しく2回目が実行される | 行の間の待ち時間（`RowDelayMs`）を長くして、2回目のクリックが必ず実行中に届くようにする（`StopTests`） |
| テスト中にたまに失敗する | テストが開いたアプリの画面を人が触った（クリックで編集中のセルが確定されるなど） | テスト中は、開いたアプリの画面に触らない |

画面が UI Automation からどう見えているか分からないときは、`DumpTree` で書き出す。

```csharp
File.WriteAllText(@"C:\temp\tree.txt", _app.DumpTree());               // 画面全体
File.WriteAllText(@"C:\temp\combo.txt", _app.DumpTree("cmbProfiles")); // 一部だけ
```

画面を触りながら調べるなら、Windows SDK の Inspect.exe（`C:\Program Files (x86)\Windows Kits\10\bin\<版>\x64\inspect.exe`）も使える。
