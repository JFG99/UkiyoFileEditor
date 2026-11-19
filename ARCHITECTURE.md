# UkiyoFileEditor – code layout

One folder per responsibility, one type per file, namespace = folder. `GlobalUsings.cs` imports every project namespace once.

| Folder / namespace | Responsibility | Types |
|---|---|---|
| `Formats/Binary` | Big-endian reads/writes, magic checks, alignment (shared by all formats) | `BigEndian` |
| `Formats/Cmp` | The `$CMP` LZ container | `CmpDecoder`, `CmpEncoder`, `CmpFormat` (constants), `CmpInfo` |
| `Formats/Stpk` | `STPZ`/`STPK` packs and message packs | `StpkLayer` (parse / rebuild one layer), `StpkArchive` (Flash packs, keeps the archive's own alignment), `StpkFormat`, `MessagePack`, `MessageRow`, `MessagePackWriter` |
| `Formats/Resident` | `game_resident_JP_m.pak` (packs inside packs) | `ResidentArchive`, `ResidentManifest` |
| `Formats/Texture` | `.srd` texture lists, DXT1 / DXT5 pixels, Flash pack pairs | `SrdTextureParser`, `FlashTexture`, `FlashPack`, `FlashPackEntry`, `DxtCodec` |
| `Models` | Plain data shown or passed between layers | `PackFile`, `LoadedPack`, `RowRef`, `IssueItem`, `SheetRow`, `ReferenceCandidate`, `ReferenceResult`, `FlashPackPair`, `PackImportResult` |
| `Services` | File / image operations without UI | `PackBackupWriter` (.old handling), `TranslatedPackBuilder`, `TranslationSheet` (CSV/TSV), `FlashPackExporter`, `FlashPackImporter`, `BitmapConverter` |
| `TextProcessing` | Text helpers | `TextTools`, `AutoFormatter`, `AutoFormatOptions`, `SearchSpec`, `SearchField`, `Csv` |
| `Validation` | Translation quality checks | `TranslationChecker`, `CheckOptions`, `Issue`, `Severity` |
| `Translation` | Stored translations and undo | `TranslationStore`, `TranslationItem`, `TranslationChange`, `UndoStack` |
| `Settings` | Persistent viewer settings | `AppSettings` |
| `UI` | Main window | `MainForm` |
| `UI/Tabs` | The tabs. `MessageTab` and `FlashTextureTab` are partial classes split by responsibility (layout, files, grid, detail, search, save, export, import …) | `MessageTab.*`, `FlashTextureTab.*`, `TextureTab`, `SystemTextTab` |
| `UI/Dialogs` | Forms and prompts | `FindReplaceForm`, `WrapDialog`, `IssuesForm`, … |
| `UI/Controls` | Reusable controls | `PixelView`, `ViewBackground` |

Rules followed: no `Formats` / `Services` / `Models` type references `UI`; file formats are read and written only through the `Formats` classes; constants instead of magic numbers; locals, parameters and fields have descriptive names.
