# Sunshine Alley Launcher — cross-platform deployment

The base V3 migration replaces the .NET 7 Windows Forms launcher with a .NET 10 / Avalonia 12 application. The current branch includes the Windows guided installer/migrator and signed updater plus the native Linux XDG installer, repair/uninstall lifecycle, desktop integration, and signed self-updater, while retaining the cross-platform game/mod architecture and command-line launch harness.

## 0. Apply the patch

The supplied Linux-behaviour Git patch targets the current V3 source archive that already contains the Windows lifecycle and cross-platform launcher. Start from that same revision with no overlapping local edits, extract the patch ZIP outside the repository, and run:

```bash
git switch feature-linux-behaviour
git apply --check /path/to/Sunshine-Alley-Launcher-Linux-Behaviour.patch
git apply --index --binary /path/to/Sunshine-Alley-Launcher-Linux-Behaviour.patch
git status --short
```

Review the staged changes, build/test them, then commit normally. Generated `bin`, `obj`, `.vs`, `artifacts`, release secrets, and signing certificates do not belong in source control or in the patch.

## 1. Resulting solution

| Project | Responsibility |
| --- | --- |
| `SunshineAlley.Core` | Server DTOs, signed API requests, RSA identity, settings contracts, file hashing, secure mod synchronization, models, and path containment. It has no Avalonia, registry, Keychain, WinForms, or OS process-discovery code. |
| `SunshineAlley.Platform` | JSON settings, legacy registry migration, OS device identity, OS secret stores, Steam discovery, native BepInEx/Doorstop launch strategies, process monitoring, and shell integration. |
| `SunshineAlley.App` | The Avalonia desktop GUI: launcher, settings, and optional-mod windows. |
| `SunshineAlley.Cli` | A GUI-independent diagnostic, verify, and launch harness. Use this first on every target OS. |
| `SunshineAlley.SmokeTests` | Dependency-free executable smoke tests for shared primitives plus XDG resolution, Linux installation detection, desktop entries, path persistence, mandatory-update policy, replacement, rollback, and executable modes. |

