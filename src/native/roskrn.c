/* Private kernel32 adapter for forks that split shell32 into windows.storage.
 * The fork's shell32 finds its storage classes with GetModuleHandle("windows.storage.dll").
 * In Validation OS that name is the native module, so map it to the private copy.
 * Every other export is forwarded to the unmodified Windows KERNEL32.
 */
#include <windows.h>

static LPCWSTR Redirect(LPCWSTR name){
 if(!name)return name;
 LPCWSTR file=name;
 for(LPCWSTR p=name;*p;p++)if(*p==L'\\'||*p==L'/')file=p+1;
 return lstrcmpiW(file,L"windows.storage.dll")==0||lstrcmpiW(file,L"windows.storage")==0?L"ros_win_storage.dll":name;
}

HMODULE WINAPI FwbGetModuleHandleW(LPCWSTR name){return GetModuleHandleW(Redirect(name));}

BOOL WINAPI FwbGetModuleHandleExW(DWORD flags,LPCWSTR name,HMODULE *module){
 if(!(flags&GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS))name=Redirect(name);
 return GetModuleHandleExW(flags,name,module);
}

