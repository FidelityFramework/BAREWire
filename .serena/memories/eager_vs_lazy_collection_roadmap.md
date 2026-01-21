# BAREWire Eager vs Lazy Collections Roadmap

> **Purpose**: Architectural analysis of which BAREWire operations should use eager (List) vs lazy (Seq) collections.
> **Status**: Roadmap for future hybrid approach (Option 3)
> **Current Implementation**: List-first (Option 1)

## Executive Summary

BAREWire currently uses **List** operations exclusively, converted from original Seq usage for FNCS compatibility. This analysis identifies which operations genuinely benefit from lazy evaluation vs those that are better served by eager collections.

**Finding**: Most BAREWire operations work on **small, bounded metadata collections** (schema types, fields, union cases). Lazy evaluation overhead is not justified for these. However, future **streaming** use cases could benefit from Seq.

## Layer Analysis

### 1. Schema Layer (Validation.fs, Analysis.fs) → **EAGER (List)**

**Current operations:**
```
List.rev       - path reversal (small, bounded)
List.exists    - condition check (short-circuit but small data)
List.append    - combining lists
List.collect   - flat map over fields/cases
List.isEmpty   - empty check
List.contains  - membership check
List.tryPick   - find first match
List.map       - transformation
List.fold      - aggregation
List.minBy     - finding minimum
List.max       - finding maximum
List.sumBy     - summing
List.forall    - check all satisfy
List.forall2   - pairwise check
Map.toList     - conversion
Map.keys       - key extraction
Map.values     - value extraction
Map.forall     - check all satisfy
```

**Why EAGER:**
- Collections are schema metadata (types, fields, cases)
- Size bounded by schema complexity (typically 10-100 items)
- Full materialization often needed anyway (e.g., `List.length`, `List.sumBy`)
- No streaming characteristics
- Lazy overhead (closures, state machines) not justified

**Exception - Short-circuit operations:**
- `List.exists`, `List.forall`, `List.tryPick` exhibit Seq-like behavior
- But on small collections, difference is negligible
- Keep as List for consistency

### 2. Encoding Layer (Encoder.fs, Decoder.fs) → **IMPERATIVE**

**Current operations:**
```
for...in loops over bytes
Manual Array.zeroCreate + indexed writes
while loops for ULEB128
```

**Why IMPERATIVE:**
- Byte-level streaming I/O
- Performance critical (hot path)
- No intermediate collections needed
- Already optimal - don't change

### 3. Future Streaming Use Cases → **LAZY (Seq)**

If BAREWire adds streaming capabilities, Seq would be appropriate for:

| Use Case | Why Seq |
|----------|---------|
| **Large file processing** | Don't load entire file into memory |
| **Streaming validation** | Validate incrementally, stop at first error |
| **Pipelined codec** | Chain encode/decode without intermediate buffers |
| **Network streaming** | Process packets as they arrive |

**Pattern for streaming:**
```fsharp
// Future: Streaming schema traversal
let rec streamTypes (schema: SchemaDefinition) : seq<string * SchemaType> =
    seq {
        for kvp in schema.Types do
            yield (kvp.Key, kvp.Value)
            yield! getNestedTypes kvp.Value
    }

// Current: Eager traversal (appropriate for small schemas)
let rec listTypes (schema: SchemaDefinition) : (string * SchemaType) list =
    schema.Types
    |> Map.toList
    |> List.collect (fun (name, typ) -> (name, typ) :: getNestedTypes typ)
```

## Specific Operation Recommendations

### Keep as List (Current Behavior)
| Operation | Reason |
|-----------|--------|
| `getReferencedTypes` | Recursive traversal, small output |
| `validateTypeInvariants` | Error collection, typically empty or small |
| `getTypeSize` / `getTypeAlignment` | Recursive metadata, bounded by schema |
| `checkCompatibility` | Pairwise comparison, small collections |
| All Map operations | Schema type maps are small |

### Consider Seq (Future Enhancement)
| Operation | When | Benefit |
|-----------|------|---------|
| `validate` cycle detection | Very deep schemas | Early termination |
| Error streaming | Many validation errors | Don't collect all before reporting |
| Schema diff | Large schema comparison | Stream differences |

## Operations Still Missing

Regardless of List vs Seq choice, these operations need implementation:

| Operation | Needed For | Priority |
|-----------|------------|----------|
| `tryPick` | Cycle detection in validation | HIGH |
| `contains` | Path membership check | HIGH |
| `minBy` | Union size calculation | MEDIUM |
| `max` | Alignment calculation | MEDIUM |

## Implementation Phases

### Phase 1: List-First (Current)
- ✅ All Baker List decompositions complete (9/9)
- ⬜ Add missing List operations: tryPick, contains, minBy, max
- ⬜ Remove array workarounds from BAREWire source

### Phase 2: Seq Operations (PRD-16 Completion)
- ✅ Seq witnesses complete (cold): map, filter, take, fold, collect
- ⬜ Add missing Seq operations: exists, append, tryPick
- ⬜ Test with Sample 16

### Phase 3: Hybrid (Future - When Streaming Needed)
- ⬜ Identify streaming use cases in BAREWire
- ⬜ Add Seq variants where lazy evaluation adds value
- ⬜ Profile to verify benefit

## Design Principle

> **Use List for bounded metadata, Seq for unbounded streams.**

BAREWire's current operations are **all** on bounded metadata:
- Schema types: bounded by schema definition
- Struct fields: bounded by struct definition
- Union cases: bounded by union definition
- Validation errors: bounded by schema size

Until BAREWire processes unbounded data streams, List is the correct choice.

## Related Memories
- `collection_machinery_architecture` - Two-tier collection model
- `prd13a_corecollections_progress` - List implementation status
- `prd16_seqoperations_progress` - Seq implementation status
- `architecture_status_january_2026` - FNCS intrinsic gaps
