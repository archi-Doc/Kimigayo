// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>The native allocation audit fails closed: every spelling of an allocation and every mentioned transfer routine
/// is counted, and a reference the interposer cannot redirect, an unknown declaration, a routine without its anchor or an
/// anchor without its routine, a bound with nothing to count or a count outside its bound fails instead of escaping the
/// counters.</summary>
public class NativeAllocationAuditTest
{
    // An exit that bypasses __kimi_exit, as a generator calling the import directly would write it: no import shares ExitProcess's
    // declaration (SPEC 21.2), so no definition but __kimi_exit's may mention it (the string lifetime audit's premise too).
    internal const string DirectExit = "define internal void @audit_direct_exit() {\nentry:\n  call void @ExitProcess(i32 0)\n  unreachable\n}\n";

    // One growth from empty: one 16-byte allocation and no copy.
    private const string Append = "var values: Array<i32> = []\nvalues@uniq.append(1)";

    // Two growths (16 and 32 bytes); the second copies four elements (16 bytes).
    private const string Growth = "var values: Array<i32> = []\nvar i = 0\nwhile i < 5\n    values@uniq.append(i)\n    i += 1";

    // Two Dictionary storages (96 and 192 bytes); the growth transfers the four occupied slots (96 bytes).
    private const string Entries = "var entries: Dictionary<i32, i32> = [:]\nvar i = 0\nwhile i < 5\n    _ = entries.tryInsert(i, i)\n    i += 1";

    // Pages mapped outside the platform layer, as a program's import that shares the runtime's VirtualAlloc declaration calls it
    // (ForeignEmissionTest.Pages): around the allocator the audit counts.
    private const string ImportedPages =
        "define internal ptr @audit_imported_pages() {\nentry:\n  %data = call ptr @VirtualAlloc(ptr null, i64 4096, i32 12288, i32 4)\n  ret ptr %data\n}\n";

    // A program mapping its own pages through its import of VirtualAlloc, with the runtime's signature.
    private const string PagesProgram = "group Native\n    #LibraryImport(\"kernel32\", \"VirtualAlloc\")\n" +
        "    public unsafe func allocate(address: raw/u8, size: u64, kind: u32, protect: u32) -> raw/u8\n" +
        "public func main()\n    var p: raw/u8 = null\n    unsafe => p = Native.allocate(null, 4096, 12288, 4)\n    require p != null else => $abort(\"allocate\")";

    [Theory]
    [InlineData("Control", "call ptr @__kimi_heap_allocate(")]
    [InlineData("TailNoalias", "tail call noalias ptr @__kimi_heap_allocate(")]
    public void EveryAllocationSpellingIsCounted(string name, string spelling)
        => Audit(name, Append, 1, 16, transformIr: ir =>
        {
            Assert.Contains("call ptr @__kimi_heap_allocate(", ir, StringComparison.Ordinal); // Every runtime allocation site, respelled.
            return ir.Replace("call ptr @__kimi_heap_allocate(", spelling, StringComparison.Ordinal);
        });

    // A count below or above its bound fails the run.
    [Theory]
    [InlineData("BelowBound", 2L, 3L)]
    [InlineData("AboveBound", 0L, 0L)]
    public void CountOutsideItsBoundFails(string name, long minimum, long maximum)
        => NativeAllocationAudit.WriteFixture("NativeAllocationAudit" + name, Append, new(minimum, maximum), 1, 16, exit: 93, stderr: string.Empty);

    [Theory]
    [InlineData("__kimi_heap_allocate")]
    [InlineData("__kimi_heap_release")]
    [InlineData("__kimi_exit")]
    public void UnredirectedReferenceFails(string symbol)
    {
        var stray = "@audit_stray = private constant ptr @" + symbol + "\n";
        Assert.Throws<DoesNotContainException>(() => Audit("Stray", Append, 1, 16, transformIr: ir => ir + stray));
    }

