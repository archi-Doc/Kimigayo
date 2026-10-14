# 23. Compiler services

[Specification index](../SPEC.md)

This chapter defines diagnostic categories, what checking a program means and the external contracts of `kimi check` and the language server, and the requirements on an optional Compiler Server Protocol. These contracts never change which programs are valid ([§1.3](01-overview.md#13-normative-status-and-wording)). Editor diagnostics are advisory and never affect a build.

## 23.1. Diagnostics

### 23.1.1. Categories

A diagnostic has a code, a severity (Error, Warning, Information or Hint) and a category. Each code has one severity and one category. Except for the codes this specification names, such as `Unsupported_Kd`, the codes, their messages and the facts they carry are implementation-defined.

| Category | Meaning |
| --- | --- |
| `Language` | The source violates a language rule. |
| `Proof` | A required fact could not be established. |
| `Unsupported` | The form is valid but the implementation does not support it. |
| `Input` | An input, the configuration, dependency content or a lock file could not be established or is invalid. |
| `Resource` | An implementation-defined limit was reached (for example Semantics cases per definition or generated instances); the program may be valid. |
| `Internal` | A compiler defect. |

A Warning reports a valid but suspicious form. The specification requires some warnings, and an implementation may issue others. A Warning, Information or Hint never rejects a program.

A form this specification allows but the implementation does not support is reported as the Error `Unsupported_Kd` (category Unsupported) at its location, never as a Language error. An implementation may leave a valid operation unsupported for some concrete Types; it reports Unsupported at the concrete use, including an instantiation, and this changes no conformance or definition checking.

Exceeding a resource limit is reported as Resource, never as Language or Proof, and never rules the program invalid.

### 23.1.2. Reporting

Each independent failed requirement is reported, even when another check fails. How failures are grouped into diagnostic records (including one failure found under several Semantics cases or instantiations), and whether a failure caused only by another reported failure is also reported, are implementation-defined; grouping never changes acceptance.

A diagnostic names a source and, optionally, a span in it, or no source for a problem outside every source. Lines and columns shown to people follow [§2.1](02-source-and-lexical-structure.md#21-source-text-and-lines); a tool protocol uses the positions its contract states.

```kimi
enum Failure
    Busy

func prepare() -> Result<i32, Failure> => .Ok(1)

func setup() -> i32
    prepare()              // Warning: Result discarded; a Warning rejects nothing.
    let text: string = 1   // Error: 1 does not fit string (Language).
    return missing         // Error: reported as well; it is independent (Language).
```

### 23.1.3. Repair candidates

A diagnostic may carry repair candidates: alternative structured edits of the source files the check read, each listing the conditions the check verified and those it could not decide. A candidate never lists an undecided condition as verified, and applying it yields a new input that only a new check validates. The repair kinds and conditions are the closed enumerations of the JSON schema ([§23.3](#233-json-check-output)); which candidates an implementation offers is implementation-defined.

## 23.2. Checking

### 23.2.1. Check units

A **check unit** is one project, target, mode (Product or Test) and Debug setting. Its result equals what `kimi check` (Product) or test preparation (Test) reports for the same configuration and inputs ([§20.3](../impl/20-compilation-configuration.md#203-commands), [§20.6.1](../impl/20-compilation-configuration.md#2061-selection-and-preparation)). Product and Test units are separate checks: product facts come from product inputs alone ([§18.6](18-modules-and-dependencies.md#186-product-and-test-inputs)), so test preparation can fail while the product check succeeds.

A check reads the project file, the `*.kimi` listing of the project directory ([§20.1](../impl/20-compilation-configuration.md#201-projects-and-sources)), the source files, lock files and dependency content. Source text follows [§2.1](02-source-and-lexical-structure.md#21-source-text-and-lines).

### 23.2.2. Results and acceptance

A Completed result reports every Language and Proof error of its unit, including those found in the concrete instances of its checked set ([§8.15](08-generics-constraints-and-contracts.md#815-generic-body-checking)); a Blocked result reports the Input error that stopped it. This covers concrete layouts and representation obligations.

A result has one **outcome**:

| Outcome | Meaning |
| --- | --- |
| Completed | Checked, with or without errors. |
| Blocked | An input or the configuration could not be established; an Input error explains it. |
| Faulted | The compiler failed internally; an Internal error explains it. |

A cancelled check reports nothing. A result is **accepted** when its outcome is Completed and it reports no Error. A rejected result reports at least one Error.

A program that `kimi check` accepts never fails `emit`, `build` or `run` with a Language or Proof error. Those commands can still fail on a Resource limit, on an operation the target does not support (Unsupported), or on a native-library resolution ([§20.4](../impl/20-compilation-configuration.md#204-native-libraries)), toolchain, output or launch failure. Whether `check` already reports a target's unsupported operations (for example `i128` division) is implementation-defined; when it reports them, it reports them for the same checked set.

```kimi
func divide(a: i128, b: i128) -> i128 => a / b
// No Language or Proof error. A target without 128-bit division reports Unsupported
// when building; check may report it too.
```

## 23.3. JSON check output

`kimi check --Format json` ([§20.3](../impl/20-compilation-configuration.md#203-commands)) writes one `kimi.check/2` document to standard output; everything else goes to standard error. The schema file [`schemas/check-output.schema.json`](schemas/check-output.schema.json) describes every field and is part of this chapter's contract. Spans are offsets and lengths in UTF-16 code units of the source text the check read; ranges are zero-based lines and characters.

The schema version changes when a field or enumeration value is removed, renamed or given a new meaning. Adding an optional field or an enumeration value keeps the version.

```text
{ "schema": "kimi.check/2", "outcome": "Completed", "accepted": false,
  "diagnostics": [ { "code": "…_Kd", "severity": "Error", "category": "Language",
                     "message": "…", "source": 0, "span": { "start": 42, "length": 1 } } ], … }
```

## 23.4. Language server

`kimi lsp` serves the Language Server Protocol over standard input and output. It publishes check diagnostics (positions in UTF-16 code units), may offer repair candidates as quick-fix code actions and may offer Hover; its other capabilities, settings and scheduling are implementation-defined. It never writes files, restores packages or runs code.

For the projects of open documents, the server publishes the diagnostics of check units. Each equals the `kimi check` or test-preparation result for the same unit on the inputs the server read, where an open document's text replaces its file on disk. Which units it checks, when it checks them and when it retires them are implementation-defined.

An open document belongs to the projects whose product sources or `TestSources` ([§20.1](../impl/20-compilation-configuration.md#201-projects-and-sources)) include it. How the server finds those projects, and when the document forms an implicit project ([§20.3.1](../impl/20-compilation-configuration.md#2031-input-resolution-and-implicit-projects)) instead, is implementation-defined.

## 23.5. Compiler Server Protocol

The Compiler Server Protocol (CSP) is an optional structured interface to the compiler for programs, including AI agents. Its wire protocol is implementation-defined. An implementation that provides it must:

- identify every source snapshot by durable content identity, so that each edit, result and piece of evidence names the inputs it rests on;
- apply edits, including repair candidates, only to the snapshot that produced them, and return reviewable source changes, never silent file writes; a repair's undecided conditions are established by checking the edited input;
- report diagnostics in the form of [§23.3](#233-json-check-output);
- give stable handles to syntax nodes, including excluded syntax with its innermost excluding directive ([§19.4](19-compile-time-directives.md#194-excluded-syntax)), and to semantic nodes of selected syntax;
- bind every check, test and measurement to its exact source and configuration, with its outcome and remaining uncertainty;
- list the unsafe promises (Unsafe Blocks with the operations that use them, unsafe calls and `#LibraryImport` declarations) with their obligations ([§5](05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory)), and the suspension points ([§24.1](24-suspension-and-asynchronous-tasks.md#241-tasks-and-task-frames)) with their task slot and the Loans and owned values live across them;
- never narrow a language rule.
