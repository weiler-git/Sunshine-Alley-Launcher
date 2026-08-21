# Linux install, release, and update runbook

This runbook covers the native per-user Linux distribution. The Linux launcher is a directly downloaded, self-contained executable. It is not a Flatpak, Snap, AppImage, DEB, or RPM, and its own installation/update flow never invokes `sudo`.

## 1. Supported release target

The production Linux RID is currently `linux-x64` (x86-64/amd64). Valheim's native Linux player and the deployed Doorstop payload must both match that architecture.

The path/update code recognizes Linux RIDs generically, but `linux-arm64` is intentionally not in the release matrix yet. Add it only after all of these are available and tested together:

- a supported native Valheim arm64 runtime;
- an arm64 Doorstop/BepInEx payload under `GameEssentials/linux-arm64`;
- a clean-machine launcher/game acceptance test;
- a `linux-arm64` active release record and immutable server asset.

## 2. XDG layout

Relative, empty, whitespace-only, or malformed XDG variables are ignored. The fallback is based on the current user's absolute home directory; a literal `~` is never concatenated or expanded by hand.

| Purpose | Environment value | Fallback | Launcher-relative path |
| --- | --- | --- | --- |
| Application files | `XDG_DATA_HOME` | `$HOME/.local/share` | `sunshine-alley/launcher/` |
| Default managed mod data | `XDG_DATA_HOME` | `$HOME/.local/share` | `sunshine-alley/data/` |
| Configuration | `XDG_CONFIG_HOME` | `$HOME/.config` | `sunshine-alley/launcher/settings.json` |
| Update/download cache | `XDG_CACHE_HOME` | `$HOME/.cache` | `sunshine-alley/launcher/Updates/` |
| Persistent state/logs | `XDG_STATE_HOME` | `$HOME/.local/state` | `sunshine-alley/launcher/` |
| Application menu | `XDG_DATA_HOME` | `$HOME/.local/share` | `applications/games.sunshinealley.launcher.desktop` |
| Application icon | `XDG_DATA_HOME` | `$HOME/.local/share` | `icons/hicolor/256x256/apps/games.sunshinealley.launcher.png` |

The installed executable is:

```text
${XDG_DATA_HOME:-$HOME/.local/share}/sunshine-alley/launcher/SunshineAlleyLauncher
```

`settings.json` remains the V3 repository's established configuration filename. It is centralized under XDG config and is never stored beside the installed Linux executable. Game and managed-mod directories are persisted there. The RSA private key remains in the desktop Secret Service provider, not JSON.

The launcher expects `secret-tool` and an unlocked freedesktop Secret Service/keyring session for its RSA identity. This is an existing runtime dependency, not part of the launcher installation. Validate it, Avalonia's native desktop dependencies, and the supported distribution/glibc baseline on every release image.

The default managed-data directory is separate from application files. A user can select another writable directory during setup or later in Settings.

## 3. First execution and installation

Publish and distribute the raw `SunshineAlleyLauncher` file. A browser normally does not preserve an HTTP object's executable mode, so the initial user flow is:

```bash
chmod u+x ./SunshineAlleyLauncher
./SunshineAlleyLauncher
```

The running process is considered the installed copy only when all of these are true:

1. `Environment.ProcessPath` equals the fixed XDG executable path using Linux case-sensitive comparison;
2. the fixed executable exists, is not a symbolic link, and has an execute bit;
3. `install.json` beside it identifies `SunshineAlleyLauncher`, the supported layout version, and a Linux RID.

The working directory and filename of the downloaded copy do not determine installed state.

For a first installation, the Avalonia setup UI:

1. shows the fixed per-user application path as information, not as a selectable location;
2. discovers or asks for the native Valheim directory;
3. asks for a separate managed-mod-data directory;
4. verifies the Valheim executable, execute permission, non-linked path, and user write access with a temporary probe;
5. validates/initializes the managed-data ownership marker;
6. copies and SHA-256-verifies the launcher into the fixed XDG application directory;
7. applies user-only read/write/execute mode to the installed executable;
8. writes the installation marker and settings atomically;
9. exports the embedded icon and writes the `.desktop` entry atomically;
10. starts the installed copy after the setup process exits.

No registry, Windows shortcut API, desktop-icon file, `/usr`, `/opt`, or privilege escalation is used.

### Earlier Linux development layout

Earlier V3 development builds used `.../sunshine-alley/launcher` as the default mod-data root, which is now the fixed application directory. If that directory has the recognized `.sunshine-alley-root` marker, setup migrates `ModPacks`, `Launcher`, and `logs` to the newly selected data root before installing the executable.

Same-filesystem moves are direct. Cross-filesystem moves use a destination-side staging directory, per-file SHA-256 verification, and a final same-filesystem rename. Source data is deleted only after verification. Conflicting non-empty source/destination subdirectories stop migration and preserve both copies.

## 4. Normal startup, repair, and uninstall

Running the installed copy starts normally. Running another published copy after an installation exists opens Repair behavior. Repair:

