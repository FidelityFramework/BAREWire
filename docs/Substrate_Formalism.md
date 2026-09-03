# BAREWire as a Formalizable Substrate

This note records the foundational reading of BAREWire that the rest of the documentation assumes: BAREWire is a substrate in the formal sense, on the same footing as a CPU, GPU, FPGA, or a quantum target, not a serialization format or a wire protocol that happens to sit near one. The consequence is what makes it consequential for the Fidelity framework, so it is worth stating on its own.

## The claim

A compute substrate can be reasoned about formally when its trusted computing base (TCB) is articulated: state precisely what the substrate is trusted to preserve, and a value carried across it inherits guarantees rather than losing them. BAREWire meets that condition. Once its TCB is written down, the framework's verification discipline extends across **any** process or processor boundary it mediates, not only the hardware targets the compiler already lowers to.

The strength of the reading is its generality. The formalism does not stop at the CPU/GPU/FPGA/quantum set. Any boundary BAREWire spans becomes crossable under the same discipline, provided its TCB is well articulated. A boundary is a boundary: hardware, process, or type-erasure all reduce to the same question of what the substrate guarantees.

## Typed contract, not memory layout

The precise articulation is a **typed contract**, and this is the distinction that carries the weight. A value crossing a BAREWire boundary preserves its case structure, its payload types, and its dimensional annotations **by construction**. The receiving endpoint reconstructs the same native value the sender held; a violation is caught at the message fabric at design time, not discovered downstream as a misread field. BAREWire behaves as a structure map between endpoints: the contract is over meaning, and the encoding serves the contract.

This is a sharper statement than "a discriminated-union-aware memory layout" or "a protocol derived from BARE." Those describe the encoding. The property of interest is the typed contract the encoding upholds. When the framework relies on BAREWire to carry meaning across a boundary, it is relying on the contract, and the contract is what a formalizable substrate provides that a memory layout does not.

> BARE is the external binary encoding. The typed contract BAREWire upholds across a boundary is the substrate guarantee. Keep the two distinct: the encoding is how bytes cross; the contract is what is preserved when they do.

## Why it is significant

Because BAREWire is a formalizable substrate with a well-articulated TCB, the verification story reaches **across** the runtime boundary and across heterogeneous processors, rather than stopping at a process edge. A composition of specialists can place components on FPGA, neuromorphic, or remote nodes, and the composition stays coherent, because BAREWire carries meaning across the hardware boundary as cleanly as within a single process. The cross-boundary consultation interfaces are the structure maps that make the composition compose.

Read BAREWire as "just a memory layout" and none of that reach holds: a memory layout carries bytes, and the receiver is trusted to interpret them correctly by convention. The typed contract reading is what lets the guarantee survive the crossing.

## The same principle across two boundaries

The substrate reading shows up in two settings that look different and are the same principle:

1. **Across a hardware or runtime boundary (the constellation).** BAREWire carries typed value that are untagged — a discriminated-union case, a structured fact with its dimensional annotations — across the runtime boundary between components. That efficient-by-construction typed contract is what lets a composition of specialists converge, and what lets those specialists work on heterogeneous processors without the composition losing coherence at the crossing.

2. **Across a type-erasure boundary (the JavaScript backend).** When code is lowered to JavaScript, JavaScript's own type system is absent at runtime. BAREWire functions as the runtime type system in that context, carrying case structure and dimensional annotations across the erasure boundary by construction. V8 is another process whose TCB must be articulated; BAREWire extends the same discipline across it exactly as across an FPGA boundary. Lowering to JavaScript is substrate-formalism applied to the erasure boundary, not a separate story.

The common root is one idea: any boundary is crossable under the formalism discipline once BAREWire is understood as a formalizable substrate with a well-articulated TCB. The hardware-composition documentation and the JavaScript-backend documentation converge on the identical typed-contract articulation because they are the same claim.

## The two missions, ranked

The substrate reading has two audiences, and the Readiness Audit (2026-09-03) ranks them.

**Primary.** BAREWire code is the typed contract between Clef components: a value crossing a BAREWire boundary between two Clef programs, or between a Clef program and the platform it runs on, preserves its case structure, payload types, and dimensional character without tagging. Read inward, the same vocabulary is the declared authority on memory layout for every processor a program runs on ([11 Platform Description](./11%20Platform%20Description.md)): memory spaces, buffer schemas, boundary surfaces, transports, each a declaration the compiler's three observers read.

**Secondary.** The same contract binds to JavaScript, .NET, Rust, C, and C++, in two modes. In the *source-shared* mode one protocol file is compiled by Fable, .NET, and Composer, and the types are the contract; this is demonstrated in WrenHello and Conclave and is the mode the WREN stack depends on. In the *schema-shared* mode a BARE schema is the interchange artifact where source cannot be shared, and each language's codec is generated from it ([03 Schema System](./03%20Schema%20System.md)). The source-shared mode is designed first and firmest; the schema-shared mode degrades to the same envelope and encoding.

The ranking decides what is built first and what a design question is measured against: a choice that serves the Clef-to-Clef contract and the declared platform layout wins over one that serves only a foreign binding.

## Where this sits

This note is the conceptual foundation the implementation chapters build on. The encoding and schema mechanics are in [Encoding and Decoding Engine](./02%20Encoding%20and%20Decoding%20Engine.md) and [Schema System](./03%20Schema%20System.md); the boundary-crossing mechanics are in [Network Protocol](./05%20Network%20Protocol.md) and [IPC Integration](./06%20IPC%20Integration.md); the hardware-descriptor integration is in [Hardware Descriptors](./08%20Hardware%20Descriptors.md). Those documents describe how bytes cross. This one states what is preserved when they do, and why that makes BAREWire a substrate rather than a format.
