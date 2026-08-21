# Windows install, signing, release, and update runbook

This runbook covers the Windows-first V3 deployment. `API_DEPLOYMENT.md` defines the server contract. Linux and macOS application updating remain separate future platform implementations.

## 1. Final Windows layout

Default per-user layout:

```text
%LOCALAPPDATA%\Sunshine Alley\
├── App\
│   ├── Sunshine Alley Launcher.exe
│   ├── install.json
│   └── .update-staging\        (transient, only during update/rollback)
├── Config\
│   └── settings.json
├── Data\
│   ├── .sunshine-alley-root
│   ├── Launcher\
│   ├── logs\
│   └── ModPacks\
└── Cache\
    └── Updates\                (reserved launcher cache)
```

The user may select another application or data directory during guided setup. They are separate locations: neither may contain the other, overlap fixed Config/Cache, or use the same directory, but they do not need the same parent. The App directory must be local, empty or already owned by a valid Sunshine Alley install marker, and outside broad personal/temporary folders. Configuration remains in the fixed per-user `Config` directory so an arbitrary downloaded copy can discover existing choices.

Installed state requires all of:

- the running `Environment.ProcessPath` equals the registered V3 executable path;
- `HKCU\Software\Sunshine Alley\Launcher3` identifies that path;
- `install.json` beside the executable has the expected product/layout marker.

The working directory is never used to determine installed state.

There is no permanently installed updater executable. At update, repair, or uninstall time the launcher copies its signed single-file EXE into a transaction-specific directory under `%TEMP%`, starts that copy in restricted helper mode, and exits. The helper waits for the GUI process and then performs the replacement/removal. A stale helper copy may remain until a later run or Windows temporary-file cleanup if antivirus holds it open.

## 2. One-time manifest signing-key setup

Install PowerShell 7 and run from the repository root:

```powershell
./scripts/windows/Initialize-UpdateSigning.ps1
```

This creates:

```text
.release-secrets\launcher-update-private.pem
src\SunshineAlley.Platform\Update\update-public-key.pem
```

The private directory is ignored by Git. Move/back it up into your protected release secret system. Never upload it to the web/API server and never include it in a release ZIP.

The public key is embedded into the launcher. Review and commit that public-key file before producing the first bridge/production release. Back up the matching private key before distributing any launcher that embeds its public half; losing it requires a trust-overlap key rotation release.

## 3. Authenticode certificate

Production releases require a Windows code-signing certificate accessible to `signtool.exe`. Keep its private key in a hardware token, managed signing service, or protected CI secret—never in this repository.

Locate the Windows SDK signing tool and verify the certificate is visible:

```powershell
Get-Command signtool.exe
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert
```

Record the certificate thumbprint and the stable publisher identity. The launcher embeds `UpdatePublisherSubject` and requires it to exactly equal either the certificate's simple subject name or complete Subject string. Use the same value for bridge and later releases. If certificate renewal changes that identity, ship a trust-transition launcher before enforcing the new value.

For an isolated development simulation, use `-DevelopmentUnsigned`. That produces a specially compiled launcher that skips Authenticode verification while retaining manifest-signature, SHA-256, URL, size, RID, version, and release-ID validation. Never send that build to ordinary players or activate it on `stable`.

## 4. Produce a Windows update release

Choose a version and a monotonically increasing release ID. Release IDs are never reused, including after a failed or withdrawn release.

Production example:

```powershell
./scripts/windows/Publish-WindowsUpdate.ps1 `
  -Version 3.1.0 `
  -ReleaseId 42 `
  -Channel stable `
  -MinimumVersion 3.0.0 `
  -PackageBaseUrl https://sunshinealley.games/launcher/releases `
  -PublisherSubject "YOUR CERTIFICATE SUBJECT TEXT" `
  -CodeSigningCertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT"
```

Development example:

```powershell
./scripts/windows/Publish-WindowsUpdate.ps1 `
  -Version 3.0.1 `
  -ReleaseId 1 `
  -Channel development `
  -PackageBaseUrl https://sunshinealley.games/launcher/releases `
  -DevelopmentUnsigned
