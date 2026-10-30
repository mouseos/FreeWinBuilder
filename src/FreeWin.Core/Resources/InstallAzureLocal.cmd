@echo off
setlocal
set "MEDIA=%~1"
cls
echo FreeWinBuilder - Azure Local with Desktop Experience
echo.
echo This installs Azure Local on disk 0 and ERASES ALL DATA on disk 0.
echo.
diskpart /s "%MEDIA%\FreeWin\list.txt"
echo.
set "ANSWER="
set /p "ANSWER=Type YES to erase disk 0 and install: "
if /i not "%ANSWER%"=="YES" (
  echo Installation cancelled. Turn off the computer or remove the media.
  exit /b 1
)
set "FIRMWARE=BIOS"
for /f "tokens=3" %%a in ('reg query HKLM\System\CurrentControlSet\Control /v PEFirmwareType') do if "%%a"=="0x2" set "FIRMWARE=UEFI"
if "%FIRMWARE%"=="UEFI" (set "LAYOUT=%MEDIA%\FreeWin\uefi.txt") else (set "LAYOUT=%MEDIA%\FreeWin\bios.txt")
echo Partitioning disk 0 for %FIRMWARE%...
diskpart /s "%LAYOUT%" || goto :failed
if not exist W:\ goto :failed
echo Applying Azure Local...
dism /Apply-Image /ImageFile:"%MEDIA%\sources\install.wim" /Index:1 /ApplyDir:W:\ || goto :failed
echo Installing the boot manager...
W:\Windows\System32\bcdboot.exe W:\Windows /s S: /f ALL || goto :failed
echo Installation complete. Restarting...
wpeutil reboot
exit /b 0
:failed
echo.
echo Installation failed. Review the messages above.
exit /b 1
