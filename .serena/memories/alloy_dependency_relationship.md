# BAREWire and Alloy Dependency Relationship

## Dependency Direction

```
BAREWire ──depends on──▶ Alloy
```

BAREWire uses Alloy as its foundation for:
- Standard F# types with native semantics (string, option, array)
- Memory operations (`Memory.copy`, `Memory.zero`)
- Platform bindings pattern

## What BAREWire Gets from Alloy

### Core Types
- Standard F# `nativeptr<'T>` (from NativeInterop)
- `Span<'T>`, `ReadOnlySpan<'T>` with native semantics
- Value types for memory operations

### Memory Primitives
```fsharp
// BAREWire can use Alloy's semantic memory primitives
open Alloy.Memory

let copyRegion src dest len =
    copy src dest len  // Alloy provides, Firefly optimizes
```

### Platform Binding Pattern
```fsharp
// BAREWire follows Alloy's Platform.Bindings convention
module Platform.Bindings =
    let mapMemory addr size flags : nativeint = Unchecked.defaultof<nativeint>
    let unmapMemory addr size : unit = ()
```

## What BAREWire Does NOT Get from Alloy

- **No BCL types**: BAREWire doesn't use `System.Array`, `System.String`, etc.
- **No high-level abstractions**: Alloy's `Console`, `Time` are application-level, not for BAREWire
- **No type shadows**: BAREWire uses standard F# types with native semantics (FNCS provides native semantics at the compiler level, no wrapper types needed)

## Alloy's Role as BNL (Base Native Library)

Alloy is the **consumption surface** - the user-facing library:

```fsharp
// User code uses Alloy
open Alloy

let main () =
    Console.WriteLine "Hello"  // Alloy provides
```

BAREWire is **infrastructure** - invisible to most users:

```fsharp
// User code does NOT directly use BAREWire for hardware access
// Instead, Farscape-generated bindings use BAREWire types internally

// Farscape generates:
let gpioDescriptor: PeripheralDescriptor = { ... }  // BAREWire type

// User sees:
GPIO.writePin GPIOA 5 true  // Clean API, descriptor hidden
```

## Build Considerations

Since BAREWire depends on Alloy, and Alloy uses `.fidproj` format:

1. **For development**: Create shadow `Alloy.fsproj` for F# LSP indexing
2. **For Fidelity builds**: BAREWire is compiled by Firefly along with Alloy

## Integration with Hardware Targets

For hardware targets like STM32:

```
Alloy (foundation) ← BAREWire (descriptors) ← Farscape (generation) → User bindings
```

- Alloy provides memory primitives
- BAREWire provides descriptor types
- Farscape populates descriptors from headers
- User code uses clean F# API

## Canonical References

- Alloy architecture: `~/repos/Alloy/.serena/memories/alloy_comprehensive_guide.md`
- BAREWire role: `~/repos/BAREWire/.serena/memories/native_binding_role.md`
- Complete pipeline: `~/repos/Firefly/docs/Quotation_Based_Memory_Architecture.md`
