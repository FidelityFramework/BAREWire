# Encoding and Decoding Engine

The encoding engine converts between native Clef values and their binary BARE representation. It is the portable half of the dual compile: `src/Encoding/` opens nothing compiler-specific and compiles from one source under Fable, .NET, and Composer. This document describes the built engine as of 2026-09-03 (Readiness Audit §4 step 2); the substrate shims and the rules the source follows are in [12 Intersection Subset](./12%20Intersection%20Subset.md).

## Design

1. **The contract first.** BARE is how bytes cross; the typed contract is what is preserved when they do ([Substrate_Formalism](./Substrate_Formalism.md)). The engine serves the contract: a value's case structure and payload types are reproduced exactly on the far side, and a malformed input is refused rather than misread.
2. **Bounded extents.** Every operation works inside a byte array it was given, at an offset it was given, and never reads or writes outside it. Capacity is declared, not grown; exceeding it is a checked fault. The declaring authority for a buffer's capacity is its `BufferSchema` in the platform description ([11](./11%20Platform%20Description.md)).
3. **No exceptions, no mutable cursor.** State is threaded through return values. A writer returns the next offset; a reader returns the value and the next offset. A failure is the fault offset, and it propagates, so a codec composes straight through and is checked once.
4. **Module functions, no interfaces.** A codec for a type is a pair of functions in the shapes below, written beside the type or generated from a schema ([03](./03%20Schema%20System.md)).

## The cursor convention

```fsharp
module Cursor =
    [<Literal>] let Fault = -1
    let isFault (offset: int) : bool
    let isOk (offset: int) : bool
    let fits (data: byte array) (offset: int) (count: int) : bool   // false for a fault offset
    let remaining (data: byte array) (offset: int) : int
```

A writer is `byte array -> int -> 'a -> int`; a reader is `byte array -> int -> 'a * int`. Given a fault offset, every writer returns a fault offset and every reader returns the type's zero value and a fault offset. `int` is the platform word; wire-width values are `int32`, `uint32`, `int64`, `uint64`, `int16`, `uint16`, `sbyte`, `byte`.

## Wire format

| BARE type | Encoding | Writer / reader |
| --- | --- | --- |
| `uint` | ULEB128, at most ten bytes | `writeUInt` / `readUInt` (`uint64`) |
| `int` | zigzag map, then ULEB128 | `writeInt` / `readInt` (`int64`) |
| `u8`..`u64`, `i8`..`i64` | fixed width, little-endian | `writeU8`..`writeU64`, `writeI8`..`writeI64` |
| `f32`, `f64` | IEEE-754, little-endian | `writeF32` / `writeF64`, through the substrate `Float` shim |
| `bool` | one byte, 0 or 1; any other byte is a fault on read | `writeBool` / `readBool` |
| `str` | `uint` byte length, then UTF-8 | `writeString` / `readString`, through the substrate `Text` shim |
| `data` | `uint` byte length, then bytes | `writeData` / `readData` |
| `data<n>` | `n` bytes, no prefix; a length disagreement is a fault | `writeFixedData` / `readFixedData` |
| `optional<T>` | one byte tag, 0 absent or 1 present, then the value; another tag is a fault | `writeOption` / `readOption` |
| `list<T>` | `uint` count, then values; a count larger than the remaining bytes is a fault before allocation | `writeList` / `readList` |
| `list<T>[n]` | `n` values, no count | `writeFixedList` / `readFixedList` |
| union tag | `uint` case index | `writeTag` / `readTag` |
| raw bytes | no prefix (the envelope payload) | `writeBytesRaw` / `readBytesRaw` |

## Primitive encoding

```fsharp
module Encoder =
    let writeU8 (data: byte array) (offset: int) (v: byte) : int =
        let ok = Cursor.fits data offset 1
        if ok then
            Array.set data offset v
        if ok then offset + 1 else Cursor.Fault

    let writeUInt (data: byte array) (offset: int) (value: uint64) : int =
        let mutable v = value
        let mutable pos = offset
        while v >= 128UL && Cursor.isOk pos do
            pos <- writeU8 data pos (byte ((v &&& 127UL) ||| 128UL))
            v <- v >>> 7
        writeU8 data pos (byte v)

    let writeInt (data: byte array) (offset: int) (value: int64) : int =
        let zz = (uint64 (value <<< 1)) ^^^ (uint64 (value >>> 63))
        writeUInt data offset zz

    let writeString (data: byte array) (offset: int) (s: string) : int =
        writeData data offset (Text.toUtf8 s)
```

