namespace BAREWire.Core

open FSharp.UMX
#if !FABLE
open FSharp.NativeInterop
#endif

/// <summary>
/// Core memory abstractions for BAREWire.
/// </summary>
/// <remarks>
/// This module contains two categories of types:
///
/// **Dual-Target (Firefly + Fable):**
/// - Buffer: Sequential write buffer used by Encoding module for WREN stack
///
/// **Firefly-Only:**
/// - Memory&lt;'T,'region&gt;: Capability-based memory with region/lifetime tracking
/// - fromNativePointer: Native pointer interop
///
/// The dual-target Buffer enables BAREWire encoding over WebSocket in WREN stack apps.
/// The Firefly-only Memory types provide compile-time memory safety guarantees.
/// </remarks>
[<AutoOpen>]
module Memory =

    // ============================================================================
    // MEMORY TYPE (Firefly only - capability-based memory model)
    // ============================================================================
    
    /// <summary>
    /// A typed view into a memory buffer.
    /// Uses measure types for region and lifetime safety.
    /// </summary>
    /// <typeparam name="'T">The logical type this memory represents</typeparam>
    /// <typeparam name="'region">Memory region marker (prevents cross-region errors)</typeparam>
    [<Struct>]
    type Memory<'T, [<Measure>] 'region> = {
        /// <summary>
        /// The underlying byte storage
        /// </summary>
        Data: byte[]
        
        /// <summary>
        /// Offset into the data array in bytes
        /// </summary>
        Offset: int<offset>
        
        /// <summary>
        /// Length of this view in bytes
        /// </summary>
        Length: int<bytes>
    }

    // ============================================================================
    // BUFFER TYPE (Dual-target: Firefly + Fable)
    // Used by Encoding module for WREN stack WebSocket communication
    // ============================================================================

    /// <summary>
    /// A mutable buffer for sequential writes.
    /// Uses struct for value semantics and stack allocation.
    /// </summary>
    /// <remarks>
    /// This type is available in both Firefly and Fable targets.
    /// In WREN stack apps, it enables BAREWire encoding for WebSocket IPC.
    /// </remarks>
    [<Struct>]
    type Buffer = {
        /// <summary>
        /// The underlying byte storage
        /// </summary>
        Data: byte[]
        
        /// <summary>
        /// Current write position
        /// </summary>
        mutable Position: int
    }

    // ============================================================================
    // MEMORY OPERATIONS (Firefly only)
    // ============================================================================

    /// <summary>
    /// Operations on Memory - pure module functions.
    /// </summary>
    /// <remarks>
    /// These operations are Firefly-only. They work with the capability-based
    /// Memory type that provides compile-time region and lifetime safety.
    /// </remarks>
    module Memory =
        /// <summary>
        /// Creates a memory view from a byte array.
        /// </summary>
        let fromArray<'T, [<Measure>] 'region> (data: byte[]) : Memory<'T, 'region> =
            { Data = data
              Offset = 0<offset>
              Length = LanguagePrimitives.Int32WithMeasure<bytes> data.Length }
        
        /// <summary>
        /// Creates a zero-filled memory region.
        /// </summary>
        let createZeroed<'T, [<Measure>] 'region> (size: int<bytes>) : Memory<'T, 'region> =
            fromArray<'T, 'region> (Array.zeroCreate (int size))
        
        /// <summary>
        /// Creates a slice of an existing memory region.
        /// No copy - just a narrower view.
        /// </summary>
        let slice<'T, 'U, [<Measure>] 'region> 
                 (mem: Memory<'T, 'region>) 
                 (off: int<offset>) 
                 (len: int<bytes>) 
                 : Memory<'U, 'region> =
            let newOffset = int mem.Offset + int off
            if newOffset < 0 || int len < 0 || newOffset + int len > mem.Data.Length then
                failwith "Slice out of bounds"
            { Data = mem.Data
              Offset = LanguagePrimitives.Int32WithMeasure<offset> newOffset
              Length = len }
        
        /// <summary>
        /// Copies data between memory regions.
        /// </summary>
        let copy<'T, 'U, [<Measure>] 'region1, [<Measure>] 'region2>
                (src: Memory<'T, 'region1>)
                (dst: Memory<'U, 'region2>)
                (count: int<bytes>) : unit =
            let srcOff = int src.Offset
            let dstOff = int dst.Offset
            let cnt = int count
            if srcOff + cnt > src.Data.Length || dstOff + cnt > dst.Data.Length then
                failwith "Copy out of bounds"
            Array.blit src.Data srcOff dst.Data dstOff cnt

    // ============================================================================
    // BUFFER OPERATIONS (Dual-target: Firefly + Fable)
    // ============================================================================

    /// <summary>
    /// Operations on Buffer - pure module functions.
    /// </summary>
    /// <remarks>
    /// These operations are available in both Firefly and Fable targets.
    /// Used by the Encoding module for BAREWire serialization.
    /// </remarks>
    module Buffer =
        /// <summary>
        /// Creates a buffer with specified capacity.
        /// </summary>
        let create (capacity: int) : Buffer =
            { Data = Array.zeroCreate capacity; Position = 0 }
        
        /// <summary>
        /// Writes a single byte to the buffer.
        /// </summary>
        let writeByte (buf: Buffer byref) (b: byte) : unit =
            if buf.Position >= buf.Data.Length then
                failwith "Buffer overflow"
            buf.Data.[buf.Position] <- b
            buf.Position <- buf.Position + 1
        
        /// <summary>
        /// Writes a span of bytes to the buffer.
        /// </summary>
        let writeBytes (buf: Buffer byref) (bytes: byte[]) : unit =
            let len = bytes.Length
            if buf.Position + len > buf.Data.Length then
                failwith "Buffer overflow"
            Array.blit bytes 0 buf.Data buf.Position len
            buf.Position <- buf.Position + len
        
        /// <summary>
        /// Gets the written portion of a buffer as a byte array.
        /// </summary>
        let toArray (buf: Buffer) : byte[] =
            buf.Data.[0 .. buf.Position - 1]
        
        /// <summary>
        /// Resets the buffer position to the beginning.
        /// </summary>
        let reset (buf: Buffer byref) : unit =
            buf.Position <- 0

#if !FABLE
    // ============================================================================
    // NATIVE POINTER OPERATIONS (Firefly only)
    // ============================================================================

    /// <summary>
    /// Creates a memory view from a native pointer.
    /// For Firefly native compilation only.
    /// </summary>
    let inline fromNativePointer<'T, [<Measure>] 'region> (ptr: nativeint) (size: int<bytes>) : Memory<'T, 'region> =
        // In Firefly, this creates a capability-backed memory view
        // The actual implementation is provided by FNCS NativePtr intrinsics
        { Data = Array.zeroCreate (int size)  // Placeholder - Firefly provides native backing
          Offset = 0<offset>
          Length = size }
#endif
