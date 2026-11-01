# LN design: one owner of a source's logical name (R5 U9a, with G289 (3))

Base `abc7e28c7` (dev). Design only; implementation waits for the planning session's GO. Major (a new representation). Probe: the
copies' expressions copied verbatim from the base and run on this host (.NET, Windows), plus a case-folding probe (scratchpad
`lnprobe/probe.cs`, `fold.cs`); rows marked (Linux) are read from the code, not run.

| Revision | Change |
| --- | --- |
| 0 (ae8737e12) | Copies, probe, owner Kotonoha, stored `SourceDocument.LogicalName`; split U9a-1..3. |
| 1 | Review REVISE: every document creator listed with its name (B1); the name is Kotonoha's query over the current base, not a value stored at parse (B2); HoverBuilder's cache goes (N1); SPEC 6.2.1's order an open issue (N2); G346's check half ends in U9a-1, no absolute name for a NUL inside the project (N3); one base for hover placement (N4); pinned Unicode data (N5); split re-sized, U9b with U9a-1 (N6); the merge dropped (N7); contract records (N8). |

## Required meaning

- IMPL 20.1: a source's **logical name** is its project-relative path with `/` separators; names must be NFC, and two names equal under
  simple case folding are an error. Product sources lie directly in the project directory (name = file name); TestSources are
  project-relative paths inside it. An implicit project's directory is its file's (IMPL 20.3.1), so its source's name is the file name.
- Its project is the source's own: a dependency's sources are named relative to the dependency's project (SPEC 18.5: a source package
  holds its sources with their logical names; `DependencySource.LogicalPath` is already the file name).
- Readers: declaration order across documents, UTF-8 byte order of the names (SPEC 6.2.1); runtime locations, displayed with control
  and non-ASCII characters and `\` escaped (IMPL 21.4.2, SPEC 22.5.1); test identities, "independent of absolute paths" (IMPL 20.6.2);
  documentation order, hover footers and links (`DocumentationSource.LogicalName`, `Binding.Documentation`, HoverRenderer, HoverPlacement).
- Not defined by IMPL: an in-memory source (`Project.AddSource(url, text)`) outside its base, on another drive or with NUL, and a project
  without a directory (an API construct; R5-M20 took the current directory). Generated Mod documents need none (SPEC 6.2.1 orders them
  after the others); the bundled library's `compiler://` documents are implementation-defined.

## The copies today (probe; root `C:\p\App`, dependency `C:\p\Lib`)

| Input path | `Kotonoha.ParseSource` :276 | `SourceLocationTable.Text` :50 | `DocumentationSource.SetLocationCore` :197 | `.PhysicalLogicalName` :201 (hover) | `TestCatalog.LogicalPath` :232 |
| --- | --- | --- | --- | --- | --- |
| `C:\p\App\tests\T.kimi` | `tests\T.kimi` | `tests/T.kimi` | `tests/T.kimi` | `tests/T.kimi` | `tests/T.kimi` |
| dependency `C:\p\Lib\Lib.kimi` | `..\Lib\Lib.kimi` | `../Lib/Lib.kimi` | `Lib.kimi` | `Lib.kimi` | `../Lib/Lib.kimi` (catalog inputs) |
| other drive `D:\x\Main.kimi` | `D:\x\Main.kimi` | `D:/x/Main.kimi` | `D:/x/Main.kimi` | null | `D:/x/Main.kimi` |
| no directory, `<cwd>\A.kimi` | absolute | `A.kimi` | absolute, `/` | null | `A.kimi` |
| no directory, `C:\elsewhere\A.kimi` | absolute | `../../../../../../elsewhere/A.kimi` | `C:/elsewhere/A.kimi` | null | as SLT |
| relative `Main.kimi` with a directory | `..\..\<cwd…>\Main.kimi` | `Main.kimi` | `Main.kimi` | `Main.kimi` | `../../<cwd…>/Main.kimi` |
| `compiler://Kimi/0.1/Console.kimi` | `..\..\<cwd…>\compiler:\Kimi\…` | the path | the path | null | (not a source module) |
| `C:\p\App\a<NUL>b.kimi` | ArgumentException | ArgumentException | ArgumentException | ArgumentException | ArgumentException |
| (Linux) file name `a\b.kimi` | `a\b.kimi` | `a\\b.kimi` | `a/b.kimi` | `a/b.kimi` | `a/b.kimi` |

