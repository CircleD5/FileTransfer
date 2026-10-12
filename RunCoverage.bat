@echo off
chcp 65001 > nul
rem ============================================================
rem テストを実行しながら、コードのカバレッジ（テストでどこまで通ったか）を計測し、HTML のレポートを作る。
rem
rem   使い方：このファイルをダブルクリックする（終わるとブラウザでレポートが開く）
rem           RunCoverage.bat /noopen とすると、ブラウザを開かない
rem
rem   ・このファイルは UTF-8（BOM なし）で保存する。日本語を正しく表示するため、最初に chcp 65001 にしている
rem   ・どこから実行しても動くよう、このファイルのあるフォルダに移ってから実行する
rem   ・道具のフォルダ名には版番号が入っている。packages を更新したら、下の set の行も合わせて直す
rem ============================================================
setlocal
cd /d "%~dp0"

set OPENCOVER=packages\OpenCover.4.7.1221\tools\OpenCover.Console.exe
set XUNIT=packages\xunit.runner.console.2.9.3\tools\net472\xunit.console.exe
set REPORTGEN=packages\ReportGenerator.5.3.11\tools\net47\ReportGenerator.exe
set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe

echo === カバレッジの計測を開始します ===

rem 【1】最新のソースでビルドする（古い DLL のまま計測しないように）
echo [1/3] ビルド中...
set MSBUILD=
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD (
    echo MSBuild が見つかりません。Visual Studio がインストールされているか確認してください。
    exit /b 1
)
"%MSBUILD%" FileTransfer2.sln -t:Build -p:Configuration=Debug -v:q -nologo
if errorlevel 1 (
    echo ビルドに失敗しました。上の出力を確認してください。
    exit /b 1
)

rem 【2】テストを実行しながらカバレッジを計測する
rem   -filter           : FileTransfer2 を計測する。テスト自身（UT）と、Visual Studio が自動で作る
rem                       Properties（Resources。このアプリでは使っていない）は計測しない
rem   -mergebyhash      : UI テストで別に起動したアプリも、同じプログラムとして合算する
rem   -returntargetcode : テストが失敗したら、このスクリプトも失敗として止める
echo [2/3] テストの実行とカバレッジの計測中...（UI テストのため、アプリの画面が何度か開きます）
"%OPENCOVER%" -register:user -target:"%XUNIT%" -targetargs:"UT\bin\Debug\UT.dll -noshadow" -filter:"+[FileTransfer2*]* -[UT*]* -[FileTransfer2*]FileTransfer2.Properties.*" -mergebyhash -returntargetcode -output:coverage.xml
if errorlevel 1 (
    echo テストが失敗したか、計測できませんでした。上の出力を確認してください。
    exit /b 1
)

rem 【3】HTML のレポートと、画面に出す集計を作る
echo [3/3] レポートを作成中...
"%REPORTGEN%" -reports:coverage.xml -targetdir:CoverageReport -reporttypes:Html;TextSummary -verbosity:Warning
if errorlevel 1 (
    echo レポートを作れませんでした。上の出力を確認してください。
    exit /b 1
)
type CoverageReport\Summary.txt

echo === 完了しました ===
if /i not "%~1"=="/noopen" start "" CoverageReport\index.html
exit /b 0
