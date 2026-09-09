# Dispatch Regions: What HelloWayland Contributes

Status: first spatial implementation, September 2026.
[`Platform/DispatchRegions.fs`](../src/Platform/DispatchRegions.fs) implements pure
checks of concrete dispatch descriptions and allocation-free byte guards. Its
.NET and JavaScript gates pass. A native Clef scalar-guard probe also passes.
Compiler extraction, preservation through lowering,
and publication/completion/retirement are separate integration obligations; a
successful spatial check does not establish them.

The first consumer is Ariel's synchronous CPU region mechanism for HelloWayland.
It needs bounded assignment and safe join/retirement without actor or mailbox
support. The common memory contract remains applicable as those surfaces arrive.

## The precedent and the evidence

[Platform Description](./11%20Platform%20Description.md) extracted general
declarations from HelloArty: physical endpoints, memory spaces, widths and
constraints, observed by emission and proof machinery. HelloWayland supplies the
next concrete case: a computation whose memory must remain valid while several
participants execute it.

Its [shadow-and-substrate account](../../HelloWayland/docs/shadow-and-substrate.md)
records a missing-input defect. CPU and GPU compiled the same pixel function,
but the GPU upload omitted the shadow candidate lists beyond its declared used
length. The common requirement is that a dispatched computation receives every
byte it may read. Its proposed multi-core CPU realization adds exclusive writes
and completion before reuse.

| Application fact | Reusable contract |
| --- | --- |
| Frame table, including indirect candidate lists | Complete input extent and validated read footprint |
| Mapped image band divided among workers | Slices of a backing allocation with disjoint write footprints |
| Pointer, table and count captured for each dispatch | Environment layout derived for the target representation |
| Unmap, resize or overwrite after rendering | Publication, completion and retirement tied to allocation lifetime |

Pixel format, shadow steps, four-row batches and Wayland calls remain application
facts. The contract can also describe independent array transforms and other
bounded maps; it makes no assumption that the consumer is a renderer.

## Reuse the existing vocabulary

| Existing surface | What it establishes | Additional connection needed |
| --- | --- | --- |
| `Memory.Region.contains` / `slice` | Spatial containment in a portable byte-array backing extent | Native allocation identity, aliases and ownership during dispatch |
| `Memory.View` and hardware layout validation | Field access and descriptor agreement with a target ABI | Captured environment agrees with the emitted worker entry |
| `Platform.MemorySpace`, `BufferSchema` | Declared capacity, access and lifetime categories | A particular runtime allocation and its acquisition/retirement events |
| `Platform.Contract`, `Obligations` | Named premises and existing declaration-derived checks | Dispatch-specific obligations derived from the actual graph |

`Region` records expose their backing array and may overlap. A valid slice alone
does not establish exclusive ownership. `BufferSchema.Lifetime` is a declared
category; it does not currently enforce a join. The existing static-storage
planner settles concrete pools, while dynamic dispatch requires evidence about
the actual allocation instance and its lifetime.

The proposed extension should describe these facts with the existing records
where possible. Its additional semantic inputs are:

- Backing allocation identity, extent, address-space relationship and lifetime.
- Relative byte slices, permitted access, and the iteration domain they serve.
- The complete input footprint, including reads reached through indices stored
  in another input region.
- The captured environment's declared layout and binding to the worker entry.
- The publication and retirement events to which the lifetime is attached.

The implemented spatial records below name allocation, slice, input, partition
and layout relationships. Runtime event relationships remain integration work.
Opaque OS handles and native pointers bind to declared regions at the platform
boundary. Independent address spaces require an explicit transport or mapping;
equal numeric addresses do not establish allocation identity or coherence.

## Implemented spatial surface

`DispatchRegion` carries a source reference and worker-entry reference alongside
the target `Hardware.AbiProfile`, iteration count, backing allocations, complete
inputs, partition footprints and both captured/worker `PeripheralLayout` values.
`DispatchRegions.validate` returns `DispatchVerdict`: `SpatiallyValid`, the
individual checks and failed findings, retaining the source and worker references
for a graph/evidence consumer. These are concrete checks of supplied metadata,
not solver verdicts or authority to publish a region. Worker references do not
perform string-based lookup or establish compiler reachability.

