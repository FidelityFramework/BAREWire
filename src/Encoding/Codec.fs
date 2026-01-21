namespace BAREWire.Encoding

open BAREWire.Encoding
open BAREWire.Encoding.Memory

/// <summary>
/// Static Codec interface for BAREWire.
/// Replaces runtime reflection with compile-time witnesses.
/// </summary>
type IBARECodec<'T> =
    abstract member Encode: Buffer byref -> 'T -> unit
    abstract member Decode: Memory -> int -> 'T * int

/// <summary>
/// High-level API for BAREWire operations.
/// </summary>
module Codec =

    /// <summary>
    /// Encodes a value to a buffer using its static codec.
    /// </summary>
    let inline encode (codec: IBARECodec<'T>) (value: 'T) (buffer: Buffer byref) =
        codec.Encode &buffer value

    /// <summary>
    /// Decodes a value from a memory region.
    /// </summary>
    let inline decode (codec: IBARECodec<'T>) (data: Memory) =
        codec.Decode data 0
