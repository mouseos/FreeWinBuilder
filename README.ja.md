# FreeWinBuilder

[English](README.md) | 日本語

Microsoft Validation OSに、ReactOSまたはそのフォークのLiberNTのデスクトップ、スタートメニュー、タスクバー、設定ツールを組み込んだ起動ISOを作成します。Windowsのカーネルと中核DLLはそのまま使い、入力ISOは変更しません。

## 必要なもの

- Windows 10/11 x64、管理者権限、作成に必要な空き容量。
- 既定の場所にインストールした[7-Zip](https://www.7-zip.org/)。
- 既定の場所にインストールした[Windows ADKのDeployment ToolsとWinPEアドオン](https://learn.microsoft.com/ja-jp/windows-hardware/get-started/adk-install)。
- [Microsoft Validation OS x64 ISO](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/validation-os-overview)。動作確認済みビルド: 26100.9549。
- 次のいずれかのx64ブートCD。
  - [ビルド配布ページ](https://reactos.org/getbuilds/)のReactOS。動作確認済みビルド: 0.4.17-dev-1268-gbba112f（x64 MSVC Debug）。
  - [リリースページ](https://github.com/LiberNT/LiberNT/releases)のLiberNT（`bootcd-iso-…-amd64-….zip` を展開して `bootcd.iso` を使用）。動作確認済みビルド: nt10.0-043-2555（Clang Debug）。

どちらかのISOに必要な部品がない場合は作成を中止します。

## 使い方

1. 配布パッケージを展開し、`dist\FreeWinBuilder.exe` を開きます。
2. 対象OSを **Microsoft Validation OS** にし、2つのISOと保存先を選んで作成を開始します。
3. 管理者権限の確認を許可します。進捗と処理ログはアプリに表示されます。
4. 保存先の `freewin-…\validation-build\iso\FreeWin-ValidationOS.iso` から起動します。

RAMからデスクトップが起動します。再起動後も残したいファイルは、別の書き込み可能なドライブへ保存してください。

画面はWindowsが日本語なら日本語、それ以外は英語です。変更したい場合は、起動前に環境変数 `FREEWIN_LANGUAGE` を `ja` または `en` に設定してください。

## Hyper-V Server 2019

同じ言語のHyper-V Server ISOとWindows Server 2019 Desktop Experience ISOを用意します。動作確認済みビルドはそれぞれ17763.737と17763.1です。先に `scripts\Install-Dependencies.ps1` でQEMUとOSCDIMGを準備し、Windows Hypervisor Platformを有効にしてください。

## ソースからのビルド

.NET 8 SDKとWindows x64用のMinGW-w64 GCCをインストールし、リポジトリのフォルダーで次を実行します。

```powershell
.\scripts\Build-Native.ps1 -GccPath 'C:\path\to\mingw64\bin\gcc.exe'
dotnet publish src/FreeWin.App/FreeWin.App.csproj -c Release -p:Platform=x64 -o dist
dotnet publish src/FreeWin.Worker/FreeWin.Worker.csproj -c Release -o dist/worker
```

不具合は[Issues](https://github.com/mouseos/FreeWinBuilder/issues)へ報告できます。第三者ライセンスは[LICENSES](LICENSES)にまとめています。
