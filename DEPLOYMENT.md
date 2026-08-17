# Sunshine Alley Launcher — cross-platform deployment

This patch replaces the .NET 7 Windows Forms launcher with a .NET 10 / Avalonia 12 application. It separates platform-neutral launcher behavior from Windows, Linux, and macOS integration, retains the existing Sunshine Alley API field names, and provides both a GUI and a command-line launch harness.

## 0. Apply the patch

The supplied binary Git patch targets the archive's original commit `ee6b03073d221ea826a82abfa55ad67989f71ae7`. Start from a clean checkout of that commit, extract the patch ZIP outside the repository, and run:

```bash
git switch -c avalonia-cross-platform
git apply --index --binary /path/to/SunshineAlley-CrossPlatform-Migration.patch
git status --short
```

Review the staged changes, then commit them normally. `--binary` is required because the patch relocates the existing image/icon assets. The old archive also contains generated `bin`, `obj`, `.vs`, publish output, and an untracked `SALauncher.pfx`; none belongs in source control or in the patch. Delete those local build outputs, and rotate the PFX if it was ever distributed.

## 1. Resulting solution

| Project | Responsibility |
| --- | --- |
| `SunshineAlley.Core` | Server DTOs, signed API requests, RSA identity, settings contracts, file hashing, secure mod synchronization, models, and path containment. It has no Avalonia, registry, Keychain, WinForms, or OS process-discovery code. |
| `SunshineAlley.Platform` | JSON settings, legacy registry migration, OS device identity, OS secret stores, Steam discovery, native BepInEx/Doorstop launch strategies, process monitoring, and shell integration. |
| `SunshineAlley.App` | The Avalonia desktop GUI: launcher, settings, and optional-mod windows. |
| `SunshineAlley.Cli` | A GUI-independent diagnostic, verify, and launch harness. Use this first on every target OS. |
| `SunshineAlley.SmokeTests` | Dependency-free executable smoke tests for device GUID derivation, settings, path containment, hashing, and legacy RSA XML conversion. |

The old Windows Forms project, COM shortcut dependency, embedded signing PFX reference, self-copying installer, and self-replacing `.exe` updater are removed. Installation and updates are now handled by RID-specific release packages. Non-secret values in the legacy Windows registry key are retained during migration; the migrated private RSA value is deleted from the registry after Credential Manager accepts it.

## 2. Toolchain and NuGet requirements

Install the .NET 10 SDK. `global.json` requests `10.0.100` and rolls forward within later .NET 10 feature bands.

The only external NuGet dependencies are centrally pinned in `Directory.Packages.props`:

| Package | Version | Used by |
| --- | ---: | --- |
| `Avalonia` | `12.1.1` | GUI framework and XAML compiler |
| `Avalonia.Desktop` | `12.1.1` | Win32, X11/Wayland, and native macOS desktop backends |
| `Avalonia.Themes.Fluent` | `12.1.1` | Fluent controls/theme |
| `Avalonia.Fonts.Inter` | `12.1.1` | Bundled application font |

No Windows Desktop workload, macOS workload, COM reference, Newtonsoft.Json package, MVVM package, or native secret-store NuGet package is required.

Build settings that matter:

- all projects target `net10.0`, not `net10.0-windows`;
- runtime identifiers are `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`;
- releases are self-contained and single-file per RID;
- trimming is disabled because Avalonia/XAML and native platform assets are not a safe place to begin an aggressive trimming rollout;
- the application runs as the current user (`asInvoker`) and never asks for elevation;
- signing credentials are external release secrets and are not stored in the repository.

## 3. Restore, build, and test

From the repository root:

```bash
dotnet --info
dotnet restore "Sunshine Alley Launcher.sln"
dotnet build "Sunshine Alley Launcher.sln" -c Release --no-restore
dotnet run --project tests/SunshineAlley.SmokeTests/SunshineAlley.SmokeTests.csproj -c Release --no-restore
```

Run the GUI on the current development OS:

```bash
dotnet run --project src/SunshineAlley.App/SunshineAlley.App.csproj
```

The included GitHub Actions workflow builds all four target RIDs on Windows 2025, Ubuntu 24.04, macOS 15 Intel, and macOS 15 Apple Silicon runners. `scripts/publish-all.sh` and `scripts/publish-all.ps1` perform a local restore, smoke test, publish, and basic archive packaging.

```bash
./scripts/publish-all.sh
```

or:

