param([Parameter(Mandatory)][string]$GccPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$compiler=(Resolve-Path -LiteralPath $GccPath).Path
$out=Join-Path $root 'src\FreeWin.Core\obj\native'
New-Item -ItemType Directory -Force -Path $out | Out-Null
# Explicit compiler path avoids picking an unrelated gcc bundled with another SDK.
& $compiler -O2 -c (Join-Path $root 'src\native\roscrt.c') -o (Join-Path $out 'roscrt.o')
if($LASTEXITCODE){throw 'Native adapter compilation failed'}
& $compiler -shared -nostdlib '-Wl,--entry,0' (Join-Path $out 'roscrt.o') (Join-Path $root 'src\native\roscrt.def') -lkernel32 -o (Join-Path $out 'roscrt.dll')
if($LASTEXITCODE){throw 'Native adapter linking failed'}
& $compiler -shared -nostdlib '-Wl,--entry,0' (Join-Path $root 'src\native\rosps.def') -o (Join-Path $out 'rosps.dll')
if($LASTEXITCODE){throw 'PSAPI forwarder linking failed'}
& $compiler -shared -nostdlib '-Wl,--entry,0' (Join-Path $root 'src\native\rostmr.def') -o (Join-Path $out 'rostmr.dll')
if($LASTEXITCODE){throw 'Private theme API forwarder linking failed'}
& $compiler -O2 -shared -static-libgcc (Join-Path $root 'src\native\rosmix.c') (Join-Path $root 'src\native\rosmix.def') -lwinmm -lole32 -luuid -luser32 -o (Join-Path $out 'rosmx.dll')
if($LASTEXITCODE){throw 'Audio endpoint bridge compilation failed'}
& $compiler -O2 -shared -static-libgcc (Join-Path $root 'src\native\notify-compat.c') (Join-Path $root 'src\native\rosusr.def') -luser32 -o (Join-Path $out 'rosusr.dll')
if($LASTEXITCODE){throw 'Native notification adapter compilation failed'}
& $compiler -Os -s -shared -nostdlib '-Wl,--entry,0' (Join-Path $root 'src\native\vos-branding.c') -lkernel32 -ladvapi32 -o (Join-Path $out 'winbrand.dll')
if($LASTEXITCODE){throw 'Validation OS branding adapter compilation failed'}
& $compiler -O2 -shared -nostdlib '-Wl,--entry,0' (Join-Path $root 'src\native\roskrn.c') (Join-Path $root 'src\native\roskrn.def') -lkernel32 -o (Join-Path $out 'roskrn32.dll')
if($LASTEXITCODE){throw 'Private kernel32 adapter compilation failed'}
& $compiler -O2 -shared -nostdlib '-Wl,--entry,0' (Join-Path $root 'src\native\rothemi.c') (Join-Path $root 'src\native\rothemi.def') -lkernel32 -o (Join-Path $out 'rothemi.dll')
if($LASTEXITCODE){throw 'Private theme initializer compilation failed'}
& $compiler -O2 -municode -mwindows -static-libgcc (Join-Path $root 'src\native\shellhost.c') (Join-Path $root 'src\native\eventlog-config.c') (Join-Path $root 'src\native\classic-config.c') (Join-Path $root 'src\native\volume-config.c') (Join-Path $root 'src\native\shell-icons.c') -luser32 -lwinmm -ladvapi32 -lwevtapi -lshell32 -lole32 -luuid -o (Join-Path $out 'FreeWinShell.exe')
if($LASTEXITCODE){throw 'Native shell launcher compilation failed'}
