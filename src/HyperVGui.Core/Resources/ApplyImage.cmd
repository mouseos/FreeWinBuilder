@echo off
setlocal EnableDelayedExpansion
reg query HKLM\SYSTEM\CurrentControlSet\Control\MiniNT >nul 2>&1 || exit /b 89
set "INPUT=%~dp0"
if not exist "%INPUT%HVGUI-SERVICING.TAG" exit /b 90
call :apply >"%INPUT%servicing.log" 2>&1
set "RESULT=!errorlevel!"
echo RESULT=!RESULT!>>"%INPUT%servicing.log"
wpeutil shutdown
exit /b

:apply
diskpart /s "%INPUT%diskpart.txt" || exit /b 91
if not exist S:\ exit /b 91
mkdir S:\scratch S:\image S:\logs
set "SOURCE="
set "PACKAGES="
for %%d in (D E F G H I J K L M N O P Q R T U V W Y Z) do if exist %%d:\sources\install.wim set "SOURCE=%%d:"
for %%d in (D E F G H I J K L M N O P Q R T U V W Y Z) do if exist %%d:\HVGUI-PACKAGES.TAG set "PACKAGES=%%d:\packages"
if not defined SOURCE exit /b 92
if not defined PACKAGES exit /b 92
copy "!SOURCE!\sources\install.wim" S:\target.wim >nul || exit /b 93
attrib -r S:\target.wim
dism /English /Apply-Image /ImageFile:S:\target.wim /Index:1 /ApplyDir:S:\image /CheckIntegrity /ScratchDir:S:\scratch /LogPath:S:\logs\apply.log || exit /b 94
xcopy "!PACKAGES!" S:\packages\ /E /I /H /Y >nul || exit /b 95
attrib -r S:\packages\* /s /d
for /d %%p in (S:\packages\*) do (
  dism /English /Image:S:\image /Add-Package /PackagePath:"%%p" /NoRestart /ScratchDir:S:\scratch /LogPath:S:\logs\add-%%~np.log
  set "RESULT=!errorlevel!"
  if not !RESULT!==0 if not !RESULT!==3010 exit /b 96
)
for /f "usebackq tokens=1,2 delims=|" %%f in ("%INPUT%features.txt") do (
  set "OWNER="
  if not "%%g"=="" set "OWNER=/PackageName:%%g"
  dism /English /Image:S:\image /Enable-Feature /FeatureName:%%f !OWNER! /All /NoRestart /ScratchDir:S:\scratch /LogPath:S:\logs\enable-%%f.log
  set "RESULT=!errorlevel!"
  if not !RESULT!==0 if not !RESULT!==3010 exit /b 97
)
if not exist S:\image\Windows\explorer.exe exit /b 98
reg load HKLM\HVGUI S:\image\Windows\System32\config\SOFTWARE || exit /b 99
set "VALID=0"
for /f "tokens=1,2,3" %%a in ('reg query "HKLM\HVGUI\Microsoft\Windows NT\CurrentVersion\Server\ServerLevels" /v Server-Gui-Shell') do if "%%a"=="Server-Gui-Shell" if "%%c"=="0x1" set "VALID=1"
set "EXPLORER=0"
for /f "tokens=1,2,3" %%a in ('reg query "HKLM\HVGUI\Microsoft\Windows NT\CurrentVersion\Winlogon\AlternateShells\AvailableShells" /v 60000') do if "%%a"=="60000" if /i "%%c"=="explorer.exe" set "EXPLORER=1"
if not "!EXPLORER!"=="1" set "VALID=0"
if "!VALID!"=="1" (
  reg query "HKLM\HVGUI\Microsoft\Windows\CurrentVersion\Run" >nul 2>&1
  if errorlevel 1 set "VALID=0"
  reg query "HKLM\HVGUI\Microsoft\Windows\CurrentVersion\Run" /v sconfig >nul 2>&1
  if not errorlevel 1 (
    reg delete "HKLM\HVGUI\Microsoft\Windows\CurrentVersion\Run" /v sconfig /f >nul 2>&1
    if errorlevel 1 set "VALID=0"
    reg query "HKLM\HVGUI\Microsoft\Windows\CurrentVersion\Run" /v sconfig >nul 2>&1
    if not errorlevel 1 set "VALID=0"
  )
)
reg unload HKLM\HVGUI || exit /b 99
if not "!VALID!"=="1" exit /b 99
dism /English /Capture-Image /ImageFile:S:\patched.wim /CaptureDir:S:\image /Name:"Hyper-V Server 2019" /Compress:fast /CheckIntegrity /ScratchDir:S:\scratch /LogPath:S:\logs\capture.log || exit /b 100
exit /b 0
