# FreeWinBuilder

English | [日本語](README.ja.md)

Create a bootable Microsoft Validation OS ISO with the ReactOS desktop, Start menu, taskbar and settings tools. Windows keeps its own kernel and core DLLs. The source ISOs are unchanged.

## Requirements

- Windows 10/11 x64, administrator access and enough free disk space.
- [7-Zip](https://www.7-zip.org/) in its default installation folder.
- [Windows ADK Deployment Tools and the Windows PE add-on](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install), in their default locations.
- A [Microsoft Validation OS x64 ISO](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/validation-os-overview). Tested build: 26100.9549.
- A ReactOS x64 Boot CD from the [builds page](https://reactos.org/getbuilds/). Tested build: 0.4.17-dev-1268-gbba112f, x64 MSVC Debug.

Image creation stops if either ISO lacks a required component.

## Use

1. Extract the release package and open `dist\FreeWinBuilder.exe`.
2. Select **Microsoft Validation OS**, choose the two ISOs and an output folder, then start the build.
3. Approve the administrator prompt. Progress and tool output appear in the app.
4. Boot the resulting `freewin-…\validation-build\iso\FreeWin-ValidationOS.iso`.

Validation OS starts directly into the desktop from RAM. Save files to a separate writable drive to keep them after a reboot.

The app uses Japanese when Windows uses Japanese and English otherwise. To override it, set `FREEWIN_LANGUAGE` to `ja` or `en` before launching.

## Hyper-V Server 2019

Use Hyper-V Server and Windows Server 2019 Desktop Experience ISOs in the same language. Tested builds: 17763.737 and 17763.1. Run `scripts\Install-Dependencies.ps1` first to prepare QEMU and OSCDIMG, and enable Windows Hypervisor Platform.

## Build from source

Install the .NET 8 SDK and a Windows x64 MinGW-w64 GCC toolchain. From the repository folder:

```powershell
.\scripts\Build-Native.ps1 -GccPath 'C:\path\to\mingw64\bin\gcc.exe'
dotnet publish src/FreeWin.App/FreeWin.App.csproj -c Release -p:Platform=x64 -o dist
dotnet publish src/FreeWin.Worker/FreeWin.Worker.csproj -c Release -o dist/worker
```

Report problems in [Issues](https://github.com/mouseos/FreeWinBuilder/issues). Third-party licenses are in [LICENSES](LICENSES).
