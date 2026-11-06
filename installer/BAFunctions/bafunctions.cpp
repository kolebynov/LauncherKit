// The BAFunctions entry points WixStdBA calls, as in WiX's BAFunctions sample (src/ext/Bal/Samples/bafunctions).

#include "precomp.h"

static HINSTANCE vhInstance = NULL;

extern "C" BOOL WINAPI DllMain(
    IN HINSTANCE hInstance,
    IN DWORD dwReason,
    IN LPVOID /* pvReserved */
    )
{
    switch (dwReason)
    {
    case DLL_PROCESS_ATTACH:
        ::DisableThreadLibraryCalls(hInstance);
        vhInstance = hInstance;
        break;

    case DLL_PROCESS_DETACH:
        vhInstance = NULL;
        break;
    }

    return TRUE;
}

extern "C" HRESULT WINAPI BAFunctionsCreate(
    __in const BA_FUNCTIONS_CREATE_ARGS* pArgs,
    __inout BA_FUNCTIONS_CREATE_RESULTS* pResults
    )
{
    HRESULT hr = S_OK;

    // Enables the Bal logging functions.
    BalInitialize(pArgs->pEngine);

    hr = CreateBAFunctions(vhInstance, pArgs, pResults);
    BalExitOnFailure(hr, "Failed to create the LauncherKit BAFunctions.");

LExit:
    return hr;
}

extern "C" void WINAPI BAFunctionsDestroy(
    __in const BA_FUNCTIONS_DESTROY_ARGS* /*pArgs*/,
    __inout BA_FUNCTIONS_DESTROY_RESULTS* /*pResults*/
    )
{
    BalUninitialize();
}
