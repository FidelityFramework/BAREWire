# Cache-Aware Layouts

> **Status (2026-09-03).** Natural alignment (Rules 2 and 3) is built: `src/Hardware/Abi.fs` carries each ABI's alignment facts and `Validator.derive` lays a struct out by them, padding included, which is the layout a `View` reads through. Cache-line size, the `[<CacheLineAligned>]` attribute, false-sharing analysis, and the DWARF annotations remain design; the natural home for the cache-line fact is a field on `AbiProfile` beside `MaxAlign`, resolved from the target the way the profile's other facts are.

BAREWire's deterministic layout system supplies inputs for compile-time analysis of cache behavior. This document specifies the layout, allocation and target evidence needed to reduce avoidable cache traffic. Known offsets alone do not establish cache residency or a performance improvement.

## Architectural Context

BAREWire controls declared memory layout at compile time. Those facts constrain Fidelity's cache analysis:

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

Because BAREWire determines layout before code generation, the compiler can analyze conditional footprints and separation requirements. Allocation addresses, access order, target topology and competing traffic determine how those layouts behave at runtime. Foreign ABI and wire layouts remain binding constraints on any transformation.

## Cache Line Fundamentals

### Hardware Cache Lines

Cached processors transfer and coordinate data at hardware-defined granularities. The platform description must supply the relevant line size and sharing domains for the selected CPU and deployment. An architecture name or target triple is insufficient to infer the entire hierarchy; some supported targets have no data cache. The examples below explicitly assume 64-byte lines unless stated otherwise.

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
    /// Supplied by the selected CPU/platform contract; no universal default
    CacheLineSize: int

    /// Minimum alignment for cache-sensitive types
    CacheAlignment: int

    /// Enable automatic padding analysis
    FalseSharingAnalysis: bool
}
```

The analysis resolves this from the selected CPU/platform contract or a supported runtime discovery contract. Unknown geometry remains unknown: it cannot support a cache-separation guarantee. Natural ABI alignment is a separate fact.

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

// BAREWire layout (24 bytes total, 8-byte aligned)
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

False-sharing analysis combines struct layouts with allocation and access evidence:

```
Analysis Input:
  - Struct definition with mutable fields
  - Target cache line size
  - Allocation base alignment, extents and array stride
  - Usage context (shared vs. thread-local)

Analysis Output:
  - Fields sharing cache lines
  - Potential concurrent access patterns
  - Suggested remediation
```

An informational report can expose a conditional risk without claiming a measured slowdown. The following is report wording, not a reserved diagnostic identifier:

```
Info: Fields 'counter_a' and 'counter_b' occupy the same line
  for this 64-byte-aligned allocation (offsets 0 and 8, line size 64).
  If different cores access them concurrently and at least one writes,
  this placement can cause false sharing.

  Separate the independently written fields at the declared line granularity
  if the access pattern warrants the added space. Aligning the whole record
  alone does not separate fields within it.
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
// 72 bytes - spans 2 lines with a 64-byte-aligned base
```

An appropriate layout report states the allocation assumption:

```
Info: Type 'LargeValue' (72 bytes) spans 2 lines at a 64-byte-aligned base.
  This describes the layout footprint, not measured traffic or residency.
  Splitting the value is useful only if its access pattern and ABI permit it.
