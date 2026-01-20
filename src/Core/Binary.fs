#nowarn "9"

namespace BAREWire.Core

#if FABLE
open Fable.Core
open Fable.Core.JsInterop
#else
open FSharp.NativeInterop
#endif

/// <summary>
/// Pure F# binary conversion utilities (Dual-target: Firefly + Fable).
/// </summary>
/// <remarks>
/// This module is fully dual-target for WREN stack support:
/// - Firefly: Uses NativePtr for zero-copy bit reinterpretation
/// - Fable: Uses JavaScript DataView for equivalent operations
///
/// No BCL dependencies. All byte manipulation functions (getXXXBytes, toXXX)
/// are pure F# and work identically on both targets.
///
/// Used by the Encoding module for BAREWire serialization over WebSocket.
/// </remarks>
[<RequireQualifiedAccess>]
module Binary =

#if FABLE
    // JavaScript helpers for float/int bit conversion via DataView
    [<Emit("(function(v) { var b = new ArrayBuffer(4); var dv = new DataView(b); dv.setFloat32(0, v, true); return dv.getInt32(0, true); })($0)")>]
    let private jsFloat32ToInt32 (value: float32) : int32 = jsNative

    [<Emit("(function(v) { var b = new ArrayBuffer(4); var dv = new DataView(b); dv.setInt32(0, v, true); return dv.getFloat32(0, true); })($0)")>]
    let private jsInt32ToFloat32 (value: int32) : float32 = jsNative

    [<Emit("(function(v) { var b = new ArrayBuffer(8); var dv = new DataView(b); dv.setFloat64(0, v, true); return dv.getBigInt64(0, true); })($0)")>]
    let private jsFloat64ToInt64 (value: float) : int64 = jsNative

    [<Emit("(function(v) { var b = new ArrayBuffer(8); var dv = new DataView(b); dv.setBigInt64(0, v, true); return dv.getFloat64(0, true); })($0)")>]
    let private jsInt64ToFloat64 (value: int64) : float = jsNative
#endif

    /// <summary>
    /// Convert from float32 to Int32 bits
    /// </summary>
    let singleToInt32Bits (value: float32) : int32 =
#if FABLE
        jsFloat32ToInt32 value
#else
        let mutable v = value
        let valuePtr = &&v
        NativePtr.read (NativePtr.ofNativeInt<int32> (NativePtr.toNativeInt valuePtr))
#endif

    /// <summary>
    /// Convert from Int32 bits to float32
    /// </summary>
    let int32BitsToSingle (value: int32) : float32 =
#if FABLE
        jsInt32ToFloat32 value
#else
        let mutable v = value
        let valuePtr = &&v
        NativePtr.read (NativePtr.ofNativeInt<float32> (NativePtr.toNativeInt valuePtr))
#endif

    /// <summary>
    /// Convert from float to Int64 bits
    /// </summary>
    let doubleToInt64Bits (value: float) : int64 =
#if FABLE
        jsFloat64ToInt64 value
#else
        let mutable v = value
        let valuePtr = &&v
        NativePtr.read (NativePtr.ofNativeInt<int64> (NativePtr.toNativeInt valuePtr))
#endif

    /// <summary>
    /// Convert from Int64 bits to float
    /// </summary>
    let int64BitsToDouble (value: int64) : float =
#if FABLE
        jsInt64ToFloat64 value
#else
        let mutable v = value
        let valuePtr = &&v
        NativePtr.read (NativePtr.ofNativeInt<float> (NativePtr.toNativeInt valuePtr))
