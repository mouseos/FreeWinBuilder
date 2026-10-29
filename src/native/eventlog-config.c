#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <winevt.h>
#include <stdio.h>

static BOOL status(SC_HANDLE service,SERVICE_STATUS_PROCESS *state){
 DWORD size=0;return QueryServiceStatusEx(service,SC_STATUS_PROCESS_INFO,(BYTE*)state,sizeof(*state),&size);
}
static BOOL wait_stopped(SC_HANDLE service){
 ULONGLONG end=GetTickCount64()+20000;SERVICE_STATUS_PROCESS state;
 do{if(!status(service,&state))return FALSE;if(state.dwCurrentState==SERVICE_STOPPED)return TRUE;Sleep(100);}while(GetTickCount64()<end);
 SetLastError(ERROR_TIMEOUT);return FALSE;
}
static BOOL initialize(FILE *log){
 HKEY mode=NULL,parent=NULL;SC_HANDLE manager=NULL,service=NULL;HANDLE process=NULL;
 BOOL renamed=FALSE,ready=FALSE;WCHAR saved[80];DWORD error=0;
 LONG result=RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SYSTEM\\CurrentControlSet\\Control\\MiniNT",0,KEY_READ,&mode);
 if(result==ERROR_FILE_NOT_FOUND){fprintf(log,"Not a MiniNT environment\n");return TRUE;}
 if(result!=ERROR_SUCCESS){fprintf(log,"MODE_OPEN=%ld\n",result);SetLastError(result);return FALSE;}
 RegCloseKey(mode);
 manager=OpenSCManagerW(NULL,NULL,SC_MANAGER_CONNECT);if(!manager)goto done;
 service=OpenServiceW(manager,L"EventLog",SERVICE_QUERY_CONFIG|SERVICE_QUERY_STATUS|SERVICE_START|SERVICE_STOP);if(!service)goto done;
 DWORD size=0;QueryServiceConfigW(service,NULL,0,&size);
 if(!size||size>65536){SetLastError(ERROR_INVALID_DATA);goto done;}
 QUERY_SERVICE_CONFIGW *config=HeapAlloc(GetProcessHeap(),0,size);if(!config){SetLastError(ERROR_NOT_ENOUGH_MEMORY);goto done;}
 BOOL own=QueryServiceConfigW(service,config,size,&size)&&config->dwServiceType==SERVICE_WIN32_OWN_PROCESS;
 HeapFree(GetProcessHeap(),0,config);if(!own){SetLastError(ERROR_SERVICE_CANNOT_ACCEPT_CTRL);goto done;}
 SERVICE_STATUS_PROCESS state;if(!status(service,&state))goto done;
 ULONGLONG pendingEnd=GetTickCount64()+20000;
 while(state.dwCurrentState==SERVICE_START_PENDING){
  if(GetTickCount64()>=pendingEnd){SetLastError(ERROR_TIMEOUT);goto done;}
  Sleep(100);if(!status(service,&state))goto done;
 }
 if(state.dwCurrentState==SERVICE_RUNNING){
  EVT_HANDLE channel=EvtOpenChannelConfig(NULL,L"Application",0);
  if(channel){EvtClose(channel);fprintf(log,"Event log already ready\n");ready=TRUE;goto done;}
 }
 fprintf(log,"INITIAL_STATE=%lu PID=%lu\n",state.dwCurrentState,state.dwProcessId);
 if(state.dwCurrentState!=SERVICE_STOPPED){
  if(state.dwProcessId){process=OpenProcess(SYNCHRONIZE,FALSE,state.dwProcessId);if(!process)goto done;}
  if(state.dwCurrentState!=SERVICE_STOP_PENDING){SERVICE_STATUS ignored;if(!ControlService(service,SERVICE_CONTROL_STOP,&ignored))goto done;}
  if(!wait_stopped(service))goto done;
  if(process&&WaitForSingleObject(process,20000)!=WAIT_OBJECT_0){SetLastError(ERROR_TIMEOUT);goto done;}
 }
 result=RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SYSTEM\\CurrentControlSet\\Control",0,KEY_READ|KEY_WRITE|DELETE,&parent);
 if(result!=ERROR_SUCCESS){SetLastError(result);goto done;}
 swprintf(saved,80,L"FreeWin_MiniNT_%lu",GetCurrentProcessId());
 result=RegRenameKey(parent,L"MiniNT",saved);if(result!=ERROR_SUCCESS){SetLastError(result);goto done;}
 renamed=TRUE;fprintf(log,"MODE_SAVED\n");
 if(!StartServiceW(service,0,NULL)&&GetLastError()!=ERROR_SERVICE_ALREADY_RUNNING)goto done;
 ULONGLONG end=GetTickCount64()+20000;
 do{
  EVT_HANDLE channel=EvtOpenChannelConfig(NULL,L"Application",0);
  if(channel){EvtClose(channel);ready=TRUE;break;}
  error=GetLastError();Sleep(100);
 }while(GetTickCount64()<end);
 if(!ready){SetLastError(error);goto done;}
 fprintf(log,"CHANNEL_API_READY\n");
done:
 error=ready?0:GetLastError();
 if(renamed){
  result=RegRenameKey(parent,saved,L"MiniNT");fprintf(log,"MODE_RESTORE=%ld\n",result);
  if(result!=ERROR_SUCCESS){ready=FALSE;error=result;}
 }
 if(parent)RegCloseKey(parent);if(process)CloseHandle(process);if(service)CloseServiceHandle(service);if(manager)CloseServiceHandle(manager);
 fprintf(log,"EVENTLOG_READY=%d ERROR=%lu\n",ready,error);fflush(log);SetLastError(error);return ready;
}

/* The media configures a separate, demand-start host so restarting this
   service never stops unrelated services or reuses a former PE RPC server. */
DWORD InitializeEventLog(void){
 WCHAR windows[MAX_PATH],path[MAX_PATH];
 if(!GetWindowsDirectoryW(windows,MAX_PATH))return GetLastError();
 swprintf(path,MAX_PATH,L"%ls\\Temp\\FreeWinEventLog.log",windows);
 FILE *log=_wfopen(path,L"ab");if(!log)return ERROR_OPEN_FAILED;
 setvbuf(log,NULL,_IONBF,0);BOOL ok=initialize(log);DWORD result=ok?ERROR_SUCCESS:GetLastError();
 fclose(log);return result;
}
