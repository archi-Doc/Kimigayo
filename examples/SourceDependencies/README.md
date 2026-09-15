# Project source dependencies

Geometry directly references the Math Library under the name `Math`. Its source alias opens `Math.Arithmetic`; the selected `twice` function retains its defining Math module.

From the repository root:

```powershell
dotnet run --project Kimi -- restore examples/SourceDependencies/Geometry/Geometry.kimiproj
dotnet run --project Kimi -- check examples/SourceDependencies/Geometry/Geometry.kimiproj --locked
```

Restore writes `Geometry.kimi.lock.json`. Check validates that lock and all selected source bodies, including unused dependency functions, without LLVM or program execution. Editing Math source requires another check but no lock update. Changing an identity, reference name or dependency edge requires restore.

This example verifies source semantics. Library and cross-module native generation remain unfinished.