#endif

    // Little-endian helpers that work everywhere
    
    let getInt16Bytes (value: int16) : byte[] =
        [| byte (value &&& 0xFFs); byte (value >>> 8) |]
    
    let getUInt16Bytes (value: uint16) : byte[] =
        [| byte (value &&& 0xFFus); byte (value >>> 8) |]
    
    let getInt32Bytes (value: int32) : byte[] =
        let b0 = byte value
        let b1 = byte (value >>> 8)
        let b2 = byte (value >>> 16)
        let b3 = byte (value >>> 24)
        [| b0; b1; b2; b3 |]
    
    let getUInt32Bytes (value: uint32) : byte[] =
        let b0 = byte value
        let b1 = byte (value >>> 8)
        let b2 = byte (value >>> 16)
        let b3 = byte (value >>> 24)
        [| b0; b1; b2; b3 |]
    
    let getInt64Bytes (value: int64) : byte[] =
        [| byte (value &&& 0xFFL); byte ((value >>> 8) &&& 0xFFL); byte ((value >>> 16) &&& 0xFFL); byte ((value >>> 24) &&& 0xFFL)
           byte ((value >>> 32) &&& 0xFFL); byte ((value >>> 40) &&& 0xFFL); byte ((value >>> 48) &&& 0xFFL); byte ((value >>> 56) &&& 0xFFL) |]
    
    let getUInt64Bytes (value: uint64) : byte[] =
        [| byte (value &&& 0xFFUL); byte ((value >>> 8) &&& 0xFFUL); byte ((value >>> 16) &&& 0xFFUL); byte ((value >>> 24) &&& 0xFFUL)
           byte ((value >>> 32) &&& 0xFFUL); byte ((value >>> 40) &&& 0xFFUL); byte ((value >>> 48) &&& 0xFFUL); byte ((value >>> 56) &&& 0xFFUL) |]

    let toInt16 (bytes: byte[]) (startIndex: int) : int16 =
        int16 bytes.[startIndex] ||| (int16 bytes.[startIndex + 1] <<< 8)
    
    let toUInt16 (bytes: byte[]) (startIndex: int) : uint16 =
        uint16 bytes.[startIndex] ||| (uint16 bytes.[startIndex + 1] <<< 8)
    
    let toInt32 (bytes: byte[]) (startIndex: int) : int32 =
        let b0 : int32 = int32 bytes.[startIndex]
        let b1 : int32 = int32 bytes.[startIndex + 1]
        let b2 : int32 = int32 bytes.[startIndex + 2]
        let b3 : int32 = int32 bytes.[startIndex + 3]
        let r1 : int32 = b1 <<< 8
        let r2 : int32 = b2 <<< 16
        let r3 : int32 = b3 <<< 24
        b0 ||| r1 ||| r2 ||| r3
    
    let toUInt32 (bytes: byte[]) (startIndex: int) : uint32 =
        uint32 bytes.[startIndex] ||| (uint32 bytes.[startIndex + 1] <<< 8) ||| (uint32 bytes.[startIndex + 2] <<< 16) ||| (uint32 bytes.[startIndex + 3] <<< 24)
    
    let toInt64 (bytes: byte[]) (startIndex: int) : int64 =
        let b0 = int64 bytes.[startIndex]
        let b1 = int64 bytes.[startIndex + 1]
        let b2 = int64 bytes.[startIndex + 2]
        let b3 = int64 bytes.[startIndex + 3]
        let b4 = int64 bytes.[startIndex + 4]
        let b5 = int64 bytes.[startIndex + 5]
        let b6 = int64 bytes.[startIndex + 6]
        let b7 = int64 bytes.[startIndex + 7]
        b0 ||| (b1 <<< 8) ||| (b2 <<< 16) ||| (b3 <<< 24) |||
        (b4 <<< 32) ||| (b5 <<< 40) ||| (b6 <<< 48) ||| (b7 <<< 56)
    
    let toUInt64 (bytes: byte[]) (startIndex: int) : uint64 =
        uint64 bytes.[startIndex] ||| (uint64 bytes.[startIndex + 1] <<< 8) ||| (uint64 bytes.[startIndex + 2] <<< 16) ||| (uint64 bytes.[startIndex + 3] <<< 24) |||
        (uint64 bytes.[startIndex + 4] <<< 32) ||| (uint64 bytes.[startIndex + 5] <<< 40) ||| (uint64 bytes.[startIndex + 6] <<< 48) ||| (uint64 bytes.[startIndex + 7] <<< 56)