On the Clef substrate `Text.toUtf8` is `String.toBytes`, an identity view: a Clef string is already a length-carried memref of UTF-8 bytes, and nothing is transcoded. On .NET and JavaScript it is a hand-written UTF-16 to UTF-8 transcoder. The three project files select the shim; nothing is chosen at run time.

## Primitive decoding

```fsharp
module Decoder =
    let readU8 (data: byte array) (offset: int) : byte * int =
        let ok = Cursor.fits data offset 1
        let v = if ok then Array.get data offset else 0uy
        (v, (if ok then offset + 1 else Cursor.Fault))

    let readUInt (data: byte array) (offset: int) : uint64 * int =
        // ULEB128; more than ten bytes is a fault; never reads past the extent
        ...

    let readData (data: byte array) (offset: int) : byte array * int =
        let len64, next = readUInt data offset
        let ok = Cursor.isOk next && len64 <= uint64 (Cursor.remaining data next)
        if ok then readBytesRaw data next (int len64) else (Array.zeroCreate 0, Cursor.Fault)
```

Decoding untrusted bytes never reads out of bounds and never raises. The length of a `data`, `str`, or `list` is checked against the remaining extent before anything is allocated.

## Aggregates as combinators

```fsharp
let writeOption (data: byte array) (offset: int) (write: byte array -> int -> 'a -> int) (v: 'a option) : int
let writeList   (data: byte array) (offset: int) (write: byte array -> int -> 'a -> int) (items: 'a array) : int
let readOption  (data: byte array) (offset: int) (read: byte array -> int -> 'a * int) : 'a option * int
let readList    (data: byte array) (offset: int) (read: byte array -> int -> 'a * int) : 'a array * int
```

A codec for a record writes its fields in order; a codec for a union writes the case index with `writeTag` and then the case's payload, and reads with `readTag` followed by a match on the index. The reader binds each field to a `let` before constructing the value, because reads are position-dependent. `src/Framing/Envelope.fs` (`writeHello`, `readHello`) is the smallest example; Conclave's `Codecs.fs` is the fullest.

## Whole values

```fsharp
module Codec =
    /// Encode into a buffer of the declared capacity; the bytes and their length, or an empty array and Fault.
    let encode (capacity: int) (write: byte array -> int -> 'a -> int) (value: 'a) : byte array * int
    /// Decode requiring exact consumption; the value and the bytes consumed, or Fault.
    let decode (read: byte array -> int -> 'a * int) (bytes: byte array) : 'a * int
    let decodeAt (read: byte array -> int -> 'a * int) (bytes: byte array) (offset: int) : 'a * int
```

## Measures

Measures are structure of the native type universe. A value of type `'T<'m>` encodes as its representation `'T`; the dimension is compile-time structure the compiler carries, and nothing is tagged or untagged. Carrying dimensional annotations *on the wire*, so that two endpoints can check them, is Readiness Audit §4 step 15 and rides on everything above.

## Schema-driven encoding

Encoding a value through a schema at run time, by walking `SchemaDefinition` and boxing field values, was the earlier design and is not built: it needs reflection and `obj`, which the native substrate does not have. The schema-shared mode ([03](./03%20Schema%20System.md), Readiness Audit §4 step 14) instead *generates* the per-type module functions above from a schema, for each language that cannot share source. The source-shared mode writes them by hand beside the shared protocol types.

## Golden vectors

`tests/EncodingTests.fs` pins the byte-exact encoding of boundary values for every row of the wire-format table: `0`, `1`, `127`, `128`, `300`, `16383`, `16384`, and the 64-bit extremes for varints; the zigzag pairs; little-endian fixed widths; `1.0`, `-2.5` for floats; empty, ASCII, two-byte, and four-byte UTF-8 strings; and the aggregate tags. `samples/RoundTrip` prints the same values natively. Agreement between the two is the differential gate of Readiness Audit §4 step 4.

## Files

```
src/Encoding/
├── Cursor.fs              # the fault offset and bounds checks
├── Substrate/
│   ├── Text.Clef.fs       # String.toBytes / String.fromBytes (identity views)
│   ├── Text.Utf16.fs      # UTF-16 <-> UTF-8 transcoding for .NET and Fable
│   ├── Float.Clef.fs      # Bits.* intrinsics
│   ├── Float.Dotnet.fs    # pointer reinterpretation
│   └── Float.Fable.fs     # DataView
├── Fmt.fs                 # portable number formatting for the text residuals
├── Encoder.fs
├── Decoder.fs
└── Codec.fs
```
