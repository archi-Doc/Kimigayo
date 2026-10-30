// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate): SPEC 4.9.6
// publishes Array `truncate` ("Destruction of the relevant `T`") and `removeAll` ("Calls of `matching`; destruction of the removed `T`")
// as rows, the operations' complete published effects, which callers use, never a library body; SPEC 15.6.4 composes the destruction a
// row lists at the call. Binding's retention of a call reads the signature and the premises' Callable bounds, never a body, so a row
// changes no retention row.
public sealed partial class RetentionTest
{
    // A raw-writing helper a Closure's captures reach (the removeAllWrite form, PLAN G266's).
    private const string RawStash = "func stash(p: raw/(raw/u8), q: raw/u8) -> bool\n    unsafe => *p = q\n    return true\n";

    // SPEC 4.9.6, 15.6.4 (PLAN G264 U0′): `truncate` and `removeAll` publish 4.9.6's rows as catalog facts that TyCtxt.Published reads as it
    // reads `clear`'s (the comparison): each destroys the Type below its receiver, parameter 0, not in place. The representation of
    // `removeAll`'s calls of `matching` is the candidates' (no column is pinned here; MirEffectsTest observes the composition). Fail-before:
    // `truncate` and `removeAll` have no row (Published is null).
    [Theory]
    [InlineData("truncate")]
    [InlineData("removeAll")]
    [InlineData("clear")]
    public void ATruncateOrRemoveAllRowDestroysBelowItsReceiver(string name)
    {
        var c = Bind(string.Empty);
        var array = (DeclarationContainerKoto)c.Library.GetSymbol(KimiDeclarationId.Array)!.Declaration;
        var operation = array.Members.OfType<FunctionKoto>().Single(x => x.Name == name).SymbolOf()!;
        var row = c.Types.Published(operation);
        Assert.True(row.HasValue, $"{name}: no published row");
        Assert.Equal((0, false), (row.Value.Destroys, row.Value.InPlace));
    }

    // SPEC 15.6.4, 4.9.6 (PLAN G264 U0′; DECISIONS BU-42, BU-47): a row changes no retention row, which reads the signature and the
    // premises' Callable bounds, never the body: `truncate` and `removeAll` over `Array<Writer>` write their receiver's unstated raw layers
    // only in its referent (`store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0`, as `clear` does), over an abstract
    // `Array<T>` its content (`store 0: own {0 Content}, reached {}, referent store 0`), and `removeAll` over `Array<i32>` with a Closure
    // that captures raw pointers writes only its referent (`referent store 0`): a transfer operation unties no input it does not write
    // (DECISIONS BU-52's G266 fix 1, the TransferWrites gate covering the untied verdict; SPEC's rule by BU-67), so the Closure is no
    // untied store; removeAllWriteBad (MirBorrowsTest, G266's, kept apart) is the form where that matters. Guards: each row holds today
    // and must hold after the rows are published.
    [Theory]
    [InlineData(Writer + "var a: Array<Writer> = []\na.truncate(0)\n", "truncate", "store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData(Writer + "var a: Array<Writer> = []\na.removeAll(matching: func (v) => true)\n", "removeAll", "store 0: own {0 Chain}, reached {0 Chain} untied, referent store 0")]
    [InlineData("func g<T>(a: uniq/Array<T>) => a.truncate(0)\n", "truncate", "store 0: own {0 Content}, reached {}, referent store 0")]
    [InlineData("func g<T>(a: uniq/Array<T>) => a.removeAll(matching: func (v) => true)\n", "removeAll", "store 0: own {0 Content}, reached {}, referent store 0")]
    [InlineData(
        RawStash + "var a: Array<i32> = [1]\nvar slot: raw/u8 = null\nlet p = slot@raw\nlet q: raw/u8 = null\na.removeAll(matching: func [p, q] (v) => stash(p, q))\n",
        "removeAll",
        "referent store 0")]
    public void ATruncateOrRemoveAllCallRetainsByItsSignatureNotItsRow(string source, string callee, string expected)
        => Assert.Equal(expected, string.Join(" | ", Calls(Bind(source), callee)));
}
