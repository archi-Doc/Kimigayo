// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The shared acceptance tests of PLAN G342 (WORKFLOW Q11; written from the problem statement before either candidate): A5's
/// Loan-stratum Work on writes that store through a raw layer whose state carries Φ_E (SPEC 15.6.4's Writer, its q addressing a w
/// derived from a parameter). Such a write may store y's Loan in storage the body does not own, where a value "may be used after every
/// point" (SPEC 15.6 Holders), so every Loan_j stays live from w_j on and A5's output, the active Loans per location, is Θ(N²)
/// (DECISIONS R4-M57's Bound). On dev, W's targets past E each receive a copy of the replicated chain, and the Work is Θ(N³). The
/// scaling rows fail on dev by design until G342; the verdict pins, the control and the warm row hold on dev and must hold after (the
/// fix is a representation change, so a record that moves is a defect).</summary>
public sealed partial class MirBorrowsTest
{
    // The forms' callees and Types: WriterF holds SPEC 15.6.4's Writer Fields without a drop, and its flush stores q through p as
    // Writer's drop does, through a `uniq/Self` receiver; Lf is PLAN G298 (3)'s leaf (WK's review 2, R2-N2).
    private const string UnownedShapes = """
        func usep<T>(p: raw/T) => ()
        struct WriterF
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
            public func flush(self: uniq/Self) => unsafe => *self.p = self.q
        struct Lf {a}
            public var value: i32 = 0
            public var peer: Option<ref/i32 during a>
            public init(p: ref/i32 during a) => self.peer = .Some(p)

        """;

    // V16 (DECISIONS R4-M57's Bound, R4-M65's precedent; PLAN G342): N writes through a raw layer whose state carries Φ_E (N = 16, 32,
    // 64; each row takes under 2 s on dev). Each iteration borrows y's referent into w_i, makes a WriterF whose q addresses w_i and
    // stores q through p: by an explicit `uniq/Self` call (call), by one whose p stays null, so its store reaches only storage the body
    // does not own (callNoSlot), or inline in the body (inline). Every Loan_j stays live from w_j on (SPEC 15.6.4's example; 15.6
    // Holders), so the output is Θ(N²), and A5's Work and bytes may grow with the Loans live at each point, but no faster: the class's
    // precedent for values whose Loans are live together, 5 (R4's G273 decision K6, DECISIONS R4-M159); quadratic is 4, cubic 8.
    // writingPlaces is PLAN G298 (3)'s shape (WK's R2-N2 probe): one call whose W walk reaches N holders past one holder, each of which
    // may retain what a read through the argument carries, Θ(N) Loans each at one point, so Θ(N²) again. callLocalQ, the control, has q
    // address a fresh local v_i, so no chain grows past E. twoHop (review 1's N2) chains through two raw hops past E: q addresses hop_i,
    // which addresses w_i, and E holds both levels' addresses, so a fix that flattens one level leaves it cubic. Sensitivity (review 1's
    // N1): at these N the factor 5 passes a residual cubic term below about a/112 of the quadratic coefficient a; N = 32, 64, 128 would
    // halve that, at about 15 s per failing row on dev. Dev (27f685a30), work then bytes, and the work's growth factor: call 193,552,
    // 649,705, 3,786,394 and 4,800, 11,584, 37,440 (6.88); callNoSlot 178,011, 583,192, 3,444,807 and 4,660, 11,316, 36,916 (7.06);
    // inline 136,878, 312,958, 1,531,163 and 4,736, 11,456, 37,184 (6.92); twoHop 305,928, 1,477,345, 10,146,963 and 5,964, 15,948,
    // 54,348 (7.40); writingPlaces 119,402, 203,348, 719,785 and 11,152, 59,088, 402,768 (6.15; bytes 7.17); callLocalQ 124,783,
    // 188,559, 423,080 and 3,040, 4,992, 11,968 (3.68).
    [Theory]
    [InlineData("call")]
    [InlineData("callNoSlot")]
    [InlineData("inline")]
    [InlineData("twoHop")]
    [InlineData("writingPlaces")]
    [InlineData("callLocalQ")]
    public void AChainPastUnownedStorageCostsAsItsOutput(string form)
    {
        var (sizes, work, bytes) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            (work[i], bytes[i]) = this.Measured(UnownedShapes + UnownedForm(form, sizes[i]) + "public func main() => ()\n");
        }

        output.WriteLine($"{form} N {string.Join(", ", sizes)}: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, 5), $"bytes {string.Join(", ", bytes)}");
    }

