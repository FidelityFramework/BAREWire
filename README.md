# BAREWire

The typed contract between Clef components, and the declared authority on memory layout for every processor a program runs on. Part of the Fidelity Framework.

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![License: Commercial](https://img.shields.io/badge/License-Commercial-orange.svg)](Commercial.md)

<p align="center">
🚧 <strong>Under Active Development</strong> 🚧<br>
<em>Rebuilt from the design documents on 2026-09-03; see <a href="docs/Implementation%20Status.md">Implementation Status</a> for what is built and gated.</em>
</p>

## What BAREWire is

A value that crosses a BAREWire boundary preserves its case structure, its payload types, and its dimensional annotations by construction. The encoding is [BARE](https://baremessages.org/); the load-bearing property is the contract the encoding upholds ([Substrate_Formalism](docs/Substrate_Formalism.md)). Read inward, the same vocabulary is a platform's declared memory layout: memory spaces with capacities, buffer schemas with capacity and framing, boundary surfaces with declared contracts, transports. The compiler observes one declaration three ways, in emission, in constraint generation, and in proof obligations ([11 Platform Description](docs/11%20Platform%20Description.md)).

Two missions, ranked ([docs/README](docs/README.md)):

- **Primary.** The contract between Clef components, and each platform's declared layout for the compiler.
- **Secondary.** The same contract bound to JavaScript, .NET, Rust, C, and C++, in two modes: *source-shared*, one protocol file compiled by Fable, .NET, and Composer, the types being the contract (WrenHello, Conclave); and *schema-shared*, a BARE schema as the interchange artifact where source cannot be shared, each language's codec generated from it.

## The tiers

One source tree, three project files. `src/BAREWire.fidproj` is the Clef build (Composer), `src/BAREWire.fsproj` the .NET build, `src/BAREWire.Fable.fsproj` the JavaScript build. Only the `Encoding/Substrate/` shim files differ between them: UTF-8 text and IEEE-754 bit casts, selected by the project file, never at run time.

| Tier | Files | What it is |
| --- | --- | --- |
| Encoding | `src/Encoding/` | The BARE codec over bounded byte extents: threaded offsets, a fault offset instead of exceptions, no mutable cursor, no interfaces ([02](docs/02%20Encoding%20and%20Decoding%20Engine.md)). |
| Framing | `src/Framing/Envelope.fs` | The one envelope: kind byte, u32 correlation, BARE payload; a length prefix only on stream transports; the schema epoch in a `Hello` control frame ([05](docs/05%20Network%20Protocol.md), adopted from Conclave.Wire). |
| Schema | `src/Schema/` | BARE's own fixed type vocabulary, validation, wire-size and packed-offset analysis, compatibility, and emission of `.bare` schema text ([03](docs/03%20Schema%20System.md)). |
| Hardware | `src/Hardware/` | Peripheral and struct descriptors in fixed widths, ABI profiles, and the layout validator that answers whether a descriptor and an ABI agree ([08](docs/08%20Hardware%20Descriptors.md), [10](docs/10%20The%20Case%20from%20Practice.md)). |
| Memory | `src/Memory/` | `Region` and `View`: bounded extents and descriptor-typed access over the portable byte model ([04](docs/04%20Memory%20Mapping.md)). |
| Platform | `src/Platform/` | The platform description and its observers: consistency checks, the memory-map manifest (the CPU's analogue of the FPGA's XDC), and proof obligations stated against declarations ([11](docs/11%20Platform%20Description.md)). |

Everything above compiles under all three compilers from one source. The rules that make that possible, each with its evidence and its preferred spelling once the compiler surface widens, are in [12 Intersection Subset](docs/12%20Intersection%20Subset.md).

## Gates

| Gate | Command |
| --- | --- |
| .NET | `dotnet run --project tests/BAREWire.Tests.fsproj` (golden-frame vectors, envelope, validator, observers) |
| JavaScript | `fable src/BAREWire.Fable.fsproj --outDir out && echo '{"type":"module"}' > out/package.json && node tests/js/roundtrip.mjs out && node tests/js/tiers.mjs out` |
| Native | `Composer compile samples/RoundTrip/RoundTrip.fidproj && samples/RoundTrip/targets/roundtrip` |

The three transcripts agree on the same values; that agreement is the cross-substrate byte-identity claim made testable ([Readiness Audit](docs/Readiness%20Audit.md) §4 step 4).

## Where it is used

| Application | What BAREWire supplies |
| --- | --- |
| HelloProof (`ship-of-theseus`) | The Linux x86_64 platform description: memory spaces, the `read` contract, and the `consoleReadln` buffer schema (capacity 1024, newline-delimited, trimmed) that the compiler's proof obligations cite instead of literals in the lowering. |
| WrenHello, Conclave | The envelope and the encoder and decoder behind the shared protocol types, compiled by Fable and Composer from one file. |
| HelloArty (Arty A7) | The descriptor vocabulary the FPGA description grows into: BRAM and register memory spaces, the UART transport carrying `ArtyReport`. |
| Farscape, HelloWayland | The `StructDescriptor` validator: a descriptor plus a target ABI in, an agreement verdict out. |

## The Fidelity Framework

| Project | Role |
| --- | --- |
| **Clef / CCS** | The language and its compiler services; the native type universe |
| **Composer** | AOT compiler: Clef → PSG → MLIR → native |
| **Fidelity.Platform** | Per-target platform source trees; depends on BAREWire for its descriptions |
| **Farscape** | C/C++ header parsing for native library bindings; consumes the descriptor vocabulary |
| **XParsec** | Parser combinators powering PSG traversal and header parsing |

## Documentation

Start with [docs/README](docs/README.md). The build plan is the [Readiness Audit](docs/Readiness%20Audit.md); the ledger is [Implementation Status](docs/Implementation%20Status.md).

## License

BAREWire is dual-licensed.

### Open Source License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE).

### Commercial License

For organizations that need to embed BAREWire in proprietary products or need commercial support, see [Commercial.md](Commercial.md).

### Patent Notice

See [PATENTS.md](PATENTS.md).

## Contributing

Contributions are welcome under the Apache License, Version 2.0. The design documents are the authority; the compiler-facing rules are in [12 Intersection Subset](docs/12%20Intersection%20Subset.md).

## Acknowledgments

- [BARE](https://baremessages.org/) by Drew DeVault, the encoding
- Andrew Kennedy's units of measure, the dimensional discipline the native type universe carries
