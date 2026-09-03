namespace BAREWire.Hardware

/// Memory-mapped hardware descriptors (docs/08).
///
/// A descriptor is a declared layout: where each register or struct field
/// sits, how wide it is, how many inline elements it has, and what access
/// the hardware or the ABI permits. Descriptors are plain records of arrays
/// so the same value is a Clef literal, a .NET value, and a JavaScript
/// object; the validator in `Validator.fs` is what makes the declared
/// contract checkable (docs/10, Evidence 1 and 2).
///
/// Every closed vocabulary is a string alias with a constant module rather
/// than a union, so the types clear the cross-assembly import wall
/// (docs/12 `string-tags`, `record-union-field`).

/// Classification of a memory region (docs/08 "Microcontroller Memory Map").
type MemoryRegionKind = string

[<RequireQualifiedAccess>]
module MemoryRegionKind =
    /// Code and constants, execute-in-place, read-only at run time.
    [<Literal>]
    let Flash: MemoryRegionKind = "flash"
    /// Normal RAM: stack, heap, .data, .bss.
    [<Literal>]
    let SRAM: MemoryRegionKind = "sram"
    /// Memory-mapped I/O: always volatile, never cached.
    [<Literal>]
    let Peripheral: MemoryRegionKind = "peripheral"
    /// Core system peripherals (NVIC, SysTick, debug).
    [<Literal>]
    let SystemControl: MemoryRegionKind = "system-control"
    /// DMA-accessible regions with cache-coherency considerations.
    [<Literal>]
    let DMA: MemoryRegionKind = "dma"
    /// Core-coupled memory: tightly coupled, not cached.
    [<Literal>]
    let CCM: MemoryRegionKind = "ccm"

    /// True when the string names a region kind.
    let isValid (k: MemoryRegionKind) : bool =
        k = Flash || k = SRAM || k = Peripheral || k = SystemControl || k = DMA || k = CCM

/// Hardware-enforced access constraint, from the CMSIS `__I`, `__O`, `__IO` qualifiers.
type AccessKind = string

[<RequireQualifiedAccess>]
module AccessKind =
    /// `__I`: reading returns hardware state; writing is undefined.
    [<Literal>]
    let ReadOnly: AccessKind = "ro"
    /// `__O`: writing triggers hardware action; reading is undefined.
    [<Literal>]
    let WriteOnly: AccessKind = "wo"
    /// `__IO`: both operations are defined.
    [<Literal>]
    let ReadWrite: AccessKind = "rw"

    /// True when the string names an access kind.
    let isValid (a: AccessKind) : bool =
        a = ReadOnly || a = WriteOnly || a = ReadWrite

/// The representation of one register or struct field element.
type Repr = string

[<RequireQualifiedAccess>]
module Repr =
    [<Literal>]
    let U8: Repr = "u8"
    [<Literal>]
    let U16: Repr = "u16"
    [<Literal>]
    let U32: Repr = "u32"
    [<Literal>]
    let U64: Repr = "u64"
    [<Literal>]
    let I8: Repr = "i8"
    [<Literal>]
    let I16: Repr = "i16"
    [<Literal>]
    let I32: Repr = "i32"
    [<Literal>]
    let I64: Repr = "i64"
    [<Literal>]
    let F32: Repr = "f32"
    [<Literal>]
    let F64: Repr = "f64"
    [<Literal>]
    let Bool: Repr = "bool"
    /// A machine pointer; its width and alignment come from the ABI profile.
    [<Literal>]
    let Pointer: Repr = "pointer"

    /// True when the string names a representation.
    let isValid (r: Repr) : bool =
        r = U8 || r = U16 || r = U32 || r = U64
        || r = I8 || r = I16 || r = I32 || r = I64
        || r = F32 || r = F64 || r = Bool || r = Pointer

/// A named bit range inside a register.
type BitFieldDescriptor = {
    Name: string
    Position: int
    Width: int
    Access: AccessKind
}

/// One register or struct field. `Count` is the inline element count: 1 for
/// a scalar, 16 for `reserved[16]`, n for n explicit padding bytes (Repr U8).
type FieldDescriptor = {
    Name: string
    Offset: int
    Repr: Repr
    Count: int
    Access: AccessKind
    BitFields: BitFieldDescriptor array
    Documentation: string option
}

/// The declared extent, alignment, and fields of a register block or struct.
type PeripheralLayout = {
    Size: int
    Alignment: int
    Fields: FieldDescriptor array
}

/// One instance of a peripheral family at a fixed base address.
type Instance = {
    Name: string
    Base: uint64
}

/// A memory-mapped peripheral family: its instances, layout, and region.
type PeripheralDescriptor = {
    Name: string
    Instances: Instance array
    Layout: PeripheralLayout
    MemoryRegion: MemoryRegionKind
}

/// A C struct layout contract: ioctl arguments, shared-memory structures,
/// DMA descriptors, foreign-API descriptors (docs/10).
type StructDescriptor = {
    Name: string
    Layout: PeripheralLayout
    Documentation: string option
}

