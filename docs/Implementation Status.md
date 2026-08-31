# Implementation Status

One ledger, so the documents in this folder are not mistaken for a description
of the repository. Most of them are **design specifications**, and several
specify modules that have never existed. That is legitimate — the designs are
the useful output of the work so far — but it needs saying in one place.

Assessed 2026-08-04 against `src/` and `src/BAREWire.fidproj`.

## Ledger

| Area | Document | State |
|---|---|---|
| Substrate reading | [Substrate_Formalism](./Substrate_Formalism.md) | Position. Current and load-bearing. |
| Platform description | [11 Platform Description](./11%20Platform%20Description.md) | Position. Current (2026-08-31): the inward reading — declared layout authority for all processors, generalized from the FPGA path. Direction, not code. |
| Evidence for the role | [10 The Case from Practice](./10%20The%20Case%20from%20Practice.md) | Position. Current. |
| Encoding | [02 Encoding and Decoding Engine](./02%20Encoding%20and%20Decoding%20Engine.md) | **Source exists** — `Encoding/{Memory,Encoder,Decoder,Codec}.fs`. Unbuilt (see below). |
| Schema | [03 Schema System](./03%20Schema%20System.md) | **Source exists** — `Schema/{Definition,Validation,Analysis,DSL}.fs`. Unbuilt. |
| Hardware descriptors | [08 Hardware Descriptors](./08%20Hardware%20Descriptors.md) | Types exist in `Hardware/Descriptors.fs`; document says PLANNED and that is accurate — there is a `create`, no validator, no consumer. |
| Memory mapping | [04 Memory Mapping](./04%20Memory%20Mapping.md) | **Design only.** No `Memory/` directory. Region and View do not exist. |
| Network protocol | [05 Network Protocol](./05%20Network%20Protocol.md) | **Design only.** No `Network/` directory. |
| IPC | [06](./06%20IPC%20Integration.md), [07](./07%20IPC%20Platform%20Specific%20APIs.md) | **Design only.** No `IPC/` directory. |
| Cache-aware layouts | [09 Cache-Aware Layouts](./09%20Cache-Aware%20Layouts.md) | **Design only.** |
| Arena | [Arena_Design](./Arena_Design.md) | **Implemented elsewhere** — elevated to a compiler intrinsic. This document is the authoritative design reference; the code is not here. |
| Tier modules (HSA / CXL / RDMA) | described on the language site | **Design only**, and described there as planned. |

## Blockers, in the order they bind

**1. The project does not build.** `src/BAREWire.fidproj` has no
`[compilation] target`, so Composer rejects it before reading any source — both
the pinned March build and HEAD. Its `[dependencies]` section is comments only
while stating that `NTUKind` comes from **FNCS**, the F# Native Compiler
Service, which the framework has moved off. Names throughout the repository
still refer to **Firefly** (now Composer) and FNCS (now Clef Compiler Services).

**2. The contract it must satisfy is an open question.**
`clef-lang-spec/spec/ntu-dimensional-architecture.md` §7.3 asks how CCS verifies
that both sides of a BAREWire contract are dimensionally consistent, and
observes that layouts likely need **Fixed** dimensions rather than **Resolved**
ones, because two endpoints may resolve the same dimension differently.
`grade-discipline.md` raises the identical question for multivector blade
support and notes the resolution is probably the same. **A schema cannot mean
anything across a boundary until this closes**, and no amount of implementation
substitutes for closing it.

**3. Its safety mechanism is downstream of measures — resolved at the type-theoretic level, open at the source level.** The README's claim is compile-time memory safety via units of measure, and the sources still carry it as `FSharp.UMX` phantom types. The type-theoretic question is settled: there is no UMX library and no UMX-extended on the native path — the measure discipline is wholly the native type universe's dimensional structure (Kennedy's frame as the type system itself; `clef-lang-spec/spec/native-type-universe.md`, `ntu-dimensional-architecture.md`). What remains is migrating the sources off UMX idioms onto NTU measures, which folds into the Clef migration of blocker 1. BAREWire rides the NTU; it no longer waits on a survival question.

**4. The test suite is orphaned.** `tests/BAREWire.Tests.fsproj` references
`..\src\BAREWire.fsproj`, which does not exist — only `BAREWire.fidproj` does —
and its test files target `src/Core/*`, a directory removed during the
compiler-services migration. The suite cannot build and has not been exercised
against the current tree.

## What is worth building first

Not Region and View, despite being the largest specified gap.

The [case from practice](./10%20The%20Case%20from%20Practice.md) points at a
**validator for `StructDescriptor`**: something that takes a descriptor and a
target ABI and answers whether they agree. Farscape needs it to stop emitting
bindings that type-check and are wrong at the ABI; HelloWayland needs it to stop
hand-padding HIP descriptors. It is useful before Region/View exists, it is
small, and it exercises the contract question in §7.3 on a concrete case rather
than in the abstract.

## Sequencing

```text
measures proven on the native path
        │
        ▼
   §7.3 closed  (Fixed vs Resolved for boundary layouts)
        │
        ▼
   StructDescriptor validator   ◄── first real code; unblocks Farscape
        │
        ▼
   Region / View  (04)          ◄── the mapping half
        │
        ▼
   tier modules  (HSA / CXL / RDMA)
```

Rescaffolding the build (blocker 1) is independent and can happen at any point;
it just should not be mistaken for progress on the other three.
