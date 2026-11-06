/*
WixStdBA's LaunchButton starts LaunchTarget and then closes the window as the user would, so on a page before any
action (the Modify page) WixStdBA first asks whether to cancel the setup. When the close follows a successful launch,
this closes the window as DefWindowProc would, without that question; the setup then exits with success.
*/

#include "precomp.h"

namespace
{
    class CCloseAfterLaunch : public CFeature
    {
    public:
        BOOL OnBeforeWindowMessage(
            __in HWND hWnd,
            __in UINT uMsg,
            __in WPARAM /*wParam*/,
            __in LPARAM /*lParam*/,
            __out LRESULT* plResult
            ) override
        {
            if (WM_CLOSE != uMsg || !m_fCloseAfterLaunch)
            {
                return FALSE;
            }

            m_fCloseAfterLaunch = FALSE;
            ::DestroyWindow(hWnd);
            *plResult = 0;
            return TRUE;
        }

        void OnAfterWindowMessage(
            __in HWND hWnd,
            __in UINT uMsg,
            __in WPARAM wParam,
            __in LPARAM /*lParam*/,
            __in LRESULT /*lResult*/
            ) override
        {
            const THEME_CONTROL* pControl = GetClickedThemeControl(uMsg, wParam);
            if (pControl && IsThemeName(pControl->sczName, L"LaunchButton"))
            {
                // WixStdBA posts WM_CLOSE only once the target has started.
                MSG msg = { };
                m_fCloseAfterLaunch = ::PeekMessageW(&msg, hWnd, WM_CLOSE, WM_CLOSE, PM_NOREMOVE);
            }
        }

    private:
        BOOL m_fCloseAfterLaunch = FALSE;
    };
}

CFeature* CreateCloseAfterLaunch()
{
    return new CCloseAfterLaunch();
}