The old Windows Forms project, external COM shortcut-library dependency, embedded signing PFX reference, and unsafe in-process updater are removed. Windows has a guided per-user installer/migrator and Authenticode-plus-signed-manifest updater. Linux now has a native XDG per-user installer, freedesktop application-menu integration, repair/uninstall helpers, and a signed-manifest self-updater with atomic replacement and rollback. macOS application updating remains future platform work. Non-secret values in the legacy Windows registry key are retained during migration; the migrated private RSA value is deleted from the registry after Credential Manager accepts it.

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
dotnet run --project src/SunshineAlley.App/SunshineAlley.App.csproj -- --portable
```

The included GitHub Actions workflow builds all four target RIDs on Windows 2025, Ubuntu 24.04, macOS 15 Intel, and macOS 15 Apple Silicon runners. `scripts/publish-all.sh` and `scripts/publish-all.ps1` perform a local restore, smoke test, and publish; Windows/macOS are archived while Linux is emitted as the directly downloadable raw executable.

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
| Windows | `%LOCALAPPDATA%\Sunshine Alley\Config` |
| Linux | `${XDG_CONFIG_HOME:-$HOME/.config}/sunshine-alley/launcher` |
| macOS | `~/Library/Application Support/Sunshine Alley/Launcher` |

On Windows, downloaded modpack data defaults to `%LOCALAPPDATA%\Sunshine Alley\Data` and the application defaults to `%LOCALAPPDATA%\Sunshine Alley\App`. They are separate sibling directories; the data directory can be changed during setup or later in Settings. On Linux the fixed application directory is `${XDG_DATA_HOME:-$HOME/.local/share}/sunshine-alley/launcher`, while default managed mod data is the separate `.../sunshine-alley/data` directory and remains user-selectable. Relative XDG values are ignored as required by the XDG base-directory rules.

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

The published single-file EXE is also the guided installer and temporary update/maintenance helper. Its default installed path is `%LOCALAPPDATA%\Sunshine Alley\App\Sunshine Alley Launcher.exe`; no permanently separate updater executable is required. It registers a per-user Installed Apps entry and can create Start Menu/Desktop shortcuts without elevation.

Production Windows update releases require both Authenticode and an offline-signed update manifest. Use `scripts/windows/Initialize-UpdateSigning.ps1` once, then `scripts/windows/Publish-WindowsUpdate.ps1` for each release. Follow [WINDOWS_RELEASE.md](WINDOWS_RELEASE.md) for the complete publish/sign/test/upload procedure and [API_DEPLOYMENT.md](API_DEPLOYMENT.md) for the new `LauncherGetUpdateV3` server contract. Do not restore the removed repository PFX reference or store any private signing key in source control.

### Linux release

The Linux release is a raw, self-contained `SunshineAlleyLauncher` executable. On first execution it installs itself under the current user's resolved XDG data directory, writes a user-level application-menu entry/icon, and thereafter updates itself through the signed V3 manifest flow. It never installs under `/opt` or `/usr` and does not require a DEB/RPM/AppImage, root, or `sudo`.

Use `scripts/linux/Publish-LinuxUpdate.ps1` to create the immutable `linux-x64` executable, update/no-update envelopes, and server record. See [LINUX_RELEASE.md](LINUX_RELEASE.md) for the exact layout, first-run/repair/uninstall behavior, update transaction and rollback design, mandatory-version API field, release commands, server activation requirements, and clean-machine acceptance matrix.

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

Installed Windows and Linux V3 launchers call only the new `LauncherGetUpdateV3` operation. Both verify an offline-signed manifest, release ID, newer version, RID/channel, approved HTTPS host, and signed size/SHA-256. Windows additionally requires the configured Authenticode publisher. Windows stages on the application volume; Linux stages under XDG cache and copies the verified candidate beside the installed executable before the final atomic rename. Both retain a verified previous executable until replacement startup confirms. An early failure automatically rolls back and blocks that release ID until a higher release is published.

The optional signed `minimumSupportedVersion` is the launch-policy boundary; Play is disabled below it even when a later update step fails or startup is offline. The highest signed boundary seen per channel/RID is retained and cannot be lowered by a replay/null policy. It is distinct from `minimumVersion`, the oldest source/bridge launcher able to consume a package. The API must provide signed bridge releases when raising package compatibility above deployed clients.

The legacy `LauncherCheckUpdate` operation remains unchanged for V2 clients and must continue serving a raw immutable V3 bridge EXE for delayed users. It must never return a V3 envelope or non-Windows artifact. macOS does not use either native replacement implementation yet.

## 11. Operational notes

- The mod-data picker accepts only a dedicated launcher-owned directory. It rejects filesystem roots, profile/Documents/Desktop/Downloads/temp locations, the launcher install, the Valheim install, unrelated non-empty folders, and paths traversing symbolic links or Windows junctions.
- The Windows App picker accepts only a local, dedicated, empty or already marked application directory. It rejects broad personal/temp folders, UNC paths, unrelated non-empty folders, overlap with Data, and reparse points.
- On first use, the launcher writes `.sunshine-alley-root` directly in the selected mod-data root. The marker has no extension and is outside `ModPacks/<id>`, so it is never included in a modpack file list sent to the existing verification endpoint.
- An existing unmarked directory is adopted only when empty or when its top level contains launcher-known entries such as `Launcher` and `ModPacks`. Once marked, other root-level launcher data is allowed; synchronization and deletion remain confined to the selected `ModPacks/<id>` directory.
- Invalid game paths are reported when chosen, when the field loses focus, and again on Save. Save requires the platform-native Valheim executable (`valheim.exe`, the Linux binary, or the macOS app executable) before persisting either directory.
- Rejected or outdated mod files are still deleted in place according to the current mod-verification response. This change adds no quarantine behavior and does not change the modpack manifest contract; launcher application updates use the separate `LauncherGetUpdateV3` contract.
- `settings.json` is written atomically and mode `0600` on Unix.
- RSA private material never enters JSON, logs, API JSON, or command arguments.
- API signatures remain RSA-2048/SHA-256/PKCS#1 v1.5 and public keys retain the legacy `<RSAKeyValue>` format for backend compatibility.
- The existing API's misspelled `Extenstion` field is deliberately retained.
- File paths are built with `Path.Combine`, `Path.GetRelativePath`, and `Path.GetFullPath`; URL path separators are parsed as URL segments and never copied into local path strings.
- All process arguments use `ProcessStartInfo.ArgumentList` rather than quoted command strings.
- The launcher does not require administrator/root privileges. If Valheim is installed somewhere the user cannot write, move/repair the Steam library permissions instead of elevating the launcher.
