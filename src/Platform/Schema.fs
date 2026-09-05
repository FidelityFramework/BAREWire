namespace BAREWire.Platform

open BAREWire.Hardware

/// The vocabulary's own schema (clef Dimensional_Range_Design.md, ruling 4 of
/// CS-12): the description records are BAREWire schema types, so each of their
/// integer fields is a wire-schema field in the sense of that note's §4.1, and
/// the schema, not the compiler, declares the field's representation. Each
/// descriptor below names one record type of this vocabulary and, for each of
/// its integer fields, the representation a value of that field holds; the
/// compiler's one structural reader seeds a read of such a field with the
/// representation's range, the same path as any wire record, and invents no
/// bound of its own. A field with no declaration here stays unobservable to
/// the compiler (CCS8011, naming the missing declaration).
///
/// Every descriptor is a literal record: a declaration is read structurally,
/// by type name and field name, and a value built through a function is not
/// a declaration. Each name is qualified by its namespace's last segment
/// (`Platform.WidthDeclaration`): a program that also compiles the Contracts
/// twin of a record (`Platform.Contracts.WidthDeclaration`, an FPGA leaf)
/// would otherwise leave the bare name naming two types (CCS8208). Only the integer fields are declared: the descriptor states
/// each field's representation, not a serialised byte layout of the record
/// (the string and array fields are not wire fields, and no description
/// crosses a wire); offsets are consecutive for the same reason and are not
/// read.
module Schema =

    /// `WidthDeclaration.Bits`: a width dimension's bits.
    let widthDeclaration : StructDescriptor =
        { Name = "Platform.WidthDeclaration"
          Layout =
            { Size = 2
              Alignment = 1
              Fields = [| { Name = "Bits"; Offset = 0; Repr = Repr.U16; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None } |] }
          Documentation = Some "the bits of one declared width dimension" }

    /// `Representation.Bits`: a numeric representation's bits.
    let representation : StructDescriptor =
        { Name = "Platform.Representation"
          Layout =
            { Size = 2
              Alignment = 1
              Fields = [| { Name = "Bits"; Offset = 0; Repr = Repr.U16; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None } |] }
          Documentation = Some "the bits of one offered numeric representation" }

    /// `TargetCore.WordSizeBits`: the core's word size in bits.
    let targetCore : StructDescriptor =
        { Name = "Platform.TargetCore"
          Layout =
            { Size = 2
              Alignment = 1
              Fields = [| { Name = "WordSizeBits"; Offset = 0; Repr = Repr.U16; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None } |] }
          Documentation = Some "the word size of the core in bits" }

    /// `BitFieldDescriptor.Position` and `.Width`: a bit range inside a register of at most 255 bits.
    let bitFieldDescriptor : StructDescriptor =
        { Name = "Hardware.BitFieldDescriptor"
          Layout =
            { Size = 2
              Alignment = 1
              Fields =
                [| { Name = "Position"; Offset = 0; Repr = Repr.U8; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None }
                   { Name = "Width"; Offset = 1; Repr = Repr.U8; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None } |] }
          Documentation = Some "a named bit range inside a register" }

    /// `FieldDescriptor.Offset` and `.Count`: a field's byte offset and inline element count.
    let fieldDescriptor : StructDescriptor =
        { Name = "Hardware.FieldDescriptor"
          Layout =
            { Size = 8
              Alignment = 1
              Fields =
                [| { Name = "Offset"; Offset = 0; Repr = Repr.U32; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None }
                   { Name = "Count"; Offset = 4; Repr = Repr.U32; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None } |] }
          Documentation = Some "one register or struct field" }

    /// `PeripheralLayout.Size` and `.Alignment`: a layout's extent and alignment in bytes.
    let peripheralLayout : StructDescriptor =
        { Name = "Hardware.PeripheralLayout"
          Layout =
            { Size = 8
              Alignment = 1
              Fields =
                [| { Name = "Size"; Offset = 0; Repr = Repr.U32; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None }
                   { Name = "Alignment"; Offset = 4; Repr = Repr.U32; Count = 1; Access = AccessKind.ReadOnly; BitFields = [||]; Documentation = None } |] }
          Documentation = Some "the declared extent and alignment of a register block or struct" }

    /// Every descriptor of this schema, in declaration order.
    let all : StructDescriptor array =
        [| widthDeclaration; representation; targetCore; bitFieldDescriptor; fieldDescriptor; peripheralLayout |]
