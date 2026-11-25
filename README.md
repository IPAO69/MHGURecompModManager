# MHGURecomp Mod Manager

A Windows desktop app for installing and managing mods for MHGURecomp.

The app starts in dark mode and English. Use the buttons in the top-right corner to switch to light mode or Thai. Your preferences are saved for the next launch. The **Open game** button is also in the top-right corner.

## Requirements

- Windows
- .NET 8 Desktop Runtime
- 7-Zip installed to import `.7z` archives

## Use

1. Open `MHGU Mod Manager.exe`.
2. Select the MHGURecomp folder containing `mhgurecomp.exe`.
3. Drop a ZIP/7Z archive or mod folder into the app, or choose **Install ZIP / 7Z** or **Add folder**.
4. Use the switch beside each mod to enable or disable it. Move mods up or down to change their load order.
5. Restart the game for changes to take effect.

ZIP files are extracted with .NET. The app uses 7-Zip to inspect and extract `.7z` archives. It checks archive paths and looks for `romfs/`, `exefs/`, and layouts such as `atmosphere/contents/<title-id>/` before installing a mod under `mods/<mod-name>/`.

Enabled states and load order are saved in `mods/load_order.txt`, which MHGURecomp reads at startup. Removing a mod sends its folder to the Recycle Bin.

## Engine Modules

The app detects an Engine Module when a folder in an imported ZIP/7Z archive or source folder contains both `<module-name>.dll` and at least one `.ini` file side by side. It installs that folder as `engine_modules/<module-name>/`. The DLL name must match the installed folder name so MHGURecomp can load it.

New Engine Modules start disabled. Use the **Engine Modules** tab to inspect the DLL/INI check, enable or disable a module, change its order, remove it, or open the `engine_modules` folder. The switches and order are written to `engine_modules/enabled.txt`; MHGURecomp applies them the next time the game starts. Modules are native code, so only enable modules from sources you trust.

## Build

Install the .NET 8 SDK on Windows, then run:

```powershell
dotnet publish .\MHGUModManager.csproj -c Release --self-contained false
```
