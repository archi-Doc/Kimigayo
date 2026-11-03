# R6-check H13: Mir/Build's syntax-class references, confirmed site by site

Status: unit H13 of [H0c](H0c-deletions.md) §4.3 (H0-design row H13), documentation only, no code change, not reviewed. Working
record, not authority: a fix unit it proposes becomes work only through the planning session (PLAN) and its own design.

Standing request: R5 leads R6-check (owner-approved design H0, DECISIONS R5-M182); H13 confirms that the MIR builder follows ARCH's
amended syntax-data rules (`docs/dev/COMPILER_ARCHITECTURE.md` :172-176 and the "MIR dispatch" row at :189; DECISIONS R5-M10, the
owner's OQ14; R5-M79, Q16 landed in `508f12a12`, under R5-M10 and R5-M23).

Base: dev `ebb04272b`. Sites are `src/Kimi/Compiler/Mir/Build/MirBuilder.<file>`, so `Values.cs:58` is `MirBuilder.Values.cs:58`
and `cs:80` is `MirBuilder.cs:80`. `KindOp` is Binding's HirOp table, `C/Binding/Binding.Hir.Operations.cs:106-149`. No build or test
was run.

Working time (WORKFLOW "Working-time limit", DECISIONS Q12): 2026-11-03 14:56–15:13 (17 min of the hour).

## 1. The rules and the three classes

R5-M10 and ARCH :174-176/:189: after R6, Mir/Build has no Koto type test or `KotoKind` dispatch "that chooses an operation no published
`HirOp` decided"; "an operator token or form read under its published `HirOp`, and a cast to read a child, are syntax data". R5-M79
names three uses as syntax data explicitly: `Published`'s value-child switch, a constructor's base call and the literal forms under
`HirOp.Literal`. Each match below is exactly one of:

- **D, syntax data under a published fact**: an operator token, a form, a literal's value or a span, read at a node whose operation the
  HIR publication (`HirOp`, `HirPlaceKind`, the plan rows) or a published Binding fact (symbol kind, `AdtDef.IsConstructor`, the closure
  definition) already decided.
- **C, a child cast**: a cast or type pattern to the Koto type that the published fact fixes, to read the node's children or a fact the
  node carries; the type decides nothing.
- **V, a violation**: the type or token decides an operation, a form or a branch where a published fact should decide.

## 2. Greps and counts

Over `src/Kimi/Compiler/Mir/Build/` (comments included; `T` = `[A-Z][A-Za-z]*Koto\b`; matches counted with `git grep -o`):

| Family | Pattern | Base | H0c | Difference |
| --- | --- | ---: | ---: | --- |
| `KotoKind.` references | `KotoKind\.` | 27 (22 lines, 2 files) | 27 | none |
| Koto type tests | `\b(is\|as) (not )?T` 13, `case T` 4 | 17 | 17 | none (H0c's "patterns" are the 4 `case` patterns) |
| `(XKoto)` casts | `\(T\)` | 37 (36 lines) | 37 | none |
| Koto switch-expression arms | `(^\s*\|[{,] )T( \{.*\})?( [a-z]\w*)?( when .*)? =>` | 13 (11 lines) | 9 | +4, below |
| Total | | **94 (83 lines)** | 90 | +4 |

- **Against H0c's 90.** Mir/Build is unchanged in Koto references between H0c's base `1ec539c79` and `ebb04272b` (one edit,
  `Defaults.cs:76-80`, G157 (1) U1 `24b1df9c2`, has none), so the +4 is counting. H0c recorded no arm pattern; its 9 match a line-start
  pattern without property patterns or inline arms, which leaves out `Values.cs:211` and `:346` (`ArrayLiteralKoto { FillLength: null }`,
  `CharLiteralKoto { Value: ... }`) and the two arms on `Places.cs:12`. (H: reconstructed, not confirmed with H0c's author.)
- **Against R5-M10's 63 + 17 = 80.** Those are lines, by one regex (type tests, `case XKoto`, property patterns, casts, `as`,
  `KotoKind.`), on 2026-10-18 (`draft/Sketches/R5 Step 0/deep-dives/q5-r6check-prereqs.md` §7). The same four greps over time:

  | Revision | Matches | Lines |
  | --- | ---: | ---: |
  | `e1750a14c` (survey day) | 97 | 86 |
  | `508f12a12^` (before Q16) | 102 | 90 |
  | `508f12a12` (Q16, R5-M79) | 93 | 82 |
  | `ebb04272b` (base) | 94 | 83 |

  Matches exceed lines where one line holds several (the operator table's neighbours: `Expansions.cs:201` 4, `:184` 2, `Operators.cs:47`
  3, `Places.cs:12` 3, `Formatting.cs:49` 2, ...). Q16 removed 10 `is`/`as` and 3 `case` matches (the 17 deciding sites) and added the 4
  casts that read under the new facts (`cs:133, 149, 155, 187`). Since Q16, by match: +4 (`Blocks.cs:24` G241 `08041b4b1`;
  `Selections.cs:68` G168b `dc88cfca8`; `Values.cs:211` alit `c135473ac`; `cs:188` G158 (3) `584ffc372`) and −3 (`Formatting.cs`'s
  `is InterpolatedStringKoto` and `is StringLiteralKoto`; one of `Values.cs`' two `is ArrayLiteralKoto`, folded into the arm at :211); the
  `Expansions.cs` range sites moved to `Selections.cs` (`ca6d96ecc`) and `Opaque.cs`' to `Opaque.Roots.cs`. 30 of the 83 lines were last
  changed after the survey, mostly by moves and restructurings.

## 3. The table (one row per match)

| # | Site | Construct | Class | Reason |
| ---: | --- | --- | :---: | --- |
| 1 | `Blocks.cs:22` | `(VariableKoto)` | C | under `HirOp.Let`, which `KindOp:135` publishes on `FieldKoto : VariableKoto` |
| 2 | `Blocks.cs:24` | `is VariableKoto` | **V** | V1 (§5): under `HirOp.Error` the type chooses `Let` |
| 3 | `Blocks.cs:28` | `(RequireKoto)` | C | under `HirOp.Require` |
| 4 | `Blocks.cs:34` | `(DiscardKoto)` | C | under `HirOp.Discard` |
| 5 | `Blocks.cs:37` | `(JumpKoto)` | C | under `HirOp.Jump` or `Continue` (`ContinueKoto : JumpKoto`) |
| 6 | `Calls.cs:24` | `is not BinaryKoto` (with `BinaryOp(Akind)`) | D | the comparison operator's relation under `HirOp.Call` (SPEC 13.4.1: `==` bool, `!=` negated, else compare with 0); the call is Binding's `CallPlan` |
| 7 | `cs:80` | arm `FunctionKoto` | C | `Published`'s value child (R5-M79: syntax data); `HirOp` not Unset and the value's not Syntax decide |
| 8 | `cs:81` | arm `PropertyAccessorKoto` | C | as 7 |
| 9 | `cs:82` | arm `PropertyKoto` | C | as 7 |
| 10 | `cs:133` | `(FunctionKoto)` | C | the symbol kind (Function or none) decides (R5-M79) |
| 11 | `cs:149` | `(PropertyAccessorKoto)` | C | symbol kind `PropertyAccessor` |
| 12 | `cs:155` | `(PropertyKoto)` | C | symbol kind `Property` with `HirRes.Static` |
| 13 | `cs:187` | `(FunctionKoto)` | C | `AdtDef.IsConstructor(function)` decides |
| 14 | `cs:188` | `as FunctionKoto` | D | a span: the written result and parameter Type nodes that locate the locals (R4-M90 D6); no operation |
| 15 | `cs:230` | `(FunctionKoto)` | C | `MirBodyKind.Closure`, from the published closure definition (R5-M79 (a)) |
| 16 | `cs:255` | `(FunctionKoto)` | C | a constructor (`self` from `IsConstructor`) |
| 17 | `Defaults.cs:183` | `is not FunctionKoto { ClosureDefinition: not null }` | C | reads the published closure definition (`FunctionKoto.ClosureDefinition`, R4-M152; R5-M79 (a)), which decides |
| 18 | `Drops.cs:206` | `(DeferredBlockKoto)` | C | a `Defer` registration exists only under `HirOp.Defer` (`Blocks.cs:31`, `Values.cs:88`) |
| 19 | `Expansions.cs:132` | `is BinaryKoto` | D | form under `HirOp.CompoundAssign` (`op=` against `++`/`--`, `KindOp:143-144`) |
| 20 | `Expansions.cs:132` | `(UnaryKoto)` | C | the `++`/`--` form's operand |
| 21 | `Expansions.cs:142` | `as MemberAccessKoto` | C | the written receiver child; the plan's accessor and `HirTables.ResOf` decide |
| 22 | `Expansions.cs:184` | `KotoKind.PostfixIncrement` | D | token under `CompoundAssign`: a postfix form gives the old value (SPEC 13.2) |
| 23 | `Expansions.cs:184` | `KotoKind.PostfixDecrement` | D | as 22 |
| 24 | `Expansions.cs:201` | `KotoKind.PrefixPlusPlus` | D | token under `CompoundAssign`: `++`/`--` to its arithmetic operator |
| 25 | `Expansions.cs:201` | `KotoKind.PostfixIncrement` | D | as 24 |
| 26 | `Expansions.cs:201` | `KotoKind.Plus` | D | as 24 (the operator read through `BinaryOp`) |
| 27 | `Expansions.cs:201` | `KotoKind.Minus` | D | as 26 |
| 28 | `Formatting.cs:49` | `(MacroKoto)` | C | under `HirOp.Interpolation` with `BoundFormatting.Acquisition` (a `$tryWrite`, R5-M79) |
| 29 | `Formatting.cs:49` | `(InvocationKoto)` | C | the writer operand, as 28 |
| 30 | `Opaque.Roots.cs:133` | `is FunctionKoto` | C | reads the published closure definition, as 17 |
| 31 | `Operators.cs:14` | `KotoKind.Plus => Add` | D | the operator-token table `BinaryOp`; each reader reads it under a published op: `Primitive` and `Recorded` (`PrimitiveOp`), `Call` (`Call`, row 6), `Update` (`CompoundAssign`) |
| 32 | `Operators.cs:15` | `KotoKind.Minus => Sub` | D | as 31 |
| 33 | `Operators.cs:16` | `KotoKind.Asterisk => Mul` | D | as 31 |
| 34 | `Operators.cs:17` | `KotoKind.Slash => Div` | D | as 31 |
| 35 | `Operators.cs:18` | `KotoKind.Percent => Rem` | D | as 31 |
| 36 | `Operators.cs:19` | `KotoKind.LessThanLessThan => Shl` | D | as 31 |
| 37 | `Operators.cs:20` | `KotoKind.GreaterThanGreaterThan => Shr` | D | as 31 |
| 38 | `Operators.cs:21` | `KotoKind.Ampersand => BitAnd` | D | as 31 |
| 39 | `Operators.cs:22` | `KotoKind.Bar => BitOr` | D | as 31 |
| 40 | `Operators.cs:23` | `KotoKind.Caret => BitXor` | D | as 31 |
| 41 | `Operators.cs:24` | `KotoKind.EqualsEquals => Eq` | D | as 31 |
| 42 | `Operators.cs:25` | `KotoKind.ExclamationEquals => Ne` | D | as 31 |
| 43 | `Operators.cs:26` | `KotoKind.LessThan => Lt` | D | as 31 |
| 44 | `Operators.cs:27` | `KotoKind.LessThanEquals => Le` | D | as 31 |
| 45 | `Operators.cs:28` | `KotoKind.GreaterThan => Gt` | D | as 31 |
| 46 | `Operators.cs:29` | `KotoKind.GreaterThanEquals => Ge` | D | as 31 |
| 47 | `Operators.cs:38` | `case MemberAccessKoto` | D | `PrimitiveOp`'s metadata form ("or the metadata of a sequence", `HirVocabulary.cs:49`) |
| 48 | `Operators.cs:41` | `case BinaryKoto` | D | a binary operator token under `PrimitiveOp` |
| 49 | `Operators.cs:44` | `case UnaryKoto { ... }` | D | unary `+` under `PrimitiveOp` |
| 50 | `Operators.cs:44` | `KotoKind.PrefixPlus` | D | as 49 |
| 51 | `Operators.cs:47` | `case UnaryKoto { ... }` | D | unary `-` and `not` under `PrimitiveOp` |
| 52 | `Operators.cs:47` | `KotoKind.PrefixMinus` | D | as 51 |
| 53 | `Operators.cs:47` | `KotoKind.Not` | D | as 51 |
| 54 | `Operators.cs:48` | `KotoKind.PrefixMinus` | D | `Neg` against `Not`, as 51 |
| 55 | `Operators.cs:132` | `KotoKind.And` | D | `and` against `or` under `HirOp.Logical` |
| 56 | `Places.cs:12` | arm `LabeledKoto` | C | `Child`: the one operand of the forms `KindOp:112-116` publishes as `Group`, `Borrow`, `Deref` and `Convert`; its callers read it only under those ops or Place kinds |
| 57 | `Places.cs:12` | arm `BinaryKoto` | C | as 56 (`ConversionKoto`, `MemberAccessKoto`, `IndexKoto` are `BinaryKoto`s) |
| 58 | `Places.cs:12` | `(UnaryKoto)` | C | as 56 (`ParenthesizedKoto`, `DereferenceKoto`) |
| 59 | `Places.cs:42` | `is MemberAccessKoto` | D | form under `HirPlaceKind.Field`: a written receiver, against an accessor's storage of its implicit self (`HirVocabulary.cs:187`) |
| 60 | `Places.cs:45` | `(IndexKoto)` | C | `HirPlaceKind.Index` |
| 61 | `Places.cs:46` | `(IndexKoto)` | C | `HirPlaceKind.ConstIndex` |
| 62 | `Places.cs:132` | `(FunctionKoto)` | C | body kind `Closure`, as 15 |
| 63 | `Selections.cs:68` | `is FromEndIndexKoto` | **V** | V2 (§5): the type chooses what to evaluate without the published op |
| 64 | `Selections.cs:146` | `as RangeKoto` | D | form under `HirOp.Range`, checked in the same expression: `a..b` against `^x` |
| 65 | `Selections.cs:231` | `is not FromEndIndexKoto` | D | `^x` under `HirOp.Range`, checked in the same condition |
| 66 | `Values.cs:58` | `(CodeBlockKoto)` | C | `Into`'s `case HirOp.Block`; `KindOp` publishes each op of rows 66–84 on exactly that form |
| 67 | `Values.cs:64` | `(IfKoto)` | C | `HirOp.If` |
| 68 | `Values.cs:67` | `(MatchKoto)` | C | `HirOp.Match` or `Try` (`TryKoto : MatchKoto`) |
| 69 | `Values.cs:70` | `(IsKoto)` | C | `HirOp.RuntimeTypeTest` |
| 70 | `Values.cs:73` | `(LoopKoto)` | C | `HirOp.Loop` |
| 71 | `Values.cs:76` | `(ForKoto)` | C | `HirOp.For` |
| 72 | `Values.cs:79` | `(WhileKoto)` | C | `HirOp.While` |
| 73 | `Values.cs:82` | `(DoKoto)` | C | `HirOp.Do` |
| 74 | `Values.cs:85` | `(RequireKoto)` | C | `HirOp.Require` |
| 75 | `Values.cs:91` | `(DiscardKoto)` | C | `HirOp.Discard` |
| 76 | `Values.cs:94` | `(JumpKoto)` | C | `HirOp.Jump` or `Continue` |
| 77 | `Values.cs:97` | `(TestVerificationKoto)` | C | `HirOp.TestVerification` |
| 78 | `Values.cs:100` | `(FunctionKoto)` | C | `HirOp.Closure` |
| 79 | `Values.cs:103` | `(UnsafeBlockKoto)` | C | `HirOp.Unsafe`, its `Body` |
| 80 | `Values.cs:112` | `(BinaryKoto)` | C | `HirOp.Logical` |
| 81 | `Values.cs:118` | `(BinaryKoto)` | C | `HirOp.Convert` (`ConversionKoto : BinaryKoto`) |
| 82 | `Values.cs:127` | `(BinaryKoto)` | C | `HirOp.Assign` |
| 83 | `Values.cs:133` | `(IndexKoto)` | C | `HirOp.Index` with a Value Place |
| 84 | `Values.cs:141` | `(DictionaryLiteralKoto)` | C | `HirOp.DictionaryLiteral` |
| 85 | `Values.cs:209` | arm `TupleLiteralKoto` | C | the elements, under `HirOp.Aggregate` |
| 86 | `Values.cs:210` | arm `ArrayLiteralKoto ... when FixedArray` | C | the elements; the Type kind decides (R5-M79) |
| 87 | `Values.cs:211` | arm `ArrayLiteralKoto { FillLength: null } ... when Array` | D | list against fill form under `Aggregate`; an Array fill stays a located `Unsupported_Kd` |
| 88 | `Values.cs:250` | `is ArrayLiteralKoto { FillLength: not null }` | D | the fill form under `Aggregate`: `MirAggregateKind.Fill` (SPEC 4.3.3) |
| 89 | `Values.cs:345` | arm `BoolLiteralKoto` | D | a literal's value under `HirOp.Literal` (R5-M79: syntax data; G122) |
| 90 | `Values.cs:346` | arm `CharLiteralKoto { Value: ... }` | D | as 89 |
| 91 | `Values.cs:347` | arm `StringLiteralKoto` | D | as 89 |
| 92 | `Values.cs:348` | arm `UnitLiteralKoto` | D | as 89 |
| 93 | `Values.cs:349` | arm `NullLiteralKoto` | D | as 89 |
| 94 | `Verifications.cs:90` | `is not BinaryKoto` | D | a comparison operator under `PrimitiveOp`, checked in the same condition |

## 4. Totals

| Family | D | C | V | Total |
| --- | ---: | ---: | ---: | ---: |
| `KotoKind.` references | 27 | 0 | 0 | 27 |
| Type tests (`is`/`as` 13, `case` 4) | 12 | 3 | 2 | 17 |
| Casts | 0 | 37 | 0 | 37 |
| Switch-expression arms | 6 | 7 | 0 | 13 |
| **Total** | **45** | **47** | **2** | **94** |

Outside the four greps, read for completeness and not counted: `Expansions.cs:205` (`KotoHelper.CompoundOperation(node.Akind)`, a token
under `CompoundAssign`: D), `cs:151` (`AccessorKind`, a getter or setter under symbol kind `PropertyAccessor`: D), the `(Koto?)` upcasts
(`cs:80, 146`), `KotoHelper.UnwrapParentheses` (`Expansions.cs:140`, `Values.cs:303`: the Property Binding designates through
parentheses) and the parameters typed `XKoto` (fixed by their callers' casts above).

## 5. Conclusion: not confirmed; two violations, proposed as fix units

92 of 94 matches follow ARCH's rules. Two do not; both were added after Q16 (R5-M79), by G241 (2026-10-25) and G168b (2026-11-01),
so the review obligation of ARCH's "MIR dispatch" row did not catch them. H0c §8's exit-gate row 6 ("ARCH:135/:149 hold or amended")
stays open until both land. Each is a Minor Mir/Build unit on the MIR side (R4, H0's division), reading facts that exist; no new fact,
vocabulary entry or HIR contract change.

- **V1, `Blocks.cs:24`** — `case HirOp.Error when item is VariableKoto variable && HirTables.Symbol(variable)?.Type is not null:
  this.Let(variable)`. R4-M125 makes a rejected local declaration declare its local, and rejected publishing it as `Let` (a second `Let`
  meaning); the builder then recovers the operation from the syntax class under `Error`. The fact that should decide exists: the node
  declares a local Binding typed, `HirTables.Symbol(item) is { Kind: BindingSymbolKind.Local, Type: not null } s &&
  ReferenceEquals(s.Declaration, item)` (Binding's symbol table, owner Binding, published through `HirTables`' Symbol column; a local's
  `Declaration` is its `FieldKoto`, `Binding.Expressions.cs:52`); the `Let` call keeps a child cast. The symbol test alone is not
  enough: an `Error` name names a typed local too. Expected behavior-preserving; the unit confirms the duplicate-binding form of R4-M125
  (`var h: i32` twice) keeps its local, that is, that each declaration owns its symbol. Checks: `MirBuildTest`, `MirBorrowsTest` (G241's
  pairs), snapshot equal.
- **V2, `Selections.cs:68`** — `Evaluate` (a selection part under an identity premise, G168b) builds a `^x`'s offset instead of the node
  by `node is FromEndIndexKoto`, with no published op. `KindOp:139` publishes `^x` as `HirOp.Range` (applied directly) or `HirOp.Call`
  (a constructed `FromEnd` value, `Binding.RangeValueCall`); only the first is not evaluable as a node. Fact: `HirTables.Op(node) ==
  HirOp.Range` (owner Binding's HIR publication), the guard `Boundary` (`Selections.cs:231`) already reads. Whether a `^x` published
  `Call` reaches `Evaluate` (a built-in selection's key not applied directly, on an identity premise) was not probed; if it does, the
  fix builds that construction call's code and records, a behavior change with a regression test (`MirBuildTest`), else it is
  behavior-preserving.

## Review (2026-11-03): ACCEPT, with notes folded in here

The independent review of `70d1872d9` reproduced the counts (94) and found no missed construct (`or`/`and`/`not` patterns, nested
property patterns, generic arguments, `typeof`, `nameof`, `GetType`, `.Parent`: none in Mir/Build). Its notes, which amend the text
above:
- V2 is unreachable through a `Call`-published `^x` by reading: the element path applies every written `^x` directly
  (`TryBindKeyedSelection` completes it with its FromEnd Type and no call, `Binding.Ranges.cs` about 185-196); a direct range's bounds
  are completed only once the range is known to be direct (`DirectBoundary`); a constructed range reaches `Evaluate` as its
  `RangeKoto`; a parenthesized `^x` is no `FromEndIndexKoto` at the test. So V2's fix is expected to be behavior-preserving.
- V1's stop condition: a local's `Declaration` is its `FieldKoto` (`Binding.Expressions.cs:52`). If a duplicate declaration (`var h`
  twice) does not own its symbol, the published fact does not decide that node, and the fix needs a Binding publication (not Minor).
- Two helper calls count as references: `KotoHelper.UnwrapParentheses` (`Expansions.cs:140`, `Values.cs:303`), a `ParenthesizedKoto`
  test by proxy, and `CompoundOperation`, a token read. Both are syntax data (D).
- Rows 7-9 are syntax data (D), not child casts (C): R5-M79 names `Published`'s value-child switch as syntax data.
Totals after the notes, over 96 references (94 plus the two helper calls): 50 syntax data (45, +3 from rows 7-9, +2 helpers), 44 child casts, 2 violations. The fixes are R5's Minor units on
`claude/r5-h13fix` (R4's consent, 2026-11-03).
