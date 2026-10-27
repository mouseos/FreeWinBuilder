#Requires -Version 5.1
param([string]$SevenZip = "$env:ProgramFiles\7-Zip\7z.exe")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root '.work'
$qemu = Join-Path $work 'qemu'
$oscdimg = Join-Path $work 'ms-tools\oscdimg.exe'
$download = Join-Path $work 'downloads'
if (-not (Test-Path -LiteralPath $SevenZip -PathType Leaf)) { throw 'Install 7-Zip before running this script.' }
New-Item -ItemType Directory -Path $download -Force | Out-Null
function Get-VerifiedFile([string]$Url, [string]$Path, [string]$Algorithm, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $partial = $Path + '.download'
        if (Test-Path -LiteralPath $partial) { throw "Incomplete download already exists: $partial" }
        Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing
        if ((Get-FileHash -LiteralPath $partial -Algorithm $Algorithm).Hash -ne $Expected) { throw "Checksum mismatch: $partial" }
        Move-Item -LiteralPath $partial -Destination $Path
    }
    if ((Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash -ne $Expected) { throw "Checksum mismatch: $Path" }
}
$installer = Join-Path $download 'qemu-w64-setup-20260811.exe'
Get-VerifiedFile 'https://qemu.weilnetz.de/w64/2026/qemu-w64-setup-20260811.exe' $installer 'SHA512' '5BCF9EED634E8575A37B74F445AF41A2FE4106DA512D0C30C368301D4C105037FDFAB40A5287367A28A957624CDDEBBC8C07E16C88AB6634F554CDF3D16BF543'
if (-not (Test-Path -LiteralPath $qemu)) {
    & $SevenZip x $installer "-o$qemu" -y -bb0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract QEMU.' }
}
foreach ($name in @('qemu-img.exe', 'qemu-system-x86_64.exe')) {
    $file = Join-Path $qemu $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing QEMU component: $name" }
    $processInfo = New-Object System.Diagnostics.ProcessStartInfo
    $processInfo.FileName = $SevenZip
    $processInfo.Arguments = 'x -so "' + $installer.Replace('"', '\"') + '" "' + $name + '"'
    $processInfo.UseShellExecute = $false
    $processInfo.CreateNoWindow = $true
    $processInfo.RedirectStandardOutput = $true
    $process = [System.Diagnostics.Process]::Start($processInfo)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $archiveHash = [BitConverter]::ToString($sha.ComputeHash($process.StandardOutput.BaseStream)).Replace('-', '') }
        finally { $sha.Dispose() }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0 -or $archiveHash -ne (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash) {
            throw "QEMU component differs from the verified archive: $name"
        }
    } finally { $process.Dispose() }
}
New-Item -ItemType Directory -Path (Split-Path -Parent $oscdimg) -Force | Out-Null
Get-VerifiedFile 'https://msdl.microsoft.com/download/symbols/oscdimg.exe/688CABB065000/oscdimg.exe' $oscdimg 'SHA256' '2000160B2C5044691B2F9A0AC308E5207F273D4880A572457AF16D05886BA861'
Write-Output 'Dependencies are ready.'
