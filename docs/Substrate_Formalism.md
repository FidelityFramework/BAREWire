# BAREWire as a Formalizable Substrate

This note records the foundational reading of BAREWire that the rest of the documentation assumes: BAREWire is a substrate in the formal sense, on the same footing as a CPU, GPU, FPGA, or a quantum target, not a serialization format or a wire protocol that happens to sit near one. The consequence is what makes it consequential for the Fidelity framework, so it is worth stating on its own.

## The claim

A compute substrate can be reasoned about formally when its trusted computing base (TCB) is articulated: state what the substrate is trusted to preserve, establish what the implementation preserves, and identify where those two meet. BAREWire supplies the common contract vocabulary for this work across memory layout, IPC, and network communication. Its role as the framework's glue is to make the premises at each crossing explicit and reusable. Naming those premises makes a proof possible; the mapping and the operations that use it still have to satisfy them.

The strength of the reading is its generality. The formalism does not stop at the CPU/GPU/FPGA/quantum set. Any boundary BAREWire spans becomes crossable under the same discipline, provided its TCB is well articulated. A boundary is a boundary: hardware, process, or type-erasure all reduce to the same question of what the substrate guarantees.

## Typed contracts and their mappings

The precise articulation is a **typed contract**. The intended guarantee is that an accepted value crossing a BAREWire boundary preserves its case structure, payload types, and dimensional meaning under the declared representation mapping. Both endpoints must implement that mapping, agree on the contract, and establish its input premises. The compiler can reject incompatible declarations before deployment; an open boundary also needs runtime rejection of malformed or incompatible input. BAREWire behaves as a structure map between endpoints: the contract is over meaning, and the encoding serves the contract.

This is a sharper statement than "a discriminated-union-aware memory layout" or "a protocol derived from BARE." Those describe the encoding. The property of interest is the typed contract the encoding upholds. When the framework relies on BAREWire to carry meaning across a boundary, it is relying on the contract, and the contract is what a formalizable substrate provides that a memory layout does not.

> BARE is the external binary encoding. The typed contract BAREWire upholds across a boundary is the substrate guarantee. Keep the two distinct: the encoding is how bytes cross; the contract is what is preserved when they do.

## Three layers, one contract

"Typed contract" names a design-time fact, and it is worth being exact about which fact, because the phrase has been read as if the bytes themselves carried types. They do not. Three layers are in play, and the discipline is that the first survives the third without the third knowing anything about it.

1. **The design-time contract.** Types, case structure, dimensional annotations, measures, and representation choices after inference. Clef establishes the static premises in the compiler; a dimension mismatch is a compile error. The PSG retains these facts and their obligations through the stages that need them. Metadata is released only after its role in analysis and preservation is fulfilled: **untagged at final lowering** does not mean erased before the compiler has finished reasoning about it. Its consequences remain in the selected operations, codecs, and checks generated for premises that depend on runtime input. Proof evidence may be retained for build review without accompanying every message.
2. **The mapping.** For each substrate and each protocol, the rule that takes a value of the contract to a byte layout and back: the BARE encoding for a wire or a queue, the C natural layout of an ABI profile for a foreign call, a map value's layout for a kernel, a `DataView` window for JavaScript. The mapping is fixed at compile time, in Fixed dimensions, and both endpoints hold it because both were compiled against the same schema. It is where the schema lives.
3. **The wire.** The final BARE payload is untagged: it omits field names, schema definitions, and dimensions. A union carries its case index and an optional its presence bit; these select values within the agreed encoding, rather than describing their types. The receiver interprets those bytes through the agreed mapping. The envelope's Hello carries an epoch and build string; neither encodes the schema itself. Establishing and enforcing contract agreement is a session responsibility; encoding these fields alone does not implement a handshake. A wrong schema can otherwise produce a plausible but incorrect value. Bounds, case, length, and representation checks remain necessary at an open boundary.

So "a value crosses the boundary with its case structure and dimensions preserved" means: the receiving contract agrees with the sending contract, the mappings preserve the declared value, and the accepted bytes are its image under that mapping. Dimensional meaning comes from the agreed declaration; inspecting bytes cannot discover whether an otherwise valid scalar was intended as metres or seconds. Golden-frame tests exercise agreement on selected examples. A universal preservation claim additionally needs the codec and lowering obligations established over their declared domains. Where this documentation says "typed contract," it means layer 1 upheld across layer 3 through layer 2.

## Why it is significant

Because BAREWire is a formalizable substrate with a well-articulated TCB, the verification story reaches **across** the runtime boundary and across heterogeneous processors, rather than stopping at a process edge. A composition of specialists can place components on FPGA, neuromorphic, or remote nodes, and the composition stays coherent, because BAREWire carries meaning across the hardware boundary as cleanly as within a single process. The cross-boundary consultation interfaces are the structure maps that make the composition compose.

