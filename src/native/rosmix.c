/* Process-private WinMM/Core Audio bridge for ReactOS sndvol32.
 * Only destination volume/mute controls are mapped to the selected endpoint.
 * Other mixer controls retain native WinMM behavior.
 */
#define COBJMACROS
#define INITGUID
#include <windows.h>
#include <mmsystem.h>
#include <mmddk.h>
#include <mmdeviceapi.h>
#include <endpointvolume.h>

typedef struct CLIENT {struct CLIENT *next; HMIXER mixer; HWND window;} CLIENT;
static SRWLOCK lock=SRWLOCK_INIT;
static CLIENT *clients;
typedef struct {MIXERCONTROLW control; IAudioEndpointVolume *volume; BOOL uninit; UINT mixer;} ENDPOINT;

static void end_endpoint(ENDPOINT *e){
 if(e->volume)IAudioEndpointVolume_Release(e->volume);
 if(e->uninit)CoUninitialize();
}

/* 0: ordinary session control; 1: endpoint control; -1: endpoint resolution failed.
 * Never silently redirect an endpoint failure to a process-session volume.
 */
static int begin_endpoint(HMIXEROBJ object,DWORD controlId,DWORD flags,ENDPOINT *out){
 ZeroMemory(out,sizeof(*out));
 if(mixerGetID(object,&out->mixer,flags&MIXER_OBJECTF_HANDLE))return -1;
 MIXERLINECONTROLSW one={0};one.cbStruct=sizeof(one);one.dwControlID=controlId;
 one.cControls=1;one.cbmxctrl=sizeof(out->control);one.pamxctrl=&out->control;
 if(mixerGetLineControlsW(object,&one,MIXER_OBJECTF_HMIXER|MIXER_GETLINECONTROLSF_ONEBYID))return -1;
 if(out->control.dwControlType!=MIXERCONTROL_CONTROLTYPE_VOLUME&&out->control.dwControlType!=MIXERCONTROL_CONTROLTYPE_MUTE)return 0;
 MIXERCAPSW caps={0};if(mixerGetDevCapsW(out->mixer,&caps,sizeof(caps)))return -1;
 BOOL destinationControl=FALSE;
 for(DWORD d=0;d<caps.cDestinations&&!destinationControl;d++){
  MIXERLINEW line={0};line.cbStruct=sizeof(line);line.dwDestination=d;
  if(mixerGetLineInfoW(object,&line,MIXER_OBJECTF_HMIXER|MIXER_GETLINEINFOF_DESTINATION))continue;
  if(!line.cControls||line.cControls>4096)continue;
  MIXERCONTROLW *controls=HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,line.cControls*sizeof(*controls));if(!controls)return -1;
  MIXERLINECONTROLSW all={0};all.cbStruct=sizeof(all);all.dwLineID=line.dwLineID;all.cControls=line.cControls;all.cbmxctrl=sizeof(*controls);all.pamxctrl=controls;
  if(!mixerGetLineControlsW(object,&all,MIXER_OBJECTF_HMIXER|MIXER_GETLINECONTROLSF_ALL))
   for(DWORD c=0;c<line.cControls;c++)if(controls[c].dwControlID==controlId)destinationControl=TRUE;
  HeapFree(GetProcessHeap(),0,controls);
 }
 if(!destinationControl)return 0;
 WCHAR *endpointId=NULL;UINT matches=0;
 for(int capture=0;capture<2;capture++){
  UINT count=capture?waveInGetNumDevs():waveOutGetNumDevs();
  for(UINT id=0;id<count;id++){
   UINT mixer=~0u;
   if(mixerGetID((HMIXEROBJ)(UINT_PTR)id,&mixer,capture?MIXER_OBJECTF_WAVEIN:MIXER_OBJECTF_WAVEOUT)||mixer!=out->mixer)continue;
   DWORD bytes=0;
   MMRESULT r=capture?waveInMessage((HWAVEIN)(UINT_PTR)id,DRV_QUERYFUNCTIONINSTANCEIDSIZE,(DWORD_PTR)&bytes,0):waveOutMessage((HWAVEOUT)(UINT_PTR)id,DRV_QUERYFUNCTIONINSTANCEIDSIZE,(DWORD_PTR)&bytes,0);
   if(r||bytes<sizeof(WCHAR)||bytes>65536)goto fail;
   WCHAR *value=HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,bytes);if(!value)goto fail;
   r=capture?waveInMessage((HWAVEIN)(UINT_PTR)id,DRV_QUERYFUNCTIONINSTANCEID,(DWORD_PTR)value,bytes):waveOutMessage((HWAVEOUT)(UINT_PTR)id,DRV_QUERYFUNCTIONINSTANCEID,(DWORD_PTR)value,bytes);
   if(r){HeapFree(GetProcessHeap(),0,value);goto fail;}
   if(endpointId)HeapFree(GetProcessHeap(),0,endpointId);endpointId=value;matches++;
  }
 }
 if(matches!=1)goto fail;
 HRESULT init=CoInitializeEx(NULL,COINIT_APARTMENTTHREADED);
 if(FAILED(init)&&init!=RPC_E_CHANGED_MODE)goto fail;
 out->uninit=SUCCEEDED(init);
 IMMDeviceEnumerator *enumerator=NULL;IMMDevice *device=NULL;
 HRESULT hr=CoCreateInstance(&CLSID_MMDeviceEnumerator,NULL,CLSCTX_INPROC_SERVER,&IID_IMMDeviceEnumerator,(void**)&enumerator);
 if(SUCCEEDED(hr))hr=IMMDeviceEnumerator_GetDevice(enumerator,endpointId,&device);
 if(SUCCEEDED(hr))hr=IMMDevice_Activate(device,&IID_IAudioEndpointVolume,CLSCTX_INPROC_SERVER,NULL,(void**)&out->volume);
 if(device)IMMDevice_Release(device);if(enumerator)IMMDeviceEnumerator_Release(enumerator);
 HeapFree(GetProcessHeap(),0,endpointId);
 if(FAILED(hr)){end_endpoint(out);ZeroMemory(out,sizeof(*out));return -1;}return 1;
