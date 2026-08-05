# BAREWire as a Formalizable Substrate

This note records the foundational reading of BAREWire that the rest of the documentation assumes: BAREWire is a substrate in the formal sense, on the same footing as a CPU, GPU, FPGA, or a quantum target, not a serialization format or a wire protocol that happens to sit near one. The consequence is what makes it load-bearing for the Fidelity framework, so it is worth stating on its own.

## The claim

A compute substrate can be reasoned about formally when its trusted computing base (TCB) is articulated: state precisely what the substrate is trusted to preserve, and a value carried across it inherits guarantees rather than losing them. BAREWire meets that condition. Once its TCB is written down, the framework's verification discipline extends across **any** process or processor boundary it mediates, not only the hardware targets the compiler already lowers to.

The strength of the reading is its generality. The formalism does not stop at the CPU/GPU/FPGA/quantum set. Any boundary BAREWire spans becomes crossable under the same discipline, provided its TCB is well articulated. A boundary is a boundary: hardware, process, or type-erasure all reduce to the same question of what the substrate guarantees.

## Typed contract, not memory layout

The precise articulation is a **typed contract**, and this is the distinction that carries the weight. A value crossing a BAREWire boundary preserves its case structure, its payload types, and its dimensional annotations **by construction**. The receiving endpoint reconstructs the same native value the sender held; a violation is caught at the message fabric, not discovered downstream as a misread field. BAREWire behaves as a structure map between endpoints: the contract is over meaning, and the encoding serves the contract.

This is a sharper statement than "a discriminated-union-aware memory layout" or "a protocol derived from BARE." Those describe the encoding. The load-bearing property is the typed contract the encoding upholds. When the framework relies on BAREWire to carry meaning across a boundary, it is relying on the contract, and the contract is what a formalizable substrate provides that a memory layout does not.

> BARE is the external binary encoding. The typed contract BAREWire upholds across a boundary is the substrate guarantee. Keep the two distinct: the encoding is how bytes cross; the contract is what is preserved when they do.

## Why it is load-bearing

Because BAREWire is a formalizable substrate with a well-articulated TCB, the verification story reaches **across** the runtime boundary and across heterogeneous processors, rather than stopping at a process edge. A composition of specialists can place components on FPGA, neuromorphic, or remote nodes, and the composition stays coherent, because BAREWire carries meaning across the hardware boundary as cleanly as within a single process. The cross-boundary consultation interfaces are the structure maps that make the composition compose.

Read BAREWire as "just a memory layout" and none of that reach holds: a memory layout carries bytes, and the receiver is trusted to interpret them correctly by convention. The typed-contract reading is what lets the guarantee survive the crossing.

## The same principle across two boundaries

The substrate reading shows up in two settings that look different and are the same principle:

1. **Across a hardware or runtime boundary (the constellation).** BAREWire carries typed values — a discriminated-union case, a structured fact with its dimensional annotations — across the runtime boundary between components. That by-construction typed contract is what lets a composition of specialists converge, and what lets those specialists live on heterogeneous processors without the composition losing coherence.

2. **Across a type-erasure boundary (the JavaScript backend).** When code is lowered to JavaScript, JavaScript's own type system is absent at runtime. BAREWire functions as the runtime type system in that context, carrying case structure and dimensional annotations across the erasure boundary by construction. V8 is another process whose TCB must be articulated; BAREWire extends the same discipline across it exactly as across an FPGA boundary. Lowering to JavaScript is substrate-formalism applied to the erasure boundary, not a separate story.

The common root is one idea: any boundary is crossable under the formalism discipline once BAREWire is understood as a formalizable substrate with a well-articulated TCB. The hardware-composition documentation and the JavaScript-backend documentation converge on the identical typed-contract articulation because they are the same claim wearing two costumes.

## Where this sits

This note is the conceptual foundation the implementation chapters build on. The encoding and schema mechanics are in [Encoding and Decoding Engine](./02%20Encoding%20and%20Decoding%20Engine.md) and [Schema System](./03%20Schema%20System.md); the boundary-crossing mechanics are in [Network Protocol](./05%20Network%20Protocol.md) and [IPC Integration](./06%20IPC%20Integration.md); the hardware-descriptor integration is in [Hardware Descriptors](./08%20Hardware%20Descriptors.md). Those documents describe how bytes cross. This one states what is preserved when they do, and why that makes BAREWire a substrate rather than a format.
