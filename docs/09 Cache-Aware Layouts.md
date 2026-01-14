# Cache-Aware Layouts

BAREWire's deterministic layout system enables compile-time analysis of cache behavior. This document specifies how BAREWire ensures cache-friendly memory layouts and prevents common cache pathologies.

## Architectural Context

BAREWire controls memory layout at compile time. This is not merely an implementation detail; it is the foundation of Fidelity's cache behavior guarantees:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Layout Control Hierarchy                             │
│                                                                         │
│  Application Code                                                       │
│       │                                                                 │
│       ▼                                                                 │
│  BAREWire Schema                                                        │
│       │                                                                 │
│       ▼                                                                 │
│  Deterministic Layout ─────────▶ Cache Behavior Analysis               │
│       │                                                                 │
│       ▼                                                                 │
│  MLIR/LLVM (respects layout)                                           │
│       │                                                                 │
│       ▼                                                                 │
│  Native Binary                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

Because BAREWire determines layout before code generation, cache behavior is analyzable at compile time.

## Cache Line Fundamentals

### Hardware Cache Lines

Modern processors operate on cache lines, not individual bytes:

| Architecture | L1 Cache Line | L2 Cache Line | L3 Cache Line |
|--------------|---------------|---------------|---------------|
| x86-64 (Intel/AMD) | 64 bytes | 64 bytes | 64 bytes |
| ARM Cortex-A (most) | 64 bytes | 64 bytes | 64 bytes |
| ARM Cortex-M (embedded) | 32 bytes | 32 bytes | N/A |
| Apple Silicon | 128 bytes | 128 bytes | 128 bytes |

### The False Sharing Problem

When two independent data items share a cache line, modifications to one invalidate the entire line for all cores:

```
Core 0                                    Core 1
   │                                         │
   ▼                                         ▼
┌──────────────────────────────────────────────────────────────────┐
│                     Cache Line (64 bytes)                         │
├──────────────────────────────────────────────────────────────────┤
│  counter_a (8 bytes)  │  padding (48 bytes)  │  counter_b (8B)   │
│  ▲                                                      ▲        │
│  │                                                      │        │
│  Modified by Core 0                          Modified by Core 1  │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                     MESI Protocol Invalidation
                     (performance cliff)
```

This is **false sharing**: logically independent data causes cache coherence traffic.

## BAREWire Cache-Aware Layout Rules

### Rule 1: Cache Line Size Awareness

BAREWire schemas carry target cache line size:

```fsharp
type LayoutConfig = {
    /// Target cache line size in bytes
    /// Default: 64 for x86-64/ARM64, 128 for Apple Silicon
    CacheLineSize: int

    /// Minimum alignment for cache-sensitive types
    CacheAlignment: int

    /// Enable automatic padding analysis
    FalseSharingAnalysis: bool
}
```

This is resolved from the target triple at compile time.

### Rule 2: Natural Alignment Preservation

All types are naturally aligned:

```
Type        Size    Alignment
----        ----    ---------
int8        1       1
int16       2       2
int32       4       4
int64       8       8
float32     4       4
float64     8       8
nativeint   8       8 (on 64-bit)
```

BAREWire never breaks natural alignment.

### Rule 3: Struct Padding for Alignment

Structs are padded to satisfy member alignment requirements:

```fsharp
// F# type
type Example = {
    a: byte      // 1 byte
    b: int64     // 8 bytes, requires 8-byte alignment
    c: byte      // 1 byte
}

// BAREWire layout (18 bytes total, 8-byte aligned)
Offset  Size  Member
------  ----  ------
0       1     a
1       7     <padding>
8       8     b
16      1     c
17      7     <padding to 8-byte boundary>
```

### Rule 4: Cache Line Alignment Attribute

For mutable shared data, explicit cache line alignment:

```fsharp
[<CacheLineAligned>]
type WorkerState = {
    mutable counter: int64
    mutable status: byte
}

// BAREWire layout (64 bytes, cache-line aligned)
Offset  Size  Member
------  ----  ------
0       8     counter
8       1     status
9       55    <padding to cache line boundary>
```

Each `WorkerState` instance starts on a cache line boundary and occupies an entire cache line.

### Rule 5: Array Element Padding

Arrays of `[<CacheLineAligned>]` types pad each element:

```fsharp
let workers: WorkerState[] = Array.zeroCreate 8

// Memory layout
Address         Content
-------         -------
0x1000          WorkerState[0] (64 bytes)
0x1040          WorkerState[1] (64 bytes)
0x1080          WorkerState[2] (64 bytes)
...
```

No two workers share a cache line.

## Compile-Time Analysis

### False Sharing Detection

BAREWire analyzes struct layouts for potential false sharing:

```
Analysis Input:
  - Struct definition with mutable fields
  - Target cache line size
  - Usage context (shared vs. thread-local)

Analysis Output:
  - Fields sharing cache lines
  - Potential concurrent access patterns
  - Suggested remediation
```

Example warning:

```
Warning FS9001: Potential false sharing in type 'SharedCounters'
  Fields 'counter_a' and 'counter_b' are both mutable and
  occupy the same cache line (offsets 0 and 8, line size 64).

  If these fields are accessed from different threads, consider:
  - Adding [<CacheLineAligned>] attribute
  - Separating into distinct types
  - Using per-thread arenas
```

### Cache Line Crossing Detection

Large structs may span cache line boundaries:

```fsharp
type LargeValue = {
    a: int64   // 8 bytes
    b: int64   // 8 bytes
    c: int64   // 8 bytes
    d: int64   // 8 bytes
    e: int64   // 8 bytes
    f: int64   // 8 bytes
    g: int64   // 8 bytes
    h: int64   // 8 bytes
    i: int64   // 8 bytes
}
// 72 bytes - spans 2 cache lines
```

