# Arena Design: Deterministic Memory for Non-Actor Scenarios

> **Status (January 2026)**: Arena is fully implemented as an FNCS intrinsic type and compiles to working native code. Sample 02 (HelloWorldSaturated) demonstrates the complete Arena lifecycle from stack-backed memory through readlnFrom and string output.
>
> **Implementation Note**: Arena was originally designed here in BAREWire but has been elevated to an FNCS intrinsic type (`Arena<[<Measure>] 'lifetime>`) with compiler-provided operations. This document remains the authoritative design reference.

## The Problem

F# functions can allocate memory on the stack using `NativePtr.stackalloc`. This memory is fast to allocate (just bump the stack pointer) and automatically freed when the function returns. However, stack-allocated memory **cannot outlive the creating function**.

Consider `Console.readln`:

```fsharp
let readln () : string =
    let buffer = NativePtr.stackalloc<byte> 256  // On readln's stack
    let len = readLineInto buffer 256
    NativeStr.fromPointer buffer len  // String points to buffer
    // PROBLEM: buffer freed here, string now points to garbage!
```

The caller receives a string pointing to memory that was freed when `readln` returned.

## Solutions in Fidelity's Memory Model

Fidelity's deterministic memory management provides several mechanisms:

| Mechanism | Lifetime | Use Case |
|-----------|----------|----------|
| Stack | Function frame | Temporaries that don't escape |
| Arena | Scope-defined | Allocations that outlive callee |
| Actor Arena | Actor lifetime | Per-actor state |
| Heap | Explicit free | Long-lived shared data |

The **Arena** is the "grade step" between stack and full actor arenas - it provides deterministic lifetime without requiring the actor infrastructure.

## Arena Design Principles

### 1. Bump Allocation

Arenas use the simplest possible allocation strategy:

```
┌─────────────────────────────────────────────┐
│ Arena Memory                                │
├─────────────┬─────────────┬────────────────┤
│ Alloc 1     │ Alloc 2     │ Free Space     │
│ (100 bytes) │ (256 bytes) │                │
└─────────────┴─────────────┴────────────────┘
              ↑                              ↑
           Position                      Capacity
```

- **Allocate**: Bump position forward, return old position as pointer
- **Deallocate**: Not supported individually (by design)
- **Free all**: Reset position to 0, or let arena go out of scope

This is O(1) allocation with zero fragmentation.

### 2. Lifetime Tracking via Types

The arena carries a lifetime marker as a type parameter:

```fsharp
type Arena<[<Measure>] 'lifetime>
```

When code allocates from an arena, the resulting pointer conceptually inherits the arena's lifetime. While F# can't enforce this statically for raw `nativeint`, the pattern is documented and the capability types (`StringCap<'lifetime>`, etc.) can track it.

### 3. Caller-Controlled Scope

The caller creates the arena and controls its lifetime:

```fsharp
let main argv =
    // Arena created on main's stack - lives until main returns
    let arenaMemory = NativePtr.stackalloc<byte> 4096
    let mutable arena = Arena.fromPointer (NativePtr.toNativeInt arenaMemory) 4096

    // All allocations from arena survive function calls
    let name = Console.readlnFrom &arena  // Uses arena, survives readlnFrom
    Console.writeln $"Hello, {name}!"

    0  // Arena freed when main returns
```