- refuses an older installer when the marker records a newer installed version;
- revalidates the native game path and managed data;
- can reset downloaded `ModPacks` only inside a recognized data root;
- atomically replaces the installed executable when repairing from another copy;
- reapplies executable permission;
- rewrites the installation marker;
- recreates the application-menu entry and icon;
- preserves settings, identity, account state, and managed data unless the user explicitly requests a reset.

The installed copy accepts `--repair` and `--uninstall`. Linux uninstall uses a copy of the launcher under the private XDG cache, waits for the GUI process, validates every fixed target and ownership marker, then removes:

- the installed executable/application directory;
- `.desktop` entry and icon;
- update cache and persistent launcher state/logs;
- configuration only when selected;
- managed mod data only when selected and its exact ownership marker is present.

Secret Service credentials are intentionally preserved. The helper refuses filesystem roots, linked targets, linked directory trees, unknown installation markers, or unmarked mod-data trees.

Manual uninstall is also straightforward:

```bash
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/applications/games.sunshinealley.launcher.desktop"
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps/games.sunshinealley.launcher.png"
rm -rf "${XDG_DATA_HOME:-$HOME/.local/share}/sunshine-alley/launcher"
rm -rf "${XDG_CACHE_HOME:-$HOME/.cache}/sunshine-alley/launcher"
rm -rf "${XDG_STATE_HOME:-$HOME/.local/state}/sunshine-alley/launcher"
```

Remove the XDG config directory and the chosen managed-data directory only when their user data should also be discarded. Adjust every command when an XDG variable is set; do not blindly use the fallback paths.

## 5. Native Valheim and mod behavior

Linux game discovery checks Steam's conventional and Flatpak-user trees, reads `appmanifest_892970.acf`, and resolves native candidates such as `valheim.x86_64`. It does not look for `valheim.exe` or use drive letters.

For a custom modpack, the repository's Unix launch strategy:

- resolves `GameEssentials/linux-x64/doorstop_libs` and the BepInEx preloader;
- writes an executable `sunshine-alley-launch.sh` in the selected game root;
- provides `DOORSTOP_*`, `LD_LIBRARY_PATH`, and `LD_PRELOAD` through that wrapper;
- launches directly through process APIs, or asks the user to set `./sunshine-alley-launch.sh %command%` once in Steam;
- resets the wrapper to pass-through mode after a non-persistent run;
- never shells out with a concatenated user path and never attempts `sudo`.

The selected Valheim directory must be writable by the current user because the wrapper is part of the injection mechanism. A linked or read-only installation produces an actionable validation error.

## 6. Linux self-update transaction

Linux uses the existing V3 signed-envelope API and embedded offline release public key. There is no invented Linux package-signing authority. Trust is provided by:

- HTTPS restricted to `sunshinealley.games`;
- an RSA-signed manifest verified with the embedded release public key;
- signed RID, channel, version, release ID, package URL, exact size, and SHA-256;
- replay/downgrade release-ID tracking;
- private, user-only XDG cache/application paths.

Update sequence:

1. the installed launcher requests `LauncherGetUpdateV3` with its version, `linux-x64`, channel, executable SHA-256, layout version, and signed identity;
2. the signed manifest is verified before its package fields are trusted;
3. the raw new executable downloads to `XDG_CACHE_HOME/.../Updates/<transaction>` using a `.download` partial file;
4. size and SHA-256 must exactly match the signed manifest before the final staged name exists;
5. executable permission is applied;
6. the current launcher copies itself to a transaction helper in the private XDG cache, starts it without a shell, and exits;
7. the helper waits for the old PID and revalidates every plan path/hash;
8. the old executable is copied and verified to a rollback file in the application directory;
9. the new executable is copied from cache to a temporary file beside the installed target, verified again, and atomically renamed over the target (so custom cache/data filesystems are safe);
10. the new launcher starts with `--post-update`, confirms its own hash, updates the installation marker/release-ID settings, and writes confirmation;
11. the helper removes rollback/staging data after confirmation;
12. if the new process exits before confirmation, the verified old executable is atomically restored and restarted with `--update-rollback`; that failed release ID is not retried.

An incomplete or failed download never changes the installed executable. An apply failure before the final rename removes its unused rollback copy and leaves the old executable intact. A long-running but unconfirmed new process is not killed after the three-minute confirmation window; its plan and backup remain for diagnosis/next-start cleanup.

Unlike Windows, Linux has no Authenticode check. The signed offline manifest and its exact package SHA-256 are the release-code trust boundary. Protect the manifest private key accordingly.

## 7. Mandatory launcher versions

The existing signed `minimumVersion` field means the oldest bridge/source launcher that is technically allowed to consume a release. It must not be reinterpreted as product support policy.

This implementation adds the optional signed field:

```json
"minimumSupportedVersion": "3.1.0"
```

