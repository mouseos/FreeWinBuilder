/* Adapt the Windows tray wire format to the private x64 ReactOS Explorer.
 * The OS USER32 and SHELL32 binaries are never replaced.
 */
#include <windows.h>
#include <shellapi.h>
#include <commctrl.h>
#include <stdio.h>
#include <string.h>

#pragma pack(push,4)
typedef struct {
 DWORD cbSize,window,id,flags,callback,icon;
 WCHAR tip[128];DWORD state,stateMask;WCHAR info[256];
 UINT timeout;WCHAR title[64];DWORD infoFlags;GUID guid;DWORD balloonIcon;
} NOTIFY32;
#pragma pack(pop)
typedef struct {DWORD signature,command;NOTIFYICONDATAW icon;} NOTIFY64;
_Static_assert(sizeof(NOTIFY32)==956,"Unexpected Windows tray wire layout");
_Static_assert(sizeof(NOTIFY64)==984,"The shell profile requires x64 NOTIFYICONDATAW");
typedef BOOL (WINAPI *SET_SUBCLASS)(HWND,SUBCLASSPROC,UINT_PTR,DWORD_PTR);
typedef BOOL (WINAPI *REMOVE_SUBCLASS)(HWND,SUBCLASSPROC,UINT_PTR);
typedef LRESULT (WINAPI *DEFAULT_SUBCLASS)(HWND,UINT,WPARAM,LPARAM);
static DEFAULT_SUBCLASS next_message;
static REMOVE_SUBCLASS remove_subclass;
static HWND installed;
static LONG converted;

static void record(const char *phase,DWORD value){
 WCHAR root[MAX_PATH],path[MAX_PATH];UINT length=GetWindowsDirectoryW(root,MAX_PATH);
 if(!length||length>=MAX_PATH-40)return;
 swprintf(path,MAX_PATH,L"%ls\\Temp\\FreeWinNotify.log",root);
 HANDLE file=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,NULL,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);
 if(file==INVALID_HANDLE_VALUE)return;
 char line[160];int count=snprintf(line,sizeof(line),"%s: %lu\r\n",phase,value);DWORD written;
 if(count>0&&count<(int)sizeof(line))WriteFile(file,line,(DWORD)count,&written,NULL);CloseHandle(file);
}

static LRESULT CALLBACK notify_message(HWND window,UINT message,WPARAM sender,LPARAM parameter,UINT_PTR id,DWORD_PTR context){
 (void)context;
 if(message==WM_COPYDATA&&parameter){
  const COPYDATASTRUCT *copy=(const COPYDATASTRUCT*)parameter;
  if(copy->dwData==1&&copy->lpData&&copy->cbData>=8+sizeof(NOTIFY32)){
   DWORD header[3];memcpy(header,copy->lpData,sizeof(header));
   if(header[0]==0x34753423&&header[2]==sizeof(NOTIFY32)){
    NOTIFY32 source;memcpy(&source,(const BYTE*)copy->lpData+8,sizeof(source));
    NOTIFY64 target={0};target.signature=header[0];target.command=header[1];
    target.icon.cbSize=sizeof(target.icon);target.icon.hWnd=(HWND)UlongToHandle(source.window);
    target.icon.uID=source.id;target.icon.uFlags=source.flags;target.icon.uCallbackMessage=source.callback;
    target.icon.hIcon=(HICON)UlongToHandle(source.icon);target.icon.dwState=source.state;target.icon.dwStateMask=source.stateMask;
    target.icon.uTimeout=source.timeout;target.icon.dwInfoFlags=source.infoFlags;target.icon.guidItem=source.guid;
    target.icon.hBalloonIcon=(HICON)UlongToHandle(source.balloonIcon);
    memcpy(target.icon.szTip,source.tip,sizeof(source.tip));target.icon.szTip[127]=0;
    memcpy(target.icon.szInfo,source.info,sizeof(source.info));target.icon.szInfo[255]=0;
    memcpy(target.icon.szInfoTitle,source.title,sizeof(source.title));target.icon.szInfoTitle[63]=0;
    COPYDATASTRUCT translated={copy->dwData,sizeof(target),&target};
    if(InterlockedIncrement(&converted)<=8)record("Native notification converted",copy->cbData);
    return next_message(window,message,sender,(LPARAM)&translated);
   }
  }
 }
 if(message==WM_NCDESTROY){remove_subclass(window,notify_message,id);installed=NULL;}
 return next_message(window,message,sender,parameter);
}

static void install_notify_adapter(void){
 HWND window=FindWindowW(L"Shell_TrayWnd",NULL);if(!window||window==installed)return;
 DWORD process=0,thread=GetWindowThreadProcessId(window,&process);
 if(process!=GetCurrentProcessId()||thread!=GetCurrentThreadId())return;
 HMODULE controls=GetModuleHandleW(L"roctrl32.dll");if(!controls)return;
 SET_SUBCLASS set=(SET_SUBCLASS)GetProcAddress(controls,"SetWindowSubclass");
 next_message=(DEFAULT_SUBCLASS)GetProcAddress(controls,"DefSubclassProc");
 remove_subclass=(REMOVE_SUBCLASS)GetProcAddress(controls,"RemoveWindowSubclass");
 if(!set||!next_message||!remove_subclass)return;
 if(set(window,notify_message,0x46574e54,0)){installed=window;record("Tray subclass installed",GetCurrentProcessId());}
}

BOOL WINAPI FwbGetMessageW(LPMSG message,HWND window,UINT minimum,UINT maximum){
 install_notify_adapter();return GetMessageW(message,window,minimum,maximum);
}

LRESULT WINAPI FwbSendMessageW(HWND window,UINT message,WPARAM first,LPARAM second){
 if(message==TB_SETBUTTONSIZE){
  WCHAR parentClass[64]={0},grandparentClass[64]={0};HWND parent=GetParent(window);
  if(parent)GetClassNameW(parent,parentClass,64);
  HWND grandparent=parent?GetParent(parent):NULL;if(grandparent)GetClassNameW(grandparent,grandparentClass,64);
  if(!lstrcmpW(parentClass,L"SysPager")&&!lstrcmpW(grandparentClass,L"TrayNotifyWnd")){
   // ReactOS allocates SM_CXSMICON+2 per icon; native v5 defaults to 23x22.
   SendMessageW(window,TB_SETPADDING,0,0);
   second=MAKELPARAM(GetSystemMetrics(SM_CXSMICON)+2,GetSystemMetrics(SM_CYSMICON)+2);
  }
 }
 return SendMessageW(window,message,first,second);
}
