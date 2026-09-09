// Fresh Fable output is required: node tests/js/dispatch.mjs <fable outDir>.
// Explicit BigInt inputs preserve the shared signed-int64 byte model.
import assert from "node:assert/strict";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

const dir = process.argv[2];
if (!dir) throw new Error("usage: node tests/js/dispatch.mjs <fable outDir>");
const load = path => import(pathToFileURL(resolve(dir, path)).href);
const D = await load("Platform/DispatchRegions.js");
const A = await load("Hardware/Abi.js");
const V = await load("Hardware/Validator.js");
const max = 9223372036854775807n;
const min = -9223372036854775808n;
const points = [min, -1n, 0n, 1n, 4n, max - 1n, max];
const mathematicalContains = (extent, offset, length) => extent >= 0n && offset >= 0n && length >= 0n && offset + length <= extent;
for (const extent of points) {
  for (const offset of points) {
    for (const length of points) {
      assert.equal(D.DispatchRegions_contains(extent, offset, length), mathematicalContains(extent, offset, length));
      assert.equal(D.DispatchRegions_fitsByteLength(extent, offset, length),
        extent >= 0n && offset >= 0n && length > 0n && offset * length <= extent);
    }
  }
}
for (const a of points) {
  for (const al of points) {
    for (const b of points) {
      for (const bl of points) {
        assert.equal(D.DispatchRegions_disjoint(a, al, b, bl),
          mathematicalContains(max, a, al) && mathematicalContains(max, b, bl) &&
          (al === 0n || bl === 0n || a + al <= b || b + bl <= a));
      }
    }
  }
}
assert.equal(D.DispatchRegions_tryByteLength(A.Abi_wasm32, 1073741824n, 4n), undefined);
assert.equal(D.DispatchRegions_tryByteLength(A.Abi_wasm32, 1073741823n, 4n), 4294967292n);
assert.equal(D.DispatchRegions_tryByteLength(A.Abi_sysvAmd64, max / 4n + 1n, 4n), undefined);
assert.equal(D.DispatchRegions_tryByteLength(A.Abi_sysvAmd64, max / 4n, 4n), max - 3n);

const layoutResult = V.Validator_derive(A.Abi_sysvAmd64, "MapEnvironment", [
  new V.NamedRepr("table", "pointer", 1),
  new V.NamedRepr("tableLength", "u64", 1),
  new V.NamedRepr("output", "pointer", 1),
]);
assert.equal(layoutResult.tag, 0);
const layout = layoutResult.fields[0].Layout;
const slice = (allocation, offset, length) => new D.ByteSlice(allocation, offset, length);
const reads = [slice("table", 0n, 32n), slice("table", 64n, 16n)];
const allocation = (id, extent, access) => new D.DispatchAllocation(id, "host-arena", extent, access, "request");
const partition = (id, lo, hi) => new D.DispatchPartition(id, lo, hi, reads, [slice("image", lo * 4n, (hi - lo) * 4n)]);
const input = new D.DispatchInput("complete-table", reads, [slice("table", 0n, 80n)]);
const region = new D.DispatchRegion("map-frame", "graph:region:17", "graph:worker:18", A.Abi_sysvAmd64, 11n,
  [allocation("table", 128n, "ro"), allocation("image", 44n, "rw")], [input],
  [partition("first", 0n, 4n), partition("middle", 4n, 8n), partition("tail", 8n, 11n)], layout, layout);
const validate = r => D.DispatchRegions_validate(r);
const reject = (r, kind) => {
  const verdict = validate(r);
  assert.equal(verdict.SpatiallyValid, false, kind);
  assert.equal(verdict.Findings.some(f => f.Kind === kind), true, kind);
};
const accepted = validate(region);
assert.equal(accepted.SpatiallyValid, true);
assert.equal(accepted.Source, region.Source);
assert.equal(accepted.WorkerEntry, region.WorkerEntry);
assert.equal(accepted.Checks.every(c => c.Holds), true);
assert.equal(validate({ ...region, Partitions: [...region.Partitions].reverse() }).SpatiallyValid, true);
assert.equal(validate({ ...region, Inputs: [{ ...input, Supplied: [slice("table", 40n, 40n), slice("table", 0n, 40n)] }] }).SpatiallyValid, true);
reject({ ...region, Inputs: [{ ...input, Supplied: [slice("table", 0n, 32n)] }] }, "complete-input");
reject({ ...region, Inputs: [{ ...input, Supplied: [slice("table", 0n, 68n), slice("table", 72n, 8n)] }] }, "complete-input");
reject({ ...region, Inputs: [{ ...input, Required: [slice("table", 0n, 32n)] }] }, "read-input-coverage");
reject({ ...region, Partitions: [{ ...region.Partitions[0], Writes: [slice("image", 0n, 17n)] }, ...region.Partitions.slice(1)] }, "exclusive-output");
reject({ ...region, Allocations: [allocation("table", 128n, "rw"), region.Allocations[1]],
  Partitions: [{ ...region.Partitions[0], Writes: [slice("table", 0n, 16n)] }, ...region.Partitions.slice(1)] }, "immutable-input");
reject({ ...region, Partitions: region.Partitions.slice(0, 2) }, "partition-coverage");
reject({ ...region, Partitions: [{ ...region.Partitions[0], Lower: min, Upper: max }, ...region.Partitions.slice(1)] }, "partition-bounds");
reject({ ...region, Partitions: [{ ...region.Partitions[0], Writes: [slice("image", max - 4n, 12n)] }, ...region.Partitions.slice(1)] }, "slice-containment");
reject({ ...region, WorkerCaptureLayout: { ...layout, Fields: [{ ...layout.Fields[0], Repr: "u64" }, ...layout.Fields.slice(1)] } }, "capture-layout-match");
reject({ ...region, Target: A.Abi_i386SysV }, "capture-layout");
assert.equal(validate({ ...region, IterationCount: 0n, Inputs: [], Partitions: [], Allocations: [] }).SpatiallyValid, true);
const highReads = [slice("table", max - 8n, 8n)];
const high = { ...region, Allocations: [allocation("table", max, "ro"), region.Allocations[1]],
  Inputs: [{ ...input, Required: highReads, Supplied: [slice("table", max - 8n, 4n), slice("table", max - 4n, 4n)] }],
  Partitions: region.Partitions.map(p => ({ ...p, Reads: highReads })),
};
assert.equal(validate(high).SpatiallyValid, true);
reject({ ...high, Inputs: [{ ...high.Inputs[0], Supplied: [slice("table", max - 8n, 3n), slice("table", max - 4n, 4n)] }] }, "complete-input");
console.log("dispatch spatial JavaScript checks passed (including signed-int64 boundary matrices)");
