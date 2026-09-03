namespace BAREWire.Encoding

open Fable.Core

/// IEEE-754 bit casts in JavaScript via a scratch `DataView`.
/// Selected by `BAREWire.Fable.fsproj`. This is the DataView shim the
/// Conclave W-05 roadmap item names; it is the only JavaScript-specific code
/// in the Encoding tier.
module Float =

    [<Emit("(function(v){var d=new DataView(new ArrayBuffer(4));d.setFloat32(0,v,true);return d.getInt32(0,true);})($0)")>]
    let f32ToBits (v: float32) : int32 = jsNative

    [<Emit("(function(b){var d=new DataView(new ArrayBuffer(4));d.setInt32(0,b,true);return d.getFloat32(0,true);})($0)")>]
    let bitsToF32 (bits: int32) : float32 = jsNative

    [<Emit("(function(v){var d=new DataView(new ArrayBuffer(8));d.setFloat64(0,v,true);return d.getBigInt64(0,true);})($0)")>]
    let f64ToBits (v: float) : int64 = jsNative

    [<Emit("(function(b){var d=new DataView(new ArrayBuffer(8));d.setBigInt64(0,BigInt(b),true);return d.getFloat64(0,true);})($0)")>]
    let bitsToF64 (bits: int64) : float = jsNative
