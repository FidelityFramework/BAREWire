# BAREWire Pure F# Design Principles

## Core Principle: No BCL Patterns

BAREWire is part of the Fidelity ecosystem which compiles to native code without .NET runtime.
All types must use pure F# idioms that won't interfere with:
- Native compilation via Firefly
- Future F* proof annotations
- Integration with fsnative compiler extensions

## What to Avoid

### ❌ Interfaces
```fsharp
// WRONG - BCL pattern
type IMemoryProvider =
    abstract GetRegion: unit -> Region
```

### ❌ Abstract Classes
```fsharp
// WRONG - BCL pattern
[<AbstractClass>]
type MemoryProviderBase() =
    abstract GetRegion: unit -> Region
```

### ❌ I-Prefix Naming
```fsharp
// WRONG - .NET naming convention
type IPeripheralDescriptor = ...
type IAccessKind = ...
```

### ❌ BCL Attributes (except where required by F#)
```fsharp
// WRONG - unnecessary BCL dependency
[<System.Serializable>]
type MyType = ...
```

## What to Use

### ✅ Record Types
```fsharp
// RIGHT - pure F# record
type MemoryModel = {
    TargetFamily: string
    Regions: Expr<RegionDescriptor list>
    Recognize: PSGNode -> MemoryOperation option
}
```

### ✅ Discriminated Unions
```fsharp
// RIGHT - pure F# DU
type AccessKind =
    | ReadOnly
    | WriteOnly
    | ReadWrite

type MemoryRegionKind =
    | Flash
    | SRAM
    | Peripheral
    | SystemControl
```

### ✅ Module Functions
```fsharp
// RIGHT - functions in modules
module PeripheralDescriptor =
    let create name instances layout region = ...
    let getField name descriptor = ...
```

### ✅ Active Patterns
```fsharp
// RIGHT - compositional pattern matching
let (|VolatileAccess|NormalAccess|) region =
    match region with
    | Peripheral | SystemControl -> VolatileAccess
    | SRAM | Flash -> NormalAccess
```

## Struct Annotations

`[<Struct>]` is acceptable and encouraged for value types:

```fsharp
// OK - performance optimization, not BCL pattern
[<Struct>]
type BitFieldDescriptor = {
    Name: string
    Position: int
    Width: int
    Access: AccessKind
}
```

## Required F# Attributes

These are part of F# itself, not BCL patterns:

```fsharp
[<Measure>] type bytes        // Units of measure
[<Struct>]                    // Value type optimization
[<RequireQualifiedAccess>]    // Module access control
[<AutoOpen>]                  // Namespace convenience
```

## Why This Matters

1. **Native Compilation**: BCL patterns require runtime support that doesn't exist in Fidelity
2. **F* Annotations**: Future proof annotations like `requires`, `ensures`, `decreases` will be attributes - minimizing BCL attributes prevents visual clutter
3. **Simplicity**: Pure F# is simpler and more composable
4. **Quotations**: BCL patterns don't quote cleanly; records and DUs do

## Canonical Reference

See `~/repos/Firefly/docs/Quotation_Based_Memory_Architecture.md`, section "Design Principle 4: Pure F# Idioms, No BCL Patterns"