    // The scaling rows' forms at N = 4, every record A5 gives on dev, located: each valid form, and the Bad twin of each form whose
    // stored value carries y's Loan, which writes y's referent before the slot's use (SPEC 15.6.4's `y@follow = 5`: the Loan w carries
    // stays live, since storage the body does not own may retain q). The twins that isolate what W's targets past E receive (review 1's
    // B1): callFirstBad's head iteration stores y0's Loan before the N call iterations, and `y0@follow = 5` errs only if that first
    // Loan survives them (dev: Anchor `y0@follow@ref`, Use `usep(slot)`); callFirstOk has no write. wpBad and wpLastBad write leaf after
    // the call, then read the first or the last holder: by SPEC 15.6.4, per argument, each holder can hold a borrow formed through `y`
    // and carries leaf's Loans, so the write errs (dev: Anchor `leaf@uniq`, Use the holder), and so does the holder's read (its own
    // `h@uniq` Loan, as G264's Setter rows read it); wpNoCall, the same lines without the call, is valid.
    [Theory]
    [InlineData("call", "")]
    [InlineData("callBad", "callBad: y@follow = 5")]
    [InlineData("callNoSlot", "")]
    [InlineData("callNoSlotBad", "callNoSlotBad: y@follow = 5")]
    [InlineData("inline", "")]
    [InlineData("inlineBad", "inlineBad: y@follow = 5")]
    [InlineData("twoHop", "")]
    [InlineData("twoHopBad", "twoHopBad: y@follow = 5")]
    [InlineData("callFirstOk", "")]
    [InlineData("callFirstBad", "callFirstBad: y0@follow = 5")]
    [InlineData("writingPlaces", "")]
    [InlineData("wpBad", "wpBad: leaf.value = 5; wpBad: h0")]
    [InlineData("wpLastBad", "wpLastBad: leaf.value = 5; wpLastBad: h3")]
    [InlineData("wpNoCall", "")]
    [InlineData("callLocalQ", "")]
    public void AChainPastUnownedStorageKeepsItsVerdicts(string form, string expected)
    {
        var c = MirBuildTest.Compile(UnownedShapes + UnownedForm(form, 4) + "public func main() => ()\n");
        Assert.Equal($"{form}: [{expected}]", $"{form}: [{string.Join("; ", Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]))}]");
    }

    // V16: A5 on a program version it checked before allocates nothing on the call form and its Bad twin (N = 8).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmChainPastUnownedStorageAllocatesNothing()
    {
        var c = MirBuildTest.Compile(UnownedShapes + UnownedForm("call", 8) + UnownedForm("callBad", 8) + "public func main() => ()\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(["callBad: y@follow = 5"], Problems(c, program, LoanCodes));
    }

    // A form's source at N, a function named by the form ({0} the index; review 1's spellings for wp*, callFirst* and twoHop*): the
    // Writer forms' slot, N iterations, then a Bad form's write and the slot's use; the holder forms' callee, holders and Tuple, then
    // the call and a Bad form's write and later use.
    private static string UnownedForm(string form, int n)
    {
        const string Made = "    let w{0} = y@follow@ref\n    var x{0} = WriterF.init(q: w{0}@raw@raw/u8)\n";
        const string Reset = "    x{0} = WriterF.init(q: null)\n";
        const string Call = "«N:" + Made + "    x{0}.p = slot@raw\n    x{0}.flush()\n" + Reset + "»";
        const string NoSlot = "«N:" + Made + "    x{0}.flush()\n" + Reset + "»";
        const string Inline = "«N:" + Made + "    x{0}.p = slot@raw\n    unsafe => *x{0}.p = x{0}.q\n" + Reset + "»";
        const string LocalQ = "«N:    var v{0}: u8 = 0\n    var x{0} = WriterF.init(q: v{0}@raw)\n    x{0}.p = slot@raw\n    x{0}.flush()\n" + Reset + "»";
        const string TwoHop = "«N:    let w{0} = y@follow@ref\n    var hop{0}: raw/u8 = w{0}@raw@raw/u8\n    var x{0} = WriterF.init(q: hop{0}@raw@raw/u8)\n" +
            "    x{0}.p = slot@raw\n    x{0}.flush()\n" + Reset + "»";
        const string First = "    let wf = y0@follow@ref\n    var xf = WriterF.init(q: wf@raw@raw/u8)\n    xf.p = slot@raw\n    xf.flush()\n" +
            "    xf = WriterF.init(q: null)\n" + Call;
        const string WriteY = "    y@follow = 5\n";
        const string Sneak = "func sneak(slot: uniq/((«N:uniq/(Option<ref/i32 during q>) during q, »i32)), y: uniq/(Lf during q) during q) -> () => ()\n";
        const string Holders = "    var z = 0\n    var leaf = Lf.init(p: z@ref)\n«N:    var h{0} = Option<ref/i32>.Some(z@ref)\n»" +
            "    var pair = («N:h{0}@uniq, »0)\n";
        const string Called = Holders + "    sneak(pair@uniq, leaf@uniq)\n";
        const string Leaf = "    leaf.value = 5\n    let k = h0\n";
        var text = form switch
        {
            "call" => Writes(Call),
            "callBad" => Writes(Call + WriteY),
            "callNoSlot" => Writes(NoSlot),
            "callNoSlotBad" => Writes(NoSlot + WriteY),
            "inline" => Writes(Inline),
            "inlineBad" => Writes(Inline + WriteY),
            "callLocalQ" => Writes(LocalQ),
            "twoHop" => Writes(TwoHop),
            "twoHopBad" => Writes(TwoHop + WriteY),
            "callFirstOk" => Writes(First, ", y0: uniq/i32"),
            "callFirstBad" => Writes(First + "    y0@follow = 5\n", ", y0: uniq/i32"),
            "writingPlaces" => $"{Sneak}func {form}()\n{Called}",
            "wpBad" => $"{Sneak}func {form}()\n{Called}{Leaf}",
            "wpLastBad" => $"{Sneak}func {form}()\n{Called}    leaf.value = 5\n    let k = h{n - 1}\n",
            "wpNoCall" => $"{Sneak}func {form}()\n{Holders}{Leaf}",
            _ => throw new ArgumentException(form, nameof(form)),
        };
        return Expand(text, n, 0);

        string Writes(string body, string more = "") => $"func {form}(y: uniq/i32{more})\n    var slot: raw/u8 = null\n{body}    usep(slot)\n";
    }
}
