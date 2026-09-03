# Hardware Descriptors

BAREWire provides a hardware descriptor system for memory-mapped peripheral access. This system enables the Fidelity framework to generate type-safe, zero-copy access to hardware registers with proper volatile semantics.

## Status

> **Implemented (2026-09-03, Readiness Audit §4 step 5).** The descriptor types live in `src/Hardware/Descriptors.fs` in fixed widths: `Repr` is a string tag (`"u8"`, `"i32"`, `"f64"`, `"pointer"`, ...) and every field carries an inline element `Count`, so `reserved[16]` is one field and explicit padding is `Repr.U8` with a count, the two shapes Farscape flattened or dropped (docs/10). `MemoryRegionKind` and `AccessKind` are string tags with constant modules. `src/Hardware/Abi.fs` holds the ABI profiles (SysV AMD64, AArch64, ARM AAPCS, i386 SysV, RISC-V 64, wasm32). `src/Hardware/Validator.fs` is the validator this document and docs/10 asked for first: `derive` computes a C natural layout under an ABI (what corrected Farscape output looks like), `validate` reports misaligned, overlapping, out-of-order, gapped, and mis-sized descriptors with named findings, `explain` prints them. `Layout.isPointerFree` states the rule a layout shared with a less trusted side must meet. `src/Hardware/Btf.fs` serializes descriptors as BTF, the kernel's type format, and reads it back; `bpftool btf dump` confirms the blob (docs/11, "The kernel as a described platform"). The quotation-based `MemoryModel` record described below is the earlier design: descriptors are plain typed records the compiler extracts structurally from the PSG (Fidelity.Platform `CANONICAL_PLATFORM_SPEC.md`, "The mechanism constraint"), and the volatile-access emission remains the compiler's.

## Overview

Hardware memory descriptors serve as the bridge between C/C++ peripheral definitions (parsed by Farscape) and the Fidelity native compilation pipeline. They capture:

1. **Peripheral structure**: Register layouts, offsets, and sizes
2. **Memory regions**: Classification of memory characteristics (volatile, cacheable, etc.)
3. **Access constraints**: Read-only, write-only, or read-write permissions
4. **Instance mapping**: Base addresses for each peripheral instance

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Hardware Descriptor Pipeline                         │
│                                                                         │
│  CMSIS/HAL Headers                                                      │
│         │                                                               │
│         ▼                                                               │
│  ┌─────────────────┐                                                    │
│  │    Farscape     │  Parses C headers, extracts struct layouts         │
│  │   (XParsec)     │  and volatile qualifiers                           │
│  └─────────────────┘                                                    │
│         │                                                               │
│         ▼                                                               │
│  ┌─────────────────┐                                                    │
│  │    BAREWire     │  PeripheralDescriptor, FieldDescriptor,           │
│  │   Descriptors   │  AccessKind, MemoryRegionKind                     │
│  └─────────────────┘                                                    │
│         │                                                               │
│         ▼                                                               │
│  ┌─────────────────┐                                                    │
│  │  Composer/Alex  │  Generates volatile MLIR for peripheral access    │
│  │   Bindings      │  using descriptor metadata                         │
│  └─────────────────┘                                                    │
│         │                                                               │
│         ▼                                                               │
│  Native Binary with correct hardware access                             │
└─────────────────────────────────────────────────────────────────────────┘
```

## Core Types

### PeripheralDescriptor

Describes a complete memory-mapped peripheral family:

```fsharp
/// Describes a memory-mapped peripheral
type PeripheralDescriptor = {
    /// Peripheral family name (e.g., "GPIO", "USART", "SPI")
    Name: string

    /// Map of instance names to base addresses
    /// e.g., GPIOA → 0x48000000, GPIOB → 0x48000400
    Instances: Map<string, unativeint>

    /// Register layout definition
    Layout: PeripheralLayout

    /// Memory region classification
    MemoryRegion: MemoryRegionKind
}
```

### PeripheralLayout

Defines the structure of a peripheral's register set:

```fsharp
/// Layout of a peripheral's register set
type PeripheralLayout = {
    /// Total size in bytes
    Size: int

    /// Required alignment (typically 4 for 32-bit registers)
    Alignment: int

    /// Individual register definitions
    Fields: FieldDescriptor list
}
```

### FieldDescriptor

Describes a single register within a peripheral:

```fsharp
/// Single register within a peripheral
type FieldDescriptor = {
    /// Register name (e.g., "ODR", "BSRR", "IDR")
    Name: string

    /// Byte offset from peripheral base address
    Offset: int

    /// Register data type
    Type: RegisterType

    /// Access constraints (read-only, write-only, read-write)
    Access: AccessKind

    /// Optional bit field definitions for sub-register access
    BitFields: BitFieldDescriptor list option

    /// Documentation from source header
    Documentation: string option
}
```

### AccessKind

Hardware-enforced access constraints from CMSIS qualifiers:

```fsharp
/// Hardware-enforced access constraints
type AccessKind =
    /// __I in CMSIS: Read-only register
    /// Reading returns hardware state
    /// Writing is undefined behavior
    | ReadOnly

    /// __O in CMSIS: Write-only register
    /// Writing triggers hardware action
    /// Reading returns undefined value
    | WriteOnly

    /// __IO in CMSIS: Read-write register
    /// Both operations have defined behavior
    | ReadWrite