```

For a complete unsigned update simulation, build the older installed test version and the newer offered version with the same manifest key and `-DevelopmentUnsigned`, using increasing versions and release IDs. Never reuse those artifacts for `stable`.

The script refuses to overwrite an existing version/RID release directory and refuses a development-unsigned `stable` build. It performs, in order:

1. verifies that the embedded public key and private manifest key exist;
2. restores the solution;
3. runs smoke tests;
4. publishes self-contained single-file `win-x64` with the requested version;
5. explicitly compiles the unsigned-update bypass off, then Authenticode-signs and verifies the EXE for production;
6. copies the final signed bytes into an immutable release directory;
7. calculates SHA-256 and size after signing;
8. creates exact update and no-update payload bytes;
9. signs both payloads with the offline manifest key;
10. writes `server-release.json` for deployment.

Output:

```text
artifacts\releases\3.1.0\win-x64\
├── SunshineAlleyLauncher.exe
├── update.envelope.json
├── no-update.envelope.json
└── server-release.json
```

Do not modify, recompress, sign, or rename the server EXE after manifest generation; doing so changes its signed hash. The installer copies it to the spaced canonical installed filename itself.

## 5. Inspect before upload

Verify locally:

```powershell
signtool verify /pa /v artifacts\releases\3.1.0\win-x64\SunshineAlleyLauncher.exe
Get-FileHash artifacts\releases\3.1.0\win-x64\SunshineAlleyLauncher.exe -Algorithm SHA256
Get-Content artifacts\releases\3.1.0\win-x64\server-release.json
```

Confirm:

- version and release ID are correct;
- channel is correct;
- `developmentUnsigned` is `false` for stable;
- SHA-256 and size match;
- package URL points to the immutable intended directory;
- Authenticode signer and timestamp are correct;
- no `.release-secrets`, PFX, private PEM, symbols, or source files are present.

Archive the complete operator output privately if desired, but upload only the four public release files listed above.

## 6. Upload and activate

Follow `API_DEPLOYMENT.md`:

1. upload into a new immutable server version directory;
2. verify server-side SHA-256/size;
3. test the public HTTPS executable URL;
4. test `LauncherGetUpdateV3` with an allow-listed account/device;
5. atomically activate the development channel record;
6. complete the client matrix;
7. separately promote a production-signed release to stable.

Uploading files does not activate a release. Activation happens only when the channel/RID active record points to the new immutable directory.

## 7. Test V3-to-V3 update

Use two versions, for example installed `3.0.0` and offered `3.0.1`:

1. Install the older published launcher through its guided setup.
2. Set `"Updates.Channel": "development"` in `%LOCALAPPDATA%\Sunshine Alley\Config\settings.json` while the launcher is closed.
3. Activate the newer development release only for the test identity.
4. Start the installed older launcher.
5. Observe manifest verification and download progress.
6. Confirm the launcher exits, the temporary helper replaces the EXE, and the new launcher starts.
7. Confirm startup writes the confirmation file and removes the rollback backup/staging directory.
8. Confirm the channel/RID-specific `Updates.HighestReleaseId[...]` setting records the accepted ID.
9. Confirm settings, MachineGuid identity, Credential Manager RSA key, modpacks, shortcuts, and Valheim paths are unchanged.

Failure tests:

- corrupt one byte of the hosted EXE: SHA-256 must reject it;
- alter the envelope: manifest signature must reject it;
- return another host/HTTP URL: URL policy must reject it;
- lower the release ID: replay protection must reject it;
- offer the same/lower version: version policy must reject it;
- make the replacement exit before confirmation: helper must restore and start the previous EXE, record the failed release ID, and refuse that ID on later launches;
- deny write access or fill the drive: update must stop before replacement;
- leave Valheim running: update remains unavailable while launcher operations are busy.

The development-unsigned build includes a rollback test hook. With a newer development release active, start the older installed build from PowerShell as follows:

```powershell
& "$env:LOCALAPPDATA\Sunshine Alley\App\Sunshine Alley Launcher.exe" `
  --simulate-update-startup-failure
```

