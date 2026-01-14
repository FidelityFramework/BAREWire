namespace BAREWire.Encoding

open BAREWire.Core

/// <summary>
/// Static Codec interface for BAREWire.
/// Replaces runtime reflection with compile-time witnesses.
/// </summary>
type IBARECodec<'T> =
    abstract member Encode: IBuffer -> 'T -> unit
    abstract member Decode: Span<byte> -> byref<int> -> 'T

/// <summary>
/// High-level API for BAREWire operations.
/// </summary>
module Codec =
    
    /// <summary>
    /// Encodes a value to a buffer using its static codec.
    /// </summary>
    let inline encode (codec: IBARECodec<'T>) (value: 'T) (buffer: IBuffer) =
        codec.Encode buffer value

    /// <summary>
    /// Decodes a value from a memory span.
    /// </summary>
    let inline decode (codec: IBARECodec<'T>) (data: Span<byte>) =
        let mutable offset = 0
        codec.Decode data &offset
