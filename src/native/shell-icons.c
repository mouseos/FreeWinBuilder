/* Icons from the matching ReactOS shell's own resources; system DLLs stay intact. */
#define COBJMACROS
#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>
#include <objbase.h>
#include <stdio.h>
#include <wchar.h>

static BOOL SetMissingIcon(HKEY key,const WCHAR *name,const WCHAR *replacement){
 WCHAR value[MAX_PATH+32],expanded[MAX_PATH+32];DWORD bytes=sizeof(value),type=0;
 LONG status=RegQueryValueExW(key,name,NULL,&type,(BYTE*)value,&bytes);
 if(status==ERROR_SUCCESS&&(type==REG_SZ||type==REG_EXPAND_SZ)){
  value[_countof(value)-1]=0;
  DWORD length=ExpandEnvironmentStringsW(value,expanded,_countof(expanded));
  if(length&&length<=_countof(expanded)){
   WCHAR *comma=wcsrchr(expanded,L',');int index=0;if(comma){*comma=0;index=(int)wcstol(comma+1,NULL,10);}
   WCHAR *path=expanded;if(*path==L'"'){path++;WCHAR *quote=wcsrchr(path,L'"');if(quote)*quote=0;}
   HICON icon=NULL;if(ExtractIconExW(path,index,&icon,NULL,1)==1&&icon){DestroyIcon(icon);return FALSE;}
  }
 }else if(status!=ERROR_FILE_NOT_FOUND)return FALSE;
 return RegSetValueExW(key,name,0,REG_SZ,(const BYTE*)replacement,(DWORD)((wcslen(replacement)+1)*sizeof(WCHAR)))==ERROR_SUCCESS;
}

HRESULT InitializeUserFilesShortcut(const WCHAR *folder){
 WCHAR profile[MAX_PATH],desktop[MAX_PATH],path[MAX_PATH],target[MAX_PATH],arguments[MAX_PATH+16],icon[MAX_PATH];
 DWORD length=GetEnvironmentVariableW(L"USERPROFILE",profile,MAX_PATH);
 if(!length)return HRESULT_FROM_WIN32(ERROR_ENVVAR_NOT_FOUND);
 if(length>=MAX_PATH)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
 DWORD attributes=GetFileAttributesW(profile);
 if(attributes==INVALID_FILE_ATTRIBUTES)return HRESULT_FROM_WIN32(GetLastError());
 if(!(attributes&FILE_ATTRIBUTE_DIRECTORY))return HRESULT_FROM_WIN32(ERROR_DIRECTORY);
 HRESULT hr=SHGetFolderPathW(NULL,CSIDL_DESKTOPDIRECTORY|CSIDL_FLAG_CREATE,NULL,SHGFP_TYPE_CURRENT,desktop);
 if(FAILED(hr))return hr;
 BOOL japanese=PRIMARYLANGID(GetUserDefaultUILanguage())==LANG_JAPANESE;
 if(swprintf(path,MAX_PATH,L"%ls\\%ls.lnk",desktop,japanese?L"ユーザーファイル":L"User files")<0||
    swprintf(target,MAX_PATH,L"%ls\\explorer.exe",folder)<0||
    swprintf(icon,MAX_PATH,L"%ls\\roshell.dll",folder)<0)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
 if(GetFileAttributesW(path)!=INVALID_FILE_ATTRIBUTES)return S_OK;
 swprintf(arguments,_countof(arguments),L"/n,\"%ls\"",profile);
 hr=CoInitializeEx(NULL,COINIT_APARTMENTTHREADED);if(FAILED(hr))return hr;
 IShellLinkW *link=NULL;IPersistFile *persist=NULL;
 hr=CoCreateInstance(&CLSID_ShellLink,NULL,CLSCTX_INPROC_SERVER,&IID_IShellLinkW,(void**)&link);
 if(SUCCEEDED(hr))hr=IShellLinkW_SetPath(link,target);
 if(SUCCEEDED(hr))hr=IShellLinkW_SetArguments(link,arguments);
 if(SUCCEEDED(hr))hr=IShellLinkW_SetWorkingDirectory(link,profile);
 if(SUCCEEDED(hr))hr=IShellLinkW_SetIconLocation(link,icon,-235);
 if(SUCCEEDED(hr))hr=IShellLinkW_SetDescription(link,japanese?L"ユーザーのファイルを開く":L"Open your files");
 if(SUCCEEDED(hr))hr=IShellLinkW_QueryInterface(link,&IID_IPersistFile,(void**)&persist);
 if(SUCCEEDED(hr))hr=IPersistFile_Save(persist,path,TRUE);
 if(persist)IPersistFile_Release(persist);
 if(link)IShellLinkW_Release(link);
 CoUninitialize();return hr;
}

DWORD InitializeShellIcons(const WCHAR *folder){
 static const struct {const WCHAR *clsid;int icon;} icons[]={
  {L"{20D04FE0-3AEA-1069-A2D8-08002B30309D}",-16},
  {L"{645FF040-5081-101B-9F08-00AA002F954E}",-32},
  {L"{208D2C60-3AEA-1069-A2D7-08002B30309D}",-18},
  {L"{21EC2020-3AEA-1069-A2DD-08002B30309D}",-137},
  {L"{26EE0668-A00A-44D7-9371-BEB064C98683}",-137},
  {L"{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}",-137},
  {L"{450D8FBA-AD25-11D0-98A8-0800361B1103}",-235},
  {L"{59031A47-3F72-44A7-89C5-5595FE6B30EE}",-235}
 };
 WCHAR dll[MAX_PATH];if(swprintf(dll,MAX_PATH,L"%ls\\roshell.dll",folder)<0)return 0;
 DWORD count=0;
 for(UINT i=0;i<sizeof(icons)/sizeof(icons[0]);i++){
  HICON check=NULL;if(ExtractIconExW(dll,icons[i].icon,&check,NULL,1)!=1||!check)continue;DestroyIcon(check);
  for(UINT priority=0;priority<2;priority++){
   WCHAR path[200],value[MAX_PATH+32];swprintf(path,200,priority?L"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CLSID\\%ls\\DefaultIcon":L"Software\\Classes\\CLSID\\%ls\\DefaultIcon",icons[i].clsid);
   swprintf(value,MAX_PATH+32,L"%ls,%d",dll,icons[i].icon);
   HKEY key=NULL;if(RegCreateKeyExW(HKEY_CURRENT_USER,path,0,NULL,0,KEY_QUERY_VALUE|KEY_SET_VALUE,NULL,&key,NULL)!=ERROR_SUCCESS)continue;
   if(SetMissingIcon(key,NULL,value))count++;
   if(icons[i].icon==-32){
    if(SetMissingIcon(key,L"Empty",value))count++;
    swprintf(value,MAX_PATH+32,L"%ls,-33",dll);if(SetMissingIcon(key,L"Full",value))count++;
   }
   RegCloseKey(key);
  }
 }
 return count;
}
