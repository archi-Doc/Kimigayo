namespace Kimi.Compiler.Target;

/// <summary>Mirror of llvm::Triple::VendorType (LLVM main, Aug 2026).</summary>
public enum VendorType
{
    Unknown = 0,

    Apple,                   // "apple"
    PC,                      // "pc"
    SCEI,                    // "scei" (alias: "sie")
    Freescale,               // "fsl"
    IBM,                     // "ibm"
    ImaginationTechnologies, // "img"
    MipsTechnologies,        // "mti"
    Nvidia,                  // "nvidia"
    CSR,                     // "csr"
    AMD,                     // "amd"
    Mesa,                    // "mesa"
    SUSE,                    // "suse"
    OpenEmbedded,            // "oe"
    Intel,                   // "intel"
    Meta,                    // "meta"
}
