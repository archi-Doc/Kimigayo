// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record KimiSettings
{
    public const string Filename = "Settings.tinyhand";

    public ColorClass Color { get; set; } = new();

    [TinyhandObject(ImplicitMemberNameAsKey = true, EnumAsString = true, SkipDefaultValues = false)]
    public partial record ColorClass
    {
        public ConsoleColor Debug { get; set; } = ConsoleColor.White;

        public ConsoleColor Information { get; set; } = ConsoleColor.White;

        public ConsoleColor Warning { get; set; } = ConsoleColor.Yellow;

        public ConsoleColor Error { get; set; } = ConsoleColor.Red;

        public ConsoleColor Fatal { get; set; } = ConsoleColor.Red;
    }
}