fail:
 if(endpointId)HeapFree(GetProcessHeap(),0,endpointId);return -1;
}

static MMRESULT details(HMIXEROBJ object,LPMIXERCONTROLDETAILS query,DWORD flags,BOOL set){
 if(!query||query->cbStruct!=sizeof(*query)||!query->paDetails||!query->cChannels||query->cMultipleItems)return MMSYSERR_INVALPARAM;
 if(flags&0xF)return set?mixerSetControlDetails(object,query,flags):mixerGetControlDetailsW(object,query,flags);
 if(flags&0x70000000u)return set?mixerSetControlDetails(object,query,flags):mixerGetControlDetailsW(object,query,flags);
 ENDPOINT e;int found=begin_endpoint(object,query->dwControlID,flags,&e);
 if(!found)return set?mixerSetControlDetails(object,query,flags):mixerGetControlDetailsW(object,query,flags);
 if(found<0)return MMSYSERR_ERROR;
 HRESULT hr=S_OK;
 if(e.control.dwControlType==MIXERCONTROL_CONTROLTYPE_MUTE){
  if(query->cbDetails!=sizeof(MIXERCONTROLDETAILS_BOOLEAN)||query->cChannels!=1){end_endpoint(&e);return MMSYSERR_INVALPARAM;}
  MIXERCONTROLDETAILS_BOOLEAN *value=query->paDetails;
  if(set)hr=IAudioEndpointVolume_SetMute(e.volume,value->fValue!=0,NULL);
  else {BOOL mute=FALSE;hr=IAudioEndpointVolume_GetMute(e.volume,&mute);if(SUCCEEDED(hr))value->fValue=mute;}
 }else{
  UINT channels=0;hr=IAudioEndpointVolume_GetChannelCount(e.volume,&channels);
  if(query->cbDetails!=sizeof(MIXERCONTROLDETAILS_UNSIGNED)||query->cChannels>channels){end_endpoint(&e);return MMSYSERR_INVALPARAM;}
  MIXERCONTROLDETAILS_UNSIGNED *values=query->paDetails;
  DWORD min=e.control.Bounds.dwMinimum,max=e.control.Bounds.dwMaximum;
  if(max<=min){end_endpoint(&e);return MMSYSERR_ERROR;}
  for(DWORD c=0;c<query->cChannels&&SUCCEEDED(hr);c++){
   float scalar=0;
   if(set){
    if(values[c].dwValue<min||values[c].dwValue>max){hr=E_INVALIDARG;break;}
    scalar=(float)((double)(values[c].dwValue-min)/(max-min));
    hr=query->cChannels==1?IAudioEndpointVolume_SetMasterVolumeLevelScalar(e.volume,scalar,NULL):IAudioEndpointVolume_SetChannelVolumeLevelScalar(e.volume,c,scalar,NULL);
   }else{
    hr=query->cChannels==1?IAudioEndpointVolume_GetMasterVolumeLevelScalar(e.volume,&scalar):IAudioEndpointVolume_GetChannelVolumeLevelScalar(e.volume,c,&scalar);
    if(SUCCEEDED(hr))values[c].dwValue=min+(DWORD)((double)scalar*(max-min)+0.5);
   }
  }
 }
 UINT mixerId=e.mixer;end_endpoint(&e);
 if(FAILED(hr))return MMSYSERR_ERROR;
 if(set){
  AcquireSRWLockShared(&lock);
  for(CLIENT *client=clients;client;client=client->next){UINT id=~0u;if(client->window&&!mixerGetID((HMIXEROBJ)client->mixer,&id,MIXER_OBJECTF_HMIXER)&&id==mixerId)PostMessageW(client->window,MM_MIXM_CONTROL_CHANGE,(WPARAM)client->mixer,query->dwControlID);}
  ReleaseSRWLockShared(&lock);
 }
 return MMSYSERR_NOERROR;
}
MMRESULT WINAPI FwbGetControlDetails(HMIXEROBJ object,LPMIXERCONTROLDETAILS query,DWORD flags){return details(object,query,flags,FALSE);}
MMRESULT WINAPI FwbSetControlDetails(HMIXEROBJ object,LPMIXERCONTROLDETAILS query,DWORD flags){return details(object,query,flags,TRUE);}
MMRESULT WINAPI FwbMixerOpen(LPHMIXER output,UINT id,DWORD_PTR callback,DWORD_PTR instance,DWORD flags){
 MMRESULT result=mixerOpen(output,id,callback,instance,flags);if(result)return result;
 CLIENT *client=HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,sizeof(*client));if(!client){mixerClose(*output);*output=NULL;return MMSYSERR_NOMEM;}
 client->mixer=*output;client->window=(flags&CALLBACK_TYPEMASK)==CALLBACK_WINDOW?(HWND)callback:NULL;AcquireSRWLockExclusive(&lock);client->next=clients;clients=client;ReleaseSRWLockExclusive(&lock);return result;
}
MMRESULT WINAPI FwbGetDevCaps(UINT_PTR id,LPMIXERCAPSW caps,UINT size){
 MMRESULT result=mixerGetDevCapsW(id,caps,size);if(!result||id>MAXDWORD)return result;
 // ReactOS mmsys uses PtrToUint(HMIXER), truncating Windows x64 mixer handles.
 // Recover only handles opened through this private bridge, with no ambiguity.
 UINT device=~0u;unsigned matches=0;AcquireSRWLockShared(&lock);
 for(CLIENT *client=clients;client;client=client->next)
  if((DWORD)(UINT_PTR)client->mixer==(DWORD)id){matches++;if(mixerGetID((HMIXEROBJ)client->mixer,&device,MIXER_OBJECTF_HMIXER))device=~0u;}
 ReleaseSRWLockShared(&lock);
 return matches==1&&device!=~0u?mixerGetDevCapsW(device,caps,size):result;
}
MMRESULT WINAPI FwbMixerClose(HMIXER mixer){
 MMRESULT result=mixerClose(mixer);if(result)return result;
 AcquireSRWLockExclusive(&lock);CLIENT **link=&clients;
 while(*link){if((*link)->mixer==mixer){CLIENT *old=*link;*link=old->next;HeapFree(GetProcessHeap(),0,old);break;}link=&(*link)->next;}
 ReleaseSRWLockExclusive(&lock);return result;
}
