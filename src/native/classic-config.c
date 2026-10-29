#include <windows.h>
#include <stdio.h>
#include <stdint.h>

/* The ReactOS appearance applet requires the classic scheme schema that VOS
   does not populate. Snapshot real user metrics; preserve existing values and
   leave the native ThemeManager settings unchanged. */

static LONG missing(HKEY key,const WCHAR *name,DWORD type,const void *data,DWORD bytes){
 LONG result=RegQueryValueExW(key,name,NULL,NULL,NULL,NULL);
 return result==ERROR_SUCCESS?ERROR_SUCCESS:result==ERROR_FILE_NOT_FOUND?RegSetValueExW(key,name,0,type,data,bytes):result;
}
static LONG text(HKEY key,const WCHAR *name,const WCHAR *value){return missing(key,name,REG_SZ,value,(lstrlenW(value)+1)*sizeof(WCHAR));}
DWORD InitializeClassicTheme(HKEY user){
 NONCLIENTMETRICSW nc={0};ICONMETRICSW icons={0};BOOL flat=FALSE;
 nc.cbSize=sizeof(nc);icons.cbSize=sizeof(icons);
 if(!SystemParametersInfoW(SPI_GETNONCLIENTMETRICS,sizeof(nc),&nc,0)||
    !SystemParametersInfoW(SPI_GETICONMETRICS,sizeof(icons),&icons,0)||
    !SystemParametersInfoW(SPI_GETFLATMENU,0,&flat,0)){
  DWORD error=GetLastError();return error?error:ERROR_GEN_FAILURE;
 }
 HKEY root=NULL,scheme=NULL,size=NULL;LONG result;
 result=RegCreateKeyExW(user,L"Control Panel\\Appearance\\New Schemes",0,NULL,0,KEY_READ|KEY_WRITE,NULL,&root,NULL);if(result)goto done;
 result=RegCreateKeyExW(root,L"FWB0",0,NULL,0,KEY_READ|KEY_WRITE,NULL,&scheme,NULL);if(result)goto done;
 result=RegCreateKeyExW(scheme,L"Sizes\\0",0,NULL,0,KEY_READ|KEY_WRITE,NULL,&size,NULL);if(result)goto done;
 BOOL japanese=PRIMARYLANGID(GetUserDefaultUILanguage())==LANG_JAPANESE;
 result=text(scheme,L"DisplayName",japanese?L"初期設定":L"Initial settings");if(result)goto done;
 result=text(size,L"DisplayName",japanese?L"初期サイズ":L"Initial size");if(result)goto done;
 result=text(root,L"SelectedStyle",L"FWB0");if(result)goto done;
 result=text(scheme,L"SelectedStyle",L"0");if(result)goto done;
 DWORD menu=flat?1:0;result=missing(size,L"FlatMenus",REG_DWORD,&menu,sizeof(menu));if(result)goto done;
 for(int i=0;i<31;i++){
  WCHAR name[32];swprintf(name,32,L"Color #%d",i);DWORD value=GetSysColor(i);
  result=missing(size,name,REG_DWORD,&value,sizeof(value));if(result)goto done;
 }
 const LOGFONTW *fonts[]={&nc.lfCaptionFont,&nc.lfSmCaptionFont,&nc.lfMenuFont,&nc.lfStatusFont,&nc.lfMessageFont,&icons.lfFont};
 for(int i=0;i<6;i++){
  WCHAR name[32];swprintf(name,32,L"Font #%d",i);
  result=missing(size,name,REG_BINARY,fonts[i],sizeof(LOGFONTW));if(result)goto done;
 }
 const int metrics[]={nc.iBorderWidth,nc.iScrollWidth,nc.iScrollHeight,nc.iCaptionWidth,nc.iCaptionHeight,nc.iSmCaptionWidth,nc.iSmCaptionHeight,nc.iMenuWidth,nc.iMenuHeight,GetSystemMetrics(SM_CXICON),icons.iHorzSpacing,icons.iVertSpacing};
 for(int i=0;i<12;i++){
  WCHAR name[32];swprintf(name,32,L"Size #%d",i);uint64_t value=metrics[i];
  result=missing(size,name,REG_QWORD,&value,sizeof(value));if(result)goto done;
 }
done:
 if(size)RegCloseKey(size);if(scheme)RegCloseKey(scheme);if(root)RegCloseKey(root);return result;
}
