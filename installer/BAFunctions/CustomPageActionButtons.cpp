/*
WixStdBA starts installing, repairing or uninstalling from its InstallButton, RepairButton and UninstallButton wherever
the theme puts them, but then hides only its own page for its state (Install or Modify) before showing the Progress
page. On a custom page (reached through ChangePageAction), such a button would leave that page's controls over the
Progress page. This hides the custom page shown when one of them is clicked, before WixStdBA starts the action.
*/

#include "precomp.h"

namespace
{
    const LPCWSTR vrgwzActionButtonNames[] = { L"InstallButton", L"RepairButton", L"UninstallButton" };
    const LPCWSTR vrgwzWixStdBAActionPageNames[] = { L"Install", L"Modify" };

    BOOL IsAnyThemeName(
        __in_z_opt LPCWSTR wzActual,
        __in_ecount(cNames) const LPCWSTR* rgwzNames,
        __in DWORD cNames
        )
    {
        for (DWORD i = 0; i < cNames; ++i)
        {
            if (IsThemeName(wzActual, rgwzNames[i]))
            {
                return TRUE;
            }
        }

        return FALSE;
    }

    class CCustomPageActionButtons : public CFeature
    {
    public:
        BOOL OnBeforeWindowMessage(
            __in HWND /*hWnd*/,
            __in UINT uMsg,
            __in WPARAM wParam,
            __in LPARAM /*lParam*/,
            __out LRESULT* /*plResult*/
            ) override
        {
            const THEME_CONTROL* pControl = GetClickedThemeControl(uMsg, wParam);
            if (!pControl || !IsAnyThemeName(pControl->sczName, vrgwzActionButtonNames, countof(vrgwzActionButtonNames)))
            {
                return FALSE;
            }

            THEME* pTheme = pControl->pTheme;
            const THEME_PAGE* pPage = ThemeGetPage(pTheme, pTheme->dwCurrentPageId);
            if (pPage && !IsAnyThemeName(pPage->sczName, vrgwzWixStdBAActionPageNames, countof(vrgwzWixStdBAActionPageNames)))
            {
                // Saves the page's variables, as WixStdBA does for the page it hides.
                HRESULT hr = ThemeShowPageEx(pTheme, pTheme->dwCurrentPageId, SW_HIDE, THEME_SHOW_PAGE_REASON_DEFAULT);
                if (FAILED(hr))
                {
                    BalLogError(hr, "Failed to hide page '%ls' before the action of '%ls'.", pPage->sczName, pControl->sczName);
                }
            }

            // WixStdBA starts the action.
            return FALSE;
        }
    };
}

CFeature* CreateCustomPageActionButtons()
{
    return new CCustomPageActionButtons();
}
