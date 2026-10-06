# RSDW Save Converter

**RSDW Save Converter** is a cross-platform desktop utility (Windows, macOS, Linux) built with [.NET 8](https://dotnet.microsoft.com/) and [Avalonia UI 11](https://avaloniaui.net/) for importing *RuneScape: Dragonwilds* Steam world and character saves into the Xbox Game Pass (PC) save container system.

> [!NOTE]
> **Fork Information**: This project is a cross-platform fork of the original Windows-only utility by [RSDWArchive/RSDWSaveConverter](https://github.com/RSDWArchive/RSDWSaveConverter). The user interface has been migrated from Windows Forms to Avalonia UI, enabling native execution on **macOS** (both Apple Silicon and Intel), **Linux**, and **Windows** with a unified codebase.

The application runs entirely locally, makes no network requests, and generates an automatic `.zip` backup of your save profile before modifying any files.

---

## Supported Saves

| Source | Game Pass Destination | Conversion |
| :--- | :--- | :--- |
| Steam world `.sav` | Existing `Qxav` world slot | Adds the 12-byte Game Pass header and zlib compression |
| Game Pass world `.xav` | Existing `Qxav` world slot | Validates and reimports the wrapped world |
| Steam character `.json` | Existing `Qjson` character slot | Validates JSON schema and preserves UTF-8 bytes exactly |

> [!IMPORTANT]
> The importer replaces an existing slot. If you do not want to overwrite your active Game Pass save, create a disposable world or character in the Game Pass version first.

---

## How to Use

### Mode A: Direct Use on Windows (Game Installed Locally)

1. Close *RuneScape: Dragonwilds*.
2. Open `RSDWSaveConverter.exe`.
3. Drag and drop your Steam `.sav`/`.xav` world or `.json` character file into the drop zone (or click **Choose Save File**).
4. The app will automatically detect your local Game Pass profile from:
   ```text
   %LOCALAPPDATA%\Packages\JagexLimited.Dominion_srxstwq7wczqa\SystemAppData\wgs
   ```
5. Select the destination slot you wish to replace.
6. Click **Import World** or **Import Character**.
7. Launch the game via the Xbox app. If prompted with a cloud synchronization conflict, choose **Use local save**.

---

### Mode B: Manual / Cross-Platform Use on macOS (or PC without the game)

You can run this application on a Mac (or any machine that does not have the game installed) to convert and inject saves into an extracted Xbox profile:

#### Step 1: Copy save files from the Windows gaming PC
1. **Your Steam save file**:
   - **World**: `.sav` file (commonly found in `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames\` or within the Steam game folder).
   - **Character**: `.json` file (commonly found in `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveCharacters\<CharacterName>.json`).
2. **Your Xbox Game Pass profile folder**:
   - Navigate to:
     ```text
     %LOCALAPPDATA%\Packages\JagexLimited.Dominion_srxstwq7wczqa\SystemAppData\wgs
     ```
   - Inside `wgs`, locate your user profile folder (a folder with a long hexadecimal name, e.g., `000900000...` or a GUID).
   - **Copy this entire profile folder** to your Mac (via USB drive, local network, etc.).
   - *Requirement*: Make sure the folder contains `containers.index` and that you previously launched the game in Game Pass to create at least one world/character slot to overwrite.

#### Step 2: Convert and inject on macOS
1. Launch **RSDW Save Converter** on your Mac.
2. Drag and drop your Steam `.sav` or `.json` file onto the app window (or click **Choose Save File**).
3. Under **Game Pass Profile**, click **Choose Profile** and select the extracted profile folder (the folder containing `containers.index`).
4. The app will read `containers.index` and populate the compatible destination slots.
5. Select the slot you want to replace.
6. Click **Import World** or **Import Character**.
7. The app will automatically:
   - Create a complete `.zip` backup of the profile folder under `~/.local/share/RSDWSaveConverter/Backups`.
   - Write the new GUID-named data blob and `container.N` blob table.
   - Atomically update `containers.index` and verify data integrity.

#### Step 3: Copy the modified profile back to the Windows gaming PC
1. Ensure *RuneScape: Dragonwilds* is completely closed on the Windows PC.
2. Copy the modified profile folder back to:
   ```text
   %LOCALAPPDATA%\Packages\JagexLimited.Dominion_srxstwq7wczqa\SystemAppData\wgs\<YourProfileFolder>\
   ```
   (Overwrite the existing files).
3. Start *RuneScape: Dragonwilds* via the Xbox app.
4. When Xbox prompts: *"Which save do you want to use?"*, select **Local Save**. The Xbox app will load your imported save and sync it to the cloud.

---

## Safety & Backups

- **Process Guard**: Import is blocked if *RuneScape: Dragonwilds* (`RSDragonwilds` or `Dominion`) is detected running (automatically verified on Windows).
- **Automatic Backups**: A full `.zip` snapshot of the WGS profile is saved before any changes are made:
  - **Windows**: `%LOCALAPPDATA%\RSDWSaveConverter\Backups`
  - **macOS / Linux**: `~/.local/share/RSDWSaveConverter/Backups`
- **Atomic Operations**: New payload and table files are written to disk before `containers.index` is replaced.
- **Post-Import Verification**: The installed payload is read back, unpacked, and verified byte-for-byte against the source. If verification fails, the original index is immediately restored.
- **In-Game Backups Preserved**: Game Pass internal backup slots (`Qbak`) are never overwritten.

---

## Download

Releases provide standalone, self-contained packages:

- `RSDWSaveConverter-v*-osx-arm64.tar.gz`: Native build for Apple Silicon Macs (M1, M2, M3, M4).
- `RSDWSaveConverter-v*-osx-x64.tar.gz`: Native build for Intel Macs.
- `RSDWSaveConverter-v*-win-x64.zip` / `.exe`: Standalone Windows build.
- `*.sha256`: SHA-256 verification manifests.

*(Note: Binaries are unsigned; on macOS, you may need to right-click and choose "Open" or run `xattr -d com.apple.quarantine RSDWSaveConverter` if Gatekeeper displays a security prompt).*

---

## Building from Source

### Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (e.g. `brew install dotnet@8` on macOS)

### Run locally (Development)
```bash
dotnet run --project src/RSDWSaveConverter.App/RSDWSaveConverter.App.csproj
```

### Build & Test
```bash
dotnet restore RSDWSaveConverter.sln
dotnet test RSDWSaveConverter.sln --configuration Release
dotnet format RSDWSaveConverter.sln --verify-no-changes --no-restore
```

### Publish Release Packages

On **macOS / Linux** (Bash):
```bash
# Apple Silicon
./scripts/publish.sh Release osx-arm64

# Intel Mac
./scripts/publish.sh Release osx-x64

# Windows x64 (cross-compilation)
./scripts/publish.sh Release win-x64
```

On **Windows** (PowerShell):
```powershell
.\scripts\publish.ps1 -Configuration Release -Runtime win-x64
```

Self-contained release archives and SHA-256 manifests will be placed in the `artifacts/` folder.

---

## Technical Format Details

### World Saves (`.xav`)
Game Pass world payloads use a 12-byte little-endian header followed by standard zlib compressed data:
```text
uint32 12                        // Header size
uint32 65536                     // Compression block size
uint32 uncompressed_save_length  // Size of raw Steam SAVE data
byte[] zlib_compressed_SAVE_data // Raw data starting with "SAVE" magic
```

### Character Saves (`.json`)
Game Pass character payloads share the same WGS container structure, but their internal `Data` blob is standard UTF-8 JSON matching the Steam character format (`meta_data.char_name`, `GameProgress`, etc.).

### Windows Gaming Services (WGS)
Xbox saves are managed via:
- `containers.index`: Binary index file (version 14) tracking slot names, GUIDs, timestamps, and states.
- `container.N`: Blob table (version 4) linking the `"Data"` blob alias to the active payload GUID.
- GUID-named files: Raw payloads stored without file extensions.

---

## Disclaimer & Attribution

- See [CHANGELOG.md](CHANGELOG.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for version history and third-party licenses.
- Upstream repository: [RSDWArchive/RSDWSaveConverter](https://github.com/RSDWArchive/RSDWSaveConverter).
- RSDW Save Converter is an independent community tool and is not affiliated with, endorsed by, or associated with Jagex or Microsoft.
