/* SPDX-License-Identifier: LGPL-2.1-or-later
 * ReactOS CRT allocation boundary adapter.
 * __dllonexit algorithm adapted from ReactOS bba112f
 * sdk/lib/crt/stdlib/atexit.c (Wine/ReactOS, LGPL-2.1-or-later).
 * Other exports are forwarded to the unmodified Windows MSVCRT.
 */
#include <windows.h>
typedef int (__cdecl *onexit_fn)(void);
onexit_fn __cdecl __dllonexit(onexit_fn fn,onexit_fn **start,onexit_fn **end){
 if(!start||!*start||!end||!*end)return NULL;
 SIZE_T len=*end-*start;
 if(len>=((SIZE_T)-1)/sizeof(onexit_fn)-1)return NULL;
 ++len;
 onexit_fn *tmp=HeapReAlloc(GetProcessHeap(),0,*start,len*sizeof(onexit_fn));
 if(!tmp)return NULL;
 *start=tmp;*end=tmp+len;tmp[len-1]=fn;return fn;
}