    // IMPL 21.4.2, 21.4.7 (the Tatara plan's B7r1, B7r2): a kernel32 call in a runtime routine other than the layer routine that owns it
    // fails the audit before any run: the common part mapping pages itself, as the runtime called the OS before the layer (B7r1); the
    // allocator mapping pages itself, beside the layer (B7r2); and C-468's N2, a page release in the exit or a page mapping in the write.
    [Theory]
    [InlineData("__kimi_alloc", "call ptr @VirtualAlloc(ptr null, i64 4096, i32 12288, i32 4)")]
    [InlineData("__kimi_heap_allocate", "call ptr @VirtualAlloc(ptr null, i64 4096, i32 12288, i32 4)")]
    [InlineData("__kimi_exit", "call i32 @VirtualFree(ptr null, i64 0, i32 32768)")]
    [InlineData("__kimi_write", "call ptr @VirtualAlloc(ptr null, i64 4096, i32 12288, i32 4)")]
    public void AnOsCallOutsideItsLayerRoutineFails(string routine, string call)
    {
        var entry = new Regex(@"(?m)^define [^\n]*@" + Regex.Escape(routine) + @"\([^\n]*\nentry:\n");
        Assert.Throws<EmptyException>(() => Audit("OsCall", Append, 1, 16, transformIr: ir =>
        {
            Assert.Single(entry.Matches(ir));
            return entry.Replace(ir, "$0  %stray = " + call + "\n");
        }));
    }

