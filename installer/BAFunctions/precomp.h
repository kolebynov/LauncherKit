#pragma once

#include <windows.h>
#include <CommCtrl.h>
#include <Richedit.h>
#include <strsafe.h>

#pragma warning(push)
#pragma warning(disable:4458) // declaration of 'xxx' hides class member
#include <gdiplus.h>
#pragma warning(pop)

#include "dutil.h"
#include "dictutil.h"
#include "locutil.h"
#include "strutil.h"
#include "thmutil.h"

#include "BootstrapperApplicationBase.h"
#include "BAFunctions.h"
#include "IBAFunctions.h"
#include "BalBaseBAFunctions.h"
#include "BalBaseBAFunctionsProc.h"

#include "Feature.h"
#include "features.h"

HRESULT WINAPI CreateBAFunctions(
    __in HMODULE hModule,
    __in const BA_FUNCTIONS_CREATE_ARGS* pArgs,
    __inout BA_FUNCTIONS_CREATE_RESULTS* pResults
    );
