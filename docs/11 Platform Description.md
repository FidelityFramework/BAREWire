# The Platform Description: What the FPGA Forced, Generalized

This note records the second position the documentation set rests on, alongside [Substrate_Formalism](./Substrate_Formalism.md). That note reads BAREWire *outward*: a formalizable substrate upholding a typed contract across any boundary. This one reads it *inward*: BAREWire as the declared authority on memory layout for every processor a program runs on — including the process's own address space, which is a boundary with the linker and loader that the framework has historically crossed by convention rather than by contract.

Status: position and direction, not implementation. It is grounded in artifacts that exist and are named below.

## The forcing function

The FPGA was the platform that could not be lied to. On the Arty there is no operating system, no linker convention, no libc habit: every pin, clock, BRAM region, port and width had to be declared or nothing synthesized. The Fidelity.Platform FPGA articulation and HelloArty were therefore *forced* to build what the CPU path never had to — a total, declarative platform description — and those artifacts are the guidance for everything below:

- **The pin map.** `[<Pin>]`/`[<Pins>]` attributes on record fields resolve to physical `PinEndpoint`s from the platform bindings; the native type universe carries a `FieldPinAttributes` slot on every type constructor, so the declaration travels *in the type*.
- **The platform coeffect.** Composer pre-computes `PlatformPinMapping` — `PinConstraint`, `ClockConstraint`, `ResetConstraint`, the device part — once, before emission (`Composer/src/MiddleEnd/PSGElaboration/Coeffects.fs`). Its own comment states the architectural pattern this note generalizes: *"Two observers, one truth, two residuals"* — hardware-module emission and XDC generation both observing one declaration.
- **Constraint emission.** `XDCTransfer` turns the coeffect into the XDC file that *directs* the downstream tools. The declaration is not audited after the fact; it is enforced forward.
- **Width inference.** `IntervalAnalysis` narrows values to inferred widths (HelloArty: 31, 20, 11, 13 bits, proven through Vivado to silicon) — the platform description includes properties the type system derived, not only properties a human wrote.

The lesson is that **CPUs have every one of those facts too; they are merely hidden behind convention.** The ELF layout hides behind the linker's defaults, the stack behind the ABI, buffer capacities behind libc habits, the syscall surface behind a table of magic numbers. What BAREWire should carry for all processors is the FPGA's honesty, generalized.

## The correspondence

| HelloArty / FPGA (exists) | The generalization (direction) |
| --- | --- |
| Pin map: `[<Pin>]` → `PinEndpoint` → XDC | Syscall/ABI surface as descriptors: conventions and *contracts* ("read writes at most `count` bytes") declared, not ambient |
| Peripheral Descriptors (BRAM, memory-mapped hardware) | The process memory map: rodata, stack, arena regions with declared extents, permissions, alignment and padding policy |
| `ClockConstraint` / `ResetConstraint` | The loader/linker contract: page permissions, section placement, entry conventions |
| Width inference | Representation selection — already shared machinery, already per-target |
| Device part (`xc7a100tcsg324-1`) | The CPU/OS/arch descriptor that the Fidelity.Platform per-target *folder tree* currently is by convention |
| XDC emitted from the coeffect | The linker script / memory-map manifest emitted from the declaration |

The last row is the deepest one. Today the CPU path inverts the FPGA's discipline: the linker chooses and the toolchain audits afterward. The generalization gives the CPU its XDC — the compiler emits layout constraints *from* the BAREWire declaration — so layout becomes **declared, directed, then confirmed**, with the confirmation (ELF cross-check, and the Rocq observer outside the compilation accounting) closing a loop the framework opened on purpose.

## Three observers, one truth

The FPGA path has two observers of the platform declaration: code emission and constraint emission. The generalization adds the third: **the proof-obligation coeffect**. One BAREWire declaration, observed by

