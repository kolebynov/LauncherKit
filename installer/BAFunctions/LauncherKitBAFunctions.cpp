// Hosts the features (features.h): creates them, subclasses WixStdBA's window and passes them its messages.

#include "precomp.h"

namespace
{
    const PFN_CREATE_FEATURE vrgpfnCreateFeatures[] =
    {
        CreateCustomPageActionButtons,
        CreateCloseAfterLaunch,
    };

    const UINT_PTR WINDOW_SUBCLASS_ID = 1;

    class CLauncherKitBAFunctions : public CBalBaseBAFunctions
    {
    public:
        CLauncherKitBAFunctions(
            __in HMODULE hModule
            ) : CBalBaseBAFunctions(hModule)
        {
        }

        ~CLauncherKitBAFunctions() override
        {
            for (CFeature* pFeature : m_rgpFeatures)
            {
                delete pFeature;
            }
        }

        HRESULT CreateFeatures()
        {
            HRESULT hr = S_OK;

            for (DWORD i = 0; i < countof(vrgpfnCreateFeatures); ++i)
            {
                m_rgpFeatures[i] = vrgpfnCreateFeatures[i]();
                BalExitOnNull(m_rgpFeatures[i], hr, E_OUTOFMEMORY, "Failed to create BAFunctions feature %u.", i);
            }

        LExit:
            return hr;
        }

    public: // IBAFunctions
        STDMETHODIMP OnThemeLoaded(
            __in HWND hWnd
            ) override
        {
            HRESULT hr = CBalBaseBAFunctions::OnThemeLoaded(hWnd);
            BalExitOnFailure(hr, "Failed to initialize the BAFunctions window.");

            if (!::SetWindowSubclass(hWnd, WindowSubclassProc, WINDOW_SUBCLASS_ID, reinterpret_cast<DWORD_PTR>(this)))
            {
                BalExitWithRootFailure(hr, E_FAIL, "Failed to subclass the WixStdBA window.");
            }

            for (CFeature* pFeature : m_rgpFeatures)
            {
                hr = pFeature->OnThemeLoaded(hWnd);
                BalExitOnFailure(hr, "A BAFunctions feature failed to initialize.");
            }

        LExit:
            return hr;
        }

    private:
        static LRESULT CALLBACK WindowSubclassProc(
            __in HWND hWnd,
            __in UINT uMsg,
            __in WPARAM wParam,
            __in LPARAM lParam,
            __in UINT_PTR /*uIdSubclass*/,
            __in DWORD_PTR dwRefData
            )
        {
            CLauncherKitBAFunctions* pThis = reinterpret_cast<CLauncherKitBAFunctions*>(dwRefData);
            LRESULT lResult = 0;

            for (CFeature* pFeature : pThis->m_rgpFeatures)
            {
                if (pFeature->OnBeforeWindowMessage(hWnd, uMsg, wParam, lParam, &lResult))
                {
                    return lResult;
                }
            }

            if (WM_NCDESTROY == uMsg)
            {
                ::RemoveWindowSubclass(hWnd, WindowSubclassProc, WINDOW_SUBCLASS_ID);
            }

            lResult = ::DefSubclassProc(hWnd, uMsg, wParam, lParam);

            for (CFeature* pFeature : pThis->m_rgpFeatures)
            {
                pFeature->OnAfterWindowMessage(hWnd, uMsg, wParam, lParam, lResult);
            }

            return lResult;
        }

        CFeature* m_rgpFeatures[countof(vrgpfnCreateFeatures)] = { };
    };
}

HRESULT WINAPI CreateBAFunctions(
    __in HMODULE hModule,
    __in const BA_FUNCTIONS_CREATE_ARGS* pArgs,
    __inout BA_FUNCTIONS_CREATE_RESULTS* pResults
    )
{
    HRESULT hr = S_OK;
    CLauncherKitBAFunctions* pBAFunctions = new CLauncherKitBAFunctions(hModule);
    BalExitOnNull(pBAFunctions, hr, E_OUTOFMEMORY, "Failed to create the LauncherKit BAFunctions object.");

    hr = pBAFunctions->CreateFeatures();
    BalExitOnFailure(hr, "Failed to create the BAFunctions features.");

    hr = pBAFunctions->OnCreate(pArgs->pEngine, pArgs->pCommand);
    BalExitOnFailure(hr, "Failed to initialize the LauncherKit BAFunctions.");

    pResults->pfnBAFunctionsProc = BalBaseBAFunctionsProc;
    pResults->pvBAFunctionsProcContext = pBAFunctions;
    pBAFunctions = NULL;

LExit:
    ReleaseObject(pBAFunctions);

    return hr;
}