```

### MemoryRegionKind

Classification of memory characteristics:

```fsharp
/// Memory region classification
type MemoryRegionKind =
    /// Code/constants, read-only at runtime
    /// Mapped to flash memory, execute-in-place
    | Flash

    /// Normal RAM (stack, heap, globals)
    /// Standard caching behavior
    | SRAM

    /// Memory-mapped I/O
    /// Always volatile, never cached
    | Peripheral

    /// ARM system peripherals (NVIC, SysTick, debug)
    /// Special access sequences may be required
    | SystemControl

    /// DMA-accessible regions
    /// Cache coherency considerations
    | DMA

    /// Core-coupled memory (if present)
    /// Tightly coupled, no cache
    | CCM
```

### BitFieldDescriptor

For registers with named bit fields:

```fsharp
/// Bit field within a register
type BitFieldDescriptor = {
    /// Field name (e.g., "UE" in USART_CR1_UE)
    Name: string

    /// Bit position (from _Pos macro)
    Position: int

    /// Number of bits
    Width: int

    /// Access constraints for this field
    Access: AccessKind
}
```

### RegisterType

Basic register data types:

```fsharp
/// Register data types
type RegisterType =
    | U8
    | U16
    | U32
    | U64
    | I8
    | I16
    | I32
    | I64
```

## CMSIS Qualifier Mapping

CMSIS headers use `__I`, `__O`, and `__IO` qualifiers that map directly to `AccessKind`:

| CMSIS Qualifier | C Definition | AccessKind | Semantics |
|-----------------|--------------|------------|-----------|
| `__I` | `volatile const` | `ReadOnly` | Hardware state register; writes undefined |
| `__O` | `volatile` | `WriteOnly` | Trigger register; reads undefined |
| `__IO` | `volatile` | `ReadWrite` | Normal volatile register |

### Examples

```c
// CMSIS GPIO register definitions
typedef struct {
    __IO uint32_t MODER;    // Mode register (RW)
    __IO uint32_t OTYPER;   // Output type (RW)
    __IO uint32_t OSPEEDR;  // Output speed (RW)
    __IO uint32_t PUPDR;    // Pull-up/pull-down (RW)
    __I  uint32_t IDR;      // Input data (RO) ← Cannot write!
    __IO uint32_t ODR;      // Output data (RW)
    __O  uint32_t BSRR;     // Bit set/reset (WO) ← Cannot read!
    __IO uint32_t LCKR;     // Lock register (RW)
    // ...
} GPIO_TypeDef;
```

Maps to:

```fsharp
let gpioLayout = {
    Size = 0x400
    Alignment = 4
    Fields = [
        { Name = "MODER";   Offset = 0x00; Type = U32; Access = ReadWrite; ... }
        { Name = "OTYPER";  Offset = 0x04; Type = U32; Access = ReadWrite; ... }
        { Name = "OSPEEDR"; Offset = 0x08; Type = U32; Access = ReadWrite; ... }
        { Name = "PUPDR";   Offset = 0x0C; Type = U32; Access = ReadWrite; ... }
        { Name = "IDR";     Offset = 0x10; Type = U32; Access = ReadOnly;  ... }
        { Name = "ODR";     Offset = 0x14; Type = U32; Access = ReadWrite; ... }
        { Name = "BSRR";    Offset = 0x18; Type = U32; Access = WriteOnly; ... }
        { Name = "LCKR";    Offset = 0x1C; Type = U32; Access = ReadWrite; ... }
    ]
}
```

## Microcontroller Memory Map

Different memory regions have different characteristics that affect code generation:

| Region | Typical Address | Characteristics | Code Gen Impact |
|--------|-----------------|-----------------|-----------------|
| Flash | `0x0800_0000` | Execute-in-place, read-only | No volatile, aggressive caching |
| SRAM | `0x2000_0000` | Stack, heap, .bss, .data | Normal memory semantics |
| Peripherals | `0x4000_0000+` | Memory-mapped I/O | Volatile, no reordering |
| System | `0xE000_0000` | ARM core peripherals | Volatile, barrier sequences |

Alex uses `MemoryRegionKind` to determine:
- Whether to emit volatile load/store
- Cache behavior hints
- Memory barrier requirements
- Valid access widths

## Integration with the native type universe

Hardware descriptors work in conjunction with the NTU's measure types — pin and layout metadata ride on the type constructors themselves (e.g. `FieldPinAttributes`; see `~/repos/clef-lang-spec/spec/native-type-universe.md`):

```fsharp
// The Clef compiler services provide these measure types
[<Measure>] type peripheral
[<Measure>] type readOnly
[<Measure>] type writeOnly
[<Measure>] type readWrite