The records reuse hardware layout and access tags and platform lifetime categories:

- `DispatchAllocation` identifies one backing instance, its platform space name,
  byte extent, access rights and declared lifetime. Every alias of that instance
  must use the same canonical identity and relative offsets. An allocation name
  is a boundary premise; this validator cannot discover aliases of native pointers
  or verify that a mapping supplies the claimed initialized bytes.
- `ByteSlice` is a half-open interval relative to that identity. Invalid, negative,
  unbound and overflowing slices are rejected before endpoint arithmetic.
- `DispatchInput.Required` includes indirect reads as well as a direct prefix.
  `Supplied` can contain unordered, overlapping or adjacent slices, but its union
  must cover every required byte without holes. Worker reads must be covered by
  declared requirements. All supplied input is treated as immutable, including
  extra supplied bytes; overlapping writes through aliases are rejected.
- `DispatchPartition` relates a bounded iteration interval to its read/write
  slices. The intervals must cover the domain exactly once, and writes from
  distinct partitions cannot overlap. Empty work and empty partitions are valid;
  an empty partition cannot claim a nonempty footprint. Relating an actual index
  expression to these footprints is the producer/compiler's obligation.

The capture layouts must each validate against the selected ABI and match in
size, alignment, field identity, offsets, representations, counts, access rights
and bit-field declarations. Documentation strings have no representation role.
An equal-sized integer cannot silently replace a captured pointer.

The allocation-free functions `contains byteLength offset length`,
`disjoint offsetA lengthA offsetB lengthB`, and
`fitsByteLength maxByteLength count elementSize` are available to compiled execution
guards. They subtract or divide before adding or multiplying. `tryByteLength`
additionally checks the selected ABI and returns the exact product or `None`.
All byte counts and offsets use explicit signed `int64` across the hosts. The
current implementation supports 32-bit and 64-bit pointer profiles; 32-bit extents
cannot exceed `2^32 - 1`, and 64-bit extents cannot exceed `2^63 - 1`, the library's
nonnegative signed representation limit. These limits are implementation bounds,
not a language dimension rule. A relative span check does not settle an actual
native base address, base-plus-offset arithmetic, alignment or cross-space mapping.

[`tests/DispatchRegionTests.fs`](../tests/DispatchRegionTests.fs) exercises the
spatial description and signed boundary arithmetic through the normal .NET runner.
[`tests/js/dispatch.mjs`](../tests/js/dispatch.mjs) runs the same boundary classes
against fresh Fable output, using exact `BigInt` inputs, including a one-byte input
hole near the signed 64-bit limit. Run it with
`node tests/js/dispatch.mjs <fable-output-directory>` after the normal Fable build.
Both hosted gates passed on 2026-09-09; the full .NET runner reported 562 checks.
Registering this source in the Clef manifest does not by itself establish native
execution of every validator branch.

Current Clef intentionally uses `int` with range/platform settlement rather than
F# representation aliases and numeric suffixes. The small
[`DispatchRegions.Clef.fs`](../src/Platform/DispatchRegions.Clef.fs) projection
therefore selects Clef `int` and unsuffixed literals for the same scalar equations;
[`tests/dispatch_projection.py`](../tests/dispatch_projection.py) verifies exact
correspondence with the hosted implementation. The hosted source retains its
explicit `int64` precision. Native clients select
[`BAREWire.DispatchCore.fidproj`](../src/BAREWire.DispatchCore.fidproj), while
[`BAREWire.Dispatch.fidproj`](../src/BAREWire.Dispatch.fidproj) includes the full
metadata validator source. These alternative manifests must not both supply the
same module to one native compilation.

The native [HelloWayland bounds probe](../../HelloWayland/tests/ariel-typed/Bounds.clef)
compiled and returned zero on Linux x86_64 on 2026-09-09, including signed-limit
containment, multiplication overflow, zero element size and overlap rejection.
This is evidence for the scalar projection, not native execution of the graph
validator or a compiler proof about an arbitrary dispatch.

## Spatial obligations