Differences: the base (root vs the source's own project: dependency rows); separators (`ParseSource` keeps `\`; Doc and TestCatalog
turn a non-Windows `\` into a separator); the no-directory base (Doc keeps the absolute path, hover gives none; SLT's memo is keyed by
the project directory, not the current directory it actually reads); relative and `compiler://` paths resolved against the process's
current directory (`ParseSource`, TestCatalog). `ParseSource`'s result is the diagnostic collection name and the path of text parsed
through that collection (`CodeContext.Parse`). A sixth reader caches: `HoverBuilder.logicalNames` from `Kotonoha.HoverSourceName`. No copy
checks NFC or case folding. G289 (3): TestCatalog lines 232 (162 characters) and 273 (235).

## Every sequence that creates a document, and its name

| Kind | Creator (parse sequence) | Kotonoha's base | Name by the rule | Today's readers keep |
| --- | --- | --- | --- | --- |
| Root module source (disk, TestSources, in-memory, deserialized) | `Kotonoha.ParseSource` (`AddSource`, `OnDeserialized`) | the project directory, else the current directory | relative to the base | — |
| Dependency module source | `Kotonoha.ParseSource` | its project's directory (`Url`'s) | relative to its own base | — |
| Text parsed into a module (tests, edits; path = the collection name) | `CodeContext.Parse(…, text)` | the module's | the path as given (relative) | — |
| Mod-generated document | `CodeContext.Parse(…, document, modId, order)` | the module's | by the rule (its path) | documentation and hover: none, by `ModId` and `generatedSourceLocations` (their rules today) |
| Library source, `$abort` | `KimiLibrarySources.ParseSource`, `KimiLibrary.CreateAbort` | none (`compiler://` Url) | the path (`compiler://Kimi/…`), as documentation has it today | hover: none for a built-in source (`SourceIdentity.IsBuiltIn`) |

(`Parser`'s nested interpolation reader reuses its document's context and creates none.)

## Owner, representation, lifetime (revised: B1, B2)

- **Owner: Kotonoha** (owner-confirmed 2026-10-19). It owns the one **base** (`Kotonoha.SourceDirectory`, renamed `Base`: the table's
  column; for a root module without a project directory the current directory, read when asked) and the one **rule**, published as the
  query `Kotonoha.LogicalName(SourceDocument)`. Every reader asks it; none chooses a base. `HoverBuilder.Placement`'s root reads the same
  `Base` (N4).