When the running version is lower, the GUI's Play command remains disabled. The highest valid signed minimum seen for each channel/RID is remembered in XDG configuration, so a replayed lower policy or later offline restart cannot silently re-enable Play. If download, verification, replacement, or restart fails after that signed policy is known, game launch remains blocked and the user can retry Update. A network/API failure before any current or remembered signed policy exists retains the existing fail-open behavior because the launcher has not been explicitly declared unsupported.

The API/release record must emit this field for mandatory enforcement. Leaving it null preserves prior behavior only for an installation that has never received a boundary; publishing null/lower does not revoke a remembered higher boundary. `minimumVersion` and `minimumSupportedVersion` may differ.

## 8. Build and local test

Install the .NET 10 SDK, then from the repository root:

```bash
dotnet restore "Sunshine Alley Launcher.sln"
dotnet build "Sunshine Alley Launcher.sln" -c Release --no-restore
dotnet run --project tests/SunshineAlley.SmokeTests/SunshineAlley.SmokeTests.csproj -c Release --no-restore
dotnet publish src/SunshineAlley.App/SunshineAlley.App.csproj \
  -c Release -r linux-x64 --self-contained true --no-restore \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -o artifacts/manual-linux-x64
chmod u+x artifacts/manual-linux-x64/SunshineAlleyLauncher
```

Use a disposable Linux desktop account/VM for first-run tests so XDG paths and Secret Service are realistic. Use `--portable` only for GUI development; portable mode intentionally bypasses guided installation and is not a release acceptance test.

Smoke tests cover XDG fallbacks/custom/relative values, install detection, desktop paths with spaces, game/mod path persistence, signed and remembered mandatory-version comparison, atomic Linux replacement/rollback, verification failure safety, executable modes, settings, hashing, and shared Windows layout invariants.

## 9. Produce the immutable Linux release

The same offline manifest key initialized for V3 Windows releases signs Linux manifests. The private key remains under `.release-secrets` or, preferably, a protected release-secret system and is never uploaded to the application/API server.

Production example:

```powershell
./scripts/linux/Publish-LinuxUpdate.ps1 `
  -Version 3.1.0 `
  -ReleaseId 43 `
  -Channel stable `
  -RuntimeIdentifier linux-x64 `
  -MinimumVersion 3.0.0 `
  -MinimumSupportedVersion 3.0.0 `
  -PackageBaseUrl https://sunshinealley.games/launcher/releases
```

Output:

```text
artifacts/releases/3.1.0/linux-x64/
+-- SunshineAlleyLauncher
+-- update.envelope.json
+-- no-update.envelope.json
+-- server-release.json
```

The script restores, runs smoke tests, publishes the self-contained single file, calculates its exact SHA-256/size, generates update/no-update payloads, signs and locally verifies both with the matching offline RSA key pair, and refuses to overwrite an immutable version/RID directory.

For an unsigned local build without release metadata, `scripts/publish-all.sh` writes the directly downloadable launcher to:

```text
artifacts/packages/SunshineAlleyLauncher-linux-x64
```

Do not rename, recompress, or modify the immutable server executable after envelope creation; its signed hash would no longer match.

## 10. Upload and activate

Upload all four files into the matching immutable server directory. The public package URL must be exactly the signed HTTPS URL, for example:

```text
https://sunshinealley.games/launcher/releases/3.1.0/linux-x64/SunshineAlleyLauncher
```

Create/atomically switch a separate active record for `(stable, linux-x64)`. Never let a Linux request select the Windows `.exe`, and never reuse a release ID after rollback/failure. Verify server-side size/SHA-256 and independently verify both stored envelopes before activation.

The legacy V2 `LauncherCheckUpdate` endpoint remains Windows-only. Linux uses only `LauncherGetUpdateV3`.

## 11. Release acceptance matrix

Before stable activation, verify on a clean x86-64 Linux desktop:

1. downloaded file runs after `chmod u+x` and offers setup;
2. custom absolute XDG variables are honored; unset variables use fallbacks;
3. application location is fixed and game/mod locations are selectable;
4. Steam discovery finds the real native Valheim directory;
5. a read-only or linked game directory is rejected without privilege escalation;
6. install marker, settings, `.desktop`, and icon paths are correct;
7. the application-menu entry launches the installed copy when paths contain spaces;
8. Repair from a second current/newer downloaded copy replaces the executable and preserves user data;
9. an older repair copy is rejected;
10. normal, Vanilla, Private World, and custom-mod launch behavior is tested;
11. Steam wrapper setup and non-persistent reset work;
12. optional update succeeds and preserves settings/Secret Service identity/modpacks;
13. corrupted size/hash/envelope/wrong RID/wrong host are rejected before replacement;
14. forced early startup failure rolls back, restarts the old build, and blocks that release ID;
15. `minimumSupportedVersion` below/current/above boundary behaves as signed;
16. uninstall preserves or removes config/data exactly according to the selected options.

Do not claim Linux release readiness from a cross-build alone. Native Avalonia, Secret Service, Steam, Doorstop, Valheim, desktop-entry discovery, executable permissions, and update/rollback behavior all require this Linux acceptance pass.
