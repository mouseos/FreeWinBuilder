# FreeWinBuilder

[日本語](#日本語)

Create a bootable Microsoft Validation OS ISO with the ReactOS desktop, Start menu, taskbar and settings tools. Windows keeps its own kernel and core DLLs. The source ISOs are unchanged.

## Requirements

- Windows 10/11 x64, administrator access and enough free disk space.
- [7-Zip](https://www.7-zip.org/) in its default installation folder.
- [Windows ADK Deployment Tools and the Windows PE add-on](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install), in their default locations.
- A [Microsoft Validation OS x64 ISO](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/validation-os-overview). Recommended build: **26100.9549**.
- The ReactOS x64 MSVC Debug Boot CD: `reactos-bootcd-0.4.17-dev-1268-gbba112f-x64-msvc-win-dbg.iso`. On the [builds page](https://reactos.org/getbuilds/), select revision `bba112f`, Boot CDs and x64 MSVC Debug, then extract the downloaded archive.

ReactOS must match this profile. Validation OS packages are read from the selected ISO. For other Validation OS builds, image creation stops if required components are missing.

## Use

1. Extract the release package and open `dist\FreeWinBuilder.exe`.
2. Select **Microsoft Validation OS**, choose the two ISOs and an output folder, then start the build.
3. Approve the administrator prompt. Progress and tool output appear in the app.
4. Boot the resulting `freewin-…\validation-build\iso\FreeWin-ValidationOS.iso`.

Validation OS starts directly into the desktop from RAM. Save files to a separate writable drive to keep them after a reboot. Image creation uses DISM directly on the host.

The app uses Japanese when Windows uses Japanese and English otherwise. To override it, set `FREEWIN_LANGUAGE` to `ja` or `en` before launching.

## Hyper-V Server 2019

The app also retains its Hyper-V Server 2019 target. Use Hyper-V Server and Windows Server 2019 Desktop Experience ISOs in the same language; recommended builds are 17763.737 and 17763.1. For this target, run `scripts\Install-Dependencies.ps1` to prepare QEMU and OSCDIMG, and enable Windows Hypervisor Platform. Server 2016 is not supported.

## Build from source

Install the .NET 8 SDK and a Windows x64 MinGW-w64 GCC toolchain. From the repository folder:

```powershell
.\scripts\Build-Native.ps1 -GccPath 'C:\path\to\mingw64\bin\gcc.exe'
dotnet publish src/FreeWin.App/FreeWin.App.csproj -c Release -p:Platform=x64 -o dist
dotnet publish src/FreeWin.Worker/FreeWin.Worker.csproj -c Release -o dist/worker
```

## 日本語

Microsoft Validation OSにReactOSのデスクトップ、スタートメニュー、タスクバー、設定ツールを組み込んだ起動ISOを作成します。Windowsのカーネルと中核DLLは維持し、入力ISOは変更しません。

### 必要なもの

- Windows 10/11 x64、管理者権限、作成に必要な空き容量。
- 既定の場所にインストールした[7-Zip](https://www.7-zip.org/)。
- 既定の場所にインストールした[Windows ADKのDeployment ToolsとWinPEアドオン](https://learn.microsoft.com/ja-jp/windows-hardware/get-started/adk-install)。
- [Microsoft Validation OS x64 ISO](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/validation-os-overview)。推奨ビルドは **26100.9549** です。
- ReactOSの `reactos-bootcd-0.4.17-dev-1268-gbba112f-x64-msvc-win-dbg.iso`。[ビルド配布ページ](https://reactos.org/getbuilds/)でリビジョン `bba112f`、Boot CDs、x64 MSVC Debugを選び、アーカイブからISOを取り出してください。

ReactOSは指定ビルドが必要です。Validation OSは選択したISO内のパッケージを使い、ほかのビルドで必要な部品が不足する場合は作成を中止します。

### 使い方

1. 配布パッケージを展開し、`dist\FreeWinBuilder.exe` を開きます。
2. 対象OSを **Microsoft Validation OS** にし、2つのISOと保存先を選んで作成を開始します。
3. 管理者権限の確認を許可します。進捗と処理ログはアプリに表示されます。
4. 保存先の `freewin-…\validation-build\iso\FreeWin-ValidationOS.iso` から起動します。

RAMからデスクトップが起動します。再起動後も残したいファイルは、別の書き込み可能なドライブへ保存してください。ISOの作成はホストのDISMで直接処理します。

画面はWindowsが日本語なら日本語、それ以外は英語です。変更したい場合は、起動前に環境変数 `FREEWIN_LANGUAGE` を `ja` または `en` に設定してください。

### Hyper-V Server 2019を使う場合

同じ言語のHyper-V Server ISOとWindows Server 2019 Desktop Experience ISOを用意します。推奨ビルドはそれぞれ17763.737と17763.1です。この対象では、先に `scripts\Install-Dependencies.ps1` でQEMUとOSCDIMGを準備し、Windows Hypervisor Platformを有効にしてください。Server 2016には対応していません。

ソースからのビルド手順は上記の「Build from source」を参照してください。不具合は[Issues](https://github.com/mouseos/FreeWinBuilder/issues)へ報告できます。第三者ライセンスは[LICENSES](LICENSES)にまとめています。
