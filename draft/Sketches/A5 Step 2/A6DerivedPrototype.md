# A6 derived rules and drop's `self` Move: prototype round ER-derived (A5 step 2, unit 2x-P10)

Date: 2026-10-19. Status: draft for 2g-D's design and 2x-E4a (review R inside 2g-D). It changes no SPEC, IMPL, compiler, library or
test file. Base: dev `adb9aa9e`; every probe output is byte-identical at `48a42168`, the unit's first base.

Inputs: the [A5 step 2 plan](../../Proposals/2026-10-18%20A5%20Step%202%20Implementation%20Plan.md) (PD17, §4.1 row 2x-P10, §4.9),
PLAN G100, G105, G121, G136, DIAGNOSTICS §4.3 ("Later phases", "Suppression"), DECISIONS R4-M10, R4-M47, R4-M61, R4-M6b, R4-M70,
R4-M74, R4-M75, the EXIT audit and the LEDGER rows a1-incomplete, b1, b3, a1-dam, a1-location (`classification.tsv`). Under R4-M70
(option P) A5 has no Opaque-derived Loan problem, so the round covers A1, A3 and A2. "(H)" marks a reading or estimate no run checked.

Files: `A6DerivedPrototype.cs` (the model), `A6DerivedPrototype.out.txt` (its result table), `A6DerivedProbe/` (the probe's source,
its case programs as `probe-cases.json` and `drop-cases.json`, and its outputs `out-cases.txt`, `out-drop-cases-e4a.txt`,
`out-corpus-records.txt`).

## Summary

