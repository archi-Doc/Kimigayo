# Strings

Owned string locals with Move, replacement, conditional cleanup, self-assignment and deferred output. Prints `next`, `done`, then `next`, each followed by LF.

```powershell
dotnet run --project Kimi -c Release --no-build -- build examples/Strings/Strings.kimiproj --LlvmBin C:/App/llvm
dotnet run --project Kimi -c Release --no-build -- run examples/Strings/Strings.kimiproj
```

Requires the pinned tools and Windows backend archive in [backend setup](../../backend/windows-x64/README.md). `writeLine(text)` acquires the string by Move. Reusing that binding requires reinitialization; assignment destroys any old value still owned before placing its replacement. Moving a Static string still transfers responsibility even though its backing bytes need no free.

This increment constructs strings only from literals. String-valued control-flow results, user-function string parameters/results, concatenation, interpolation and explicit ownership adaptations remain unsupported. See [STATUS C.48](../../STATUS.md#c48-owned-string-locals-and-conditional-cleanup-2026-09-13).
