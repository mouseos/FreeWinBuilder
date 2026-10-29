/* Private ntdll adapter for the fork's shell32.
 * ReactOS RtlGUIDFromString reads the leading 38 characters, so the shell parses
 * nested names such as "::{CLSID}\::{CLSID}" with it. Windows requires the whole
 * string to be the GUID. Give Windows only the leading GUID; forward all else.
 */
#include <windows.h>
#include <winternl.h>

NTSYSAPI NTSTATUS NTAPI RtlGUIDFromString(PUNICODE_STRING, GUID *);

NTSTATUS NTAPI FwbRtlGUIDFromString(PUNICODE_STRING text, GUID *guid){
 if(text&&text->Buffer&&text->Length>38*sizeof(WCHAR)&&text->Buffer[0]==L'{'&&text->Buffer[37]==L'}'){
  UNICODE_STRING head=*text;
  head.Length=head.MaximumLength=38*sizeof(WCHAR);
  return RtlGUIDFromString(&head,guid);
 }
 return RtlGUIDFromString(text,guid);
}
