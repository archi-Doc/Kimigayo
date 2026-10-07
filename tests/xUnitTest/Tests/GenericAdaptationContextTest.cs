// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// SPEC 7.2.3, 8.10 and 13.5: selection and physical validation use the same stored and default-call Types.
public class GenericAdaptationContextTest
{
    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void NestedDefaultUpcastsKeepTheirCalleeTypeArguments(string mode)
    {
        var source = "open struct Base<T>\n    public let value: i32 = 9\n" +
            "struct Derived<T>: Base<T>\n    public init() => ()\n" +
            "func inspect<s/T>(value: s/Derived<T>, number: i32 = (value@objref/Base<T>).value) -> i32\n" +
            "    s is object\n    T is Owned and ObjectPayload\n    return number\n" +
            $"func outer<T>(number: i32 = inspect<{mode}/T>(Derived<T>.init()@{mode})) -> i32\n" +
            "    T is Owned and ObjectPayload\n    return number\n" +
            "require outer<i32>() == 9 and outer<bool>() == 9 else => $abort(\"default\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationDefaultUpcast" + mode, source, 2, 2, 40);
    }

    [Fact]
    public void NestedDefaultsSelectBorrowOrCreationWithoutKeepingPreparedLoans()
    {
        const string Source = "func inspect<s/T>(value: i32 = 7, number: i32 = label work: do\n" +
            "    _ = value@s\n    exit to work 9\n) -> i32\n    s is obj or ref\n    return number\n" +
            "func outer<s/T>(number: i32 = inspect<s/T>()) -> i32\n    s is obj or ref\n    return number\n" +
            "require outer<obj/i32>() == 9 else => $abort(\"object\")\n" +
            "require outer<ref/i32 during static>() == 9 else => $abort(\"reference\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationDefaultSlot", Source, 1, 1, 20);
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void StoredGenericHandlesBorrowTheirConcreteSlot(string mode)
    {
        var source = "struct Box<E>\n    public var item: E\n    public init(item: E) => self.item = item@move\n" +
            "func inspect<s/T>(box: ref/Box<s/T>)\n    s is object\n    _ = box.item@objref/T\n" +
            $"let box = Box<{mode}/i32>.init(7@{mode})\ninspect<{mode}/i32>(box@ref)\n" +
            "require box.item@follow == 7 else => $abort(\"stored\")";
        NativeAllocationAudit.WriteFixture("GenericAdaptationStoredSlot" + mode, source, 1, 1, 20);
    }
}
