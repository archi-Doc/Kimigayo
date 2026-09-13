# Match guards

This example prints `negative`, `zero` and `positive`, each followed by LF, and exits 0.

```powershell
dotnet run --project Kimi -c Release --no-build -- build examples/Guards/Guards.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release --no-build -- run examples/Guards/Guards.kimiproj
```

The Subject is acquired once. A successful Pattern evaluates its guard once; a mismatch skips it. The guard secures its bool, cleans its temporaries, then selects the body or continues to the next arm. Independent effects of a false guard remain. Abort, divergence or an outward transfer prevents that path from selecting a body or continuing to a later arm.

In a guard, `n` is a read-only candidate for the Subject. The body's `n` is a separate local initialized only after successful guard cleanup. Even `var` candidates cannot be reassigned in the guard; `var` body locals may be reassigned. Candidate reads use the acquired Subject snapshot without extra scalar storage.

C.53 supports guards over bool, the ten integer Types through 64 bits and Unit. String Subjects remain supported only without guards. References, decomposition and general candidate borrows remain pending. Guarded arms never establish exhaustiveness, including `if true`, so retain an unguarded covering arm. Covered arms still receive static checks.

See [STATUS C.53](../../STATUS.md#c53-copy-subject-match-guards-2026-09-13) and [backend setup](../../backend/windows-x64/README.md).