1. emission (Alex witnesses the declared layout into MLIR),
2. constraint generation (XDC on FPGA; linker/memory-map manifest on CPU),
3. obligation generation (Tier 2 proofs stated *against the declaration*).

The evidence that the third observer needs the declaration is concrete and recent. The HelloProof exercise (ship-of-theseus repository, 2026-08) ran a differential between artifact-provable facts and compiler obligations and found exactly the facts that had no declared authority: a `readln` buffer whose capacity (1024) and framing (newline trim) lived in emission-witness code where no proof could reach them, and a rodata map that could only be audited after linking because nothing had declared it before. Both findings are DMM-remit facts (DTS/DMM paper: memory-space identifiers as an enumeration sort in the same decidable constraint system; obligations as hyperedges on the graph the lowering traverses), and both resolve the same way: **the fact gets a declared home in BAREWire vocabulary, and the obligation cites the declaration.**

[Arena_Design](./Arena_Design.md) is the same story one layer down: the bump-allocator mechanism is authoritatively designed *here*, was elevated to a compiler intrinsic, and its allocation-discipline facts (position, capacity, span) are exactly what the arena-bound obligation family will be stated against.

## The TCB, mechanized

[Substrate_Formalism](./Substrate_Formalism.md) rests on one condition: a substrate is formalizable when its trusted computing base is *articulated*. This note names the mature form of articulation: **schema-resident declarations.** The kernel's `read` contract, the allocator's freshness, the far side of every FFI/IPC/wire boundary — today these are assumptions stated in prose; under the platform description they are declared entries the obligations cite by name. The framework's verification taxonomy (proven / layout / assumed / unverified) then has all four standings machine-enumerable — including what is deliberately *not* proven, per the boundary rule: proofs go to the boundary, never across it; the far side is a declared assumption, and foreign code worth more than an assumption is ported, not proven in place.

## What Fidelity.Platform looks like when this lands

Each per-target tree stops being a folder of conventions and becomes a **BAREWire-described platform value**: the memory spaces (stack, arena, heap, static, BRAM, neuron state — the DMM enumeration sort) with concrete capacities and orderings; buffer schemas with capacity and framing; boundary surfaces with declared contracts; transports. `Console.readln` reads its capacity from the platform's declared buffer schema. The syscall table stops being string literals and becomes a described interface. Cross-target transfer marshals against the same schemas on both ends — which is what makes compile-time transfer-fidelity claims (the DTS/DMM paper's "BAREWire over PCIe, fidelity 1.0") computable at all, and what [10 The Case from Practice](./10%20The%20Case%20from%20Practice.md) shows going wrong wherever the schema was hand-rolled instead.

The FPGA lives this way because it had no choice. The CPU gets it by decision.

## Where this sits

- [Substrate_Formalism](./Substrate_Formalism.md) — the outward reading this note complements; one substrate discipline, two directions.
- [10 The Case from Practice](./10%20The%20Case%20from%20Practice.md) — the evidence, including HelloArty's widths and the §7.3 acid test; unchanged by this note and strengthened by it.
- [08 Hardware Descriptors](./08%20Hardware%20Descriptors.md) — its "Future: Unified Memory Description" section is this direction's seed; the peripheral descriptor is the pattern the process's own map generalizes.
- [04 Memory Mapping](./04%20Memory%20Mapping.md) — Region/View is the mapping vocabulary the platform description will bind to (design status unchanged).
- [Arena_Design](./Arena_Design.md) — the allocation mechanism whose discipline the arena obligations will cite.
- `~/repos/speakez-lab/fidelity-proof-callsheet/` — the Tier 2 obligation families and the BAREWire milestones (maturation to par, layout declaration authority, boundary marshaling authority, platform description) this note is the rationale for.
- `~/repos/ship-of-theseus/HelloProof/EXTRACTION.md` — the differential findings that made the missing authority visible.
- `~/repos/arxiv-papers/dts-dmm-paper.md` — the DMM remit this note lives in.
