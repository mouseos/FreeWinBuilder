# FreeWin Builder

[日本語](#日本語)

Create a Hyper-V Server 2019 installation ISO with a graphical desktop. The original ISOs are not modified.

## Requirements

- A 64-bit Windows PC with hardware virtualization and Windows Hypervisor Platform enabled.
- [7-Zip 25.01 or later](https://www.7-zip.org/) installed in its default location, and enough free disk space for the build.
- A [Hyper-V Server 2019 ISO](https://www.microsoft.com/en-us/evalcenter/evaluate-hyper-v-server-2019) and a [Windows Server 2019 ISO](https://www.microsoft.com/en-us/evalcenter/evaluate-windows-server-2019) containing Desktop Experience, both in the same language. Recommended builds are 17763.737 and 17763.1, respectively.

Other 17763 builds can be selected; the build stops if required components are missing or do not match. Japanese and English ISOs have been tested. Server 2016 is not supported.

## Use

1. Download and extract the latest package from [Releases](https://github.com/mouseos/FreeWinBuilder/releases/latest).
2. In the extracted folder, run `powershell -ExecutionPolicy Bypass -File .\scripts\Install-Dependencies.ps1`. This downloads QEMU and OSCDIMG.
3. Open `dist\FreeWin.App.exe`, select both ISOs and an output folder, and start the build.

The app uses Japanese when Windows uses Japanese and English otherwise. To override it, set `FREEWIN_LANGUAGE` to `ja` or `en` before launching.

To build from source, install the .NET 8 SDK and run:

```powershell
dotnet publish src/FreeWin.App/FreeWin.App.csproj -c Release -p:Platform=x64 -o dist
dotnet publish src/FreeWin.Worker/FreeWin.Worker.csproj -c Release -o dist/worker
```

## 日本語

Hyper-V Server 2019にデスクトップを追加したインストールISOを作成します。元のISOは変更しません。

### 必要なもの

- ハードウェア仮想化とWindows Hypervisor Platformを有効にした64ビットWindows PC。
- 既定の場所にインストールした[7-Zip 25.01以降](https://www.7-zip.org/)と、作成に必要な空き容量。
- 同じ言語の[Hyper-V Server 2019 ISO](https://www.microsoft.com/ja-jp/evalcenter/evaluate-hyper-v-server-2019)と、Desktop Experienceを含む[Windows Server 2019 ISO](https://www.microsoft.com/ja-jp/evalcenter/evaluate-windows-server-2019)。推奨ビルドはそれぞれ17763.737と17763.1です。

ほかの17763系列も選択できますが、必要な部品が不足または不一致の場合は作成を中止します。日本語版と英語版で動作確認済みです。Server 2016には対応していません。

### 使い方

1. [Releases](https://github.com/mouseos/FreeWinBuilder/releases/latest)から最新版をダウンロードして展開します。
2. 展開したフォルダーで `powershell -ExecutionPolicy Bypass -File .\scripts\Install-Dependencies.ps1` を実行します。QEMUとOSCDIMGがダウンロードされます。
3. `dist\FreeWin.App.exe` を開き、2つのISOと保存先を選んで作成を開始します。

画面はWindowsが日本語なら日本語、それ以外は英語です。変更したい場合は、起動前に環境変数 `FREEWIN_LANGUAGE` を `ja` または `en` に設定してください。

ソースからビルドする場合は.NET 8 SDKをインストールし、上記の `dotnet publish` を実行してください。
