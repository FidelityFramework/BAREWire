# BAREWire Assessment - December 2024

## As Foundation for Farscape (Hardware Bindings)

### What BAREWire Has
1. **Core BARE Type System** - Complete primitive and aggregate types
2. **Memory Abstraction** - `Memory<'T, 'region>`, `Region`, `MemoryView`
3. **Schema System** - Type-safe schema building with draft/validated states
4. **Units of Measure** - `offset`, `bytes`, `region`, `Access.ReadOnly/ReadWrite/WriteOnly`
5. **Platform Abstraction** - Interfaces for Memory, IPC, Network, Sync

### What's Missing for Hardware Memory Mapping

| Need | Current BAREWire | Gap |
|------|------------------|-----|
| `MemoryRegionKind` | Nothing | No Flash/SRAM/Peripheral classification |
| Fixed addresses | `byte[]` backing | No compile-time constant addresses |
| Volatile semantics | None | No volatile load/store primitives |
| Bit field descriptors | `StructField` only | Missing position/width/access |
| Peripheral instances | Nothing | No GPIOA/GPIOB/GPIOC mapping |

### BCL Dependencies (Problem for Fidelity)
Platform providers use: `System.Array.Copy`, `String.IsNullOrEmpty`, `String.Split`
These need native implementations for Firefly compilation.

### Recommended Implementation

**New Module: `BAREWire.Hardware`**

```fsharp
type AccessKind = ReadOnly | WriteOnly | ReadWrite

type MemoryRegionKind = Flash | SRAM | Peripheral | SystemControl | DMA | CCM

type BitFieldDescriptor = {
    Name: string; Position: int; Width: int; Access: AccessKind
}

type RegisterDescriptor = {
    Name: string; Offset: int; Width: int; Access: AccessKind
    BitFields: BitFieldDescriptor list; Documentation: string option
}

type PeripheralDescriptor = {
    Name: string; BaseAddress: unativeint
    Registers: RegisterDescriptor list; Region: MemoryRegionKind
    Instances: Map<string, unativeint>
}

type ChipMemoryMap = {
    Chip: string; Peripherals: PeripheralDescriptor list
    Interrupts: (string * int) list
    MemoryRegions: (MemoryRegionKind * unativeint * int) list
}
```

### Flow: Farscape → BAREWire → Firefly

1. **Farscape** parses CMSIS headers → generates `ChipMemoryMap` as F# source
2. **ChipMemoryMap** uses BAREWire descriptor types
3. **Firefly PSG** tree-shakes to used peripherals only
4. **Alex** consumes descriptors → emits volatile MLIR at fixed addresses

### Key Design Principle

Developers write clean F# like `GPIO.GPIOA.writePin 5 true`
- BAREWire descriptors encode what "GPIOA pin 5" means (address, register, bit)
- Firefly/Alex emits the correct volatile store at compile time
- No runtime memory allocation for peripheral access
