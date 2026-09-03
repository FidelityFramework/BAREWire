// The JavaScript differential for the Encoding and Framing tiers.
//
// Build:  fable src/BAREWire.Fable.fsproj --outDir <dir>
//         echo '{"type":"module"}' > <dir>/package.json
// Run:    node tests/js/roundtrip.mjs <dir>
//
// Prints the same transcript lines as tests/EncodingTests.fs pins and
// samples/RoundTrip prints natively; a mismatch is a failure (exit 1).

import { pathToFileURL } from "node:url";
import { resolve } from "node:path";

const dir = process.argv[2];
if (!dir) { console.error("usage: node roundtrip.mjs <fable outDir>"); process.exit(2); }
const load = async (p) => import(pathToFileURL(resolve(dir, p)).href);
const Encoder = await load("Encoding/Encoder.js");
const Decoder = await load("Encoding/Decoder.js");
const Codec = await load("Encoding/Codec.js");
const Envelope = await load("Framing/Envelope.js");

const hex = (a) => Array.from(a, (b) => b.toString(16).padStart(2, "0")).join(" ");
let failures = 0;
const expect = (name, actual, expected) => {
  if (actual !== expected) { failures++; console.log(`FAIL ${name}: expected ${expected}, got ${actual}`); }
};

const buf = new Uint8Array(64);
let o = Encoder.writeUInt(buf, 0, 300n);
o = Encoder.writeInt(buf, o, -2n);
o = Encoder.writeBool(buf, o, true);
o = Encoder.writeString(buf, o, "héllo");
o = Encoder.writeU32(buf, o, 0x12345678);
o = Encoder.writeF64(buf, o, -2.5);
o = Encoder.writeTag(buf, o, 3);
expect("written", o, 24);
expect("bytes", hex(buf.slice(0, o)), "ac 02 03 01 06 68 c3 a9 6c 6c 6f 78 56 34 12 00 00 00 00 00 00 04 c0 03");

const [v1, r1] = Decoder.readUInt(buf, 0);
const [v2, r2] = Decoder.readInt(buf, r1);
const [v3, r3] = Decoder.readBool(buf, r2);
const [v4, r4] = Decoder.readString(buf, r3);
const [v5, r5] = Decoder.readU32(buf, r4);
const [v6, r6] = Decoder.readF64(buf, r5);
const [v7, r7] = Decoder.readTag(buf, r6);
expect("uint", v1, 300n);
expect("int", v2, -2n);
expect("bool", v3, true);
expect("str", v4, "héllo");
expect("u32", v5, 0x12345678);
expect("f64", v6, -2.5);
expect("tag", v7, 3);
expect("read", r7, 24);
const [, rbad] = Decoder.readU32(buf, 62);
expect("faultread", rbad, -1);

const frame = Envelope.Envelope_ask(9, buf.slice(0, o));
const bytes = Envelope.Envelope_encodeMessage(frame);
expect("framelen", bytes.length, 29);
expect("framehead", hex(bytes.slice(0, 5)), "01 09 00 00 00");
const [f, at] = Envelope.Envelope_decodeMessage(bytes);
expect("at", at, 5);
expect("kind", f.Kind, 1);
expect("corr", f.Correlation, 9);
expect("payload", f.Payload.length, 24);
const sd = Envelope.Envelope_tryDecodeStream(Envelope.Envelope_encodeStream(frame));
expect("consumed", sd.Consumed, 33);
const short = Envelope.Envelope_tryDecodeStream(Envelope.Envelope_encodeStream(frame).slice(0, 32));
expect("shortconsumed", short.Consumed, -2); // Envelope.Incomplete: read more and retry
const h = Envelope.Envelope_hello(1, "abc123");
expect("hello", hex(h.Payload), "02 06 61 62 63 31 32 33");
const [hv, hc] = Envelope.Envelope_tryReadHello(h);
expect("helloconsumed", hc, 8);
expect("hellobuild", hv.Build, "abc123");
const [enc, encLen] = Codec.encode(8, Encoder.writeUInt, 300n);
expect("enc", hex(enc), "ac 02");
expect("enclen", encLen, 2);

console.log(failures === 0 ? "javascript differential: agrees" : `javascript differential: ${failures} mismatches`);
process.exit(failures === 0 ? 0 : 1);