```

## Arena Integration

### Per-Arena Cache Isolation

Arenas establish ownership regions. Physical separation additionally requires aligned bases and extents rounded to the relevant line granularity, maintained through allocation reuse:

```
┌─────────────────────────────────────────────────────────────────────────┐
│  Arena A (Actor 1)                Arena B (Actor 2)                     │
│  ──────────────────                ──────────────────                   │
│  │ WorkerState  │                 │ WorkerState  │                     │
│  │ LocalBuffer  │                 │ LocalBuffer  │                     │
│  │ TempData     │                 │ TempData     │                     │
│  ──────────────────                ──────────────────                   │
│                                                                         │
│  Bases and extents must satisfy the declared line separation.           │
│  Queues and allocator metadata require separate analysis.               │
└─────────────────────────────────────────────────────────────────────────┘
```

### Arena Allocation Alignment

The allocation policy must honor alignment and rounded extent together. Aligning an offset is sufficient only when the arena base satisfies the same alignment. Bounds and overflow checks remain allocator obligations:

```fsharp
// Arena allocator respects cache line boundaries
let alloc<'T when 'T :> ICacheLineAligned> (arena: Arena) : Ptr<'T> =
    // Align to cache line boundary
    let alignedOffset = alignUp arena.CurrentOffset CacheLineSize
    let reservedBytes = alignUp sizeof<'T> CacheLineSize
    arena.CurrentOffset <- alignedOffset + reservedBytes
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

### Target Fact Resolution

The target triple selects broad architecture and ABI requirements. The CPU/platform description supplies cache geometry, relevant coherence granularity and sharing domains. A runtime-discovered geometry requires an allocation strategy valid for the discovered value. A missing fact must not silently become a 64-byte assumption. Reports name the target and the provenance of each fact; changing the target invalidates dependent placement conclusions.

### Conditional Layout

Layouts can vary by platform when necessary:

```fsharp
// Same logical type, different physical layout
[<CacheLineAligned>]
type WorkerState = {
    mutable counter: int64
}

// With a declared 64-byte separation: 64-byte extent and alignment
// With a declared 128-byte separation: 128-byte extent and alignment
```

## Integration with Verification Workflow

BAREWire layout information feeds into the verification workflow:

1. **Compile time**: BAREWire emits layout metadata (field offsets, padding, alignment)
2. **Debug info**: DWARF includes cache-relevant annotations
3. **Runtime**: `perf c2c` collects HITM events
4. **Analysis**: Events correlated with layout metadata
5. **Report**: Tests predictions for the recorded workload and target. HITM can identify contention from true or false sharing; it is not independently a false-sharing proof.

See the [language's evaluation contract](../../clef-lang-spec/spec/expressions.md#default-demand-and-sharing) and the [CPU cache analysis](../../clef-lang-site/hugo/content/docs/internals/hardware/cache-aware-compilation-cpu.md). Default information explains settled facts. Optional performance advisories state assumptions and space/traffic tradeoffs; correctness and mandatory placement violations retain their owning diagnostics.

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
// Separate ownership, subject to the arena's physical placement contract
let spawnWorker () =
    Actor.spawn (fun () ->
        let arena = Arena.create 4096<bytes>
        let myCounter = Arena.alloc<int64> arena
        // myCounter is in a separate arena
        // Line separation additionally needs aligned arena bases and extents
    )

Array.init 8 (fun _ -> spawnWorker ())
// 8 workers, 8 owned arenas; queues and allocation metadata still coordinate
```

## Relationship to Other BAREWire Components

| Component | Cache-Aware Layouts Role |
|-----------|--------------------------|
| Memory Mapping | Layouts include alignment constraints |
| Schema System | Cache annotations in schema definitions |
| Hardware Descriptors | Device memory attributes come from the platform's access contract |
| IPC Integration | Shared memory regions cache-line aligned |

## Implementation Status

| Feature | Status |
|---------|--------|
| Cache geometry from CPU/platform facts | Planned |
| `[<CacheLineAligned>]` attribute | Planned |
| False sharing analysis | Planned |
| Arena cache-line alignment | Planned |
| DWARF cache annotations | Planned |

## References

- Intel 64 and IA-32 Architectures Optimization Reference Manual, Chapter 8
- ARM Cortex-A Series Programmer's Guide, Cache chapter
- What Every Programmer Should Know About Memory (Ulrich Drepper)
- `perf c2c` documentation: https://man7.org/linux/man-pages/man1/perf-c2c.1.html
