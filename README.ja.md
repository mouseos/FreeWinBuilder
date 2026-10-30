# FreeWinBuilder

[English](README.md) | 日本語

デスクトップを持たないWindowsのエディションに、デスクトップを加えた起動ISOを作成します。

- Microsoft Validation OSに、ReactOSまたはそのフォークのLiberNTのデスクトップ、スタートメニュー、タスクバー、設定ツールを組み込みます。Windowsのカーネルと中核DLLはそのまま使います。
- Azure LocalにWindows Serverのデスクトップを加えます（[Azure Local](#azure-local)を参照）。
- Hyper-V Server 2019にWindows Server 2019のデスクトップを加えます。

入力ファイルは変更しません。

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

Validation OSのイメージは英語のみで、IME（日本語入力など）は動きません。Validation OSには英語以外の表示言語もIMEのパッケージもないためです。これはValidation OS向けだけの制限で、Hyper-V Serverは元のISOの言語のまま使えます。

画面はWindowsが日本語なら日本語、それ以外は英語です。変更したい場合は、起動前に環境変数 `FREEWIN_LANGUAGE` を `ja` または `en` に設定してください。

## Hyper-V Server 2019

同じ言語のHyper-V Server ISOとWindows Server 2019 Desktop Experience ISOを用意します。動作確認済みビルドはそれぞれ17763.737と17763.1です。先に `scripts\Install-Dependencies.ps1` でQEMUとOSCDIMGを準備し、Windows Hypervisor Platformを有効にしてください。

## Azure Local

Windows Serverのデスクトップ（スタートメニュー、タスクバー、エクスプローラー、サーバーマネージャー）を備えたAzure LocalのインストールISOを作成します。Windows ServerのDesktop ExperienceのパッケージをDISMでAzure Localのイメージに追加します。エディションはAzure Local（Azure Stack HCI）のままです。

7-Zip、Windows ADKのDeployment ToolsとWinPEアドオンのほかに、次のファイルが必要です。

- Azure Local x64（ServerAzureStackHCICor）のISO、WIMまたはESD。動作確認済みビルド: 26100.1。
- 同じビルドのWindows Server 2025 DatacenterまたはStandard（Desktop Experience）のISO、WIMまたはESD。動作確認済みビルド: 26100.1。
- 同じビルドのWindows Server言語パック。Windows Server 2025またはAzure LocalのUUPファイルにある `Microsoft-Windows-Server-LanguagePack-Package-amd64-<言語>.esd` を使います。

表示言語は言語パックの言語になります。デスクトップの言語リソース、フォント、IMEはWindows Serverのイメージから取り込むので、Windows Serverは言語パックと同じ言語のものを使ってください。Azure Localの言語は問いません。違う言語の場合は、言語パックの言語を既定にします。英語と日本語で動作を確認しています。

アプリで対象OSを **Azure Local** にし、3つのファイルと保存先を選んで、管理者権限の確認を許可します。保存先に `freewin-…\azure-local-build\FreeWin-AzureLocal.iso` ができます。

このISOから起動するとディスクの一覧が表示され、`YES` の入力を求められます。入力するとディスク0を消去し、UEFIまたはBIOSに合わせてAzure Localをインストールして、通常の初回セットアップへ再起動します。累積更新は含まないので、セットアップ後に適用してください。

## ソースからのビルド

.NET 8 SDKとWindows x64用のMinGW-w64 GCCをインストールし、リポジトリのフォルダーで次を実行します。

```powershell
.\scripts\Build-Native.ps1 -GccPath 'C:\path\to\mingw64\bin\gcc.exe'
dotnet publish src/FreeWin.App/FreeWin.App.csproj -c Release -p:Platform=x64 -o dist
dotnet publish src/FreeWin.Worker/FreeWin.Worker.csproj -c Release -o dist/worker
```

不具合は[Issues](https://github.com/mouseos/FreeWinBuilder/issues)へ報告できます。第三者ライセンスは[LICENSES](LICENSES)にまとめています。
