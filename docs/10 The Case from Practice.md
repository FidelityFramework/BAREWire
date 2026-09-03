# The Case from Practice

[Substrate_Formalism](./Substrate_Formalism.md) states what BAREWire *is*: a
formalizable substrate whose trusted computing base can be articulated, upholding
a typed contract across a boundary rather than describing a memory layout. That
note argues the position from first principles.

This note argues it from evidence. Two projects in the framework crossed
boundaries without BAREWire and had to hand-roll what it would have provided.
Both succeeded. Both are instructive precisely *because* they succeeded — the
hand-rolling did not fail loudly, it failed in a characteristic and repeatable
way, and reading how it failed says what the formalism is for.

## The claim

> Dimensional types do not merely *benefit* from a boundary formalism. They
> **demand** one. A type system that carries width, residency, unit, or grade
> through compilation has nothing to say the moment a value crosses a process,
> a processor, or an ABI — unless something upholds the contract on the far
> side. Absent that, every crossing is hand-rolled, and the annotations stop at
> the boundary.

The corollary is what makes the role look small today and large tomorrow. When
an interface is simple, hand-rolling is cheap and the loss is invisible: a
framebuffer is bytes, and bytes survive anything. **The cost scales with how
much the type was carrying.** As interfaces acquire properties — resolved
widths, memory residency, blade support, quire accumulators — the amount lost at
each crossing grows, and a role that looks tangential becomes pervasive.

## Evidence 1 — Farscape, where layout knowledge is lost at the exact point it is known

Farscape parses C and C++ headers to generate Clef bindings. It is, by
construction, the place in the framework where struct layout is *known*: the
declarations are right there. It is also where that knowledge is currently
discarded.

`Farscape/docs/14_Binding_Generation_Gaps.md` records what the generator does
with a non-trivial declaration:

- a union member is emitted as `handle: union` — a placeholder type that does
  not exist and does not type-check;
- a fixed array `reserved[16]` is flattened to a single scalar;
- bitfields and nested arrays are discarded;
- and the deepest issue, which is not a generator bug at all: **a Clef record
  cannot overlay a C struct.** Records are memref-backed, so a record passed to
  a C callee arrives as the address of a slot rather than as the struct. A
  binding can satisfy every idiom rule in the boundary spec and still be wrong
  at the ABI.

The last point is the one that matters here. It is not fixable by generating
better records, because the representation is the problem. What is needed is a
*layout description* that both sides agree on, separate from the language's own
representation choices — which is what a schema is, and what BAREWire's
`StructDescriptor` was introduced for: "ioctl args, shared-memory structures,
DMA descriptors… used by Farscape to validate memory layout contracts across
heterogeneous processors."

That type exists in `src/Hardware/Descriptors.fs` today. It has a constructor
and no validator and no consumer. **The gap is not conceptual; it is that
nothing yet checks the contract the descriptor describes.**

## Evidence 2 — HelloWayland, where the formalism is reimplemented by hand and says so

HelloWayland renders a 3D glyph from one tracer compiled to both a CPU loop and
a gfx1151 code object, presenting through DMA-BUF. Its framebuffer path is
genuinely zero-copy: GBM allocates, `gbm_bo_get_fd` exports a DMA-BUF, and both
Wayland and HIP import the same descriptor. On a unified-memory APU the handoff
really is a pointer, and no library is needed to make it one.

The interesting part is what it took to *get* to that pointer. To import the
buffer, HIP's `hipExternalMemoryHandleDesc` has to be constructed exactly. The
generated binding could not express it, so the descriptor is hand-laid-out in
`src/Gpu/Fill.clef` — a record whose fields include `KindPad`, `FdPad`,
`UnionTail`, and `Rsv0` through `Rsv7`, with the C union written as its widest
member so the following fields land where C puts them.

The comment above it is the entire argument for this document, written by
someone who had no alternative:

> These are what corrected Farscape output would look like; the eventual home is
> a BAREWire schema, whose validated field offsets would replace the explicit
> padding below.

That is a hand-rolled schema, annotated as a hand-rolled schema, with a forward
reference to the thing that should own it. The code works. It is also
unverifiable: nothing checks that `UnionTail` is the right width on the next
ROCm release, and the failure mode is a silently misread field.

