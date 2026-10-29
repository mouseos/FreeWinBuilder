/* Private ole32 adapter for forks that register their in-box shell classes to their
 * own DLLs. Their shell code activates classes such as CLSID_ShellLink by CLSID; in
 * Validation OS those CLSIDs name the native implementations, which do not accept
 * the fork's objects. In-process requests first use the private shell DLLs already
 * loaded in the process; anything they do not implement goes to the Windows OLE32.
 * Every other export is forwarded unchanged.
 */
#include <windows.h>
#include <objbase.h>

typedef HRESULT (WINAPI *GET_CLASS)(REFCLSID, REFIID, void **);
static const WCHAR *const modules[] = {
 L"ros_win_storage.dll", L"roshell.dll", L"roexplorframe.dll", L"rodocvw.dll", L"robrowse.dll"
};

static HRESULT PrivateClass(REFCLSID clsid, DWORD context, REFIID iid, void **object){
 if(!(context&CLSCTX_INPROC_SERVER))return CLASS_E_CLASSNOTAVAILABLE;
 for(SIZE_T i=0;i<sizeof(modules)/sizeof(modules[0]);i++){
  HMODULE module=GetModuleHandleW(modules[i]);
  GET_CLASS get=module?(GET_CLASS)GetProcAddress(module,"DllGetClassObject"):NULL;
  if(get&&SUCCEEDED(get(clsid,iid,object)))return S_OK;
 }
 return CLASS_E_CLASSNOTAVAILABLE;
}

HRESULT WINAPI FwbCoGetClassObject(REFCLSID clsid, DWORD context, COSERVERINFO *server, REFIID iid, void **object){
 if(!server&&object&&SUCCEEDED(PrivateClass(clsid,context,iid,object)))return S_OK;
 return CoGetClassObject(clsid,context,server,iid,object);
}

HRESULT WINAPI FwbCoCreateInstance(REFCLSID clsid, IUnknown *outer, DWORD context, REFIID iid, void **object){
 IClassFactory *factory=NULL;
 if(object&&SUCCEEDED(PrivateClass(clsid,context,&IID_IClassFactory,(void**)&factory))){
  HRESULT hr=factory->lpVtbl->CreateInstance(factory,outer,iid,object);
  factory->lpVtbl->Release(factory);
  if(SUCCEEDED(hr))return hr;
 }
 return CoCreateInstance(clsid,outer,context,iid,object);
}

HRESULT WINAPI FwbCoCreateInstanceEx(REFCLSID clsid, IUnknown *outer, DWORD context, COSERVERINFO *server, DWORD count, MULTI_QI *results){
 IClassFactory *factory=NULL;
 if(!server&&count&&results&&SUCCEEDED(PrivateClass(clsid,context,&IID_IClassFactory,(void**)&factory))){
  IUnknown *unknown=NULL;HRESULT hr=factory->lpVtbl->CreateInstance(factory,outer,&IID_IUnknown,(void**)&unknown);
  factory->lpVtbl->Release(factory);
  if(SUCCEEDED(hr)){
   DWORD found=0;
   for(DWORD i=0;i<count;i++){
    results[i].hr=unknown->lpVtbl->QueryInterface(unknown,results[i].pIID,(void**)&results[i].pItf);
    if(SUCCEEDED(results[i].hr))found++;else results[i].pItf=NULL;
   }
   unknown->lpVtbl->Release(unknown);
   return found==count?S_OK:found?CO_S_NOTALLINTERFACES:E_NOINTERFACE;
  }
 }
 return CoCreateInstanceEx(clsid,outer,context,server,count,results);
}
