namespace BAREWire.Hardware

/// A target ABI's scalar sizes and alignments: the facts a C compiler uses
/// to lay out a struct (docs/09 "Natural Alignment Preservation"). A
/// descriptor is validated against one of these; the same descriptor can
/// agree with one ABI and disagree with another (docs/10, the Fixed-versus-
/// Resolved question, exercised on a concrete case).
type AbiProfile = {
    Name: string
    PointerSize: int
    PointerAlign: int
    I64Align: int
    F64Align: int
    MaxAlign: int
}

/// The ABI profiles the framework targets, and the size and alignment of
/// each representation under a profile.
module Abi =

    /// x86-64 System V (Linux, BSD, macOS).
    let sysvAmd64 : AbiProfile =
        { Name = "sysv-amd64"; PointerSize = 8; PointerAlign = 8; I64Align = 8; F64Align = 8; MaxAlign = 16 }

    /// AArch64 AAPCS64 (Linux, Apple Silicon).
    let aarch64 : AbiProfile =
        { Name = "aarch64"; PointerSize = 8; PointerAlign = 8; I64Align = 8; F64Align = 8; MaxAlign = 16 }

    /// 32-bit ARM AAPCS, the Cortex-M and ARM EABI: 64-bit scalars are 8-aligned.
    let armAapcs : AbiProfile =
        { Name = "arm-aapcs"; PointerSize = 4; PointerAlign = 4; I64Align = 8; F64Align = 8; MaxAlign = 8 }

    /// i386 System V: 64-bit scalars are only 4-aligned.
    let i386SysV : AbiProfile =
        { Name = "i386-sysv"; PointerSize = 4; PointerAlign = 4; I64Align = 4; F64Align = 4; MaxAlign = 4 }

    /// RISC-V 64 LP64D.
    let riscv64 : AbiProfile =
        { Name = "riscv64"; PointerSize = 8; PointerAlign = 8; I64Align = 8; F64Align = 8; MaxAlign = 16 }

    /// WebAssembly 32-bit: 4-byte pointers, 8-aligned 64-bit scalars.
    let wasm32 : AbiProfile =
        { Name = "wasm32"; PointerSize = 4; PointerAlign = 4; I64Align = 8; F64Align = 8; MaxAlign = 8 }

    /// Every profile, in a fixed order.
    let all : AbiProfile array =
        [| sysvAmd64; aarch64; armAapcs; i386SysV; riscv64; wasm32 |]

    /// The profile with the given name, when one exists.
    let tryFind (name: string) : AbiProfile option =
        let n = Array.length all
        let mutable i = 0
        let mutable found = -1
        while i < n && found < 0 do
            let p = Array.get all i
            if p.Name = name then
                found <- i
            i <- i + 1
        if found < 0 then None else Some (Array.get all found)

    /// The byte width of one element of a representation; 0 for an unknown repr.
    let reprSize (abi: AbiProfile) (repr: Repr) : int =
        if repr = Repr.U8 || repr = Repr.I8 || repr = Repr.Bool then 1
        elif repr = Repr.U16 || repr = Repr.I16 then 2
        elif repr = Repr.U32 || repr = Repr.I32 || repr = Repr.F32 then 4
        elif repr = Repr.U64 || repr = Repr.I64 || repr = Repr.F64 then 8
        elif repr = Repr.Pointer then abi.PointerSize
        else 0

    /// The natural alignment of a representation under the profile; 1 for an unknown repr.
    let reprAlign (abi: AbiProfile) (repr: Repr) : int =
        if repr = Repr.U8 || repr = Repr.I8 || repr = Repr.Bool then 1
        elif repr = Repr.U16 || repr = Repr.I16 then 2
        elif repr = Repr.U32 || repr = Repr.I32 || repr = Repr.F32 then 4
        elif repr = Repr.U64 || repr = Repr.I64 then abi.I64Align
        elif repr = Repr.F64 then abi.F64Align
        elif repr = Repr.Pointer then abi.PointerAlign
        else 1

    /// The smallest multiple of `align` that is at least `value`; `value` itself when `align` is 1 or less.
    let alignUp (value: int) (align: int) : int =
        if align <= 1 then value
        else ((value + align - 1) / align) * align

    /// The byte extent of a field: element size times inline count.
    let fieldSize (abi: AbiProfile) (field: FieldDescriptor) : int =
        (reprSize abi field.Repr) * field.Count
