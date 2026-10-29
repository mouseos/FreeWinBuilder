/* Initialize missing XP-compatible sndvol32 preferences from real mixer devices.
 * Binary layout: ReactOS bba112f base/applications/sndvol32/sndvol32.h.
 * Existing preferences are never overwritten.
 */
#include <windows.h>
#include <mmsystem.h>
#include <wchar.h>

typedef struct {DWORD flags;WCHAR name[MIXER_LONG_NAME_CHARS];} LINE_STATE;
static void add_line(LINE_STATE *states,UINT *count,const MIXERLINEW *line){
 if(!line->cControls||!line->szName[0])return;
 for(UINT i=0;i<*count;i++)if(!wcscmp(states[i].name,line->szName))return;
 lstrcpynW(states[*count].name,line->szName,MIXER_LONG_NAME_CHARS);states[*count].flags=0;(*count)++;
}
DWORD InitializeVolumeConfig(HKEY user){
 DWORD changed=0;UINT devices=mixerGetNumDevs();
 for(UINT id=0;id<devices;id++){
  MIXERCAPSW caps={0};HMIXER mixer=NULL;
  if(mixerGetDevCapsW(id,&caps,sizeof(caps))||mixerOpen(&mixer,id,0,0,0))continue;
  caps.szPname[MAXPNAMELEN-1]=0;
  for(DWORD d=0;d<caps.cDestinations;d++){
   MIXERLINEW dest={0};dest.cbStruct=sizeof(dest);dest.dwDestination=d;
   if(mixerGetLineInfoW((HMIXEROBJ)mixer,&dest,MIXER_OBJECTF_HMIXER|MIXER_GETLINEINFOF_DESTINATION)||dest.cConnections>4096)continue;
   LINE_STATE *states=HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,(dest.cConnections+1)*sizeof(LINE_STATE));if(!states)continue;
   UINT count=0;add_line(states,&count,&dest);
   for(DWORD s=0;s<dest.cConnections;s++){
    MIXERLINEW line={0};line.cbStruct=sizeof(line);line.dwDestination=d;line.dwSource=s;
    if(!mixerGetLineInfoW((HMIXEROBJ)mixer,&line,MIXER_OBJECTF_HMIXER|MIXER_GETLINEINFOF_SOURCE))add_line(states,&count,&line);
   }
   if(count){
    WCHAR path[512];swprintf(path,512,L"Software\\Microsoft\\Windows\\CurrentVersion\\Applets\\Volume Control\\%ls\\%ls",caps.szPname,dest.szName);
    HKEY key=NULL;if(RegCreateKeyExW(user,path,0,NULL,0,KEY_QUERY_VALUE|KEY_SET_VALUE,NULL,&key,NULL)==ERROR_SUCCESS){
     DWORD size=0,type=0;
     if(RegQueryValueExW(key,L"LineStates",NULL,&type,NULL,&size)==ERROR_FILE_NOT_FOUND)
      if(RegSetValueExW(key,L"LineStates",0,REG_BINARY,(BYTE*)states,count*sizeof(LINE_STATE))==ERROR_SUCCESS)changed++;
     RegCloseKey(key);
    }
   }
   HeapFree(GetProcessHeap(),0,states);
  }
  mixerClose(mixer);
 }
 return changed;
}