BAREWire reports:

```
Info FS9002: Type 'LargeValue' (72 bytes) spans 2 cache lines.
  For hot-path access, consider splitting into cache-line-sized chunks
  or ensuring alignment with [<CacheLineAligned>].
```

## Arena Integration

### Per-Arena Cache Isolation

Arenas provide natural cache isolation:

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Arena A (Actor 1)                Arena B (Actor 2)                     │
│  ──────────────────                ──────────────────                   │
│  │ WorkerState  │                 │ WorkerState  │                     │
│  │ LocalBuffer  │                 │ LocalBuffer  │                     │
│  │ TempData     │                 │ TempData     │                     │
│  ──────────────────                ──────────────────                   │
│                                                                         │
│  Arenas are allocated with cache-line-aligned base addresses.           │
│  Inter-arena false sharing is structurally impossible.                  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Arena Allocation Alignment

Arena allocations respect cache alignment:

```fsharp
// Arena allocator respects cache line boundaries
let alloc<'T when 'T :> ICacheLineAligned> (arena: Arena) : Ptr<'T> =
    // Align to cache line boundary
    let alignedOffset = alignUp arena.CurrentOffset CacheLineSize
    arena.CurrentOffset <- alignedOffset + sizeof<'T>
    Ptr.ofOffset arena.Base alignedOffset
```

## Schema Annotations

BAREWire schemas can specify cache requirements:

```fsharp
let workerSchema =
    schema "Worker"
    |> withCacheConfig {
        CacheLineSize = 64
        FalseSharingAnalysis = true
    }
    |> withType "WorkerState" (
        struct' [
            field "counter" int64 |> mutable' |> hotPath
            field "status" byte |> mutable'
        ]
        |> cacheLineAligned
    )
```

### Schema Attributes

| Attribute | Effect |
|-----------|--------|
| `cacheLineAligned` | Pad type to cache line boundary |
| `hotPath` | Mark field for cache optimization analysis |
| `mutable'` | Mark field as mutable (triggers false sharing analysis) |
| `coldPath` | Exclude from cache optimization (rarely accessed) |

## Platform-Specific Behavior

### Target Triple Resolution

Cache line size is resolved from the target:

```fsharp
let getCacheLineSize (target: TargetTriple) =
    match target.Architecture, target.Vendor with
    | "aarch64", "apple" -> 128   // Apple Silicon
    | "aarch64", _ -> 64          // Other ARM64
    | "x86_64", _ -> 64           // x86-64
    | "arm", _ -> 32              // ARM Cortex-M
    | _ -> 64                     // Default
```

### Conditional Layout

Layouts can vary by platform when necessary:

```fsharp
// Same logical type, different physical layout
[<CacheLineAligned>]
type WorkerState = {
    mutable counter: int64
}

// On x86-64: 64 bytes (padded to 64-byte line)
// On Apple Silicon: 128 bytes (padded to 128-byte line)
```

## Integration with Verification Workflow

BAREWire layout information feeds into the verification workflow:

1. **Compile time**: BAREWire emits layout metadata (field offsets, padding, alignment)
2. **Debug info**: DWARF includes cache-relevant annotations
3. **Runtime**: `perf c2c` collects HITM events
4. **Analysis**: Events correlated with layout metadata
5. **Report**: Confirms or refutes compile-time predictions

See `~/repos/Firefly/docs/Verification_Workflow_Architecture.md` for the complete workflow.

## Examples

### Example 1: Naive Counter Array (False Sharing)

```fsharp
// BAD: Adjacent counters share cache lines
type NaiveCounters = {
    counters: int64[]  // Each counter is 8 bytes, 8 per cache line
}

let naiveCounters = { counters = Array.zeroCreate 8 }

// Thread 0 increments counters[0]
// Thread 1 increments counters[1]
// Both counters on same cache line → false sharing
```

### Example 2: Padded Counter Array (No False Sharing)

```fsharp
// GOOD: Each counter on its own cache line
[<CacheLineAligned>]
type PaddedCounter = {
    mutable value: int64
}

let paddedCounters = Array.init 8 (fun _ -> { value = 0L })

// Thread 0 increments paddedCounters[0].value
// Thread 1 increments paddedCounters[1].value
// Different cache lines → no false sharing
```

### Example 3: Arena-Isolated Counters (Structural Isolation)

```fsharp
// BEST: Each worker has its own arena
let spawnWorker () =
    Actor.spawn (fun () ->
        let arena = Arena.create 4096<bytes>
        let myCounter = Arena.alloc<int64> arena
        // myCounter is in a separate arena
        // No other actor can access this cache line
    )

Array.init 8 (fun _ -> spawnWorker ())
// 8 workers, 8 arenas, structural isolation
```

## Relationship to Other BAREWire Components

| Component | Cache-Aware Layouts Role |
|-----------|--------------------------|
| Memory Mapping | Layouts include alignment constraints |
| Schema System | Cache annotations in schema definitions |
| Hardware Descriptors | Peripheral regions are never cached |
| IPC Integration | Shared memory regions cache-line aligned |

## Implementation Status

| Feature | Status |
|---------|--------|
| Cache line size from target triple | Planned |
| `[<CacheLineAligned>]` attribute | Planned |
| False sharing analysis | Planned |
| Arena cache-line alignment | Planned |
| DWARF cache annotations | Planned |

## References

- Intel 64 and IA-32 Architectures Optimization Reference Manual, Chapter 8
- ARM Cortex-A Series Programmer's Guide, Cache chapter
- What Every Programmer Should Know About Memory (Ulrich Drepper)
- `perf c2c` documentation: https://man7.org/linux/man-pages/man1/perf-c2c.1.html
