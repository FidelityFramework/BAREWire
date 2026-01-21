namespace BAREWire.Encoding

open BAREWire.Core

/// <summary>
/// BAREWire encoding functions (Dual-target: Firefly + Fable).
/// </summary>
/// <remarks>
/// This module provides BARE-compatible binary encoding for both targets:
/// - Firefly: Native compilation with FNCS string semantics
/// - Fable: JavaScript compilation for WREN stack WebSocket IPC
///
/// All functions use the dual-target Buffer type and pure F# operations.
/// String encoding uses Utf8 module which handles platform differences.
///
/// BARE encoding format:
/// - Integers: ULEB128 (unsigned) or zigzag ULEB128 (signed)
/// - Strings: Length-prefixed UTF-8 bytes
/// - Booleans: Single byte (0 or 1)
/// </remarks>
module Encoder =

    /// Writes a uint64 using ULEB128 encoding
    let inline writeUInt (buffer: Buffer byref) (value: uint64) =
        let mutable v = value
        while v >= 128UL do
            Buffer.writeByte &buffer (byte (v ||| 128UL))
            v <- v >>> 7
        Buffer.writeByte &buffer (byte v)

    /// Writes an int64 using zigzag ULEB128 encoding
    let inline writeInt (buffer: Buffer byref) (value: int64) =
        let zigzag = (value <<< 1) ^^^ (value >>> 63)
        writeUInt &buffer (uint64 zigzag)

    /// <summary>
    /// Writes a string to the buffer.
    /// In Firefly (NTU), string is ALREADY UTF-8, so we just copy bytes.
    /// In .NET/Fable, we must encode from UTF-16 to UTF-8.
    /// </summary>
    let inline writeString (buffer: Buffer byref) (s: string) =
        // Use UTF-8 encoding - works for both .NET and Fable
        // In Firefly, FNCS provides native string semantics
        let bytes = String.toBytes s
        writeUInt &buffer (uint64 bytes.Length)
        for b in bytes do Buffer.writeByte &buffer b

    /// Writes a boolean (single byte)
    let inline writeBool (buffer: Buffer byref) (v: bool) =
        Buffer.writeByte &buffer (if v then 1uy else 0uy)
