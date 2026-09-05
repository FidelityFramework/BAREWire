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
| Peripheral Descriptors (BRAM, memory-mapped hardware)¹ | The process memory map: rodata, stack, arena regions with declared extents, permissions, alignment and padding policy |
| `ClockConstraint` / `ResetConstraint` | The loader/linker contract: page permissions, section placement, entry conventions |
| Width inference | Representation selection — already shared machinery, already per-target |
| Device part (`xc7a100tcsg324-1`) | The CPU/OS/arch descriptor that the Fidelity.Platform per-target *folder tree* currently is by convention |
| XDC emitted from the coeffect | The linker script / memory-map manifest emitted from the declaration |

¹ Landing on the Arty side (Readiness Audit §4 step 9): as of 2026-09-03 the Arty tree carries an additive `ArtyA7_100T.Description.clef` in BAREWire vocabulary beside its pin bindings, declaring BRAM, register, and flash memory spaces, the UART transport, and the `uartTx` buffer schema for `ArtyReport`, with `Fidelity.Platform/docs/BAREWire_Rebase_Plan.md` mapping the remaining Contracts types. The pin map keeps one authority (the bindings the compiler already projects to XDC) until Composer projects it from the description.

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

The core is described the same way (`TargetCore`, `Platform/Description.fs`; clef's Dimensional_Vetting_Plan.md D8, landed as CS-7b). Beside its ISA identity it declares its **width dimensions** by name (`Widths`: a CPU declares `Pointer` and `Register`; a fabric binding declares its port widths) and the **numeric representations** it offers (`Representations`: each with a capability of `native`, `emulated` or `unavailable`, a family, its width, its exact range as decimal text, and its boundary semantics of `wrap`, `saturate` or `exact`). The language carries only the name (`int32`, `float64`, `nativeint`); the description says what that name is on the target. The compiler reads the declaration once, at saturation, and never supplies a width or a representation of its own: a site whose dimension is undeclared is CCS8203, a seal whose representation is not offered is CCS8204. `Check.run` refuses a name declared twice, a tag the vocabulary does not name, a width of no bits, and a `Register` width that disagrees with the word size; `Manifest.emit` prints one `width` and one `representation` line per declaration.

## The kernel as a described platform

eBPF is the second member, after WebAssembly, of a class the platform taxonomy is learning to name: hosted, verified instruction sets, where admission is gated by a checker rather than by physics (FPGA) or an ABI (CPU). The Composer design series (`~/repos/Composer/docs/ebpf-targeting/`, July 2026) and the essay [Building Bulletproof eBPF Programs](https://clef-lang.com/blog/building-bulletproof-ebpf-programs/) make three claims on this library, and the platform description carries each of them without leaving its vocabulary.

**Hooks are pins.** An attach point is an endpoint: a name (`xdp`, `tracepoint/...`, `lsm`), a context type it hands the program, a return convention it expects back. A helper is an endpoint with a signature and a contract. Both are `Endpoint`s on a `BoundarySurface` of kind Syscall's sibling, `HostApi`, exactly as the Arty's pins are endpoints on a surface of kind Pins. What the kernel adds, and the FPGA never needed, is the **version axis**: a helper that arrived in kernel 5.17 is a capability with a floor, a map kind from 5.8 another. Every endpoint, memory space, and transport therefore carries `Since` and `Until` (host versions as text, empty when unbounded), and a project's pinned host floor filters the candidate set; a use below the floor is a witnessed failure naming the version that would satisfy it. This is the *versioned capability matrix* Composer's series asks to be designed once, at the contracts level, for eBPF, WebAssembly, MCU silicon revisions, and CPU ISA extensions alike. It is designed here because Fidelity.Platform's contracts are an alias layer over these types (Readiness Audit §4 step 9).

**Maps are declared shared memory.** A BPF program is short-lived; its persistent state lives in maps, fixed-layout key and value records shared between the kernel program and user space, and its stream to user space is a ring buffer. That is the shared-memory boundary this library exists to declare: a map is a `MemorySpace` of kind Map (with its map kind, hash, array, per-CPU array, ring buffer, as a declared property), and a ring is a `BufferSchema` with `Ring` framing over fixed slots and producer and consumer indices. AF_XDP's UMEM rings and io_uring's submission and completion rings are the same shape one layer over. Doc [06](./06%20IPC%20Integration.md) declared no segment layout before; these are its first.

**Map values are pointer-free by construction.** The verifier's information-flow demand, that no kernel pointer reach user-readable storage, is a design-time property of a declared layout with no `Pointer` representation; the map's bytes carry no such marker, the declaration does. `Hardware.Layout.isPointerFree` states it, and a kernel-visible buffer or map whose layout fails it is a `Check` finding, discharged before the verifier is consulted. This is the smallest example of the TCB principle above: the obligation cites a declaration, and the declaration is checkable.

**Verifier limits are declared facts.** The 512-byte stack, the instruction budget, the ISA level are `Limits` on the description, named and numbered; the stack ceiling in particular is a `MemorySpace` of kind Stack with capacity 512, so the Tier 2 stack-sum obligation ranges over a declared bound. The frames themselves come from the compiler's escape classification; the ceiling comes from here.

**BTF is a schema serialization.** The kernel already speaks types: BTF, the BPF Type Format, is its schema language, and modern kernels want it for map definitions, bpf-to-bpf calls, and CO-RE relocation. BTF describes structs in C natural layout with member offsets in bits, which is what `Hardware.Validator.derive` computes under an ABI profile; a BTF emitter over `StructDescriptor` arrays is therefore a sibling of the `.bare` text emitter (docs [03](./03%20Schema%20System.md), schema-shared mode). Writing the type and string tables is this library's job; placing the section in the ELF object is the compiler's.

**The seam with Fidelity.Platform and Composer.** BAREWire supplies the vocabulary, the observers (`Check`, `Manifest`, `Obligations`), and the emitters (`.bare`, BTF). Fidelity.Platform supplies the leaves: a `BPF/Linux/6.x` description enumerating that kernel series' hooks, helpers, map kinds, and limits in this vocabulary, and a profile composing it with the host CPU's word size and endianness by reference rather than by copy, as its `StrixHalo_ArtyLab` profile composes substrates today. Composer supplies the admissibility coeffects (loop bounds, register ranges, guard legibility), the `BpfProgram` declaration root, and section placement. The seam is smooth because each side reads the others' artifacts and none restates them.

**wBPF and the coherent interconnect.** wBPF (HCDS '25) is an eBPF runtime for CXL memory-pooling systems that coordinates tracing across nodes sharing a pool. The framework's reading ([Next-Generation Memory Coherence](https://clef-lang.com/docs/internals/memory-fabrics/next-generation-memory-coherence/)) is that the same schema-settled region that crosses the syscall boundary into the kernel also crosses a coherent interconnect, with the kernel probe as its witness. Nothing new is needed in the vocabulary for that: a pool is a memory space with a coherence domain as a declared property, and the probe watches exactly the transactions the declaration names.

**The acid test is ThreeBody.** The gravitational demo (`~/repos/ThreeBody`) routes close encounters to an Arty A7 over raw Layer 2 Ethernet, redirected on the host by an XDP program into AF_XDP rings, because the FTDI latency timer on the board's USB path makes a 1 ms floor and a 64-byte frame at 100 Mbit/s is 5 µs of wire. The value that crosses is a b-posit32 with an 800-bit quire (25 32-bit words), sealed at the force site; it must arrive as the same representation the FPGA computed, with its units. Its transport document leaves one item open: the per-timestep payload in bytes, which decides whether 12.5 MB/s binds before the timestep does. That number is a schema fact. The close-encounter frame is a `BufferSchema` whose layout is fixed width, so its size is computed from the declaration, and the bandwidth check is a `Leq` obligation of the payload against the transport's declared `MaxUnit`; the rate is declared for the consumer's timestep budget, an obligation that needs a period the platform description does not hold and the simulation does. It is also the case that forces the §7.3 answer: the FPGA's widths after inference are not the host's, and the contract is written in Fixed dimensions so both sides read the frame the same way.

## Where this sits

- [Substrate_Formalism](./Substrate_Formalism.md) — the outward reading this note complements; one substrate discipline, two directions.
- [10 The Case from Practice](./10%20The%20Case%20from%20Practice.md) — the evidence, including HelloArty's widths and the §7.3 acid test; unchanged by this note and strengthened by it.
- [08 Hardware Descriptors](./08%20Hardware%20Descriptors.md) — its "Future: Unified Memory Description" section is this direction's seed; the peripheral descriptor is the pattern the process's own map generalizes.
- [04 Memory Mapping](./04%20Memory%20Mapping.md) — Region/View is the mapping vocabulary the platform description will bind to (design status unchanged).
- [Arena_Design](./Arena_Design.md) — the allocation mechanism whose discipline the arena obligations will cite.
- `~/repos/speakez-lab/fidelity-proof-callsheet/` — the Tier 2 obligation families and the BAREWire milestones (maturation to par, layout declaration authority, boundary marshaling authority, platform description) this note is the rationale for.
- `~/repos/ship-of-theseus/HelloProof/EXTRACTION.md` — the differential findings that made the missing authority visible.
- `~/repos/arxiv-papers/dts-dmm-paper.md` — the DMM remit this note lives in.
- `~/repos/Composer/docs/ebpf-targeting/` — the kernel as a verified substrate: the verifier as a design-time contract (01), the platform shape and the versioned capability matrix (02), BTF and maps (03), admissibility as obligations (04), ThreeBody's data and observation planes (05).
- `~/repos/ThreeBody/docs/fpga-transport.md` — why Layer 2 over RJ45, and the open payload question the buffer schema answers.