```powershell
./scripts/publish-all.ps1
```

Output is written beneath `artifacts/publish` and `artifacts/packages`.

## 4. OS runtime prerequisites

### Windows 10/11 x64

The self-contained build has no separate .NET requirement. Windows Credential Manager stores the RSA private key. Steam is discovered through `HKCU\SOFTWARE\Valve\Steam` with the conventional Program Files location as fallback.

The launcher preserves the exact `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` value previously sent by the Windows Forms launcher. That means existing server-side Windows device records and bans continue to address the same identifier.

### Desktop Linux x64

Install Avalonia's desktop libraries and the Secret Service CLI. On Debian/Ubuntu:

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libsecret-tools
```

An unlocked freedesktop Secret Service provider is required in the user's desktop session (for example GNOME Keyring or another compatible provider). The launcher refuses to put the RSA private key in JSON or a plaintext fallback file when Secret Service is unavailable.

Steam locations checked include the normal `~/.steam/steam`, `~/.local/share/Steam`, and Flatpak Steam trees.
For Flatpak Steam, the generated wrapper and external mod-data path must also be visible inside the sandbox. Either keep mod data in a Steam-visible library location or grant the narrow directory explicitly, for example `flatpak override --user --filesystem=/absolute/mod-data/path com.valvesoftware.Steam`.

### macOS 12+ (Intel and Apple Silicon)

The self-contained build uses the native Avalonia macOS backend. The RSA private key is stored with the Security.framework Keychain API. The user can receive a normal Keychain access prompt on first use.

Intel and Apple Silicon need separate builds unless you combine and sign them as a universal application in your release pipeline.

## 5. Settings, identity, and RSA migration

Settings move from the registry into an atomic JSON file:

| OS | Settings directory |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Sunshine Alley\Launcher` |
| Linux | `${XDG_CONFIG_HOME:-~/.config}/sunshine-alley/launcher` |
| macOS | `~/Library/Application Support/Sunshine Alley/Launcher` |

Downloaded modpack data defaults to the corresponding per-user data directory and can be changed in Settings.

On the first Windows run:

1. the exact Windows MachineGuid is read;
2. values under `HKCU\SOFTWARE\Sunshine Alley` are copied into JSON;
3. the matching legacy XML RSA private key is converted to PKCS#8, written to Windows Credential Manager, and then removed from the registry;
4. the public XML key and signature remain non-secret JSON settings;
5. other, non-secret values in the old registry key are left in place for transition/rollback;
6. a migration marker prevents repeated work.

New platform device identifiers are chosen in this order:

| OS | Stable source |
| --- | --- |
| Windows | The unchanged registry `MachineGuid` |
| Linux | DMI `product_uuid`, then `/etc/machine-id`, transformed deterministically into a UUIDv8-shaped GUID |
| macOS | IOKit's `IOPlatformUUID` |
| Any OS fallback | One generated GUID persisted in the JSON settings store |

A MAC address is intentionally not used: it changes with adapters, privacy randomization, docks, and VMs, and is trivial to spoof. OS machine identifiers are more stable, but no client-side hardware identifier is cheat-proof. A user with administrator/root access can still clone or replace it, so device bans should remain one signal alongside account, server, and behavioral controls.

Secret locations:

| OS | Private-key facility |
| --- | --- |
| Windows | Credential Manager generic credential, local-machine persistence |
| Linux | freedesktop Secret Service through `secret-tool`; the secret is passed over stdin, not command arguments |
| macOS | Login Keychain through Security.framework |

The former `SALauncher.pfx` is not referenced. Delete it from old working copies and release archives. If that certificate/private key was ever distributed, rotate it rather than reusing it for release signing.

## 6. Required server modpack layout

The existing `LauncherServerList` and `LauncherVerifyFiles` operations are retained. `LauncherVerifyFiles` now also receives `RuntimeIdentifier`; an existing backend that ignores unknown JSON fields remains compatible, while an updated backend can filter native payloads by RID.

Each modpack should contain this logical layout (directory matching is case-insensitive in the resolver, but consistent casing is strongly recommended):

```text
ModPacks/<id>/
  BepInEx/
    core/
      BepInEx.Preloader.dll
    config/
    plugins/
  GameEssentials/
    defaultconfig/
      Some.Plugin.cfg.txt
    win-x64/
      winhttp.dll
      doorstop_libs/
        ...Windows Doorstop files...
    linux-x64/
      doorstop_libs/
        libdoorstop_x64.so
    osx-x64/
      doorstop_libs/
        libdoorstop_x64.dylib
    osx-arm64/
      doorstop_libs/
        libdoorstop_arm64.dylib
```