## Evidence 3 — the same error, one scale down

The two cases above are about layout across an ABI. The third is about
dimension within a single process, and it is the same error.

HelloWayland's depth test spent a long time adding a logo-space quantity to a
screen-space one. Both were `int`. They differ by a scale factor and a
perspective divide, so every sphere's pole-to-rim depth came out 3.36× too
large, and the intersection curve between two surfaces landed in the wrong
place. The bug was invisible to the type system and expensive to find by eye.

Nothing about that error is peculiar to being inside one process. **A unit
confusion within a process and a wire-format mismatch across one are the same
failure at different scales** — a quantity interpreted under the wrong
convention because the convention was carried in a name rather than a type.

This is why the patent's claim is one system and method with three phases of
implementation rather than three inventions. Memory mapping, IPC, and network
transport are the same problem at three scales, and the blog note in
`Clef_migration` says so in as many words: *"not three separate problems but one
problem at three scales."* The formalism that fixes any one of them is the
formalism that fixes all three.

## Why the role grows

Today's boundaries in this framework are mostly simple, and simple boundaries
hide the cost:

| interface | what crosses | what is lost by hand-rolling |
| --- | --- | --- |
| framebuffer | bytes | nothing — bytes are bytes |
| HIP descriptor | a fixed C struct | correctness under version drift |
| FPGA port | a value of resolved width | the width, which was inferred and is not recoverable |
| UMA buffer | a pointer plus residency | which agent the memory is optimal for |
| multivector | a blade support | the grade structure, hence composability |
| quire | an exact accumulator | the exactness, which was the point |

The first row is why BAREWire looks optional right now. The rest are why it
will not stay optional. Each new property the type system learns to carry is a
property that must survive a crossing, and **none of them survive `void*`.**

The framework is already acquiring those rows. Width inference is proven on
silicon in HelloArty. Residency hints are the subject of the unified-memory
work. Grade discipline is specified. Posit and quire are in the numeric
roadmap. Every one of them arrives at a boundary eventually.

## What this implies for BAREWire's design

Three things follow, and none of them are implementation tasks.

**1. The schema is downstream of the dimensional contract, not upstream of the
encoding.** BAREWire's documents mostly describe an encoding with a schema over
it. The reading that matches the evidence is the reverse: the contract is the
artifact, and BARE is how it is serialized when serialization is what the
boundary needs. Across a UMA pointer handoff nothing is serialized at all, and
the contract still has to hold. Substrate_Formalism already says this; the
document set has not caught up to it.

**2. The contract must be expressible in Fixed dimensions.** This is not settled
and the spec says so. `clef-lang-spec/spec/ntu-dimensional-architecture.md` §7.3
asks how CCS verifies that both sides of a BAREWire contract are dimensionally
consistent, and observes that layouts likely need **Fixed** dimensions —
concrete widths and alignments — rather than **Resolved** ones, because the two
sides may resolve the same dimension differently. `grade-discipline.md` raises
the identical question for blade support at a process boundary and notes the
resolution is probably the same. **Until §7.3 closes, a schema cannot mean
anything across a boundary**, and no amount of implementation fixes that.

**3. Validation is the missing half, not mapping.** The repository contains the
encoding and schema halves. The mapping half — Region and View, specified at
length in [04 Memory Mapping](./04%20Memory%20Mapping.md) — is design only. But
the evidence above suggests the *first* thing worth building is neither: it is a
validator that can take a `StructDescriptor` and an ABI and answer whether they
agree. Farscape needs that to emit correct bindings; HelloWayland needs it to
stop hand-padding; and it is useful before Region/View exists.

## Where it stops being optional: ThreeBody

