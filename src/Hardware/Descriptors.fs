namespace BAREWire.Hardware

/// Memory region classification (from ARM memory map)
type MemoryRegionKind =
    /// Code/constants, read-only at runtime
    | Flash
    /// Normal RAM: stack, heap, globals
    | SRAM
    /// Memory-mapped I/O, volatile
    | Peripheral
    /// ARM core peripherals: NVIC, SysTick
    | SystemControl
    /// DMA-accessible regions
    | DMA
    /// Core-coupled memory (if present)
    | CCM

/// Hardware-enforced access constraints (from CMSIS __I/__O/__IO)
type AccessKind =
    /// __I - reads hardware state, writes are UB
    | ReadOnly
    /// __O - writes trigger action, reads undefined
    | WriteOnly
    /// __IO - normal volatile access
    | ReadWrite

/// Bit field within a register
[<Struct>]
type BitFieldDescriptor = {
    /// Name of the bit field
    Name: string
    /// Bit position (0-31 for 32-bit registers)
    Position: int
    /// Width in bits
    Width: int
    /// Access constraints for this field
    Access: AccessKind
}

/// Single register/field within a peripheral
type FieldDescriptor = {
    /// Name of the register
    Name: string
    /// Byte offset from peripheral base
    Offset: int
    /// Native type of the register
    Type: NTUKind
    /// Access constraints
    Access: AccessKind
    /// Bit fields within this register (empty for simple fields)
    BitFields: BitFieldDescriptor array
    /// Optional documentation
    Documentation: string
}

/// Layout of a peripheral's register set
type PeripheralLayout = {
    /// Total size in bytes
    Size: int
    /// Required alignment
    Alignment: int
    /// Fields/registers in this peripheral
    Fields: FieldDescriptor array
}

/// Describes a memory-mapped peripheral
type PeripheralDescriptor = {
    /// Peripheral name (e.g., "GPIOA", "USART1")
    Name: string
    /// Base addresses for each instance (e.g., "GPIOA" -> 0x40020000)
    Instances: (string * unativeint) array
    /// Register layout
    Layout: PeripheralLayout
    /// Memory region this peripheral belongs to
    MemoryRegion: MemoryRegionKind
}

/// Volatility classification for memory regions
type VolatilityKind = | VolatileRegion | NonVolatileRegion

/// Cacheability classification for memory regions
type CacheabilityKind = | CacheableRegion | NonCacheableRegion

/// Executability classification for memory regions
type ExecutabilityKind = | ExecutableRegion | DataOnlyRegion

/// Functions for memory region classification (active patterns not yet supported in FNCS)
module MemoryRegion =
    /// Classifies whether a region requires volatile access
    let getVolatility region : VolatilityKind =
        match region with
        | Peripheral | SystemControl -> VolatileRegion
        | Flash | SRAM | DMA | CCM -> NonVolatileRegion

    /// Classifies whether a region is cacheable
    let getCacheability region : CacheabilityKind =
        match region with
        | Flash | SRAM | CCM -> CacheableRegion
        | Peripheral | SystemControl | DMA -> NonCacheableRegion

    /// Classifies whether a region allows code execution
    let getExecutability region : ExecutabilityKind =
        match region with
        | Flash | SRAM -> ExecutableRegion
        | Peripheral | SystemControl | DMA | CCM -> DataOnlyRegion

    /// Check if region is volatile
    let isVolatile region : bool =
        match getVolatility region with
        | VolatileRegion -> true
        | NonVolatileRegion -> false

    /// Check if region is cacheable
    let isCacheable region : bool =
        match getCacheability region with
        | CacheableRegion -> true
        | NonCacheableRegion -> false

    /// Check if region is executable
    let isExecutable region : bool =
        match getExecutability region with
        | ExecutableRegion -> true
        | DataOnlyRegion -> false

/// Module for constructing field descriptors
module Field =
    /// Create a simple field with no bit fields
    let simple name offset ntuKind access : FieldDescriptor =
        { Name = name
          Offset = offset
          Type = ntuKind
          Access = access
          BitFields = Array.zeroCreate 0
          Documentation = "" }

    /// Create a field with documentation
    let withDoc doc (field: FieldDescriptor) : FieldDescriptor =
        { field with Documentation = doc }

    /// Create a field with bit fields
    let withBitFields bitFields (field: FieldDescriptor) : FieldDescriptor =
        { field with BitFields = bitFields }

/// Module for constructing bit field descriptors
module BitField =
    /// Create a bit field descriptor
    let create name position width access : BitFieldDescriptor =
        { Name = name
          Position = position
          Width = width
          Access = access }

    /// Create a single-bit flag
    let flag name position access : BitFieldDescriptor =
        create name position 1 access

/// Module for constructing peripheral descriptors
module Peripheral =
    /// Create a peripheral with single instance
    let single name baseAddr layout region : PeripheralDescriptor =
        { Name = name
          Instances = [| (name, baseAddr) |]
          Layout = layout
          MemoryRegion = region }

    /// Create a peripheral with multiple instances
    let multi name instances layout region : PeripheralDescriptor =
        { Name = name
          Instances = instances
          Layout = layout
          MemoryRegion = region }

/// Module for constructing peripheral layouts
module Layout =
    /// Create a layout from fields
    let create size alignment fields : PeripheralLayout =
        { Size = size
          Alignment = alignment
          Fields = fields }

    /// Create a layout with natural alignment
    let withNaturalAlignment size fields : PeripheralLayout =
        create size 4 fields  // Default 4-byte alignment for ARM
