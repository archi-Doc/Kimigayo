// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace XunitTest;

// A freestanding Kernel (SPEC 22.4.5): Hello.kimi's startup body and Board.kimi's platform without its comments, read by
// ContractBindingTest for the Board's conformance to Platform and by StartupBindingTest for the platform's selection. The Board's
// Contract members are `public` (SPEC 8.6.3's Access row, 9.4.6); `terminate` is BoardTerminate, so Board alone lacks one requirement.
internal static class KernelSources
{
    internal const string Hello = "Console.writeLine(\"Hello\")\n";

    internal const string Board = """
        struct Board
            Self is Copy
            Self is Platform
            var next: usize
            var end: usize
            var screen: raw/u16
            var cell: isize
            var ticks: i64

            init(next: usize, end: usize, screen: raw/u16)
                self.next = next
                self.end = end
                self.screen = screen
                self.cell = 0
                self.ticks = 0

            public func start(boot: Boot) -> Self
                let window = boot.directMapOffset
                return Board.init(window + 0x0100_0000, window + 0x0300_0000, (window + 0xB_8000)@raw/u16)

            public func mapPages(self: uniq/Self, bytes: isize) -> raw/u8
                if self.end - self.next < bytes@usize
                    return null
                let run = self.next
                self.next += bytes@usize
                return run@raw/u8

            public func releasePages(self: uniq/Self, run: raw/u8, bytes: isize) -> i64 => 0

            public func write(self: uniq/Self, stream: i32, text: Text.Utf8Slice) -> i64
                let bytes = text.bytes()
                var i: isize = 0
                while i < bytes.length
                    if bytes[i] == 10
                        self.cell = (self.cell / 80 + 1) * 80
                    else
                        unsafe => self.screen[self.cell] = bytes[i]@u16 + 0x0700
                        self.cell += 1
                    i += 1
                return 0

            public func clock(self: uniq/Self) -> i64
                self.ticks += 1
                return self.ticks

        """;

    internal const string BoardTerminate = "    public func terminate(self: uniq/Self, status: i32) -> Never\n        loop => ()\n";
}
