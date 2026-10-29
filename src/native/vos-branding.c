#define WIN32_LEAN_AND_MEAN
#include <windows.h>

/* VOS lacks winbrand.dll. Supply the formatting entry point used by its
   official Disk Management UI; ownership is GlobalFree at the call site. */
static const WCHAR *replacement(const WCHAR *text, SIZE_T *skip,
                                const WCHAR *product)
{
    static const WCHAR *tokens[] = {L"%WINDOWS_LONG%", L"%WINDOWS_SHORT%"};
    for (unsigned t = 0; t < 2; ++t) {
        SIZE_T n = 0;
        while (tokens[t][n] && text[n] == tokens[t][n]) ++n;
        if (!tokens[t][n]) { *skip = n; return product; }
    }
    *skip = 1;
    return NULL;
}

__declspec(dllexport) LPWSTR WINAPI BrandingFormatString(LPCWSTR format)
{
    if (!format) { SetLastError(ERROR_INVALID_PARAMETER); return NULL; }
    WCHAR product[256] = L"Microsoft Validation OS";
    DWORD size = sizeof(product);
    if (RegGetValueW(HKEY_LOCAL_MACHINE,
            L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",
            L"ProductName", RRF_RT_REG_SZ, NULL, product, &size) != ERROR_SUCCESS)
        lstrcpyW(product, L"Microsoft Validation OS");
    SIZE_T count = 0, skip;
    for (const WCHAR *p = format; *p; p += skip) {
        const WCHAR *value = replacement(p, &skip, product);
        SIZE_T add = value ? (SIZE_T)lstrlenW(value) : 1;
        if (count > ((SIZE_T)-1) / sizeof(WCHAR) - add - 1) {
            SetLastError(ERROR_NOT_ENOUGH_MEMORY); return NULL;
        }
        count += add;
    }
    LPWSTR result = GlobalAlloc(GMEM_FIXED, (count + 1) * sizeof(WCHAR));
    if (!result) return NULL;
    WCHAR *out = result;
    for (const WCHAR *p = format; *p; p += skip) {
        const WCHAR *value = replacement(p, &skip, product);
        if (value) { while (*value) *out++ = *value++; }
        else *out++ = *p;
    }
    *out = 0;
    return result;
}
