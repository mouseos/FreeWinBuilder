/* Starts the ReactOS shell after the optional official RAM crash-dump setup. */
#define COBJMACROS
#include <windows.h>
#include <objbase.h>
#include <docobj.h>
#include <shlguid.h>
#include <mmsystem.h>
#include <stdio.h>
#include <wchar.h>

static HANDLE logfile=INVALID_HANDLE_VALUE;
typedef struct {HMODULE module;IOleCommandTarget *object;BOOL com;} TRAY;
/* stobject stops adding every tray icon when its volume icon cannot find a mixer.
 * Without audio, start it with volume deferred and enable volume once audio appears. */
typedef struct {BOOL held,existed;DWORD original;} VOLUME_HOLD;
#define SYSTRAY_KEY L"Software\\Microsoft\\Windows\\CurrentVersion\\Applets\\SysTray"
#define SYSTRAY_VOLUME 0x00000004
DWORD InitializeVolumeConfig(HKEY user);
DWORD InitializeShellIcons(const WCHAR *folder);
HRESULT InitializeUserFilesShortcut(const WCHAR *folder);
DWORD InitializeEventLog(void);
DWORD InitializeClassicTheme(HKEY user);
static void log_status(const char *phase,DWORD code){
 char line[160];int n=snprintf(line,sizeof(line),"%s: %lu\r\n",phase,code);DWORD written;
 if(logfile!=INVALID_HANDLE_VALUE)WriteFile(logfile,line,(DWORD)n,&written,NULL);
}
static void hold_volume(VOLUME_HOLD *hold){
 HKEY key=NULL;if(RegCreateKeyExW(HKEY_CURRENT_USER,SYSTRAY_KEY,0,NULL,0,KEY_QUERY_VALUE|KEY_SET_VALUE,NULL,&key,NULL))return;
 DWORD value=0x7,size=sizeof(value),type=0;
 hold->existed=RegQueryValueExW(key,L"Services",NULL,&type,(BYTE*)&value,&size)==ERROR_SUCCESS&&type==REG_DWORD;
 if(!hold->existed)value=0x7;
 if(value&SYSTRAY_VOLUME){
  DWORD reduced=value&~SYSTRAY_VOLUME;hold->original=value;
  hold->held=RegSetValueExW(key,L"Services",0,REG_DWORD,(const BYTE*)&reduced,sizeof(reduced))==ERROR_SUCCESS;
 }
 RegCloseKey(key);
}
static void release_volume(VOLUME_HOLD *hold){
 HKEY key=NULL;if(!RegOpenKeyExW(HKEY_CURRENT_USER,SYSTRAY_KEY,0,KEY_SET_VALUE,&key)){
  if(hold->existed)RegSetValueExW(key,L"Services",0,REG_DWORD,(const BYTE*)&hold->original,sizeof(hold->original));
  else RegDeleteValueW(key,L"Services");
  RegCloseKey(key);
 }
 hold->held=FALSE;
 HWND tray=FindWindowW(L"SystemTray_Main",NULL);
 log_status("Deferred volume icon enabled",tray&&PostMessageW(tray,WM_USER+220,SYSTRAY_VOLUME,1)?0:GetLastError());
}
static void start_tray(const WCHAR *folder,TRAY *tray){
 HRESULT hr=CoInitializeEx(NULL,COINIT_APARTMENTTHREADED);
 log_status("Tray COM initialization",(DWORD)hr);if(FAILED(hr))return;tray->com=TRUE;
 WCHAR path[MAX_PATH];swprintf(path,MAX_PATH,L"%ls\\stobject.dll",folder);
 tray->module=LoadLibraryExW(path,NULL,LOAD_WITH_ALTERED_SEARCH_PATH);
 if(!tray->module){log_status("Tray module load error",GetLastError());return;}
 typedef HRESULT (WINAPI *GETCLASS)(REFCLSID,REFIID,LPVOID*);
 GETCLASS get=(GETCLASS)GetProcAddress(tray->module,"DllGetClassObject");
 if(!get){log_status("Tray factory export error",GetLastError());return;}
 CLSID clsid;hr=CLSIDFromString(L"{35CEC8A3-2BE6-11D2-8773-92E220524153}",&clsid);
 if(FAILED(hr)){log_status("Tray CLSID error",(DWORD)hr);return;}
 IClassFactory *factory=NULL;hr=get(&clsid,&IID_IClassFactory,(void**)&factory);
 log_status("Tray class factory",(DWORD)hr);if(FAILED(hr))return;
 hr=IClassFactory_CreateInstance(factory,NULL,&IID_IOleCommandTarget,(void**)&tray->object);
 IClassFactory_Release(factory);log_status("Tray object creation",(DWORD)hr);if(FAILED(hr))return;
 hr=IOleCommandTarget_Exec(tray->object,&CGID_ShellServiceObject,OLECMDID_NEW,OLECMDEXECOPT_DODEFAULT,NULL,NULL);
 log_status("Tray start command",(DWORD)hr);
}
static void stop_tray(TRAY *tray){
 if(tray->object){HRESULT hr=IOleCommandTarget_Exec(tray->object,&CGID_ShellServiceObject,OLECMDID_SAVE,OLECMDEXECOPT_DODEFAULT,NULL,NULL);log_status("Tray stop command",(DWORD)hr);IOleCommandTarget_Release(tray->object);}
 if(tray->module)FreeLibrary(tray->module);if(tray->com)CoUninitialize();
}