The argument is carried into the replacement, which exits before Avalonia only when compiled with `AllowUnsignedUpdates=true`. The helper must restore the older EXE, restart it without re-triggering the hook, record the failed release ID, and leave normal game launching available. Production builds do not compile this hook.

## 8. Test V2-to-V3 without changing production V2

Do not alter the production `LauncherCheckUpdate` operation during development. Use an isolated Windows VM and one of these safe methods:

Keep `LauncherGetUpdateV3` inactive/no-update for the bridge version during the first migration pass. After V3 is installed and the migration result is inspected, close it, set `"Updates.Channel": "development"`, activate the newer development release for the test identity, and continue with the V3-to-V3 procedure in section 7. This separates bridge/migration failures from updater failures.

### Staging V2 API

Build a local V2 test launcher whose `Shared.webURL` points to a staging API implementing the unchanged V2 `OK-or-URL` response. Have staging return the raw V3 bridge EXE URL for only that test hash.

### Local V2 callback simulation

In a disposable V2 test branch, make the update callback use the V3 bridge URL instead of the server result. Do not deploy that V2 build. This exercises V2 download, rename, replacement, and restart behavior exactly as production would after receiving the URL.

Test sequence:

1. Snapshot the VM.
2. Install/run V2 with real legacy registry values, RSA key, shortcuts, and populated `ModPacks`.
3. Trigger its updater with the V3 bridge EXE.
4. Confirm V2 renames itself and starts V3 from the old root path.
5. Confirm V3 recognizes legacy mode and shows guided migration.
6. Accept defaults: `App`, `Config`, and `Data`.
7. Confirm the journaled directory migration completes and the marker is written.
8. Confirm the installed copy starts from `App` and old shortcuts are replaced.
9. Confirm the legacy root EXE and `.temp.*.exe` files are removed after the V2 process exits.
10. Confirm MachineGuid and the RSA identity survive migration.
11. Confirm V3 checks only `LauncherGetUpdateV3` thereafter.
12. Restore the VM and repeat interruption/failure cases.

Only after this passes should the production V2 endpoint begin returning the immutable bridge URL for recognized old hashes. Keep that bridge and endpoint available for delayed users.

## 9. Guided setup, repair, and uninstall tests

Fresh setup:

- run the published EXE from Downloads;
- confirm it is not treated as installed based on working directory;
- verify default App/Data paths and Steam game discovery;
- test custom independent App/Data locations;
- reject roots, Documents/Desktop/Downloads as App or data, non-empty unowned App folders, overlapping paths, App UNC paths, and junctions;
- verify Start Menu/Desktop shortcut choices and Installed Apps registration.

Repair:

- run `"Sunshine Alley Launcher.exe" --repair` or use the registered repair entry;
- verify application registration and shortcuts are rebuilt;
- confirm repair refuses to move an existing App directory; preserve Data/Config through uninstall and reinstall when intentionally moving App;
- run an older setup EXE against a newer registered install and confirm it refuses to downgrade the installed launcher;
- leave reset-ModPacks unchecked and confirm data is preserved;
- explicitly select reset and confirm only the marked `ModPacks` tree is removed.

Uninstall:

- launch through Windows Installed Apps;
- default removal deletes App, shortcuts, update cache, and registration while preserving Data, Config, and Credential Manager identity;
- optional data deletion requires the recognized marker and rejects links/junctions;
- optional configuration deletion does not remove the RSA credential;
- test cancellation before confirmation and an interrupted helper.

## 10. Operational safeguards included