    // Design-b7b §3.3 (C-478 B1): outside the layer routine that owns it no definition mentions a runtime kernel32 function, a program's
    // own import included, so no page escapes the counters and no exit the hook: a program importing VirtualAlloc and calling it, the
    // same call written into a program and an exit around __kimi_exit fail, each for its call (B7r1's process-heap block is a declaration
    // outside the whitelist since B7r3, UnknownDeclarationFails).
    [Theory]
    [InlineData(PagesProgram, "", " mentions VirtualAlloc")]
    [InlineData(Append, ImportedPages, "audit_imported_pages mentions VirtualAlloc")]
    [InlineData(Append, DirectExit, "audit_direct_exit mentions ExitProcess")]
    public void AProgramsOsCallFails(string source, string definition, string offender)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source)) + definition;
        var failure = Assert.Throws<EmptyException>(() => NativeAllocationAudit.AssertOsCallsInLayer(ir));
        Assert.Contains(offender, failure.Message, StringComparison.Ordinal);
    }

    // IMPL 21.4.2, 21.4.7 (the Tatara plan's B7r1): Hello's module and a Dictionary program's, which also defines the nullable Alloc, call
    // kernel32 only inside the platform layer's routines and exit only through __kimi_exit.
    [Theory]
    [InlineData("Console.writeLine(\"Hello, world!\")", false)]
    [InlineData(Entries, true)]
    public void TheOsCallsLieInThePlatformLayer(string source, bool nullable)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source));
        Assert.Equal(nullable, ir.Contains("define internal ptr @__kimi_try_allocate(", StringComparison.Ordinal));
        NativeAllocationAudit.AssertOsCallsInLayer(ir);
    }

    // A declaration outside the whitelist fails before any run: another allocator's, a page query, and the process heap's allocator,
    // which left kernel32's set with the runtime's declarations (the Tatara plan's decision 23, B7r3).
    [Theory]
    [InlineData("declare dllimport ptr @HeapAlloc(ptr, i32, i64)")]
    [InlineData("declare dllimport ptr @HeapReAlloc(ptr, i32, ptr, i64)")]
    [InlineData("declare dllimport ptr @VirtualQuery(ptr, ptr, i64)")]
    [InlineData("declare dllimport ptr @RtlAllocateHeap(ptr, i32, i64)")]
    [InlineData("declare extern_weak dllimport ptr @HeapReAlloc(ptr, i32, ptr, i64)")]
    [InlineData("declare ptr @HeapReAlloc(ptr, i32, ptr, i64)")]
    public void UnknownDeclarationFails(string declaration)
        => Assert.Throws<ContainsException>(() => Audit("Declaration", Append, 1, 16, transformIr: ir => ir + declaration + "\n"));

    [Fact]
    public void ArrayGrowthUnderABoundIsCounted()
        => Audit("Growth", Growth, 2, 48, transferred: 16);

    [Fact]
    public void RespelledTransferRoutineIsCounted()
        => Audit("Transfer", Entries, 2, 288, transferred: 96, transformIr: Respell);

    [Fact]
    public void TransferRoutineWithoutItsAnchorFails()
        => Assert.Throws<EqualException>(() => Audit("Unanchored", Entries, 2, 288, transferred: 96, transformIr: Unanchor));

    // A renamed transfer routine whose body keeps its anchor would leave its copies uncounted: the one routine is gone, so the bound's first
    // check fails it, as it fails a bound with no routine (BoundWithoutTransferRoutineFails).
    [Fact]
    public void AnchorWithoutItsRoutineFails()
    {
        var failure = Assert.Throws<TrueException>(() => Audit("Renamed", Entries, 2, 288, transferred: 96, transformIr: Rename));
        Assert.Contains("transfer bound", failure.Message, StringComparison.Ordinal);
    }

    // DECISIONS R5-M145: the byte copy written inline, as MIR code generation wrote it before both generators called one routine, leaves a
    // transferring program's copies uncounted, so even a bound whose minimum is 0 fails instead of passing vacuously, also when the routine
    // stays defined beside the inline copies (a module that defines the storage byte primitives as a pair). Without the definition no routine
    // is left to count, so the bound's first check fails it; with it, the check for the module's calls does.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnInlineByteCopyFails(bool defined)
    {
        var failure = Assert.Throws<TrueException>(() => Audit("Inline", Entries, 2, 288, transferred: new(0, 96), transformIr: defined ? InlineCalls : Inline));
        Assert.Contains(defined ? "byte copy" : "transfer bound", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundWithoutTransferRoutineFails()
    {
        var failure = Assert.Throws<TrueException>(() => Audit("Uncounted", "let n = 1", 0, 0, transferred: 0));
        Assert.Contains("transfer bound", failure.Message, StringComparison.Ordinal);
    }

    // Audits source as fixture NativeAllocationAudit<name> with as many releases as allocations.
    private static void Audit(
        string name,
        string source,
        int allocations,
        long bytes,
        NativeAllocationAudit.Bound? transferred = null,
        Func<string, string>? transformIr = null)
        => NativeAllocationAudit.WriteFixture(
            "NativeAllocationAudit" + name,
            source,
            allocations,
            allocations,
            bytes,
            transferred: transferred,
            transformIr: transformIr);

    // Another spelling of the transfer routine's definition, as a different generator might write it. A missing definition fails with a
    // ContainsException, not the EqualException TransferRoutineWithoutItsAnchorFails expects through Unanchor.
    private static string Respell(string ir)
    {
        const string Definition = "define internal void @__kimi_transfer_bytes(";
        Assert.Contains(Definition, ir, StringComparison.Ordinal);
        return ir.Replace(Definition, "define internal void @\"__kimi_transfer_bytes\"(", StringComparison.Ordinal);
    }

    // The respelled routine without its anchor: the copy is a memmove, as lowering MoveBytes by its contract would write it.
    private static string Unanchor(string ir)
        => Respell(ir).Replace("@llvm.memcpy.p0.p0.i64(ptr %destination", "@llvm.memmove.p0.p0.i64(ptr %destination", StringComparison.Ordinal);

    // The byte copy's calls written as the intrinsic they call, its definition removed.
    private static string Inline(string ir)
    {
        var definition = Regex.Match(ir, @"(?ms)^define internal void @__kimi_transfer_bytes\(.*?^\}\n").Value;
        Assert.NotEmpty(definition);
        return InlineCalls(ir.Replace(definition, string.Empty, StringComparison.Ordinal));
    }

    // The byte copy's calls written as the intrinsic they call, its definition kept.
    private static string InlineCalls(string ir)
    {
        var inlined = Regex.Replace(ir, @"call void @__kimi_transfer_bytes\(([^)]*)\)", "call void @llvm.memcpy.p0.p0.i64($1, i1 false)");
        Assert.NotEqual(ir, inlined);
        return inlined;
    }

    // The transfer routine under another name with its body unchanged.
    private static string Rename(string ir)
        => ir.Replace("__kimi_transfer_bytes", "__kimi_byte_transfer", StringComparison.Ordinal);
}