int WINAPI wWinMain(HINSTANCE instance,HINSTANCE previous,PWSTR command,int show){
 (void)instance;(void)previous;(void)command;(void)show;
 WCHAR folder[MAX_PATH],windows[MAX_PATH],path[MAX_PATH],cmd[MAX_PATH],line[MAX_PATH*3];
 if(!GetModuleFileNameW(NULL,folder,MAX_PATH)||!GetWindowsDirectoryW(windows,MAX_PATH))return 2;
 WCHAR *slash=wcsrchr(folder,L'\\');if(!slash)return 2;*slash=0;
 SECURITY_ATTRIBUTES sa={sizeof(sa),NULL,TRUE};
 swprintf(path,MAX_PATH,L"%ls\\Temp\\FreeWinShell.log",windows);
 logfile=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,&sa,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);
 log_status("Event log initialization",InitializeEventLog());
 log_status("Classic appearance initialization",InitializeClassicTheme(HKEY_CURRENT_USER));
 swprintf(path,MAX_PATH,L"%ls\\System32\\SetCrashDumpUSB.cmd",windows);
 if(GetFileAttributesW(path)!=INVALID_FILE_ATTRIBUTES){
  swprintf(cmd,MAX_PATH,L"%ls\\System32\\cmd.exe",windows);
  swprintf(line,MAX_PATH*3,L"\"%ls\" /d /c call \"%ls\"",cmd,path);
  STARTUPINFOW si={sizeof(si)};PROCESS_INFORMATION pi={0};
  HANDLE input=CreateFileW(L"NUL",GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,&sa,OPEN_EXISTING,0,NULL);
  si.dwFlags=STARTF_USESHOWWINDOW;si.wShowWindow=SW_HIDE;
  if(logfile!=INVALID_HANDLE_VALUE&&input!=INVALID_HANDLE_VALUE){si.dwFlags|=STARTF_USESTDHANDLES;si.hStdInput=input;si.hStdOutput=logfile;si.hStdError=logfile;}
  if(CreateProcessW(cmd,line,NULL,NULL,TRUE,CREATE_NO_WINDOW,NULL,folder,&si,&pi)){
   WaitForSingleObject(pi.hProcess,INFINITE);DWORD result;GetExitCodeProcess(pi.hProcess,&result);
   log_status("SetCrashDumpUSB exit",result);CloseHandle(pi.hThread);CloseHandle(pi.hProcess);
  }else log_status("SetCrashDumpUSB launch error",GetLastError());
  if(input!=INVALID_HANDLE_VALUE)CloseHandle(input);
 }
 swprintf(path,MAX_PATH,L"%ls\\explorer.exe",folder);
 swprintf(line,MAX_PATH*3,L"\"%ls\"",path);
 STARTUPINFOW si={sizeof(si)};PROCESS_INFORMATION pi={0};
 DWORD result=1;
 log_status("Missing shell icon values initialized",InitializeShellIcons(folder));
 log_status("Volume preference groups initialized",InitializeVolumeConfig(HKEY_CURRENT_USER));
 log_status("User files shortcut initialization",(DWORD)InitializeUserFilesShortcut(folder));
 if(CreateProcessW(path,line,NULL,NULL,FALSE,0,NULL,folder,&si,&pi)){
  log_status("Explorer started",pi.dwProcessId);
  DWORD started=GetTickCount(),lastVolume=started;BOOL trayStarted=FALSE;TRAY tray={0};VOLUME_HOLD hold={0};
  for(;;){
   DWORD wait=MsgWaitForMultipleObjects(1,&pi.hProcess,FALSE,250,QS_ALLINPUT);
   if(wait==WAIT_OBJECT_0)break;
   if(wait==WAIT_FAILED){log_status("Shell wait error",GetLastError());break;}
   MSG message;while(PeekMessageW(&message,NULL,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
   DWORD now=GetTickCount();
   if(!trayStarted){HWND window=FindWindowW(L"Shell_TrayWnd",NULL);DWORD owner=0;if(window)GetWindowThreadProcessId(window,&owner);
    // Give the audio driver a short head start before the volume icon looks for a mixer.
    if(owner==pi.dwProcessId&&(mixerGetNumDevs()||now-started>=10000)){
     UINT devices=mixerGetNumDevs();log_status("Audio mixers at tray start",devices);
     if(!devices)hold_volume(&hold);
     trayStarted=TRUE;start_tray(folder,&tray);
    }}
   if(hold.held&&mixerGetNumDevs())release_volume(&hold);
   if(now-started<60000&&now-lastVolume>=2000){lastVolume=now;DWORD count=InitializeVolumeConfig(HKEY_CURRENT_USER);if(count)log_status("Late audio devices initialized",count);}
  }
  stop_tray(&tray);
  GetExitCodeProcess(pi.hProcess,&result);log_status("Explorer exit",result);CloseHandle(pi.hThread);CloseHandle(pi.hProcess);
 }else{
  result=GetLastError();log_status("Explorer launch error",result);
  MessageBoxW(NULL,L"ReactOS Explorer could not start. See Windows\\Temp\\FreeWinShell.log.",L"FreeWinBuilder",MB_OK|MB_ICONERROR);
 }
 if(logfile!=INVALID_HANDLE_VALUE)CloseHandle(logfile);return (int)result;
}