For a map over `[0, n)`, each admitted partition has `0 <= lo <= hi <= n`.
Partitions cover the domain exactly once. Every emitted access maps to a valid
byte slice of its backing allocation, and writes from distinct partitions are
disjoint. Shared reads require the backing input to remain unchanged for the
region's duration, including through aliases outside the worker function.

The compiler must establish count-to-byte conversions and address arithmetic
against the selected representation. A proof over mathematical offsets does not
authorize an overflowing native multiplication. Dynamic extents need validated
runtime inputs or generated guards where static facts cannot settle them.

The environment carries the target's actual array/pointer representation. A
table reference must retain the extent evidence needed by its reads; treating an
array descriptor as an assumed single pointer loses that connection. Field
offsets and padding come from layout settlement and are consumed by emission.

Row boundaries are one possible partition policy. Avoiding false sharing also
requires base alignment, stride and cache-line facts; spatially disjoint writes
establish race separation even when they occupy the same cache line.

## Temporal obligations

The intended lifecycle is:

1. Acquire backing storage, validate extents, and initialize the input/environment.
2. Admit the region and publish its initialized state to its participants.
3. Assign each partition once; keep the input immutable and output slices exclusive.
4. Observe completion and acquire the participants' writes before consuming output.
5. Retire the dispatch only after every participant relinquishes its captures
   and bookkeeping. Release its storage use at that point. Unmap, reclamation,
   resize or mutation must also respect any downstream consumer's outstanding
   use and the platform mapping contract.

Completed output and retired dispatch state are separate facts. A remaining-work
counter can reach zero before a worker finishes accessing its counters. Failure
also needs retirement: partial admission, a worker fault or rejected work must
not release a frame still in use. A failed output is not presented as complete.
Reused dispatch storage needs a generation or equivalent identity discipline so
that a delayed completion cannot retire a later use of the same storage.

BAREWire supplies the layout and boundary-contract vocabulary. CCS carries the
allocation, access and lifetime relationships in the PSG. Ariel supplies the
publication, dispatch, completion and retirement mechanism, under the platform's
declared synchronization assumptions. A pure layout validator cannot establish
that runtime event order by itself.
Neither a transport's `Ordered` flag nor volatile access supplies that
synchronization contract.

Ariel's scheduler requirements remain in the
[scheduler contract](../../clef-lang-spec/spec/scheduler-contract.md). This memory
contract supplies premises to that integration and does not redefine actor turns,
supervision authority, fairness or admission.

## Evidence and lowering

Following HelloArty's pattern, one settled description feeds emission, constraints
and obligations. A compiler-derived dispatch record links source nodes, backing
regions, footprints, environment layout and synchronization premises. Compiler
and native-artifact checks must agree with that record; an editor reads its
results without manufacturing obligations.

Fixed-stride affine bounds can use integer arithmetic obligations; finite-width
address arithmetic can use bit-vector obligations where appropriate. The
generator selects a supported fragment from the actual expressions. Lifecycle
and scheduler properties additionally require transition reasoning and named
substrate premises. Declaring a contract proven is not a substitute for evidence
that its implementation satisfies it.

Source identities and proof metadata remain compiler/evidence artifacts. Final
payloads remain untagged with respect to them. Counts, pointers or generation
state required by execution have an explicit operational purpose and layout.

## Acceptance evidence

The spatial implementation rejects overlapping output slices, truncated indirect
input, overflowed byte extents and mismatched environment layouts. The lifecycle
harness must additionally exercise delayed participants, zero work,
partial admission, failure and attempted reuse before retirement. Successful
completion must cover every requested element exactly once.

HelloWayland supplies the integration oracle: identical captured inputs produce
identical output bytes in serial and multi-core CPU execution. A regression based
on the omitted shadow lists must fail the extent contract before dispatch.
Performance measurements are separate from these correctness checks.

Implementation order and repository ownership are recorded in
[Composer's multi-core CPU plan](../../Composer/docs/multi-core-cpu.md); the
[HelloWayland note](../../HelloWayland/docs/multi-core-cpu.md) applies it to the
frame loop. Sibling-repository links assume the usual workspace layout.
