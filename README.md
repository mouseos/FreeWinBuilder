# FreeWinBuilder

English | [日本語](README.ja.md)

Create bootable Windows ISOs that add a desktop to editions that ship without one:

- Microsoft Validation OS with the desktop, Start menu, taskbar and settings tools of ReactOS or its fork LiberNT. Windows keeps its own kernel and core DLLs.
- Azure Local with the Windows Server desktop (see [Azure Local](#azure-local)).
- Hyper-V Server 2019 with the Windows Server 2019 desktop.

The source files are unchanged.

## Requirements

- Windows 10/11 x64, administrator access and enough free disk space.
- [7-Zip](https://www.7-zip.org/) in its default installation folder.
- [Windows ADK Deployment Tools and the Windows PE add-on](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install), in their default locations.
- A [Microsoft Validation OS x64 ISO](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/validation-os-overview). Tested build: 26100.9549.
- One of these x64 boot CDs:
  - ReactOS from the [builds page](https://reactos.org/getbuilds/). Tested build: 0.4.17-dev-1268-gbba112f, x64 MSVC Debug.
  - LiberNT from its [releases](https://github.com/LiberNT/LiberNT/releases) (`bootcd-iso-…-amd64-….zip`, extract `bootcd.iso`). Tested build: nt10.0-043-2555, Clang Debug.

Image creation stops if either ISO lacks a required component.

## Use

1. Extract the release package and open `dist\FreeWinBuilder.exe`.
2. Select **Microsoft Validation OS**, choose the two ISOs and an output folder, then start the build.
3. Approve the administrator prompt. Progress and tool output appear in the app.
4. Boot the resulting `freewin-…\validation-build\iso\FreeWin-ValidationOS.iso`.

Validation OS starts directly into the desktop from RAM. Save files to a separate writable drive to keep them after a reboot.

The Validation OS image is English only, and no input method (IME) works there. Validation OS provides no display language other than English and no IME package. This applies only to the Validation OS target; Hyper-V Server builds keep the language of their ISOs.

The app uses Japanese when Windows uses Japanese and English otherwise. To override it, set `FREEWIN_LANGUAGE` to `ja` or `en` before launching.

## Hyper-V Server 2019

Use Hyper-V Server and Windows Server 2019 Desktop Experience ISOs in the same language. Tested builds: 17763.737 and 17763.1. Run `scripts\Install-Dependencies.ps1` first to prepare QEMU and OSCDIMG, and enable Windows Hypervisor Platform.

## Azure Local

Creates an Azure Local installation ISO with the Windows Server desktop: Start menu, taskbar, File Explorer and Server Manager. DISM adds the Desktop Experience packages of Windows Server to the Azure Local image; the edition stays Azure Local (Azure Stack HCI).

You need, besides 7-Zip and the Windows ADK Deployment Tools and WinPE add-on:

- Azure Local x64 (ServerAzureStackHCICor) as an ISO, WIM or ESD. Tested build: 26100.1.
- Windows Server 2025 Datacenter or Standard (Desktop Experience) as an ISO, WIM or ESD of the same build. Tested build: 26100.1.
- The Windows Server language pack of the same build, `Microsoft-Windows-Server-LanguagePack-Package-amd64-<language>.esd` from the UUP files of Windows Server 2025 or Azure Local.

Select **Azure Local** in the app, choose the three files and an output folder, and approve the administrator prompt. The result is `freewin-…\azure-local-build\FreeWin-AzureLocal.iso`.

Booting the ISO lists the disks and asks you to type `YES`. It then erases disk 0, installs Azure Local for UEFI or BIOS and restarts into the normal first-start setup. Cumulative updates are not included; install them after setup.

## Build from source

Install the .NET 8 SDK and a Windows x64 MinGW-w64 GCC toolchain. From the repository folder:

```powershell
.\scripts\Build-Native.ps1 -GccPath 'C:\path\to\mingw64\bin\gcc.exe'
dotnet publish src/FreeWin.App/FreeWin.App.csproj -c Release -p:Platform=x64 -o dist
dotnet publish src/FreeWin.Worker/FreeWin.Worker.csproj -c Release -o dist/worker
```

Report problems in [Issues](https://github.com/mouseos/FreeWinBuilder/issues). Third-party licenses are in [LICENSES](LICENSES).
