#pragma once

/*
The features of LauncherKitBAFunctions.dll (see Feature.h). To add one, implement CFeature in its own file, declare
its factory here and add the factory to vrgpfnCreateFeatures (LauncherKitBAFunctions.cpp), which creates them in order.
*/
typedef CFeature* (*PFN_CREATE_FEATURE)();

// Install, Repair and Uninstall buttons on custom pages of the theme (CustomPageActionButtons.cpp).
CFeature* CreateCustomPageActionButtons();

// Closing the window without the cancel question after the Launch button started the target (CloseAfterLaunch.cpp).
CFeature* CreateCloseAfterLaunch();