// BAREWire descriptors inform these types
// Standard F# nativeptr with measure parameters for region/access semantics
type Ptr<'T, [<Measure>] 'region, [<Measure>] 'access>

// Farscape generates bindings combining both
type GPIO_TypeDef = {
    MODER: Ptr<uint32, peripheral, readWrite>
    IDR: Ptr<uint32, peripheral, readOnly>
    BSRR: Ptr<uint32, peripheral, writeOnly>
}
```

The dependency chain:

```
Clef compiler services ──provide types──▶ Farscape ──uses descriptors from──▶ BAREWire
```

## Code Generation Impact

When Alex encounters peripheral access in the PSG, it uses descriptor information:

```fsharp
// Clef source
let value = gpio.IDR  // Read input register

// Alex checks descriptor:
// - Field "IDR" has Access = ReadOnly ✓
// - MemoryRegion = Peripheral → volatile

// Generated MLIR
%base = llvm.mlir.constant(0x48000000 : i64) : i64
%ptr = llvm.inttoptr %base : i64 to !llvm.ptr
%idr_ptr = llvm.getelementptr %ptr[0x10] : (!llvm.ptr) -> !llvm.ptr
%value = llvm.load volatile %idr_ptr : !llvm.ptr -> i32
```

For write-only registers:

```fsharp
// Clef source
gpio.BSRR <- 0x20u  // Set pin 5

// Alex checks descriptor:
// - Field "BSRR" has Access = WriteOnly ✓

// Generated MLIR (no read preceding write)
%base = llvm.mlir.constant(0x48000000 : i64) : i64
%ptr = llvm.inttoptr %base : i64 to !llvm.ptr
%bsrr_ptr = llvm.getelementptr %ptr[0x18] : (!llvm.ptr) -> !llvm.ptr
%value = llvm.mlir.constant(0x20 : i32) : i32
llvm.store volatile %value, %bsrr_ptr : i32, !llvm.ptr
```

## Tree-Shaking

BAREWire descriptors participate in Fidelity's tree-shaking:

1. Reachability analysis identifies used peripherals
2. Only referenced descriptors are included
3. Final binary contains only necessary peripheral metadata
4. Unused peripherals (GPIOB, GPIOC, etc.) are eliminated

## Relationship to Existing BAREWire Components

Hardware descriptors extend BAREWire's existing abstractions:

| Existing Component | Hardware Extension |
|-------------------|-------------------|
| `Region<'T, 'region>` | Fixed-address hardware region |
| `View<'T, 'region>` | Type-safe register access via descriptors |
| Schema system | Peripheral layout as schema |
| Memory mapping | Fixed addresses known at compile time |

Key difference: Hardware regions have **fixed addresses** determined at compile time from linker scripts and device specifications, not dynamically allocated.

## Future: Unified Memory Description

The hardware descriptor system unifies with BAREWire's IPC/serialization memory descriptions — the peripheral descriptor's pattern (declared layout, fixed extents, access constraints) generalizes to the process's own memory map, buffer schemas, and boundary contracts for every processor (see [11 Platform Description](./11%20Platform%20Description.md)):

- **Hardware peripherals**: Fixed addresses, volatile, hardware-constrained
- **Shared memory IPC**: Dynamic addresses, potentially volatile
- **Network buffers**: Dynamic, schema-driven, serialization-aware

All use the same underlying memory layout primitives with different allocation and access semantics.

## Implementation Location

Hardware descriptor types live in:

```
BAREWire/
└── src/
    └── Hardware/
        ├── Descriptors.fs   # PeripheralDescriptor, FieldDescriptor (Repr, Count),
        │                    # AccessKind, MemoryRegionKind, BitFieldDescriptor,
        │                    # StructDescriptor, Layout.isPointerFree
        ├── Abi.fs           # AbiProfile and the known profiles
        ├── Validator.fs     # derive, validate, explain
        └── Btf.fs           # BTF emitter and reader
```



## Related Documentation

| Document | Location |
|----------|----------|
| Core Types and Measures | `./01 Core Types and Measures.md` |
| Memory Mapping | `./04 Memory Mapping.md` |
| Farscape Integration | `~/repos/Farscape/docs/02_BAREWire_Integration.md` |
| Native type universe | `~/repos/clef-lang-spec/spec/native-type-universe.md` (supersedes the FNCS specification) |
| Memory interlock requirements | `~/repos/Composer/docs/` (repository renamed from Firefly) |
