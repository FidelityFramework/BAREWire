namespace BAREWire.Core

/// <summary>
/// Capability-based memory access types for Fidelity's memory management.
/// Capabilities combine pointer, bounds, permissions, and lifetime into a single type-safe abstraction.
/// </summary>
/// <remarks>
/// The capability model follows: cap = (address, length, permissions, lifetime)
/// This is fundamentally different from traditional pointers: ptr = address
/// 
/// Benefits:
/// - Compile-time bounds checking via length
/// - Permission enforcement via access measures
/// - Lifetime tracking prevents dangling references
/// - Type-safe memory operations
/// </remarks>
[<AutoOpen>]
module Capability =

    // ============================================================================
    // LIFETIME MARKERS
    // ============================================================================
    
    /// <summary>
    /// Lifetime markers as units of measure.
    /// These encode when memory becomes invalid.
    /// </summary>
    module Lifetime =
        /// <summary>
        /// Static lifetime - lives forever (global data, string literals in .rodata)
        /// </summary>
        [<Measure>] type static'
        
        /// <summary>
        /// Stack lifetime - dies when creating stack frame pops
        /// </summary>
        [<Measure>] type stack
        
        /// <summary>
        /// Caller lifetime - survives return to caller's frame
        /// Used when callee writes into buffer provided by caller
        /// </summary>
        [<Measure>] type caller
        
        /// <summary>
        /// Arena lifetime - dies when arena is freed
        /// Used for RAII actor arenas
        /// </summary>
        [<Measure>] type arena
        
        /// <summary>
        /// Heap lifetime - explicit management required
        /// Must be explicitly freed (rare in Fidelity)
        /// </summary>
        [<Measure>] type heap

    // ============================================================================
    // ACCESS MARKERS
    // ============================================================================
    
    /// <summary>
    /// Access permission markers as units of measure.
    /// Encode what operations are permitted on memory.
    /// </summary>
    module Access =
        /// <summary>
        /// Read-only access - memory may be read but not written
        /// </summary>
        [<Measure>] type ro
        
        /// <summary>
        /// Write-only access - memory may be written but not read
        /// </summary>
        [<Measure>] type wo
        
        /// <summary>
        /// Read-write access - memory may be both read and written
        /// </summary>
        [<Measure>] type rw

    // ============================================================================
    // REGION MARKERS
    // ============================================================================
    
    /// <summary>
    /// Memory region markers as units of measure.
    /// Prevent mixing pointers from different regions.
    /// </summary>
    module Region =
        /// <summary>
        /// General-purpose memory region
        /// </summary>
        [<Measure>] type mem
        
        /// <summary>
        /// I/O buffer region
        /// </summary>
        [<Measure>] type io
        
        /// <summary>
        /// String data region (UTF-8)
        /// </summary>
        [<Measure>] type str

    // ============================================================================
    // CAPABILITY TYPE
    // ============================================================================
    
    /// <summary>
    /// A capability is a type-safe reference to memory with bounds, permissions, and lifetime.
    /// </summary>
    /// <typeparam name="'T">The element type of the memory</typeparam>
    /// <typeparam name="'region">The memory region (prevents cross-region confusion)</typeparam>
    /// <typeparam name="'access">The access permissions (ro, wo, rw)</typeparam>
    /// <typeparam name="'lifetime">The lifetime scope (static', stack, caller, arena, heap)</typeparam>
    /// <remarks>
    /// This is the core abstraction that makes memory safety possible without GC.
    /// The type system tracks all four capability components:
    /// - Pointer: where the memory is
    /// - Length: how much memory is accessible
    /// - Access: what operations are permitted
    /// - Lifetime: when the memory becomes invalid
    /// </remarks>
    [<Struct>]
    type Cap<'T, [<Measure>] 'region, [<Measure>] 'access, [<Measure>] 'lifetime> = {
        /// <summary>
        /// The base address of the memory region
        /// </summary>
        Pointer: nativeint
        
        /// <summary>
        /// The length in bytes of the accessible region
        /// </summary>
        Length: int
    }

    // ============================================================================
    // STRING CAPABILITY
    // ============================================================================
    
    /// <summary>
    /// A string capability is a UTF-8 string reference with lifetime tracking.
    /// This is the Fidelity native string representation.
    /// </summary>
    /// <typeparam name="'lifetime">The lifetime scope of the string data</typeparam>
    /// <remarks>
    /// - String literals: StringCap&lt;Lifetime.static'&gt;
    /// - Read from stdin: StringCap&lt;Lifetime.caller&gt; (buffer outlives readln frame)
    /// - Concatenation result: depends on allocation strategy
    /// 
    /// The Length field contains the byte count, NOT character count.
    /// F# string operations work with this representation transparently.
    /// </remarks>
    [<Struct>]
    type StringCap<[<Measure>] 'lifetime> = {
        /// <summary>
        /// Pointer to UTF-8 encoded string data (NOT null-terminated)
        /// </summary>
        Data: nativeint
        
        /// <summary>
        /// Length in bytes of the UTF-8 data
        /// </summary>
        Length: int
    }

    // ============================================================================
    // BUFFER CAPABILITY
    // ============================================================================
    
    /// <summary>
    /// A mutable buffer capability for I/O and accumulation.
    /// </summary>
    /// <typeparam name="'T">The element type</typeparam>
    /// <typeparam name="'lifetime">The lifetime scope</typeparam>
    [<Struct>]
    type BufferCap<'T, [<Measure>] 'lifetime> = {
        /// <summary>
        /// Pointer to buffer storage
        /// </summary>
        Data: nativeint
        
        /// <summary>
        /// Total capacity in elements
        /// </summary>
        Capacity: int
        
        /// <summary>
        /// Current position (mutable during writes)
        /// </summary>
        mutable Position: int
    }

    // ============================================================================
    // CAPABILITY OPERATIONS
    // ============================================================================
    
    /// <summary>
    /// Operations on capabilities - module functions, not interface methods.
    /// Following pure F# design principles.
    /// </summary>
    module Cap =
        /// <summary>
        /// Creates a capability from a raw pointer and length.
        /// UNSAFE: Caller must ensure pointer validity and lifetime.
        /// </summary>
        let inline create<'T, [<Measure>] 'region, [<Measure>] 'access, [<Measure>] 'lifetime> 
            (ptr: nativeint) 
            (len: int) 
            : Cap<'T, 'region, 'access, 'lifetime> =
            { Pointer = ptr; Length = len }
        
        /// <summary>
        /// Gets the length of a capability in bytes.
        /// </summary>
        let inline length (cap: Cap<'T, 'region, 'access, 'lifetime>) = cap.Length
        
        /// <summary>
        /// Creates a narrowed view of a capability (slice operation).
        /// No memory copy - just a narrower capability with same lifetime.
        /// </summary>
        /// <param name="cap">The source capability</param>
        /// <param name="offset">Start offset in bytes</param>
        /// <param name="len">Length of slice in bytes</param>
        let inline slice<'T, [<Measure>] 'region, [<Measure>] 'access, [<Measure>] 'lifetime>
            (cap: Cap<'T, 'region, 'access, 'lifetime>)
            (offset: int)
            (len: int)
            : Cap<'T, 'region, 'access, 'lifetime> =
            if offset < 0 || len < 0 || offset + len > cap.Length then
                failwith "Slice out of bounds"
            { Pointer = cap.Pointer + nativeint offset; Length = len }
        
        /// <summary>
        /// Drops write permission, converting rw to ro.
        /// </summary>
        let inline asReadOnly (cap: Cap<'T, 'region, Access.rw, 'lifetime>) 
            : Cap<'T, 'region, Access.ro, 'lifetime> =
            { Pointer = cap.Pointer; Length = cap.Length }

    // ============================================================================
    // STRING CAPABILITY OPERATIONS
    // ============================================================================
    
    /// <summary>
    /// Operations on string capabilities.
    /// </summary>
    module StringCap =
        /// <summary>
        /// Creates a string capability from pointer and length.
        /// </summary>
        let inline create<[<Measure>] 'lifetime> (ptr: nativeint) (len: int) 
            : StringCap<'lifetime> =
            { Data = ptr; Length = len }
        
        /// <summary>
        /// Gets the byte length of a string capability.
        /// </summary>
        let inline length (s: StringCap<'lifetime>) = s.Length
        
        /// <summary>
        /// Checks if a string capability is empty.
        /// </summary>
        let inline isEmpty (s: StringCap<'lifetime>) = s.Length = 0

    // ============================================================================
    // BUFFER CAPABILITY OPERATIONS
    // ============================================================================
    
    /// <summary>
    /// Operations on buffer capabilities.
    /// </summary>
    module BufferCap =
        /// <summary>
        /// Creates a buffer capability from pointer and capacity.
        /// </summary>
        let inline create<'T, [<Measure>] 'lifetime> (ptr: nativeint) (capacity: int) 
            : BufferCap<'T, 'lifetime> =
            { Data = ptr; Capacity = capacity; Position = 0 }
        
        /// <summary>
        /// Gets remaining capacity in a buffer.
        /// </summary>
        let inline remaining (buf: BufferCap<'T, 'lifetime>) = 
            buf.Capacity - buf.Position
        
        /// <summary>
        /// Converts a buffer to a string capability of the written portion.
        /// The resulting string has the SAME lifetime as the buffer.
        /// </summary>
        let inline toStringCap (buf: BufferCap<byte, 'lifetime>) : StringCap<'lifetime> =
            { Data = buf.Data; Length = buf.Position }

    // ============================================================================
    // ARENA TYPE (FNCS Intrinsic)
    // ============================================================================
    //
    // Arena<'lifetime> is now an FNCS intrinsic type, not a library type.
    // The type and operations (Arena.fromPointer, Arena.alloc, Arena.allocAligned,
    // Arena.remaining, Arena.reset) are provided by FNCS.
    //
    // Usage remains the same:
    //   let mutable arena = Arena.fromPointer ptr 4096
    //   let ptr = Arena.alloc &arena 256
    //
    // The lifetime measure parameter tracks the arena's scope at compile time.
    // All allocations from an arena share its lifetime.
    // ============================================================================
