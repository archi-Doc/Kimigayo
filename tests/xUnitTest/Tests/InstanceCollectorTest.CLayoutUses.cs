// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G158 (3) acceptance rows of LayoutAttributeBindingTest.CLayoutUses.cs under MIR code generation (sel), where the instance collector
// reports the checked set's layout records at check (DECISIONS R5-M41, R5-M55): the same verdicts, one record per use and none at the
// declaration, so a collector record and a Binding record of one use never stand together (SPEC 23.1.2 lets one failure's reports be
// grouped, never doubled).
public sealed partial class InstanceCollectorTest
{
    [Theory]
    [MemberData(nameof(LayoutAttributeBindingTest.AcceptedRows), MemberType = typeof(LayoutAttributeBindingTest))]
    [MemberData(nameof(LayoutAttributeBindingTest.UseRows), MemberType = typeof(LayoutAttributeBindingTest))]
    [MemberData(nameof(LayoutAttributeBindingTest.InferredRows), MemberType = typeof(LayoutAttributeBindingTest))]
    [MemberData(nameof(LayoutAttributeBindingTest.TwinRows), MemberType = typeof(LayoutAttributeBindingTest))]
    public void UnderMirCodeGenerationEachUseOfAnUnavailableCLayoutIsOneRecord(string form, string source, string records)
        => LayoutAttributeBindingTest.AssertUses(form, source, records, sel: true);

    // Kept apart with their default-pipeline rows: a bodyless import's by-value parameter and part (2), a Never Field.
    [Theory]
    [MemberData(nameof(LayoutAttributeBindingTest.PendingImportRows), MemberType = typeof(LayoutAttributeBindingTest))]
    [MemberData(nameof(LayoutAttributeBindingTest.PendingNeverRows), MemberType = typeof(LayoutAttributeBindingTest))]
    public void UnderMirCodeGenerationTheKeptApartRowsPendingOwner(string form, string source, string records)
        => LayoutAttributeBindingTest.AssertUses(form, source, records, sel: true);
}
