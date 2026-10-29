/* Private theme initializer for the fork's uxtheme (loaded as rotheme.dll).
 * The engine only opens themes after its user API hook callback has run. Windows
 * keeps the session hook for its own uxtheme, so call the engine's documented
 * ReactOS-style callback once with the original user32 functions instead.
 * All other exports are forwarded to the private engine unchanged.
 */
#include <windows.h>
#include <uxtheme.h>

typedef struct {BYTE *bits; DWORD size;} UAHOWP;
typedef struct {
 DWORD size; void *DefWindowProcA,*DefWindowProcW; UAHOWP DefWndProcArray;
 void *GetScrollInfo,*SetScrollInfo,*EnableScrollBar,*AdjustWindowRectEx,*SetWindowRgn,*PreWndProc,*PostWndProc;
 UAHOWP WndProcArray; void *PreDefDlgProc,*PostDefDlgProc; UAHOWP DlgProcArray;
 void *GetSystemMetrics,*SystemParametersInfoA,*SystemParametersInfoW,*ForceResetUserApiHook,
      *DrawFrameControl,*DrawCaption,*MDIRedrawFrame,*GetRealWindowOwner;
} USERAPIHOOK;
typedef BOOL (CALLBACK *INITHOOK)(int,USERAPIHOOK*);
typedef BOOL (WINAPI *ISAPPTHEMED)(void);
typedef HTHEME (WINAPI *OPENTHEME)(HWND,LPCWSTR);
typedef HTHEME (WINAPI *OPENTHEMEEX)(HWND,LPCWSTR,DWORD);
typedef HTHEME (WINAPI *OPENTHEMEDPI)(HWND,LPCWSTR,UINT);

static INIT_ONCE once=INIT_ONCE_STATIC_INIT;
static HMODULE engine;

static BOOL CALLBACK initialize(PINIT_ONCE o,PVOID p,PVOID *c){
 (void)o;(void)p;(void)c;
 engine=LoadLibraryW(L"rotheme.dll");if(!engine)return TRUE;
 INITHOOK init=(INITHOOK)GetProcAddress(engine,"ThemeInitApiHook");if(!init)return TRUE;
 HMODULE user=GetModuleHandleW(L"user32.dll");USERAPIHOOK hook={0};hook.size=sizeof(hook);
 hook.DefWindowProcA=GetProcAddress(user,"DefWindowProcA");hook.DefWindowProcW=GetProcAddress(user,"DefWindowProcW");
 hook.GetScrollInfo=GetProcAddress(user,"GetScrollInfo");hook.SetScrollInfo=GetProcAddress(user,"SetScrollInfo");
 hook.EnableScrollBar=GetProcAddress(user,"EnableScrollBar");hook.AdjustWindowRectEx=GetProcAddress(user,"AdjustWindowRectEx");
 hook.SetWindowRgn=GetProcAddress(user,"SetWindowRgn");hook.GetSystemMetrics=GetProcAddress(user,"GetSystemMetrics");
 hook.SystemParametersInfoA=GetProcAddress(user,"SystemParametersInfoA");hook.SystemParametersInfoW=GetProcAddress(user,"SystemParametersInfoW");
 hook.DrawFrameControl=GetProcAddress(user,"DrawFrameControl");hook.DrawCaption=GetProcAddress(user,"DrawCaption");
 init(0,&hook); /* uahLoadInit: marks the engine's hooks active and loads the configured theme */
 return TRUE;
}
static FARPROC target(const char *name){InitOnceExecuteOnce(&once,initialize,NULL,NULL);return engine?GetProcAddress(engine,name):NULL;}

BOOL WINAPI FwbIsAppThemed(void){ISAPPTHEMED f=(ISAPPTHEMED)target("IsAppThemed");return f?f():FALSE;}
HTHEME WINAPI FwbOpenThemeData(HWND w,LPCWSTR c){OPENTHEME f=(OPENTHEME)target("OpenThemeData");return f?f(w,c):NULL;}
HTHEME WINAPI FwbOpenThemeDataEx(HWND w,LPCWSTR c,DWORD x){OPENTHEMEEX f=(OPENTHEMEEX)target("OpenThemeDataEx");return f?f(w,c,x):NULL;}
HTHEME WINAPI FwbOpenThemeDataForDpi(HWND w,LPCWSTR c,UINT d){OPENTHEMEDPI f=(OPENTHEMEDPI)target("OpenThemeDataForDpi");return f?f(w,c,d):NULL;}