Read BAREWire as "just a memory layout" and none of that reach holds: a memory layout carries bytes, and the receiver is trusted to interpret them correctly by convention. The typed contract reading is what lets the guarantee survive the crossing.

## The same principle across two boundaries

The substrate reading shows up in two settings that look different and are the same principle:

1. **Across a hardware or runtime boundary (the constellation).** BAREWire carries the untagged encoding of a value between components. Its case and dimensional contract remain in the compiler declarations; dimensional annotations do not accompany the bytes. Matching mappings and established boundary premises let components on different processors compose without losing the value's agreed meaning at the crossing.

2. **Across a type-erasure boundary (the JavaScript backend).** JavaScript retains runtime value categories, but does not enforce Clef's dimensional types. The compiler's task is to preserve the consequences of those types in the generated operations and boundary checks. BAREWire supplies the contracts for byte-backed views and messages; it does not replace the host's type system or prove arbitrary JavaScript. Ordinary records and closures may use host objects without a native address layout. The selected engine, its numeric operations, buffer ownership, and foreign calls form the relevant runtime premises. Lowering to JavaScript is another application of the same preservation discipline.

The common root is one idea: any boundary is crossable under the formalism discipline once BAREWire is understood as a formalizable substrate with a well-articulated TCB. The hardware-composition documentation and the JavaScript-backend documentation converge on the identical typed-contract articulation because they are the same claim.

## The current JavaScript evidence and the next link

The current Fable build executes the shared codec, schema, layout, memory-view,
and platform-observer code as JavaScript. Its test harness also dispatches
declaration-derived SMT obligations to cvc5. The solver proves those formulas
under their stated premises; it does not read or certify the generated JavaScript.
[Intersection Subset §5.1](./12%20Intersection%20Subset.md#51-what-the-javascript-solver-gate-establishes)
records the exercised properties and their limits. In particular, the current
BARE schema and JavaScript gate do not demonstrate dimensional preservation.

For the proposed Composer-to-JSIR path, the missing link is the correspondence
between each carried obligation and the operations that realize it. A region
bound must constrain the actual byte access; a wide integer must retain its
value through the chosen host representation; a decoder must establish the
premises of the value it admits. Certified lowering rules or checks at affected
edges establish that correspondence. Re-solving an unchanged declaration formula
after emission does not establish it by itself.

This also allows proofs about computation between BAREWire boundaries. A Clef
function may preserve a dimension, a range, or an allowed state transition, and
the JavaScript pathway must preserve that property under its own operation
semantics. BAREWire provides the incoming and outgoing contracts with which such
local facts compose. It cannot make a foreign SDK obey an undeclared law, infer
physical units from a numeric payload, or turn a TypeScript signature into a proof
of the implementation behind it. The [JavaScript boundary specification](../../clef-lang-spec/spec/javascript-boundary.md)
defines the planned narrowing and foreign-contact discipline; [Conformance §6](../../clef-lang-spec/spec/conformance.md#6-the-preservation-obligation-through-lowering)
defines the preservation obligation. This is a design obligation for the JSIR
pathway, not a claim that the present Fable gate implements that profile.

## The two missions, ranked

The substrate reading has two audiences, and the Readiness Audit (2026-09-03) ranks them.

**Primary.** BAREWire code is the typed contract between Clef components: a value crossing a BAREWire boundary between two Clef programs, or between a Clef program and the platform it runs on, preserves its case structure, payload types, and dimensional character without tagging. Read inward, the same vocabulary is the declared authority on memory layout for every processor a program runs on ([11 Platform Description](./11%20Platform%20Description.md)): memory spaces, buffer schemas, boundary surfaces, transports, each a declaration the compiler's three observers read.

**Secondary.** The same contract binds to JavaScript, .NET, Rust, C, and C++, in two modes. In the *source-shared* mode one protocol file is compiled by Fable, .NET, and Composer, and the types are the contract; this is demonstrated in WrenHello and Conclave and is the mode the WREN stack depends on. In the *schema-shared* mode a BARE schema is the interchange artifact where source cannot be shared, and each language's codec is generated from it ([03 Schema System](./03%20Schema%20System.md)). The source-shared mode is designed first and firmest; the schema-shared mode degrades to the same envelope and encoding.

The ranking decides what is built first and what a design question is measured against: a choice that serves the Clef-to-Clef contract and the declared platform layout wins over one that serves only a foreign binding.

## Where this sits

This note is the conceptual foundation the implementation chapters build on. The encoding and schema mechanics are in [Encoding and Decoding Engine](./02%20Encoding%20and%20Decoding%20Engine.md) and [Schema System](./03%20Schema%20System.md); the boundary-crossing mechanics are in [Network Protocol](./05%20Network%20Protocol.md) and [IPC Integration](./06%20IPC%20Integration.md); the hardware-descriptor integration is in [Hardware Descriptors](./08%20Hardware%20Descriptors.md). Those documents describe how bytes cross. This one states what is preserved when they do, and why that makes BAREWire a substrate rather than a format.
