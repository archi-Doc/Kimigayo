/* The linker script of x86_64-unknown-none (the Tatara plan's decision 31; IMPL 21.6.1-21.6.2): the compiler's own, fixed for the row
   and embedded in Kimi.dll (TargetProfile.LinkerScript, the catalog's linkerScript), never a project's. The native build stages it and
   passes it to ld.lld (-T); its bytes and SHA-256 are the catalog's. The image is linked at 0xFFFFFFFF80000000 (the kernel code model)
   in three PT_LOAD segments, each page-aligned: text (R+X), rodata with the unwind tables (R), data (R+W). The data segment begins with
   Limine's requests between their start and end markers, kept through --gc-sections. An input section this script does not place
   is a link error (--orphan-handling=error), so the linker's own non-allocated tables are placed too, at address 0. */
OUTPUT_FORMAT(elf64-x86-64)
ENTRY(__kimi_start)

PHDRS
{
    text PT_LOAD FLAGS(5);
    rodata PT_LOAD FLAGS(4);
    data PT_LOAD FLAGS(6);
}

SECTIONS
{
    . = 0xffffffff80000000;
    .text : { *(.text .text.*) } :text

    . = ALIGN(0x1000);
    .rodata : { *(.rodata .rodata.*) } :rodata
    .eh_frame : { KEEP(*(.eh_frame)) } :rodata

    . = ALIGN(0x1000);
    .data : {
        KEEP(*(.limine_requests_start))
        KEEP(*(.limine_requests))
        KEEP(*(.limine_requests_end))
        *(.data .data.*)
    } :data
    .bss : { *(.bss .bss.*) *(COMMON) } :data

    .symtab 0 : { *(.symtab) }
    .strtab 0 : { *(.strtab) }
    .shstrtab 0 : { *(.shstrtab) }

    /DISCARD/ : { *(.note .note.*) *(.comment) }
}