- **Prototype:** 58 cases (33 A1 at Opaque sites, 15 A3, 10 drop's `self`), each with its SPEC verdict, under 13 rule variants.
  The recommended rules (variant Recommended) hide no independent failure (SPEC 23.1.2) outside 7 listed cases, whose remaining
  results are limits every variant shares (§5 F4, F5); the brief's rules as written (Brief) hide one (o08).
- **The brief's rules hold, with three refinements:** (R1) a `@move` node Binding resolved inside the site is a Move (o08); (R2) H3
  suppresses a problem at a site only through a Binding failure of the same rule, not through any `Binding.FailureCauses` of the
  site (PD17's text, variant AnyCause, hides 10 independent failures); (R3) A3's flag is Binding's guess rule applied at unresolved
  leaves (today's `Binding.IsGuess` flags a whole unresolved match, which hides r15's independent fallthrough on the default
  pipeline today and would hide it on the MIR).
- **Probe over the corpus** (2,234 programs, 547 Binding-incomplete): today 38 A1/A2 records; the write rule removes 17 (Milestone
  41's 4 among them), adds 1 and keeps 21, each classified in §4.2. A3: 32 rows, 27 derived and 5 direct under the MIR rule, the same
  verdict as `Binding.GuessedCompletion` on every row. Self-check: rerunning the analyses without the rewrite changes 0 records in 598
  programs.
- **Drop's `self`:** with 2x-E4a's Binding rule and no Move at the rejected node, each case reports one Binding record per Move and no
  MIR record, and A2's field cleanup verdicts become Keep. Today, in the probe's 11 Move cases (12 Moves), neither Binding nor the
  MIR reports a Move (the MIR reports dependent Moved records in d12, d13, d17 and the 16.3.1 variant), and the old path reports
  `Unsupported_Kd` in 9 (not d03 or the 16.3.1 variant).
- **Open (owner):** an independent failure inside a failed Block, do, if or match, or behind a failed call's operand, stays lost
  without kept children beyond R5-Q21's two forms (F4); b3 (A3 inside a failed do) is such a loss and becomes a default-pipeline
  regression at R6-check unless F4's extension lands.

## How to run

- Prototype: copy `A6DerivedPrototype.cs` to an empty folder outside the repository and `dotnet run A6DerivedPrototype.cs` (SDK
  11.0.100-rc.2; `-v` prints every record per variant). The exit code is Recommended's HIDDEN count outside listed cases (0).
- Probe: build `src/Kimi` (`dotnet build src/Kimi/Kimi.csproj -c Release --no-restore`), copy `src/Kimi/bin/Release/net10.0` to
  `<scratch>/kimibin`, put `A6DerivedProbe/Program.cs` into `<scratch>/probe/` with the project file below (an assembly named
  `Benchmark`, which Kimi's InternalsVisibleTo admits), `dotnet build -c Release -o <scratch>/probebin`, then run
  `dotnet probebin/Benchmark.dll <inputs>`: `PROBE_ALL=1 ... probe-cases.json`, `PROBE_ALL=1 PROBE_E4A=1 ... drop-cases.json`, and for
  the corpus the ledger sources of `bin/worktrees/a5-1b5/artifacts/verify/mir-diff/a5-1b5-b1/sources` with
  `tests/diagnostics/syntax.json` (about 3 minutes; full outputs and the self-check in the unit worktree's
  `artifacts/verify/2x-P10/`).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>Benchmark</AssemblyName>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>CS8600;CS8602;CS8604;CS1591;CS8618</NoWarn>
  </PropertyGroup>
  <ItemGroup><Reference Include="..\kimibin\*.dll" /></ItemGroup>
</Project>
```

## 1. Model

**Today's code.** The builder replaces a node Binding rejected by one Opaque value whose site lists the locals its syntax names
(`MirBuilder.cs:413-439`); every root is visited with `Reads | Writes | Borrows` (`MirDescriptors.cs:99, 314-320`), which A1 treats
as a read of the root's whole subtree (`MirInit.cs:191`): no Define, no Move. `MirReport.cs:9-10` says Add drops derived problems
(R4-M10); it drops none. A3 reports every failure body a runtime path takes to its end (`MirLints`).

**A1 at an Opaque site (the model).** The builder would publish each root's access kind (G136; plan 2g-2a):

| Kind | Meaning (the builder's source) | A1 action | Class of a problem it finds |
| --- | --- | --- | --- |
| Write p | the resolved target Place of a rejected assignment, Tuple targets element by element | MirInit's Define of p's Move Path only: strict ancestors constructed (SPEC 15.1.2), a let path placed once (15.1.1), then p's subtree initialized; a target without a Move Path (through a reference, an index or a computed Property) checks its prefix and changes no state | Repeat |
| Update p | a rejected compound assignment or `++`/`--` | Use(p), then Define(p) | Repeat |
| ReadWhole r | r named as a value (an argument, `self@ref`, `let s = x`) | Use(r): r's subtree initialized (15.1.1, 15.1.2) | Repeat |
| ReadPlace p | a resolved operand's Field Place (`x.a`, `self.b`) | Use(p) | Repeat |
| ReadPart r | r only the prefix of a Field path whose Place is not published | r's own bit; a missing part reported as well (Recommended) or derived (PartDerived) | Repeat / Unknown |
| Move r | an `@move` node Binding resolved (R1) | Use(r), then r Moved (MirInit's Take) | Repeat |
| TypeName r | a name in a Type expression (`[N of u8]`) | nothing: no read | — |
| Unknown r | r named inside a non-leaf site (a failed Block, do, if or match) | Use(r) at the site, state unchanged (today's rule) | Direct |

An Opaque read is never a Move; a sibling part of a written Place keeps its state. A problem at a later use is Direct.

**Publication (H3, DIAGNOSTICS §4.3).** A Direct problem is published with its code. A Repeat problem (a failure A1 finds whatever
the rejected node does) is suppressed when a Binding failure at the site judges the same rule (one failure, Binding's record), else
published with its code. An Unknown problem (one that depends on what the rejected node does) is derived from the site's causes and
suppressed, since every site rests on a published Binding record or a located construction `Unsupported_Kd`. Rules are matched by
family: `init` and `complete` (SPEC 15.1.1, 15.1.2, told apart only by the message), `let` (15.1.1), and Binding's own (`type`,
`call`, `name`, `take` for 16.3.1, `helper` for G100's `self@ref`).

**A3.** A failure body's end reached on a runtime path that passes no flagged site is a Direct `RequireFallthrough_Kd`; reached
only through flagged sites, Derived (suppressed). The flag marks a site whose completion Binding left undecided: a body the parser
supplied, or an unresolved expression that is no recovery (`Binding.IsGuess`, `Binding.Lints.cs:419-434`), taken at unresolved
leaves (R3).

**Drop's `self`.** 2x-E4a: Binding's Take check treats every part of the destruction receiver as reached through exclusive access
(SPEC 16.3.1, 15.1.3), so a Move of a part of `self` is rejected (`ExclusivePathTake_Kd`, as for `uniq/Self` today: d10, d10b); the
rejected node is an Opaque site reading `self` (no Move); A2 judges the field cleanup (R4-M6b) from that state.

**SPEC verdict and checks.** Each case lists the independent failures SPEC requires published (SPEC 23.1.2), Binding's included.
HIDDEN: one that neither Binding nor the MIR publishes (unsound for SPEC 23.1.2). FALSE: a published MIR record SPEC does not list, a
dependent or non-failure published as independent, or a repeat of a Binding record (DIAGNOSTICS "Later phases").

**Variants.** Today; R4-M10 (as written: every problem at a site dropped); Brief (the unit brief's rules with the classes above);
Recommended (Brief with R1; R2 and R3 as described); AnyCause (H3 suppresses every problem at a site, PD17's text); RootWrite (a
write initializes the root's subtree, the EXIT audit's 2g-2 wording); MoveSyntactic (a rejected `@move` node counts as a Move);
NonLeafDerived, NonLeafAssumeInit (Unknown roots derived, or assumed initialized); FlagIsGuess (IsGuess flags a whole unresolved
compound node, as `StructuralCompletion.cs:148` does today); FlagUnresolved (a bare `BindingState` test); NoE4a (no 2x-E4a);
PartDerived (ReadPart's missing parts derived).

## 2. Cases

"Probe" is the built compiler's record set today (`out-cases.txt`, `out-drop-cases-e4a.txt`): B = Binding, CF = the old
control-flow check, OLD = the old ownership path, MIR = the MIR analyses. "Model" is Recommended's published set (prototype `-v`).
Programs are abbreviated; full sources are in `probe-cases.json` and `drop-cases.json`.

**A1 at Opaque sites**

| Case | Program | SPEC verdict | Probe today | Model |
| --- | --- | --- | --- | --- |
| o01 | `var x: i32; x = "text"; return x` | B TypeMismatch only; `return x` is dependent (23.1.2) | B; MIR Uninit at the assignment and at `x` | B |
| o02 | ctor `self.a = a; self.b = b` (b: string) | B only (6.3: b assigned) | B; MIR Uninit at `self.b = b` and the ctor | B |
| o03 | ctor `self.a = a` (rejected), b never set | B; Uninit at the ctor end (15.1.2, 6.3) | B; MIR Uninit at the site and the ctor | B; Uninit at the end |
| o04b | x.b moved; `x.a = "s"`; `look(x.b@ref)` | B; Moved at `look` (15.1.1) | B; MIR Moved at the site and at `look` | B; Moved at `look` |
| o05b | `var x: Pair; x.a = "s"` | B; a part written in a never-constructed Place (15.1.2) | B; MIR Uninit at the site | B; Uninit at the site |
| o06 | `var x: i32; let s: string = x` | B; x read uninitialized (15.1.1) | B; MIR Uninit at the site | same |
| o07 | x.b moved; `let s: string = x.a` | B only (x.a initialized) | B; MIR Moved at the site | B |
| o07p | o07, read Place not published (listed) | B only | as o07 | B; Moved at the site (FALSE) |
| o21 | ctor `self.a = self.b + "s"` before `self.b = a` | B; self.b read uninitialized (15.1.1) | B; MIR Uninit at the site | same |
| o21p | o21, read Place not published | as o21 | as o21 | same (PartDerived: HIDDEN) |
| o08 | `two(x@move)` (wrong arity); `look(x@ref)` | B; Moved at `look` (15.1.5 `@move` marks x Moved; 23.1.2, H) | B only (CLI: NoApplicableOverload only) | B; Moved at `look` |
| o09 | `let x: i32; x = 1; x = "s"` | B; a let placed twice (15.1.1) | B only (CLI: TypeMismatch only) | B; ReassignedLet |
| o10 | `let x = 1; x = "s"` | B; a let placed twice (15.1.1) | B only | B; ReassignedLet |
| o11 | a moved; `a.1 += "x"` | B; a Moved Place read (15.1.1) | B; MIR Moved at the site | same |
| o13 | `x = unknown()` (value rejected, assignment published) | B only | B | B |
| o14 | `if c => x = "s"`; `return x` | B; x uninitialized on the else path (15.1.1) | B; MIR Uninit at the site and at `x` | B; Uninit at `x` |
| o15 | `if c => x = 1 else => x = "t"`; `return x` | B only | B; MIR Uninit at the site and at `x` | B |
| o16 | ctor `(self.a, self.b) = (a, b)` (B InvalidAssignment) | B only | B; MIR Uninit at the site and the ctor | B |
| o17b | x.b moved; `x = "s"`; `look(x.b@ref)` | B only (a whole write replaces x, 15.1.2) | B; MIR Moved at the site and at `look` | B |
| o18 | `let a = (...)`; a moved; `a = ("new", "x")` | B; a Moved let written (15.1.1) | B; MIR Moved at the site | B; ReassignedLet (the code o18b's valid twin gets) |
| o19 | `y = do: x = 1; see(x); "s"`; `x + y` (listed) | B only | B; MIR Uninit at `do` and `x + y` | same (2 FALSE) |
| o24 | `do: if c => x = 1; "s"`; `return x` (listed) | B; x uninitialized when not c | B; MIR Uninit at `do`, at `x` | same (FALSE at `do`) |
| o25 | `do: see(x); "s"`, x uninitialized (listed) | B; x read uninitialized | B; MIR Uninit at `do` | same |
| o26 | `do: consume(x@move); "s"`; `look(x@ref)` (listed) | B; Moved at `look` | B only | B only (HIDDEN) |
| o27 | `two(x@ref)` (wrong arity), x uninitialized | B; x borrowed uninitialized (15.1.5) | B; MIR Uninit at the site | same |
| o28 | `let a = (...)`; `a.1 = 2` | B InvalidAssignment only (15.1.5) | B only | B (ReassignedLet suppressed: same rule) |
| o30 | `y = do: see(x); x = 1; "s"`; `x + y` (listed) | B; x read before its write | B; MIR Uninit at `do`, at `x + y` | same (FALSE at `x + y`) |
| bdfa | param a; `a.0@move`; `a.0 = "new"` (B InvalidAssignment) | B only | B; MIR Moved at the site | B |
| 72a6 | ctor `let escaped = self` before init | B InvalidAssignment (15.1.2) | B; MIR Uninit at the site | B (suppressed: same rule) |
| 52ea | ctor `self.a = self.b`, b a getter | B InvalidAssignment (15.1.2, accessors taking the whole self) | B; MIR Uninit at the site | B (suppressed) |
| 4ce1 | `let N: i32; let row: [N of u8]` | B InvalidTypeFormation only | B; MIR Uninit at the site | B |
| b45a | SPEC 14.9.3 block: `use(port)` unresolved | B; port uninitialized on the false path (the example's Error) | B; MIR Uninit at the site | same |
| f1a0 | ctor `value = 1` (QualificationRequired) (listed) | B only (the ctor-end failure is dependent) | B; MIR Uninit at the ctor end | same (FALSE) |

**A3**

| Case | Failure body | SPEC verdict | Probe today | Model |
| --- | --- | --- | --- | --- |
| r01 | `require c else => logFailure()`, unresolved | B only: completion undecided (14.11, 23.1.2) | B; MIR fallthrough; MIR rule derived, GuessedCompletion rests | B |
| r02 | `else` then `if d => logFailure()` | B; fallthrough on the false path | B; MIR fallthrough; direct, direct | B; fallthrough |
| r03 | `else => log(1)`: one candidate, Unit, none applicable | B only: no candidate selected, completion undecided (H) | B; MIR fallthrough; derived, rests | B |
| r04 | `else => stop(1)`: `stop` returns Never, none applicable | B only | as r03 | B |
| r05 | `y: i32 = do: require c else => (); "s"` | B; the nested fallthrough (14.11, 23.1.2) | B; CF fallthrough; no MIR record | not modelled: lost on the MIR (F4) |
| r06 | `require unknownCond() else => ()` | B; fallthrough | B; CF and MIR fallthrough; direct, direct | B; fallthrough |
| r07 | `require a else => require b else => logFailure()` | B; the outer fallthrough | B; MIR both; outer direct, inner derived | B; outer |
| r08, r09 | `logFailure(); logMore()`, `logFailure(); ()` | B only | B; MIR fallthrough; derived, rests | B |
| r10, r12 | `loop => logFailure()`, `logFailure(); return` | B only | B | B |
| r11 | `else => log("x")` (Binding-complete) | fallthrough | CF and MIR fallthrough | fallthrough |
| r13 | `else => if unknownCond() => ()` | B only: the condition may be Never | B; MIR fallthrough; derived, rests | B |
| r15 | `else => match d` with `true => logFailure()`, `false => ()` | B; fallthrough through the written `false` arm (14.11; DIAGNOSTICS "a written branch that falls through") | B; MIR fallthrough; derived, rests (CLI: UnresolvedBinding only) | B; fallthrough |
| 42d4 | `require a == 1 else =>`, body supplied by the parser | syntax error only | MIR fallthrough; derived (supplied), rests | syntax error |
| b9d8 | `else => 0 < a < 2`, a recovered chained comparison | syntax error; fallthrough (DIAGNOSTICS) | MIR fallthrough; direct, direct | syntax error; fallthrough |

**Drop's `self`** (probe with `PROBE_E4A=1` simulating 2x-E4a)

| Case | Program in `drop` | SPEC verdict | Probe today | Model (probe and prototype) |
| --- | --- | --- | --- | --- |
| d01, d02, d03, d04 | `self.buffer@move` (then restored, d02), a Copy Field's `@move` (d03), `self.inner.x@move` (d04) | Error at the Move (16.3.1: no field can be Moved out; 15.1.5: `@move` transfers even a Copy value) | B none; MIR none; OLD Unsupported_Kd (not d03) | B take; no MIR record |
| d05, d06, d07, d08 | `exchange(self.buffer@uniq, ...)`, a Copy read, a shared borrow, a local's part Move | valid | none | none |
| d09 | `Sink.take(self@move)` | Error (16.3.1) | B InvalidAssignment at `self` | same |
| d10, d10b | `self.buffer@move`, `self.count@move` through `uniq/Self` (not drop) | Error (15.1.3) | B ExclusivePathTake_Kd | same |
| d11, d15, d16 | the Move under an if; as an argument; of a base Field | Error at the Move | B none; MIR none; OLD Unsupported_Kd | B take; A2 `_1.base1` open → keep (d16) |
| d12 | Move, then `observe(self.buffer@ref)` | Error at the Move; the later use is dependent | MIR Moved at the use; OLD Unsupported, Moved | B take; no MIR record |
| d13 | `self.box.value@move`, Box declares a drop | Error at the Move | MIR Moved at the Drop (A2, R4-M6b) | B take; A2 open → keep |
| d14 | Move, and `let n: string = 1` | Error at the Move; TypeMismatch | B TypeMismatch only | B take and TypeMismatch |
| d17 | two `self.value@move` (Box<T>) | an Error at each Move | MIR Moved at the second; OLD Unsupported ×2, Moved | B take ×2; A2 elide → keep |
| 16.3.1 | SPEC 16.3.1's example with `let moved = self.buffer@move` | Error at the Move (B's helper-borrow record is G100's, 2x-E6) | B InvalidAssignment; MIR Moved at `let whole = self@ref`, at the two later uses | B take, B InvalidAssignment; no MIR record |

## 3. Variant results

| Variant | HIDDEN | outside listed cases | FALSE | Cases |
| --- | ---: | ---: | ---: | --- |
| Today | 15 | 14 | 39 | HIDDEN o08, o09, o10, o18, o26 and every drop Move (10 in 9 cases); FALSE the false and dependent records of §2 |
| R4-M10 | 24 | 21 | 21 | HIDDEN also o05b, o06, o11, o21, o21p, o25, o27, o30, b45a: every independent failure at a site |
| Brief | 2 | 1 | 6 | HIDDEN o08, o26 |
| **Recommended** | 1 | 0 | 6 | HIDDEN o26; FALSE o07p, o19 (2), o24, o30, f1a0: all listed |
| AnyCause | 11 | 10 | 5 | HIDDEN o05b, o06, o09, o10, o11, o18, o21, o21p, o27, b45a |
| RootWrite | 4 | 3 | 6 | HIDDEN o03, o04b, o05b (a sibling part) |
| MoveSyntactic | 1 | 0 | 11 | FALSE d12, d13, 16.3.1 (dependent Moved records) |
| NonLeafDerived | 3 | 0 | 4 | HIDDEN o25, o30 (a read inside the node), o26 |
| NonLeafAssumeInit | 4 | 0 | 2 | HIDDEN o24, o25, o26, o30 |
| FlagIsGuess | 2 | 1 | 6 | HIDDEN r15 |
| FlagUnresolved | 3 | 2 | 7 | HIDDEN r15, b9d8; FALSE 42d4 |
| NoE4a | 11 | 10 | 12 | HIDDEN every drop Move |
| PartDerived | 2 | 1 | 5 | HIDDEN o21p |

## 4. Probe over the corpus

### 4.1 Method and totals

The corpus is the 1,901 sources the a5-1b5-b1 ledger saved (every mir-pipeline test compilation with a record on either side at
`806110fc`) and the 333 programs of `tests/diagnostics/syntax.json`: 2,234 programs, 547 Binding-incomplete (`Binding.Result` not
complete or a lint Error, R4-M47's section rule). The probe rewrites each Opaque assignment site it resolves (11: 9 writes, 2
updates; 4 targets without a Move Path kept) into the model's form and reruns A3, A1 and A2 on the rewritten MIR (header of
`A6DerivedProbe/Program.cs`). It does not publish, so the Repeat/Unknown classes are the prototype's; the probe's at-site records
were classified by reading each source (§4.2).

| Records (547 programs) | Today | Gone | Added | Kept |
| --- | ---: | ---: | ---: | ---: |
| A1/A2 at a rejected assignment's site | 11 | 11 | 1 | 0 |
| A1/A2 at another site | 8 | 0 | 0 | 8 |
| A1/A2 elsewhere | 19 | 6 | 0 | 13 |
| A3 `RequireFallthrough_Kd` | 32 | — | — | 27 derived, 5 direct |

### 4.2 Each changed or kept A1/A2 record (`out-corpus-records.txt`)

- **Gone (17), each dependent, SPEC 23.1.2:** Milestone 41's two copies (`0b3cedcb4cc5`, `0f5a66264a43`, PLAN G105's Milestone 41
  rows) and `c5d10f36f256`: `self.selfWeak = selfWeak@move` under Binding's Weak `Unsupported_Kd`, at the site and the ctor end (6);
  `9cfbf7464ca1`, `c4d576c541d1`, `565362ba10e0`: a ctor Field assignment Binding rejected, at the site and the ctor end (6);
  `ad097f61eee7`, `af580abd707d`, `bdfacc4f7404`, `c44d6f026e55`: a write to a parameter or its part after a part's Move,
  Binding's InvalidAssignment (4); `4698f40020c9`: `a.0 = "new"` on a let (1).
- **Added (1):** `4698f40020c9`, ReassignedLet at `a.0 = "new"`: a repeat of Binding's InvalidAssignment, suppressed under R2 (the
  probe cannot publish; o28 is its prototype twin).
- **Kept at another site (8):** `b45a1011b584` (`use(port)`, SPEC 14.9.3's Error): independent, published. `453a8425e420`,
  `52ea3ab966fd`, `fd855292b745`, `72a63ab4e96b`, `cd80dd8ab0f3`: a constructor's read of an incomplete `self` that Binding rejects
  with InvalidAssignment (SPEC 15.1.2, 6.3.4): repeats, suppressed under R2 (72a6, 52ea). `4ce1412d0adc`: N in `[N of u8]`, no read
  (TypeName). `0ea87e2f3caf` (the 16.3.1 Move variant): gone under 2x-E4a.
- **Kept elsewhere (13):** independent and published: `14093527ff9b` ×3 (SPEC 14.10.3's p, x, y), `4349d7b7b4cc`, `580e694acd50`,
  `d3b4f9141d81`; `6f754574826c` (a ctor leaving the Field h, whose Type failed, uninitialized; independent, H). Gone under 2x-E4a:
  `0ea87e2f3caf` ×2. Unit 1c-6's (`DefaultArgumentMove_Kd`, R4-M69): `02c68520fdb6`, `658bd9e2efb9`, `e9da10aec0c2`
  (`StaticMovePathRequired_Kd` in a default). Listed: `f1a041fd01cb` (F5).
- **A3:** 26 rows derived through parser-supplied bodies (syntax.json's recoveries and `require ... else =>` sources), 1 through an
  unresolved call (`b054fe57226c`, SPEC 14.11's `logFailure()`), 5 direct; `Binding.GuessedCompletion` gives the same 32 verdicts. No
  corpus row passes a non-leaf flagged site (r15's shape is constructed).
- **Coverage:** Opaque sites: 474, 34 of them non-leaf with roots (if 22, match 11, do 1); no A1 record at or after one today.

## 5. Findings

- **F1. The write rule is sound, and needs the written Place.** "An Opaque write completes the written Place's Move Path only"
  removes every false record of a1-incomplete's shape and keeps the independent ones (o03, o04b, o05b, o14). Initializing the
  root's subtree (RootWrite, the EXIT audit's 2g-2 wording) hides o03, o04b and o05b (SPEC 15.1.2, 6.3). The publication must
  carry each written Place (a MirPlace built from the resolved target, Tuple targets element by element), not only an access kind
  per root: 2g-2a's "access kind" is to be read as (kind, Place) rows.
- **F2. "An Opaque read is no Move" needs R1.** A `@move` node Binding resolved inside a rejected call is a Move whose effect is
  independent of the call's failure (o08; SPEC 15.1.5, 15.1.1, 23.1.2, H). Both pipelines hide it today (CLI: NoApplicableOverload
  only). Recommended: the kind Move for an `@move` node Binding resolved (applied as A1's Take). A rejected `@move` node (a Move of
  drop's `self` after 2x-E4a) stays no Move: taking the kind from the syntax (MoveSyntactic) publishes dependent Moved records (d12,
  d13, 16.3.1). A refinement of the brief's rule, decided in 2g-D.
- **F3. The cause contract (PD17, AR-7) must match by rule.** Suppressing every problem at a site through
  `Binding.FailureCauses` (AnyCause) hides 10 independent failures: an uninitialized read at a site Binding rejected for another
  rule (o06, o21 and its twin o21p, o27, b45a), a let placed twice (o09, o10, o18), a part write into a never-constructed Place
  (o05b), a Moved read in an update (o11). R4-M10 as written hides them too (21). Publishing every certain problem direct (no
  Repeat class) duplicates Binding's record in 5 corpus programs (`453a`, `52ea`, `fd85`, `72a6`, `cd80`) and in o28 and o29.
  Options: (a) Repeat problems are derived only
  through a Binding check of the same rule: the problem's cause fact names the site and its class, and H3 matches the site's Binding
  check key by requirement (DIAGNOSTICS §4.3 already keys prerequisites by requirement); (b) PD17 as written (unsound); (c) Repeat
  problems direct (sound; duplicate records, against "Later phases"). Recommended (a). The match needs a Binding requirement finer
  than `BindingFailure.InvalidAssignment`: in the 16.3.1 Move variant, InvalidAssignment is G100's helper-borrow defect and must not
  suppress an A1 failure. For 2g-D and AR-7 (OQ6's fifth item holds only with R5's acceptance, R4-M74).
- **F4. Non-leaf sites and failed calls' operands need kept children beyond Q21 (owner question 1).** A site containing statements
  is one Opaque value. No A1 rule for its roots is both SPEC 23.1.2-sound and precise: direct (today, Recommended) publishes
  dependent records (o19, o24, o30) and loses a Move inside (o26); derived hides a read inside (o25, o30); assumed initialization
  hides a use after a conditional write (o24). A3 loses an independent fallthrough nested in a failed do (r05, b3 row
  `b34361a58578`), which the old control-flow check reports (CF record in both): at R6-check, when the MIR replaces that check, this
  becomes a default-pipeline loss. R5-Q21 (R5-M23; `Q8-package.md` §3.8) keeps the children of failed closures and failed compound
  Places only; a failed Block, do, if or match and a failed call's resolved operands stay replaced. Corpus count: 0 A1 records at
  non-leaf sites, 1 A3 row (b3).
- **F5. Further kind facts.** A name in a Type expression is not a read (4ce1: a false Uninit today). ReadPlace needs the resolved
  operand's Place: with the root alone (ReadPart), reporting a missing part publishes a false record (o07p) and deriving it hides an
  independent read (o21p, PartDerived); so a read is published as its Place where the operand resolved, the rule F1 gives writes. An
  assignment whose target Binding left unresolved writes an unknown Place (f1a0: a dependent ctor-end record stays direct; listed).
- **F6. The write rule recovers independent failures both pipelines miss today** (o09, o10, o18: a let placed twice after a rejected
  assignment; SPEC 15.1.1; CLI: TypeMismatch only). Open issue (Binding): a let part's write permission is judged by Binding's
  InvalidAssignment when the value type-checks (o28, o29) and by A1's ReassignedLet when only the value fails (o22), two owners of
  SPEC 15.1.5's rule; under R2 the duplicates are suppressed, but one owner is the cause fix.
- **F7. A3's flag is Binding's guess rule.** A bare `BindingState` test hides b9d8's independent fallthrough (a recovered chained
  comparison, DIAGNOSTICS "Later phases") and publishes 42d4's dependent one (a parser-supplied body). With IsGuess the 32 corpus rows
  agree with `Binding.GuessedCompletion`. The builder should publish Binding's verdict per site (one rule, Binding's) rather than
  derive it.
- **F8. Binding's guess rule flags a whole unresolved compound node (default pipeline today).** `StructuralCompletion.Compose` tests
  the guess before descending (`StructuralCompletion.cs:148`), so an unresolved match whose written `false => ()` arm completes is a
  guess: r15's independent fallthrough is derived and suppressed (CLI: UnresolvedBinding_Kd only), against DIAGNOSTICS' "a written
  branch that falls through" stays direct; SPEC 14.11, 23.1.2. The MIR rule inherits it through the flag (FlagIsGuess). Fix: the
  guess applies to unresolved leaves and a compound node descends into its written parts; the builder's flag for a non-leaf site is
  Binding's guessed completion of that node. Owner: Binding (BU), a diagnostic fix in rejected programs; corpus count 0.
- **F9. Drop's `self` (2x-E4a, 2x-E4b).** 2x-E4a's rule must cover every part of `self`, Copy Fields (d03: `@move` transfers even a
  Copy value, 15.1.5) and base Fields (d16) included; with the Opaque no-Move rule it leaves exactly one Binding record per Move and no
  MIR record in every case, and A2's field cleanup verdicts are Keep (d13, d16, d17). A2 needs no rule of its own (a second copy of
  Binding's rule; R4-M6b rejected a consumer recognizing the receiver). Today the MIR accepts every such Move and the old path reports
  `Unsupported_Kd` in 9 of the probe's 11 Move cases (not d03; the 16.3.1 variant, Binding-incomplete, has no old ownership
  record).

## 6. Owner questions and SPEC gaps

1. **(F4) Kept children beyond Q21.** Options: (a) extend R5-Q21, in W1, to keep the items of a failed Block, do, if or match and a
   failed call's resolved operands (G121's general form; a further change of R4-M61's contract, R5's planning session); (b) an R4
   unit after W1 with the same contract; (c) record the residue (independent failures inside rejected compound nodes, including
   b3's A3 record) as a STATUS limit with an owner-approved stage. Recommended (a): it is G121's cause, and b3 otherwise regresses
   at R6-check.
2. **SPEC reading (report only).** SPEC 23.1.2 does not say whether the effect of a valid operand inside a rejected node (o08's Move)
   or the completion of a call without a selected candidate (r03) is independent of the node's failure. The model reads the first as
   independent and the second as undecided (H). Options: (a) record both readings in 2g-D's DIAGNOSTICS contract; (b) a SPEC
   clarification in a separate session. Recommended (a), with (b) only if 2g-D's review disputes them.

No SPEC change is needed for the rules modelled, and no loss (a form SPEC accepts that a rule rejects) was found: the model's
records lie in Binding-rejected programs or are failures SPEC states (r11: 14.11; the drop Moves: 16.3.1), and the valid drop cases
(d05 to d08) stay accepted.

## 7. Unverified

- The probe resolves an assignment's target by name among the site's roots and Fields by stored-Field name (shadowing is not
  handled); it rewrites assignments only, so the other kinds (Move, ReadPlace, TypeName, Unknown) and the classes are the
  prototype's, applied to the corpus records by reading their sources.
- The corpus holds only programs with a record on either side at `806110fc`: Binding-rejected programs with none were not saved, so
  records the write rule adds there (o09's shape) are not counted.
- 2x-E4a is simulated by turning the Move into a read in the built MIR; Binding's rule itself is not implemented.
- The prototype's A1 has branches but no loops (MirInit's union join is assumed to carry over, H); A3's derived rule in the probe walks
  the MIR from each failure body's entry, not MirLints' accumulated walk.
- The readings marked (H) in §2 (o08, r03, 6f75) and the sizes are unchecked by a SPEC session.
