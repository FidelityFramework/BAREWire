namespace BAREWire.Encoding

/// Whole-value codecs as module-function combinators.
///
/// A codec for a type is a pair of module functions in the shape the Encoder
/// and Decoder use, `write: byte array -> int -> 'a -> int` and
/// `read: byte array -> int -> 'a * int`, written beside the type by hand or
/// generated from a schema (docs/03, schema-shared mode). There is no codec
/// interface: shared code is module functions in the intersection subset
/// (Readiness Audit §5).
///
/// Results follow the cursor convention rather than `option`
/// (SUBSET(generic-option): a generic `'a option` construction does not
/// witness in the current Composer snapshot; the preferred spelling when it
/// does is `byte array option` and `'a option`).
module Codec =

    /// Encode a value into a buffer of the declared capacity. Returns the
    /// bytes written and their length, or an empty array and `Cursor.Fault`
    /// when the value does not fit or the writer faulted. Capacity is a
    /// declared fact, not a growth policy: a buffer schema names it, and
    /// exceeding it is a checked fault.
    let encode (capacity: int) (write: byte array -> int -> 'a -> int) (value: 'a) : byte array * int =
        let size = if capacity < 0 then 0 else capacity
        let data : byte array = Array.zeroCreate size
        let next = write data 0 value
        if Cursor.isOk next then (Array.sub data 0 next, next) else (Array.zeroCreate 0, Cursor.Fault)

    /// Decode a value from bytes, requiring exact consumption. Returns the
    /// value and the bytes consumed, or `Cursor.Fault` when the reader
    /// faulted or bytes remain.
    let decode (read: byte array -> int -> 'a * int) (bytes: byte array) : 'a * int =
        let v, next = read bytes 0
        let exact = Cursor.isOk next && next = Array.length bytes
        (v, (if exact then next else Cursor.Fault))

    /// Decode a value at an offset without the exact-consumption rule; the
    /// caller receives the next offset and applies its own rule.
    let decodeAt (read: byte array -> int -> 'a * int) (bytes: byte array) (offset: int) : 'a * int =
        read bytes offset
