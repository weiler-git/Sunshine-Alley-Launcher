#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_dir="${project_root}/artifacts"
app_project="${project_root}/src/SunshineAlley.App/SunshineAlley.App.csproj"
cli_project="${project_root}/src/SunshineAlley.Cli/SunshineAlley.Cli.csproj"

mkdir -p "${artifacts_dir}/publish" "${artifacts_dir}/packages"

dotnet restore "${project_root}/Sunshine Alley Launcher.sln"
dotnet run --project "${project_root}/tests/SunshineAlley.SmokeTests/SunshineAlley.SmokeTests.csproj" -c Release --no-restore

for rid in win-x64 linux-x64 osx-x64 osx-arm64; do
  publish_dir="${artifacts_dir}/publish/${rid}"
  mkdir -p "${publish_dir}/app" "${publish_dir}/tools"

  dotnet publish "${app_project}" \
    -c Release \
    -r "${rid}" \
    --self-contained true \
    --no-restore \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None \
    -o "${publish_dir}/app"

  dotnet publish "${cli_project}" \
    -c Release \
    -r "${rid}" \
    --self-contained true \
    --no-restore \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None \
    -o "${publish_dir}/tools"

  case "${rid}" in
    win-*)
      package="${artifacts_dir}/packages/SunshineAlleyLauncher-${rid}.zip"
      (cd "${publish_dir}" && zip -q -r "${package}" .)
      ;;
    linux-*)
      package="${artifacts_dir}/packages/SunshineAlleyLauncher-${rid}.tar.gz"
      cp "${project_root}/build/packaging/linux/sunshine-alley-launcher.desktop" "${publish_dir}/"
      tar -C "${publish_dir}" -czf "${package}" .
      ;;
    osx-*)
      bundle="${artifacts_dir}/publish/${rid}/Sunshine Alley Launcher.app"
      mkdir -p "${bundle}/Contents/MacOS" "${bundle}/Contents/Resources"
      cp -R "${publish_dir}/app/." "${bundle}/Contents/MacOS/"
      cp "${project_root}/build/packaging/macos/Info.plist" "${bundle}/Contents/Info.plist"
      chmod +x "${bundle}/Contents/MacOS/SunshineAlleyLauncher"
      package="${artifacts_dir}/packages/SunshineAlleyLauncher-${rid}.zip"
      (cd "$(dirname "${bundle}")" && zip -q -r "${package}" "$(basename "${bundle}")")
      ;;
  esac
done

echo "Packages written to ${artifacts_dir}/packages"