`BepInEx.Unity.Mono.Preloader.dll` is also recognized for layouts using that name. The old Windows layout with `GameEssentials/winhttp.dll` and `GameEssentials/doorstop_libs` remains discoverable.

Server download rules enforced by the client:

- every URL must use HTTPS;
- the host and port must match `https://sunshinealley.games/`;
- the URL path must be inside `ModPacks/<selected-id>/`;
- decoded `.` / `..` and embedded path separators are rejected;
- an `Illegal.File` deletion is ignored unless its resolved path is inside the selected local modpack root;
- downloads use a `.download` file and an atomic final move.

Default configuration files keep the existing convention: `name.cfg.txt` in `GameEssentials/defaultconfig` is copied as `BepInEx/config/name.cfg` only when the destination is missing.

Do not send a Windows-only `winhttp.dll` pack to Linux/macOS and call it cross-platform. Each native Doorstop binary must match both OS and architecture. Validate the exact BepInEx/Valheim combination you deploy; the launcher cannot make an incompatible native injection library compatible.

## 7. Platform-specific BepInEx launch behavior

### Windows

For a custom modpack, the Windows strategy:

1. backs up every game-root file it will replace into `.sunshine-alley-backup`;
2. deploys the selected pack's `winhttp.dll` and `doorstop_libs`;
3. generates `doorstop_config.ini` with an absolute, platform-native preloader path;
4. starts Valheim directly or with Steam;
5. restores the exact original files after the game exits unless Persistent is enabled.

The Vanilla profile writes a temporary `enabled = false` Doorstop configuration so an existing user BepInEx install cannot accidentally load mods. The Private World profile restores the pre-launch game state and leaves the user's own installation in control.

### Linux and macOS

Unix platforms do not use `winhttp.dll` or a Windows-style `doorstop_config.ini`. The launcher writes `sunshine-alley-launch.sh` and supplies:

- `DOORSTOP_ENABLE`;
- `DOORSTOP_INVOKE_DLL_PATH`;
- `DOORSTOP_CORLIB_OVERRIDE_PATH` when present;
- `LD_LIBRARY_PATH` and `LD_PRELOAD` on Linux;
- `DYLD_LIBRARY_PATH` and `DYLD_INSERT_LIBRARIES` on macOS.

Direct launching executes that wrapper with the native Valheim binary. Steam cannot reliably receive those variables from a second process when its client is already running, so Steam must be configured once to execute the wrapper.

Linux Steam launch option:

```text
./sunshine-alley-launch.sh %command%
```

macOS Steam launch option (the launcher/CLI prints the exact absolute path):

```text
"/absolute/path/to/Valheim/sunshine-alley-launch.sh" %command%
```

When Persistent is off, the wrapper becomes a safe pass-through after Valheim exits. When Persistent is on, it retains the selected injection environment. Vanilla always writes pass-through behavior. Private World calls an existing `start_game_bepinex.sh` or `run_bepinex.sh` when found.

macOS library injection can be blocked by library validation, hardened-runtime, quarantine, or an incompatible Doorstop build. Test both Intel and Apple Silicon packages on clean machines and on the current native Valheim build before releasing them.

## 8. CLI-first acceptance test

Use the harness before troubleshooting Avalonia. Substitute the published CLI executable name on Windows.

```bash
dotnet run --project src/SunshineAlley.Cli -- doctor --game "/path/to/Valheim" --data "/path/to/launcher-data"
dotnet run --project src/SunshineAlley.Cli -- servers
dotnet run --project src/SunshineAlley.Cli -- verify --modpack 1 --data "/path/to/launcher-data"
dotnet run --project src/SunshineAlley.Cli -- doctor --modpack 1 --game "/path/to/Valheim" --data "/path/to/launcher-data"
dotnet run --project src/SunshineAlley.Cli -- launch --modpack 1 --game "/path/to/Valheim" --data "/path/to/launcher-data" --no-steam
dotnet run --project src/SunshineAlley.Cli -- launch --modpack 1 --game "/path/to/Valheim" --data "/path/to/launcher-data" --dry-run
```

Acceptance criteria on each OS:

1. `doctor` finds Steam and the correct native Valheim executable.
2. The device identity source is OS-backed and not fallback on normal hardware.
3. The RSA key is visible in the OS credential UI but absent from `settings.json`.
4. `verify` reaches a verified state twice in succession without redownloading unchanged files.
5. A deliberately malformed server URL and an out-of-root illegal-file response are rejected/ignored.
6. Direct custom launch produces `BepInEx/LogOutput.log` and loads the expected plugins.
7. Steam custom launch works after the Unix wrapper option is configured.
8. Non-persistent launch restores Windows files or resets the Unix wrapper after Valheim exits.
9. Vanilla loads no BepInEx plugins.
10. Private World respects a separately installed local BepInEx profile.

The explicit `device-id` command prints the value used in the API sender. Treat its output as user/device data in support tickets and logs.

## 9. Publishing commands

Individual publish examples:

```bash
dotnet publish src/SunshineAlley.App/SunshineAlley.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish src/SunshineAlley.App/SunshineAlley.App.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish src/SunshineAlley.App/SunshineAlley.App.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish src/SunshineAlley.App/SunshineAlley.App.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Always publish for a specific RID: Avalonia and .NET include platform-native assets, and a single portable output is not a substitute for four target artifacts.

### Windows release

The script produces a folder/ZIP suitable for an installer input. Sign the final executable and installer with a certificate supplied by your release environment, for example with `signtool`. Do not restore the removed repository PFX reference.

Recommended installer behavior:

- install under `%LOCALAPPDATA%\Programs\Sunshine Alley Launcher` for a non-admin per-user install;
- create Start Menu/Desktop shortcuts in the packaging layer;
- preserve the per-user settings/data directories on update;
- offer an explicit checkbox before deleting settings/modpacks during uninstall.

### Linux release

The script produces a `tar.gz` and includes a `.desktop` template. A DEB/RPM/AppImage pipeline should install the executable under `/opt/sunshine-alley`, the desktop entry under `/usr/share/applications`, and an icon under the normal hicolor icon tree. Declare at least the Avalonia libraries listed above and `libsecret-tools` as dependencies.

### macOS release, signing, and notarization

Build/sign on macOS. The script creates a basic `.app` using `build/packaging/macos/Info.plist`. Before public distribution, sign all nested native content and the app with Developer ID, archive it, notarize it, and staple the ticket. A typical release operator flow is:

```bash
codesign --force --deep --options runtime --timestamp \
  --sign "Developer ID Application: YOUR TEAM" \
  "Sunshine Alley Launcher.app"

ditto -c -k --keepParent \
  "Sunshine Alley Launcher.app" \
  "SunshineAlleyLauncher-osx-arm64.zip"

xcrun notarytool submit "SunshineAlleyLauncher-osx-arm64.zip" \
  --keychain-profile "sunshine-alley-notary" --wait

xcrun stapler staple "Sunshine Alley Launcher.app"
codesign --verify --deep --strict --verbose=2 "Sunshine Alley Launcher.app"
spctl --assess --type execute --verbose=4 "Sunshine Alley Launcher.app"
```

Do not copy Apple certificates, passwords, API keys, or notarization profiles into the repository. The included CI intentionally stops at unsigned artifacts until your protected release workflow injects those credentials.

## 10. Update policy

The old updater downloaded and replaced a single running Windows `.exe`; that design is not valid for signed `.app` bundles, Linux packages, or four RID-specific payloads. This migration therefore makes the package/installer the update authority.

For automatic updates later, publish a signed manifest with one entry per RID, version, package URL, SHA-256, and signature, then integrate a cross-platform updater that stages and swaps whole application packages outside the running process. Do not point the old `LauncherCheckUpdate` response at Linux/macOS clients or perform an in-process executable overwrite.

## 11. Operational notes

- `settings.json` is written atomically and mode `0600` on Unix.
- RSA private material never enters JSON, logs, API JSON, or command arguments.
- API signatures remain RSA-2048/SHA-256/PKCS#1 v1.5 and public keys retain the legacy `<RSAKeyValue>` format for backend compatibility.
- The existing API's misspelled `Extenstion` field is deliberately retained.
- File paths are built with `Path.Combine`, `Path.GetRelativePath`, and `Path.GetFullPath`; URL path separators are parsed as URL segments and never copied into local path strings.
- All process arguments use `ProcessStartInfo.ArgumentList` rather than quoted command strings.
- The launcher does not require administrator/root privileges. If Valheim is installed somewhere the user cannot write, move/repair the Steam library permissions instead of elevating the launcher.