This is **explicit and controllable** - the developer decides:
- How much memory to reserve
- Where it comes from (stack, heap, mmap'd region)
- When it gets freed (scope exit)

### 4. No Hidden Allocation

Arenas don't secretly allocate. The backing memory must come from somewhere explicit:

```fsharp
// From stack (common case)
let mem = NativePtr.stackalloc<byte> size
let arena = Arena.fromPointer (NativePtr.toNativeInt mem) size

// From heap (if needed)
let mem = Sys.mmap ... // Future: when heap intrinsics exist
let arena = Arena.fromPointer mem size
```

## Relationship to Actor Arenas

In the full Fidelity model, each actor has a private arena:

```
┌─────────────────────────────────────────┐
│ Actor                                   │
│ ┌─────────────────────────────────────┐ │
│ │ Private Arena                       │ │
│ │ - Message buffers                   │ │
│ │ - Processing temporaries            │ │
│ │ - Local state                       │ │
│ └─────────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

When the actor terminates, the entire arena is freed in one operation.

The simple `Arena<'lifetime>` type is the **same pattern** with simpler scope semantics:
- Actor arena: freed when actor terminates
- Simple arena: freed when creating scope exits

Code that uses arenas today will work naturally with actor arenas when that infrastructure is built.

## Usage Patterns

### Pattern 1: Main-Scoped Arena

For applications where all dynamic allocations can share one arena:

```fsharp
let main argv =
    let mem = NativePtr.stackalloc<byte> 8192
    let mutable arena = Arena.fromPointer (NativePtr.toNativeInt mem) 8192

    // All functions use the arena
    let input = Console.readlnFrom &arena
    let result = processInput &arena input
    let output = formatResult &arena result

    Console.writeln output
    0
```

### Pattern 2: Scoped Arena for Batches

For processing that should release memory between batches:

```fsharp
let processBatches items =
    let mem = NativePtr.stackalloc<byte> 4096
    let mutable arena = Arena.fromPointer (NativePtr.toNativeInt mem) 4096

    for batch in items |> Seq.chunkBySize 100 do
        for item in batch do
            processItem &arena item
        Arena.reset &arena  // Free all batch temporaries
```

### Pattern 3: Caller-Provided Arena (Library Functions)

Library functions accept arena as parameter, letting caller control lifetime:

```fsharp
/// Read line into arena-allocated buffer
let readlnFrom (arena: Arena<'lifetime> byref) : string =
    let buffer = Arena.alloc &arena 256
    let len = readLineIntoPtr buffer 256
    NativeStr.fromPointer buffer len
```

## What Arena Does NOT Do

- **No garbage collection** - Allocations are never individually freed
- **No compaction** - No defragmentation
- **No thread safety** - Single-threaded access assumed (for now)
- **No automatic growth** - Fixed capacity, overflow fails

These limitations are intentional. Arenas are for deterministic, predictable allocation patterns where the developer knows the memory budget upfront.

## Future Extensions

As Fidelity evolves, the arena model may gain:

1. **Growable arenas** - Chain of fixed blocks when needed
2. **Arena pools** - Pre-allocated arena instances for hot paths
3. **Thread-local arenas** - Per-thread default arena
4. **Actor integration** - Arena as actor's private allocator

The current simple design is the foundation these build upon.

## Implementation Status (January 2026)

Arena is implemented as an **FNCS intrinsic type**:

**Type Definition** (in FNCS NativeGlobals.fs):
```fsharp
/// Arena type: Arena<'lifetime>
/// Layout: { Base: nativeint, Capacity: int, Position: int } = 3 platform words
let arenaTyCon =
    mkTypeConRefWithMeasures "Arena"
        [TypeParamKind.Measure]  // 'lifetime is a measure parameter
        (TypeLayout.NTUCompound 3)
```

**Operations** (in FNCS Intrinsics.fs):

| Operation | Type | Description |
|-----------|------|-------------|
| `fromPointer` | `nativeint -> int -> Arena<'lifetime>` | Create arena from backing memory |
| `alloc` | `Arena<'lifetime> byref -> int -> nativeint` | Bump allocate bytes |
| `allocAligned` | `Arena<'lifetime> byref -> int -> int -> nativeint` | Aligned allocation |
| `remaining` | `Arena<'lifetime> -> int` | Query remaining capacity |
| `reset` | `Arena<'lifetime> byref -> unit` | Reset position to 0 |

**Alex Code Generation** (in Firefly):
- Arena is recognized as a compiler-provided intrinsic
- MLIR generation uses struct operations (InsertValue/ExtractValue)
- Templates handle GEP for bump allocation
- Proper handling of byref parameters for mutation

See the Firefly Serena memory `arena_intrinsic_architecture` for full implementation details.
