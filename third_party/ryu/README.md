# Ryu decimal conversion cores

Upstream: [ulfjack/ryu](https://github.com/ulfjack/ryu), commit `4c0618b0e44f7ef027ebae05d2cc7812048f7c8f`.
The files under `ryu/` are unchanged upstream sources. Kimigayo uses the Boost Software License option in [LICENSE-Boost](LICENSE-Boost).

`backend/windows-x64/generate-ryu.ps1` extracts the allocation-free `f2d` and `d2d` cores, adds primitive-argument wrappers, and compiles them with the pinned LLVM toolchain. The generated LLVM is embedded in the compiler. No C runtime or platform formatting API is required. The compiler build does not download sources or require a C compiler.

The cores compute a decimal mantissa and exponent for the original binary width. Kimigayo's UTF-8 runtime handles special values, notation selection, exact reservation and encoding. Upstream's string output and allocation wrappers are not compiled.