Everything above is retrospective. The forward case is
[ThreeBody](https://github.com/speakeztech/threebody), and it is the point at
which the three phases stop being three scales of one analogy and become one
system with all three present at once.

ThreeBody runs a gravitational simulation whose physics decomposes by distance
regime, each regime mapped to the processor that suits it: close encounters on
an FPGA in b-posit arithmetic, the medium field on the GPU, the far field on an
NPU, orchestration on the CPU — all from one Clef source. Its README already
names BAREWire as the protocol connecting the actors and the FPGA sidecar.

The FPGA link is **Layer 2 Ethernet over RJ45** — an Arty A7-100T beside a
Strix Halo host — bridged on the host by an XDP program into AF_XDP rings
(`ThreeBody/docs/fpga-transport.md` records why: the board's USB port fronts an
FTDI UART whose latency timer sets a 1 ms floor). What that asks of this
library is in [11](./11%20Platform%20Description.md) §"The kernel as a
described platform". With that transport, a single demo spans:

| phase | boundary in ThreeBody |
| --- | --- |
| memory mapping | CPU / GPU / NPU sharing the unified pool, as HelloWayland does today |
| IPC | the eBPF boundary — a genuine privilege and address-space crossing |
| wire | raw Layer 2 frames to the FPGA — no IP stack in the path |

One patent, three phases, one machine. That is a considerably better
demonstration than three separate systems each showing one scale.

The transport choice also sharpens what the contract is *for*. Latency
dominates: a close-encounter regime hands work to the FPGA and needs the answer
back inside a timestep, so round-trip cost matters more than bandwidth. Raw
Layer 2 with no IP stack is the lowest-latency path available short of PCIe.
**A contract that costs a serialization pass on every crossing is not viable at
that budget** — which is precisely why the typed contract reading matters more
than the encoding one. What has to survive the wire is meaning, and the cheapest
encoding that preserves it wins.

### Why this is the acid test for §7.3

The open question in
`ntu-dimensional-architecture.md` §7.3 — whether boundary layouts must be
expressed in **Fixed** rather than **Resolved** dimensions, because two
endpoints may resolve the same dimension differently — is abstract right up
until this demo, and then it is the whole problem.

**Post width inference, the FPGA endpoint's widths are not the host's.**
HelloArty already demonstrates inference narrowing values to 31, 20, 11 and 13
bits and lowering them through Vivado. The host side is x86-64, where the same
quantity is a byte-addressed machine integer. Two endpoints, the same
dimension, different resolutions, and a wire between them. §7.3 is not a
philosophical question in that system; it is the frame format.

The b-posit side sharpens it further. `Quire64` is 32×uint64 — an exact
accumulator whose entire purpose is that it does not lose information. Crossing
it as bytes and trusting the far side to reassemble it correctly forfeits the
one property it exists to provide. This is the "quire" row of the table above,
in a real system rather than as an illustration.

### And a genuinely new intersection

eBPF carries its own memory-safety discipline: programs are checked by an
in-kernel verifier before they are permitted to run. So the crossing is not
merely between two type systems but between **two verification regimes** —
Clef's dimensional guarantees and PSG/MLIR-SMT verifications on one side, the eBPF verifier's on the other,
with BAREWire's contract as the thing that must compose with both.

That is where "how memory safety is conceptually framed and mechanically
enacted" gets an answer with teeth. A typed contract that survives a kernel
verifier and an FPGA fabric is a much stronger claim than one that survives a
memcpy — and it is the claim the substrate reading in
[Substrate_Formalism](./Substrate_Formalism.md) has always implied: *a boundary
is a boundary*, and a privilege boundary is no different in kind from a
hardware one, provided the TCB on each side is articulated.

## What this note does not claim

It does not claim BAREWire would have made either project faster. HelloWayland's
framebuffer path is already optimal and would be identical. Farscape's bindings
would still need the representation fix, because a validator that reports
disagreement does not by itself generate a correct binding.

The claim is narrower and more durable: **in both projects the contract was
written down by hand, in a comment, and then not checked.** The formalism's job
is to make that contract a checkable artifact. That job does not depend on the
boundary being slow, or remote, or serialized. It depends only on there being
two sides that must agree.

## See also

- [Substrate_Formalism](./Substrate_Formalism.md) — the position argued from
  first principles; this note is its evidence.
- [08 Hardware Descriptors](./08%20Hardware%20Descriptors.md) — the descriptor
  types, including `StructDescriptor`. Status: design.
- [04 Memory Mapping](./04%20Memory%20Mapping.md) — Region and View. Status: design.
- `Farscape/docs/14_Binding_Generation_Gaps.md` — the binding-generation
  evidence, including the records-cannot-overlay-C-structs finding.
- `clef-lang-spec/spec/ntu-dimensional-architecture.md` §7.3 — the open contract
  question.