/// Facts about memory region kinds (docs/08 table).
module MemoryRegion =

    /// True when accesses must be volatile: memory-mapped I/O, core
    /// peripherals, and DMA-visible memory.
    let isVolatile (k: MemoryRegionKind) : bool =
        k = MemoryRegionKind.Peripheral || k = MemoryRegionKind.SystemControl || k = MemoryRegionKind.DMA

    /// True when the region takes normal caching: flash and SRAM.
    let isCacheable (k: MemoryRegionKind) : bool =
        k = MemoryRegionKind.Flash || k = MemoryRegionKind.SRAM

    /// True when code executes from the region: flash, execute-in-place.
    let isExecutable (k: MemoryRegionKind) : bool =
        k = MemoryRegionKind.Flash

/// Bit field constructors.
module BitField =

    /// A bit range at `position` of `width` bits.
    let create (name: string) (position: int) (width: int) (access: AccessKind) : BitFieldDescriptor =
        { Name = name; Position = position; Width = width; Access = access }

    /// A single-bit flag.
    let flag (name: string) (position: int) (access: AccessKind) : BitFieldDescriptor =
        { Name = name; Position = position; Width = 1; Access = access }

/// Field constructors.
module Field =

    /// A scalar field.
    let simple (name: string) (offset: int) (repr: Repr) (access: AccessKind) : FieldDescriptor =
        let none : BitFieldDescriptor array = Array.zeroCreate 0
        { Name = name; Offset = offset; Repr = repr; Count = 1; Access = access; BitFields = none; Documentation = None }

    /// An inline array field of `count` elements (`reserved[16]`).
    let array (name: string) (offset: int) (repr: Repr) (count: int) (access: AccessKind) : FieldDescriptor =
        let none : BitFieldDescriptor array = Array.zeroCreate 0
        { Name = name; Offset = offset; Repr = repr; Count = count; Access = access; BitFields = none; Documentation = None }

    /// Explicit padding: `count` bytes that carry no value.
    let padding (name: string) (offset: int) (count: int) : FieldDescriptor =
        let none : BitFieldDescriptor array = Array.zeroCreate 0
        { Name = name; Offset = offset; Repr = Repr.U8; Count = count; Access = AccessKind.ReadOnly; BitFields = none; Documentation = None }

    /// The same field with documentation attached.
    let withDoc (field: FieldDescriptor) (doc: string) : FieldDescriptor =
        { Name = field.Name; Offset = field.Offset; Repr = field.Repr; Count = field.Count; Access = field.Access; BitFields = field.BitFields; Documentation = Some doc }

    /// The same field with bit fields attached.
    let withBitFields (field: FieldDescriptor) (bits: BitFieldDescriptor array) : FieldDescriptor =
        { Name = field.Name; Offset = field.Offset; Repr = field.Repr; Count = field.Count; Access = field.Access; BitFields = bits; Documentation = field.Documentation }

/// Layout constructors and lookups.
module Layout =

    /// A layout of the declared size and alignment over the given fields.
    let create (size: int) (alignment: int) (fields: FieldDescriptor array) : PeripheralLayout =
        { Size = size; Alignment = alignment; Fields = fields }

    /// The field with the given name, when the layout declares one.
    let tryField (layout: PeripheralLayout) (name: string) : FieldDescriptor option =
        let fields = layout.Fields
        let n = Array.length fields
        let mutable i = 0
        let mutable found = -1
        while i < n && found < 0 do
            let f = Array.get fields i
            if f.Name = name then
                found <- i
            i <- i + 1
        if found < 0 then None else Some (Array.get fields found)

    /// True when no field of the layout has the Pointer representation.
    /// A layout shared with a less trusted side (a BPF map value, a ring slot,
    /// a frame on a wire) must be pointer-free: an address means nothing on
    /// the far side, and the kernel verifier refuses a kernel pointer in
    /// user-readable storage (docs/11, "The kernel as a described platform").
    let isPointerFree (layout: PeripheralLayout) : bool =
        let fields = layout.Fields
        let n = Array.length fields
        let mutable i = 0
        let mutable clean = true
        while i < n do
            let f = Array.get fields i
            if f.Repr = Repr.Pointer then
                clean <- false
            i <- i + 1
        clean

/// Peripheral constructors and lookups.
module Peripheral =

    /// A peripheral family with one instance.
    let single (name: string) (baseAddress: uint64) (layout: PeripheralLayout) (region: MemoryRegionKind) : PeripheralDescriptor =
        let one : Instance = { Name = name; Base = baseAddress }
        let instances : Instance array = [| one |]
        { Name = name; Instances = instances; Layout = layout; MemoryRegion = region }

    /// A peripheral family with several instances (GPIOA, GPIOB, ...).
    let multi (name: string) (instances: Instance array) (layout: PeripheralLayout) (region: MemoryRegionKind) : PeripheralDescriptor =
        { Name = name; Instances = instances; Layout = layout; MemoryRegion = region }

    /// The register with the given name, when the peripheral declares one.
    let tryField (peripheral: PeripheralDescriptor) (name: string) : FieldDescriptor option =
        Layout.tryField peripheral.Layout name

/// Struct descriptor constructors.
module StructLayout =

    /// A struct contract over a layout.
    let create (name: string) (layout: PeripheralLayout) (doc: string option) : StructDescriptor =
        { Name = name; Layout = layout; Documentation = doc }