- **Representation: a query, not a stored column.** Revision 0 stored `SourceDocument.LogicalName` at parse; B2 shows the base can change
  after parse (the public `Project.Directory` setter: `EmissionArtifactsTest.LogicalSourcePathsAreIndependentOfCheckoutDirectory` analyzes,
  then sets the directory, then generates) and a parse can precede `Prepare` (`CreateForTest`, then `Parse`: the root's base, the
  project's directory or the current directory; a test Kotonoha whose `Url` is no path has none). The name is therefore the
  rule over the document's path and the module's current base, so it needs no writer and no invalidation of its own; this also removes
  B1's question of one writer across the four creators. Readers memoize as today: SLT per (document, offset), its memo cleared when the
  resolved base changes (R5-M20's directory key generalized: the current directory now counts, which closes the not-invalidated gap);
  documentation takes the name at parse (`SetLocation(name)`, as `SetLocationCore` computes it at parse today); TestCatalog at discovery.
- **Rule (total, no throwing `Path` call)**: a fully qualified path that lies under the base by a prefix test (the host's path comparer;
  the base normalized once with `GetFullPath`, never the path) is its remainder, with the host's separators as `/`; any other path
  (outside the base, another drive, relative, `compiler://`) is its own text with the host's separators as `/`. Sources the project
  enumerates are already full normalized paths (directory listing, `GetFullPath` of TestSources, dependency directory plus file name), so
  the prefix test equals `GetRelativePath` for them; an in-memory path with `.` or `..` segments keeps them (a change, API inputs only). A
  NUL inside the project gets its relative name, never an absolute one (N3, R5-M20), and SLT escapes it as `\u{0}` (IMPL 21.4.2).
- The parse-entry merge is dropped (N7): with no writer it is unnecessary, and `CodeContext.Parse` also parses non-root containers.

## The NFC and case-fold check

No new code: a project's invalid source set is already `ProjectPreparationFailed_Kd` (Input, at the project file, cause as a note, as for
duplicate TestSources); a dependency's, DependencyResolver's existing failure. The data are pinned like `UnicodeIdentifierData` and
`MarkdownUnicodeData` (N5): Unicode 15.0.0, each source file's SHA-256, a generator script (as `GenerateMarkdownUnicode.ps1`), the
measured size and one check site (Kotonoha's names, at project preparation). NFC uses the compiler's own normalization
(`UnicodeIdentifierHelper`'s decomposition and composition), not the host's ICU; if its tables cover identifier scalars only, U9a-3 extends
them. Simple case folding needs a CaseFolding (C+S) table: `OrdinalIgnoreCase` misses `ẞ`/`ß` and `ſ`/`s` (probe); accepting it would
narrow the rule (AGENTS).

## Options

- **(A) Kotonoha owns the base and the rule, published as one query; every reader switched. Recommended.**
- **(A′) Revision 0's stored `SourceDocument.LogicalName`**: rejected (B2: stale after the directory setter; four creators to write it).
- **(B) A shared helper** taking a base: rejected, every reader would still choose the base, where the dependency and no-directory bugs
  live. **(C) Naming at the input layer**: rejected, in-memory, deserialized, generated and library documents need other paths.

## Behavior changes (fail-first, lowest public layer)

| Unit | Change | Test |
| --- | --- | --- |
| U9a-1 | A dependency's location and catalog input are `Lib.kimi`, not `../Lib/Lib.kimi` | `Locations.Text`; TestCatalog inputs |
| U9a-1 | Documentation names a no-directory source `A.kimi`, not its absolute path | `DocumentationSource.LogicalName` |
| U9a-1 | A relative in-memory path is not resolved against the process directory (TestCatalog, the collection name) | catalog; snapshot |
| U9a-1 | A NUL path inside the project checks (no `CheckFaulted_Kd`) and its location is relative, `\u{0}` escaped (G346's check half; documentation's `SetLocationCore` throws too, so it moves in U9a-1) | check result; `Locations.Text` |
| U9a-2 | Hover names a no-directory and an other-drive source (null today); library and generated stay null | `HoverSourceName` (HoverLinkTest) |
| U9a-2 | `HoverBuilder.Placement` resolves links from the one base (a no-directory project: the current directory) | HoverLinkTest |
| — | A non-Windows `\` stays a character; a current-directory change clears SLT's memo | by review (no Linux host; changing a process's directory in parallel tests is unsafe) |

## Split (each ≤ 1 h)

1. **U9a-1** (≈ 1 h, Major): the base and the query in Kotonoha with its contract header (N8); SLT (path code gone, memo keyed by the
   resolved base; header contract), TestCatalog (G289 (3) with its copy), `ParseSource`'s collection name and documentation's
   `SetLocation(name)` (`SetLocationCore`'s copy gone) switched; the four U9a-1 tests. Net about −35 code lines, +45 test lines. No
   behavior-preserving step comes first: every reader differs on some input.
2. **U9b** (G346, same merge as U9a-1): SLT no longer throws, so G330b's row (`EmissionArtifactsTest.AnExceptionEndsTheCommandsResultAsFaulted`)
   loses its trigger. U9b finds another real exception inside `Publish`'s `generating` exclusion, or, if none exists, removes that
   exclusion with its row (R5-M169 amended). Design it before U9a-1 lands (N6). About 30 min.
3. **U9a-2** (≈ 45 min): hover: `PhysicalLogicalName` gone, `HoverSourceName` the query with the built-in and generated rules,
   `HoverBuilder`'s cache removed (N1; no hold), `Placement`'s one base. Net about −25 lines.
4. **U9a-3** (≈ 1 h): the NFC and case-fold check with its pinned data; STATUS's Source row.

## Records

DECISIONS: a new entry (the base, the query, the rule, the creator table; rejected A′, B, C), amending R5-M20 (the base replaces the
directory key; the rule moves to Kotonoha). ARCH: a canonical row "Logical source name | `Kotonoha.LogicalName`, over `Kotonoha.Base` |
`Core/Kotonoha.cs`" and the Runtime-locations row reading it (N8). SLT's and Kotonoha's contract headers (N8). CODEMAP: "Hover source
links" loses `PhysicalLogicalName` (U9a-2). STATUS: at U9a-3. PLAN LN, G346 and G289 (3) close at the merges. Open issue for PLAN (N2):
SPEC 6.2.1's source order. `Project` sorts full paths in UTF-16 ordinal order and appends TestSources after the product sources
(`Project.cs:287-289, 483-505`), where SPEC orders documents by their names' UTF-8 bytes.

## Questions for the planning session

1. U9b lands with U9a-1, and may remove R5-M169's `generating` exclusion if no other trigger exists: agreed?
2. U9a-3's fold table (generated, pinned data): proceed, or record the check as a STATUS known limit for now?
