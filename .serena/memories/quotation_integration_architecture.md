# BAREWire Quotation Integration Architecture

## Role in the Four-Component Pipeline

BAREWire provides the **memory description vocabulary** that Farscape uses to generate quotations:

```
Farscape parses C headers
    ↓
Uses BAREWire types (PeripheralDescriptor, FieldDescriptor, AccessKind, etc.)
    ↓
Generates Expr<PeripheralDescriptor> quotations
    ↓
Generates Active Patterns for PSG recognition
    ↓
Generates MemoryModel record for fsnative integration
```

## BAREWire's Contribution

BAREWire provides the **types**, Farscape generates the **quotations**:

```fsharp
// BAREWire defines this type
type PeripheralDescriptor = {
    Name: string
    Instances: Map<string, unativeint>
    Layout: PeripheralLayout
    MemoryRegion: MemoryRegionKind
}

// Farscape generates quotations using BAREWire types
let gpioQuotation: Expr<PeripheralDescriptor> = <@
    { Name = "GPIO"
      Instances = Map.ofList [("GPIOA", 0x48000000un)]
      Layout = gpioLayout
      MemoryRegion = Peripheral }
@>
```

## Type Contracts for Farscape

BAREWire must provide these types for Farscape's quotation generation:

| Type | Purpose | Quotation Usage |
|------|---------|-----------------|
| `PeripheralDescriptor` | Complete peripheral definition | `Expr<PeripheralDescriptor>` |
| `FieldDescriptor` | Register within peripheral | Part of Layout |
| `AccessKind` | Read/Write constraints | Compile-time safety |
| `MemoryRegionKind` | Memory classification | Volatile semantics |
| `BitFieldDescriptor` | Sub-register fields | Bit manipulation |
| `RegisterType` | Register data types | Type-safe access |

## MemoryModel Record (Pure F#)

The integration surface is a **record type**, not an interface:

```fsharp
/// Memory model provided by target-specific plugins
/// Pure F# record - no BCL patterns (interfaces, abstract classes)
type MemoryModel = {
    TargetFamily: string
    PeripheralDescriptors: Expr<PeripheralDescriptor> list
    RegisterConstraints: Expr<RegisterConstraint> list
    Regions: Expr<RegionDescriptor list>
    Recognize: PSGNode -> MemoryOperation option
    CacheTopology: Expr<CacheLevel list> option
    CoherencyModel: Expr<CoherencyPolicy> option
}
```

**Why record, not interface?**
- Pure F# idiom, no BCL dependencies
- Won't visually compete with future F* proof annotations
- Simpler composition (record update syntax)
- No inheritance hierarchies to manage

## Active Pattern Generation

Farscape generates active patterns using BAREWire type information:

```fsharp
// Generated from BAREWire AccessKind
let (|ReadOnlyRegister|WriteOnlyRegister|ReadWriteRegister|) (field: FieldDescriptor) =
    match field.Access with
    | ReadOnly -> ReadOnlyRegister
    | WriteOnly -> WriteOnlyRegister
    | ReadWrite -> ReadWriteRegister

// Generated from BAREWire PeripheralDescriptor
let (|GpioAccess|_|) (node: PSGNode) : GpioAccessInfo option = ...
```

## Implementation Priority

1. **Core Types** - `AccessKind`, `MemoryRegionKind`, `RegisterType` (enums/DUs)
2. **Descriptor Types** - `FieldDescriptor`, `PeripheralLayout`, `PeripheralDescriptor`
3. **Constraint Types** - `RegisterConstraint`, `RegionDescriptor` (for quotations)

## Canonical Reference

See `~/repos/Firefly/docs/Quotation_Based_Memory_Architecture.md` for the complete architecture.
