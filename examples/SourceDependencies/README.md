# Project source dependencies

Application references Geometry, which directly references the Math Library under the name `Math`. Geometry's source alias opens `Math.Arithmetic`; the selected `twice` function retains its defining Math module. Application can use Geometry's public API without gaining direct visibility of Math.

From the repository root:

```powershell
dotnet run --project Kimi -- restore examples/SourceDependencies/Geometry/Geometry.kimiproj
dotnet run --project Kimi -- check examples/SourceDependencies/Geometry/Geometry.kimiproj --locked
dotnet run --project Kimi -- emit examples/SourceDependencies/Geometry/Geometry.kimiproj --locked
```

Restore writes `Geometry.kimi.lock.json`. Check validates that lock and all selected source bodies, including unused dependency functions, without LLVM or program execution. Editing Math source requires another check but no lock update. Changing an identity, reference name or dependency edge requires restore.

`emit` writes Library inspection IR and its matched manifest, with internal language functions and no OS entry. It requires no LLVM installation. A Library has no runnable executable or public native ABI.

To execute the transitive source call with the configured Windows x64 toolchain:

```powershell
dotnet run --project Kimi -- restore examples/SourceDependencies/Application/Application.kimiproj
dotnet run --project Kimi -- build examples/SourceDependencies/Application/Application.kimiproj --locked
dotnet run --project Kimi -- run examples/SourceDependencies/Application/Application.kimiproj --locked
```

The program verifies `doubled(21) == 42` and prints `source modules`. Build checks all dependency bodies and generates one final Application. Run uses that existing executable; rebuild after source changes. Source packages, full module/native supply records and dynamic static initialization remain unfinished.
