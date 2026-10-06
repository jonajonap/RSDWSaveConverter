# Changelog

All notable changes to RSDW Save Converter are documented here.

## 0.2.0 - 2026-10-06

### Added

- Cross-platform port to Avalonia UI 11 running on .NET 8.
- Native standalone executables for macOS Apple Silicon (`osx-arm64`), macOS Intel (`osx-x64`), and Windows (`win-x64`).
- Cross-platform packaging script (`scripts/publish.sh`) with automatic SHA-256 generation.
- Safe process detection and atomic file replacement fallbacks for Unix / POSIX filesystems.
- Native storage pickers for macOS and Windows, with original dark and gold aesthetic preserved.

## 0.1.0 - 2026-09-22

### Added

- Portable, self-contained Windows x64 application.
- Steam `.sav` and Game Pass `.xav` world import.
- Steam character `.json` import with byte-for-byte preservation.
- Automatic Dragonwilds WGS profile discovery and manual profile selection.
- Existing world and character destination previews.
- Complete pre-import WGS ZIP backups.
- Transactional index replacement, rollback, and post-import verification.
- Versioned release EXE/ZIP assets and SHA-256 manifest.

### Known Limitations

- Imports replace an existing destination slot; creating a brand-new WGS slot is not yet supported.
- The executable is not code-signed.