- named mutex prevents concurrent GUI/setup instances;
- helper modes execute before Avalonia initialization;
- helper plans are accepted only from protected staging/temp roots and registered targets;
- updates are disabled during busy launcher/game operations;
- staging is under `App` on the same volume as the target;
- disk-space check includes the package, rollback EXE, and safety margin;
- downloads enforce signed size, maximum size, SHA-256, HTTPS host, and final redirect host;
- the temporary helper rechecks both the staged SHA-256 and Authenticode publisher immediately before replacement;
- manifest signing key is separate from TLS, player RSA, and Authenticode keys;
- lower/equal versions, replayed release IDs, and previously rolled-back release IDs are rejected;
- both the staged replacement and previous rollback EXE are hash-checked immediately before use;
- previous EXE remains until the new one confirms runtime initialization;
- failed early startup triggers rollback; slow-but-running startup preserves diagnostic backup rather than killing the process;
- restart arguments are preserved except internal helper arguments;
- maintenance deletion requires exact registered paths/markers and rejects reparse points;
- repair and uninstall preserve RSA identity by default;
- helper failures write local operational logs without credentials/device IDs.

Update-helper failures are written beneath `%LOCALAPPDATA%\Sunshine Alley\Data\logs`; maintenance/uninstall failures are written beneath `%LOCALAPPDATA%\Sunshine Alley\MaintenanceFailures` so a failed self-removal still leaves a support log outside App.

## 11. Other important considerations

- Keep this a per-user, non-elevated install. The default uses `%LOCALAPPDATA%` and HKCU; adding elevation would split registry, Credential Manager, and filesystem identity between users.
- Snapshot a populated V2 VM and back up the legacy mod directory before the first production migration exercise. Migration is journaled and cross-volume copies are hash-verified, but interruption, disk failure, and antivirus still need real-world tests.
- Setup also discovers the prior Avalonia test layout under `...\Sunshine Alley\Launcher`; if its settings point at a custom V3 data root, that recognized root is migrated instead of being silently abandoned.
- The extensionless `.sunshine-alley-root` file is placed only at the Data root. It is not inside a modpack and must not be sent in `LauncherVerifyFiles`; existing server mod-file allow-list behavior is unchanged.
- A custom Data path can be on another local volume. Cloud-synchronized, removable, and network locations add locking/latency risks and should be treated as unsupported until tested. The App path is intentionally restricted to a local drive for atomic replacement.
- Do not delete the Windows Credential Manager RSA entry during repair or normal uninstall. Identity removal should be a separate, explicit support action because it changes the player's server identity.
- Keep the old non-secret V2 registry values during the bridge period. V3 uses a separate `Launcher3` registration and JSON settings; only the migrated legacy RSA private value is removed from the old registry.
- Retain the legacy endpoint, bridge EXE, TLS compatibility, and URL for longer than the longest expected dormant-player period. Test that path periodically from a frozen V2 VM.
- Code-signing certificate renewal and manifest-key rotation are independent events. Preserve the Authenticode publisher identity where possible, and use an overlap release before changing embedded manifest trust.
- Publish symbols/source maps only to private diagnostics storage. Public release storage needs only the EXE and API deployment metadata/envelopes.
- Windows x64 is the supported updater target in this phase. Do not label Windows ARM64, Linux, or macOS updating as supported until their helper/package semantics have their own test matrix.

For local GUI development without installing the current build, use the explicit bypass:

```powershell
dotnet run --project src/SunshineAlley.App/SunshineAlley.App.csproj -- --portable
```

`--portable` is a developer switch, not an installed-state fallback. Updates remain disabled because the process does not match the registered executable.

## 12. Production promotion checklist

- [ ] Git commit/tag identifies the exact source.
- [ ] Clean Release build and smoke tests pass.
- [ ] Permanent manifest public key is embedded; private key is protected and backed up.
- [ ] Authenticode signing and timestamp verification pass.
- [ ] Stable artifact is not compiled with `AllowUnsignedUpdates`.
- [ ] Version and release ID are new and increasing.
- [ ] Manifest/package SHA-256 and size match locally and on server.
- [ ] Development update and rollback matrix passes on a clean VM.
- [ ] V2-to-V3 migration matrix passes on a populated legacy VM.
- [ ] Fresh install, repair, and uninstall pass without administrator rights.
- [ ] Development channel remains allow-listed.
- [ ] Stable activation is atomic and monitored.
- [ ] Legacy `LauncherCheckUpdate` and immutable bridge remain available.
