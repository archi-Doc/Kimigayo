# Match execution

This example uses exhaustive matches on owned strings and integers. It prints `accepted`, `other` and `matched`, each followed by LF, and exits 0.

```powershell
dotnet run --project Kimi -c Release --no-build -- build examples/Matches/Matches.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release --no-build -- run examples/Matches/Matches.kimiproj
```

The Subject is acquired once before testing arms in source order. Matching an owned string moves it into the match, even for a literal or `_` pattern. A selected `let`/`var` binding then acquires the Subject; a literal or `_` leaves its destruction responsibility with the match. Pattern string constants are compared without constructing owned values. Results are secured before arm cleanup and remaining Subject destruction, and delivered only if cleanup completes.

C.52 supports bool, the ten integer Types through 64 bits, Unit and owned string Subjects; literal, wildcard, whole-Subject binding and grouped patterns; and existing scalar/string results, functions, loops and defer. Every match must be exhaustive. C.53 adds [guards](../Guards/README.md) for bool/integer/Unit Subjects. Guarded string matches, enum/Tuple decomposition, borrowed Subjects, char and floating-point execution remain unsupported. A covered arm still receives diagnostics. See [STATUS C.52](../../STATUS.md#c52-unguarded-whole-subject-match-execution-2026-09-13) and [backend setup](../../backend/windows-x64/README.md).
