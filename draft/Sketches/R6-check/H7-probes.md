# H7 step-0 probes (dev vs D2's patch)

- Base: dev `4862b9fc7` (the design read `50c6e1f50`; both gate sites are unchanged, same lines). Patch: D2 (a)+(b) exactly, on the local
  branch `r5-h7-probe` @ `b2dc74c12` in `bin/worktrees/r5-h7` (not pushed); diff `h7-probe.patch` (-16/+3 lines).
- CLIs: `cli-dev\` (compiler id d8c16833) and `cli-h7\` (compiler id 1e558473), `dotnet <cli>\Kimi.dll check probes\<name>\P.kimiproj`,
  text and `--Format json`. Raw output: `out\dev\<name>.{txt,json}` and `out\h7\<name>.{txt,json}`. Project: x86_64-pc-windows-msvc, Application, O0.
- Records are `code line:col span`. Messages, the same in both CLIs: Unsupported_Kd "This form is outside the implemented subset";
  TypeMismatch_Kd "Values must be of the same type" (label "expected A, found B"); NonNumericOperand_Kd "The operator + requires numeric
  operands, but this operand has Type string" (label "string has no +", plus the string Note); UnresolvedBinding_Kd "Required Name or Type
  information remains unresolved after final Binding"; NoApplicableOverload_Kd "No unique applicable overload can be established from the
  supplied arguments" (label "none of 1 candidates applies", candidate `Main.kimi:1:6: f`).
- Class (§3's rule): **(i)** acceptance move (an accepted program that dev rejects); **(ii)** lost independent record (a lost Unsupported_Kd at
  a 128-bit operation that has no failing record of its own); **gain** = new records on a program dev already rejects.

| Probe | Source | dev records | H7 records | Class |
| --- | --- | --- | --- | --- |
| P0 (control) | `let x: i128 = 10`<br>`let y = x / 3` | Unsupported_Kd 2:9 `x / 3` | Unsupported_Kd 2:9 `x / 3` (now A3b's) | same |
| P0f (control) | `let x: i128 = 10`<br>`let y = x@f64` | Unsupported_Kd 2:9 `x@f64` | Unsupported_Kd 2:9 `x@f64` (now A3b's) | same |
| P1 | `let x: i128 = 10`<br>`let y = x / 3 + "s"` | Unsupported_Kd 2:9 `x / 3`; NonNumericOperand_Kd 2:9 `x / 3 + "s"` | TypeMismatch_Kd 2:17 `"s"` (expected i128, found string) | **(ii)**: `x / 3`'s record lost. Also the parent's NonNumericOperand_Kd is gone and a TypeMismatch_Kd at `"s"` appears instead (see Surprises 2) |
| P1f | `let x: i128 = 10`<br>`let y = x@f64 + "s"` | Unsupported_Kd 2:9 `x@f64`; NonNumericOperand_Kd 2:9 `x@f64 + "s"` | TypeMismatch_Kd 2:17 `"s"` (expected f64, found string) | **(ii)**, as P1 |
| P2 | `let x: i128 = 10`<br>`missing(x / 3)` | UnresolvedBinding_Kd 2:1 `missing`; Unsupported_Kd 2:9 `x / 3` | UnresolvedBinding_Kd 2:1 `missing` | **(ii)** |
| P2f | `let x: i128 = 10`<br>`missing(x@f64)` | UnresolvedBinding_Kd 2:1 `missing`; Unsupported_Kd 2:9 `x@f64` | UnresolvedBinding_Kd 2:1 `missing` | **(ii)** |
| P3 | `func f(a: i64) => ()`<br>`let x: i128 = 10`<br>`f(x / 3)` | Unsupported_Kd 3:3 `x / 3` | NoApplicableOverload_Kd 3:1 `f(x / 3)` | **(ii)** plus a gain (the call's record; dev's call check stayed silent on the failed operand) |
| P4 | `let x: i128 = 10`<br>`let y: i64 = x / 3` | Unsupported_Kd 2:14 `x / 3` | TypeMismatch_Kd 2:14 `x / 3` (expected i64, found i128) | **(ii)**, not gain-only as expected: A3b reports nothing. The TypeMismatch_Kd shares the span but is the `let`'s record (`FailMismatch(variable, variable.InitializerKoto, ...)`, `Binding.Variables.cs:97`), so the operation has no record of its own (see Surprises 1) |
| P5 | `let x: i128 = 10`<br>`let y = (x / 3)@f64` | Unsupported_Kd 2:10 `x / 3` | Unsupported_Kd 2:9 `(x / 3)@f64`; Unsupported_Kd 2:10 `x / 3` | gain only (the conversion's record) |
| P6 | `open struct Base`<br>`    protected init(a: i32 = 1) => ()`<br>`    protected init(b: string = "x") => ()`<br>`struct Leaf: Base`<br>`    public var n: i128 = 7@i128 / 2`<br>`public func main() => ()` | Unsupported_Kd 5:26 `7@i128 / 2` | none; **accepted** (exit 0, `"accepted":true`) | **(i)** |
| P6f | as P6, with `    public var n: f64 = (7@i128 + 1)@f64` | Unsupported_Kd 5:25 `(7@i128 + 1)@f64` | none; **accepted** | **(i)** (the float-conversion counterpart) |
| P7 | `let x: i128 = 10`<br>`let y = if true => x / 3 else => "s"` | TypeMismatch_Kd 2:9 `if true => x / 3 else => "s"`; Unsupported_Kd 2:20 `x / 3` | TypeMismatch_Kd 2:9 `if true => x / 3 else => "s"` | **(ii)** |
| P8 | `let x: i128 = 10`<br>`let y = x / 3`<br>`let z: i64 = y` | Unsupported_Kd 2:9 `x / 3` | Unsupported_Kd 2:9 `x / 3`; TypeMismatch_Kd 3:14 `y` (expected i64, found i128) | gain only |

## Conclusion

- **D4 (i) confirmed:** P6 is accepted under H7, and so is its float-conversion counterpart P6f. A Field initializer of a constructorless
  layer has no MIR body, so neither A3b nor Binding reports it.
- **D4 (ii) confirmed, and wider than the design expected:** P1, P2, P3 and P7 lose the 128-bit operation's Unsupported_Kd as expected, and
  so do the float counterparts P1f and P2f. **P4 also loses it.** The design expected P4 to be gain-only (TypeMismatch_Kd beside A3b's
  record); it reports only the TypeMismatch_Kd.
- As expected: P5 and P8 are gain-only. The controls P0 and P0f keep the same code, location and text (now A3b's record).

## Surprises

1. **P4 (the annotated `let`):** the mismatch fails the variable node itself. (H) HIR then publishes the `let` as an Error that replaces its
   initializer (`Binding.Hir.Operations.cs`, `PositionOf`/`Keeps`), so the operation is never built. This is the "failed statement" class
   of D4 (ii), not D4 (iii). §2's new test row (`let y: i64 = x / 3` -> TypeMismatch_Kd and Unsupported_Kd) is refuted: H7 gives only
   TypeMismatch_Kd.
2. **P1/P1f, the parent's own record changes:** dev reports NonNumericOperand_Kd at the whole binary (2:9, `x / 3 + "s"`). H7 reports
   TypeMismatch_Kd at `"s"` (2:17, expected i128/f64). The left operand now completes, so the operator check takes the fit path. Read
   literally, Q13 rule 3 counts dev's NonNumericOperand_Kd as a second lost record: rule W does not apply, because the input it read
   no longer fails. Read otherwise, it is the same failure reported under another code. R5 decides which.
3. **P3:** besides the loss, the call gains NoApplicableOverload_Kd. Dev stayed silent because the argument had failed; this is a dependent
   gain.
