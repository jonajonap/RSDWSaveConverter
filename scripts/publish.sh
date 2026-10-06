#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
RUNTIME="${2:-osx-arm64}"
SKIP_TESTS="${3:-false}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS_ROOT="$REPO_ROOT/artifacts"
OUTPUT="$ARTIFACTS_ROOT/$RUNTIME"
PROJECT_PATH="$REPO_ROOT/src/RSDWSaveConverter.App/RSDWSaveConverter.App.csproj"

# Extract version from csproj
VERSION=$(grep -oE '<Version>[^<]+</Version>' "$PROJECT_PATH" | sed 's/<[^>]*>//g' | tr -d '[:space:]')
if [ -z "$VERSION" ]; then
    echo "Error: Version not found in $PROJECT_PATH" >&2
    exit 1
fi

RELEASE_STEM="RSDWSaveConverter-v${VERSION}-${RUNTIME}"
ARCHIVE="$ARTIFACTS_ROOT/${RELEASE_STEM}.tar.gz"
CHECKSUM="$ARTIFACTS_ROOT/${RELEASE_STEM}.sha256"

mkdir -p "$ARTIFACTS_ROOT"
rm -rf "$OUTPUT" "$ARCHIVE" "$CHECKSUM"

if [ "$SKIP_TESTS" != "true" ]; then
    echo "Running test suite..."
    dotnet test "$REPO_ROOT/RSDWSaveConverter.sln" --configuration "$CONFIGURATION"
fi

echo "Publishing $RUNTIME ($CONFIGURATION)..."
dotnet publish "$PROJECT_PATH" \
    --configuration "$CONFIGURATION" \
    --runtime "$RUNTIME" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    --output "$OUTPUT"

cp "$REPO_ROOT/README.md" "$OUTPUT/"
cp "$REPO_ROOT/CHANGELOG.md" "$OUTPUT/"
cp "$REPO_ROOT/THIRD_PARTY_NOTICES.md" "$OUTPUT/"

if [[ "$RUNTIME" =~ ^win- ]]; then
    ARCHIVE="$ARTIFACTS_ROOT/${RELEASE_STEM}.zip"
    (cd "$ARTIFACTS_ROOT" && zip -rq "$ARCHIVE" "$RUNTIME")
else
    tar -czf "$ARCHIVE" -C "$ARTIFACTS_ROOT" "$RUNTIME"
fi

if command -v shasum >/dev/null 2>&1; then
    SHA=$(shasum -a 256 "$ARCHIVE" | awk '{print $1}')
elif command -v sha256sum >/dev/null 2>&1; then
    SHA=$(sha256sum "$ARCHIVE" | awk '{print $1}')
else
    SHA="unknown"
fi

echo "$SHA  $(basename "$ARCHIVE")" > "$CHECKSUM"

echo "=========================================="
echo "Published release $VERSION for $RUNTIME"
echo "Archive: $ARCHIVE"
echo "Checksum: $(cat "$CHECKSUM")"
echo "=========================================="
