// The JavaScript differential for the Schema, Hardware, and Platform tiers.
//
// Build:  fable src/BAREWire.Fable.fsproj --outDir <dir>
//         echo '{"type":"module"}' > <dir>/package.json
// Run:    node tests/js/tiers.mjs <dir>
//
// Fable mangles primed F# names (`struct'` is `struct$0027`, `process'` is
// `Lifecycle_process$0027`), hence the bracket lookups below.
// The same declarations tests/{Schema,Hardware,Platform}Tests.fs make, built
// through the compiled JavaScript: schema validation and text, the layout
// validator and BTF, and a Linux description whose obligations are handed to
// cvc5 from here. Agreement with the .NET values is the differential.

import { pathToFileURL } from "node:url";
import { resolve } from "node:path";
import { writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { tmpdir } from "node:os";

const dir = process.argv[2];
if (!dir) { console.error("usage: node tiers.mjs <fable outDir>"); process.exit(2); }
const load = async (p) => import(pathToFileURL(resolve(dir, p)).href);
const DSL = await load("Schema/DSL.js");
const Def = await load("Schema/Definition.js");
const Val = await load("Schema/Validation.js");
const Ana = await load("Schema/Analysis.js");
const Emit = await load("Schema/Emit.js");
const Desc = await load("Hardware/Descriptors.js");
const Abi = await load("Hardware/Abi.js");
const V = await load("Hardware/Validator.js");
const Btf = await load("Hardware/Btf.js");
const P = await load("Platform/Description.js");
const Check = await load("Platform/Check.js");
const Obl = await load("Platform/Obligations.js");
const Manifest = await load("Platform/Manifest.js");
const Decimal = await load("Platform/DecimalText.js");

let failures = 0;
const expect = (name, actual, expected) => {
  if (actual !== expected) { failures++; console.log(`FAIL ${name}: expected ${expected}, got ${actual}`); }
};

// ---- Schema ----
let s = DSL.schema("Message");
s = DSL.withType("UserId", DSL.string, s);
s = DSL.withType("Attachment", DSL.optional(DSL.data), s);
s = DSL.withType("Message", DSL["struct$0027"]([DSL.field("id", DSL.typeRef("UserId")), DSL.field("attachment", DSL.typeRef("Attachment"))]), s);
expect("schema valid", Val.Validation_validate(s).length, 0);
const text = Emit.schema(s);
expect("schema text has struct", text.includes("type Message struct {"), true);
expect("schema text optional", text.includes("type Attachment optional<data>"), true);
const header = DSL["struct$0027"]([DSL.field("a", DSL.u8), DSL.field("b", DSL.u16), DSL.field("c", DSL.u32)]);
const size = Ana.Analysis_wireSize(s, header);
expect("fixed struct min", size.Min, 7);
expect("fixed struct fixed", size.IsFixed, true);

// ---- Hardware ----
const abi = Abi.Abi_sysvAmd64;
const sockResult = V.Validator_derive(abi, "sockaddr_in", [
  new V.NamedRepr("sin_family", "u16", 1), new V.NamedRepr("sin_port", "u16", 1),
  new V.NamedRepr("sin_addr", "u32", 1), new V.NamedRepr("sin_zero", "u8", 8)]);
expect("sockaddr derives successfully", sockResult.tag, 0);
if (sockResult.tag !== 0) throw new Error("valid layout failed to derive");
const sock = sockResult.fields[0];
const huge = V.Validator_derive(abi, "huge", [new V.NamedRepr("data", "u64", 536870912)]);
expect("4 GiB layout is rejected", huge.tag, 1);
expect("sockaddr size", sock.Layout.Size, 16);
expect("sockaddr agrees", V.Validator_validate(abi, sock).Agrees, true);
expect("sockaddr pointer-free", Desc.Layout_isPointerFree(sock.Layout), true);
const bits = new Desc.BitFieldDescriptor("bad", 2147483647, 1, "read-only");
const field = new Desc.FieldDescriptor("register", 0, "u64", 1, "read-only", [bits], undefined);
const badBits = new Desc.StructDescriptor("badBits", new Desc.PeripheralLayout(8, 8, [field]), undefined);
expect("bit-field endpoint cannot wrap", V.Validator_validate(abi, badBits).Agrees, false);
for (const [lo, hi, order] of [
  ["9007199254740993", "9007199254740992", 1],
  ["1.00000000000000000001", "1.00000000000000000000", 1],
  ["-0.001", "-0.01", 1], ["0001.00", "+1", 0], ["-0", "0.0", 0],
  ["-999999999999999999999999999999", "999999999999999999999999999999", -1]]) {
  expect(`exact decimal ${lo}/${hi}`, Decimal.compare(lo, hi), order);
}
const blob = Btf.Btf_emit(abi, [sock]);
const image = Btf.Btf_read(blob);
expect("btf parses", image.Ok, true);
const st = Btf.Btf_tryStruct(image, "sockaddr_in");
expect("btf struct size", st ? st.SizeOrType : -1, 16);
expect("btf sin_addr bit offset", st ? st.MemberBitOffsets[2] : -1, 32);

// ---- Platform: the Linux description, checked, its obligations dispatched ----
const readContract = P.ContractModule_assumed("read-bound", "writes at most count bytes into buf; returns n <= count; n = 0 is end of input", ["CWE-120"]);
const spaces = [
  P.MemorySpaceModule_withGrowth(P.MemorySpaceModule_create("stack", "stack", 8388608n, 16), "down"),
  P.MemorySpaceModule_withGrowth(P.MemorySpaceModule_create("arena", "arena", 4096n, 16), "up"),
  P.MemorySpaceModule_withBase(P.MemorySpaceModule_create("rodata", "rodata", 4096n, 4096), 0x402000n),
  P.MemorySpaceModule_withBase(P.MemorySpaceModule_create("text", "text", 4096n, 4096), 0x401000n)];
const surfaces = [P.BoundarySurfaceModule_create("syscalls", "syscall", [P.EndpointModule_syscall("read", 0, [readContract])])];
const buffers = [P.BufferSchemaModule_delimited("consoleReadln", "str", 1024n, 10, true, "arena")];
const desc = new P.PlatformDescription("cpu-linux-x86_64", "Linux x86-64 (libc)", "CPU", undefined, spaces, surfaces, buffers, [],
  P["Lifecycle_process$0027"]("_start", "exit_group", "volatile"), [], []);
const boundedDescription = (floor, parameter) => {
  const contract = P.ContractModule_withReturnBound(readContract, floor, parameter);
  return {...desc, Surfaces: [P.BoundarySurfaceModule_create("syscalls", "syscall", [P.EndpointModule_syscall("read", 0, [contract])])]};
};
const boundedManifest = Manifest.emit(boundedDescription(-4095n, "count"));
expect("manifest carries changed floor", boundedManifest !== Manifest.emit(boundedDescription(0n, "count")), true);
expect("manifest carries changed bound parameter", boundedManifest !== Manifest.emit(boundedDescription(-4095n, "capacity")), true);
for (const [lo, hi] of [["NaN", "1"], ["127", "-128"], [" ", " "], ["1.00000000000000000001", "1"]]) {
  const core = P.TargetCoreModule_withRepresentations(P.TargetCoreModule_create("linux", "x86_64", 64, "little", "libc"),
    [P.RepresentationModule_create("audit", "native", "ieee", 64, lo, hi, "exact")]);
  expect(`invalid representation ${lo}/${hi}`, Check.Check_run({...desc, Core: core}).some(f => f.Kind === "invalid-range"), true);
}
const findings = Check.Check_run(desc);
expect("linux description consistent", findings.length, 0);
const obs = Obl.Obligations_ofDescription(desc);
const ids = obs.map(o => o.Id);
expect("has input_copy_bound", ids.includes("input_copy_bound_consolereadln"), true);
expect("has spaces_disjoint", ids.includes("spaces_disjoint"), true);
let dispatched = 0;
const solve = (o, expected) => {
  const path = resolve(tmpdir(), `barewire-js-${o.Id}.smt2`);
  writeFileSync(path, Obl.Obligations_smtLib(o).replace("(reset)", ""));
  const r = spawnSync("cvc5", [path], { encoding: "utf8" });
  if (r.error) { failures++; console.error(`FAIL cvc5 ${o.Id}: ${r.error.message}`); return; }
  expect(`cvc5 exit ${o.Id}`, r.status, 0);
  expect(`cvc5 ${o.Id}`, r.stdout.trim(), expected);
  dispatched++;
};
for (const o of obs) solve(o, "unsat");
const mapping = (first, a, ac, second, b, bc) => ({...desc, Buffers: [], Surfaces: [], Transports: [], Spaces: [
  P.MemorySpaceModule_withBase(P.MemorySpaceModule_create(first, "rodata", ac, 1), a),
  P.MemorySpaceModule_withBase(P.MemorySpaceModule_create(second, "rodata", bc, 1), b)]});
const disjointness = d => Obl.Obligations_ofDescription(d).find(o => o.Kind === "memory-map-disjointness");
for (const [a, ac, b, bc, intersects] of [
  [9223372036854775803n, 16n, 9223372036854775799n, 16n, true],
  [-9223372036854775808n, 8n, -9223372036854775804n, 8n, true],
  [-9223372036854775808n, 8n, -9223372036854775800n, 8n, false],
  [-9223372036854775808n, 9223372036854775807n, 0n, 1n, false],
  [-4n, 8n, 0n, 8n, true], [-4n, 4n, 0n, 8n, false]]) {
  for (const swapped of [false, true]) {
    const d = swapped ? mapping("first", b, bc, "second", a, ac) : mapping("first", a, ac, "second", b, bc);
    expect(`memory overlap ${a}/${b} swapped=${swapped}`, Check.Check_run(d).some(f => f.Kind === "overlapping-spaces"), intersects);
    solve(disjointness(d), intersects ? "sat" : "unsat");
  }
}
for (const [b, intersects] of [[4n, true], [16n, false]]) {
  const d = mapping("alpha-beta", 0n, 8n, "Alpha Beta", b, 8n);
  solve(disjointness(d), intersects ? "sat" : "unsat");
}
const triples = {...desc, Buffers: ["console-readln", "Console Readln", "console_readln_2"].map(name => P["BufferSchemaModule_fixed$0027"](name, "str", 8n, "arena"))};
const tripleIds = Obl.Obligations_ofDescription(triples).map(o => o.Id);
expect("generated obligation ids cannot collide with declarations", new Set(tripleIds).size, tripleIds.length);
solve({Id: "minimum_bound", Kind: "buffer-capacity", Logic: "QF_LIA", Statement: "minimum signed integer is below zero", Source: "audit", Refs: [],
  Form: {Kind: "leq", A: -9223372036854775808n, B: 0n, Names: [], Values: []}}, "unsat");
console.log(`dispatched ${dispatched} obligations to cvc5 from the JavaScript build`);
console.log(failures === 0 ? "javascript tiers differential: agrees" : `javascript tiers differential: ${failures} mismatches`);
process.exit(failures === 0 ? 0 : 1);
