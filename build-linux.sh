#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
output="$root/release-linux-x64"
while (($#)); do
    case "$1" in
        --output)
            if (($# < 2)) || [[ -z $2 ]]; then echo "--output requires a directory" >&2; exit 1; fi
            output="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: bash build-linux.sh [--output directory]"
            echo "Requires .NET 10 SDK and Python 3. Set DOTNET_PATH to select a dotnet executable."
            echo "The first build downloads clang-format 18.1.8 into .tools."
            echo "Builds one standalone executable and a separate AI_GUIDE.md."
            echo "Output must be empty or contain only these two release files."
            exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done
if [[ $(uname -s) != Linux || $(uname -m) != x86_64 ]]; then
    echo "This build targets Linux x86_64." >&2; exit 1
fi
dotnet_bin="${DOTNET_PATH:-dotnet}"
command -v "$dotnet_bin" >/dev/null || { echo "Install .NET 10 SDK or set DOTNET_PATH." >&2; exit 1; }
command -v python3 >/dev/null || { echo "Building requires Python 3." >&2; exit 1; }
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
sdk_version="$("$dotnet_bin" --version)"
[[ $sdk_version == 10.* ]] || { echo "Select .NET 10 SDK; found $sdk_version." >&2; exit 1; }
output="$(realpath -m -- "$output")"
[[ $output != "$root" && $output != / ]] || { echo "Choose a separate output directory." >&2; exit 1; }
python3 "$root/tools/check_linux_output.py" "$output"
python3 "$root/tools/setup_linux_formatter.py"
sdk_root="$(dirname -- "$(readlink -f -- "$(command -v "$dotnet_bin")")")"
mkdir -p "$root/.tools/linux-notices"
for notice in LICENSE.txt ThirdPartyNotices.txt; do
    if [[ ! -f $sdk_root/$notice ]]; then echo "Missing .NET notice: $sdk_root/$notice" >&2; exit 1; fi
    cp "$sdk_root/$notice" "$root/.tools/linux-notices/"
done
# Publish in a fresh temporary directory so previous multi-file outputs cannot leak into the archive.
stage="$(mktemp -d -t gp-generator-publish-XXXXXXXX)"
trap 'rm -rf -- "$stage"' EXIT
"$dotnet_bin" publish "$root/cli/GpStudio.Cli.csproj" -c Release -r linux-x64 \
    --self-contained true -o "$stage" --nologo \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true
chmod 755 "$stage/gp-project-generator"
# Reject loose runtime/helper files, then test a renamed copy with no companion files.
python3 "$root/tools/check_linux_output.py" "$stage" --published
python3 "$root/tests/linux_cli.py" --exe "$stage/gp-project-generator"
mkdir -p "$output"
cp "$stage/gp-project-generator" "$output/"
cp "$stage/AI_GUIDE.md" "$output/"
"$output/gp-project-generator" --version
tar -czf "$output.tar.gz" -C "$stage" gp-project-generator AI_GUIDE.md
printf 'Verified single executable: %s/gp-project-generator\nAI guide: %s/AI_GUIDE.md\nArchive: %s.tar.gz\n' "$output" "$output" "$output"
