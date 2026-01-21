namespace BAREWire.Encoding

/// Memory abstractions for BAREWire serialization.
[<AutoOpen>]
module Memory =

    // ============================================================================
    // MEMORY TYPE - Simple byte buffer view
    // ============================================================================

    /// A view into a memory buffer.
    [<Struct>]
    type Memory = {
        /// The underlying byte storage
        Data: byte array

        /// Offset into the data array in bytes
        Offset: int

        /// Length of this view in bytes
        Length: int
    }

    // ============================================================================
    // BUFFER TYPE - Sequential write buffer
    // ============================================================================

    /// A mutable buffer for sequential writes.
    [<Struct>]
    type Buffer = {
        /// The underlying byte storage
        Data: byte array

        /// Current write position
        mutable Position: int
    }

    // ============================================================================
    // MEMORY OPERATIONS
    // ============================================================================

    module Memory =
        /// Creates a memory view from a byte array.
        let fromArray (data: byte array) : Memory =
            { Data = data; Offset = 0; Length = data.Length }

        /// Creates a zero-filled memory region.
        let createZeroed (size: int) : Memory =
            fromArray (Array.zeroCreate size)

        /// Creates a slice of an existing memory region.
        let slice (mem: Memory) (off: int) (len: int) : Memory =
            let newOffset = mem.Offset + off
            if newOffset < 0 || len < 0 || newOffset + len > mem.Data.Length then
                failwith "Slice out of bounds"
            { Data = mem.Data; Offset = newOffset; Length = len }

        /// Copies bytes from one memory region to another.
        let copy (src: Memory) (dst: Memory) (count: int) : unit =
            if count < 0 || src.Offset + count > src.Data.Length || dst.Offset + count > dst.Data.Length then
                failwith "Copy out of bounds"
            Array.blit src.Data src.Offset dst.Data dst.Offset count

    // ============================================================================
    // BUFFER OPERATIONS
    // ============================================================================

    module Buffer =
        /// Creates a buffer with the specified capacity.
        let create (capacity: int) : Buffer =
            { Data = Array.zeroCreate capacity; Position = 0 }

        /// Writes a byte to the buffer.
        let writeByte (buf: Buffer byref) (b: byte) : unit =
            if buf.Position >= buf.Data.Length then
                failwith "Buffer overflow"
            buf.Data.[buf.Position] <- b
            buf.Position <- buf.Position + 1

        /// Writes multiple bytes to the buffer.
        let writeBytes (buf: Buffer byref) (bytes: byte array) : unit =
            if buf.Position + bytes.Length > buf.Data.Length then
                failwith "Buffer overflow"
            Array.blit bytes 0 buf.Data buf.Position bytes.Length
            buf.Position <- buf.Position + bytes.Length

        /// Gets the written data as a memory view.
        let toMemory (buf: Buffer) : Memory =
            { Data = buf.Data; Offset = 0; Length = buf.Position }

        /// Resets the buffer position to the beginning.
        let reset (buf: Buffer byref) : unit =
            buf.Position <- 0

        /// Gets the remaining capacity.
        let remaining (buf: Buffer) : int =
            buf.Data.Length - buf.Position

        /// Gets the current position.
        let position (buf: Buffer) : int =
            buf.Position
