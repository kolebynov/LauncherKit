#pragma once

/*
A self-contained supplement to WixStdBA's behavior. CLauncherKitBAFunctions creates every feature of features.h and
passes it WixStdBA's window once the theme is loaded, then every message of that window before and after WixStdBA
handles it (the window is subclassed, so a feature sees even the clicks of WixStdBA's own buttons, which WixStdBA
does not forward to BAFunctions). Features find theme controls and pages by name, so they work with any theme.
*/
class CFeature
{
public:
    virtual ~CFeature() = default;

    virtual HRESULT OnThemeLoaded(
        __in HWND /*hWnd*/
        )
    {
        return S_OK;
    }

    // Before WixStdBA: returns TRUE (with *plResult) to handle the message instead of WixStdBA and the later features.
    virtual BOOL OnBeforeWindowMessage(
        __in HWND /*hWnd*/,
        __in UINT /*uMsg*/,
        __in WPARAM /*wParam*/,
        __in LPARAM /*lParam*/,
        __out LRESULT* /*plResult*/
        )
    {
        return FALSE;
    }

    // After WixStdBA handled a message that no feature handled before it.
    virtual void OnAfterWindowMessage(
        __in HWND /*hWnd*/,
        __in UINT /*uMsg*/,
        __in WPARAM /*wParam*/,
        __in LPARAM /*lParam*/,
        __in LRESULT /*lResult*/
        )
    {
    }
};

// The theme control a window message reports clicked (thmutil's WM_THMUTIL_CONTROL_WM_COMMAND), or NULL.
inline const THEME_CONTROL* GetClickedThemeControl(
    __in UINT uMsg,
    __in WPARAM wParam
    )
{
    if (WM_THMUTIL_CONTROL_WM_COMMAND != uMsg)
    {
        return NULL;
    }

    const THEME_CONTROLWMCOMMAND_ARGS* pArgs = reinterpret_cast<const THEME_CONTROLWMCOMMAND_ARGS*>(wParam);
    return BN_CLICKED == HIWORD(pArgs->wParam) ? pArgs->pThemeControl : NULL;
}

inline BOOL IsThemeName(
    __in_z_opt LPCWSTR wzActual,
    __in_z LPCWSTR wzName
    )
{
    return wzActual && CSTR_EQUAL == ::CompareStringOrdinal(wzActual, -1, wzName, -1, FALSE);
}
