namespace BAREWire.Descriptors

/// Binding descriptors (clef-lang-spec platform-bindings.md, "Layer 2: Binding
/// Libraries"): the declaration a generator (Farscape) emits beside every
/// extern, carrying the C ABI representation of each parameter and of the
/// return as data, in a quotation the compiler reads structurally by type name
/// and field name and never evaluates (expressions.md, "Quoted Expressions").
///
/// The width of a C integer lives here, never in the Clef signature: the
/// signature spells `int` and the descriptor says `Integer (Signed, 32)`
/// (Dimensional_Range_Design.md, "The Farscape leg"). A descriptor is a
/// declaration: a binding whose value is `Expr<FunctionDescriptor>` is never
/// initialised or emitted, so the closed vocabularies here are unions, the
/// spec's own spelling (`CDecl | StdCall | FastCall`), not string tags; the
/// import wall docs/12 `record-union-field` names is a lowering concern, and
/// a declaration is never lowered.

/// Whether an integer representation carries a sign.
type Signedness =
    | Signed
    | Unsigned

/// A parameter's or return's representation at the C ABI: the family and its
/// bits as data. `Named` cites a descriptor by its binding name (a struct, an
/// opaque handle) and carries no width of its own.
type TypeRef =
    | Integer of Signedness * int
    | Float of int
    | Pointer of int
    | Bool
    | Void
    | Named of string

/// How an argument crosses: by value, or by reference to the caller's storage.
type PassBy =
    | Value
    | Reference
    /// The callee may read the referenced storage but must not mutate it.
    | ReadOnlyReference

/// The calling convention the C side expects.
type CallConv =
    | CDecl
    | StdCall
    | FastCall

/// Who owns what the call returns or is handed.
type Transfer =
    | CallerOwns
    | CalleeOwns
    | Borrowed

/// One parameter: its C name, its representation, and how it is passed.
type ParameterInfo = {
    Name: string
    Type: TypeRef
    PassBy: PassBy
}

/// The descriptor of one extern (the spec's `FunctionDescriptor`, its field
/// names exactly): the C symbol, its parameters in order, its return, its
/// calling convention and the ownership transfer of its result.
type FunctionDescriptor = {
    CName: string
    Parameters: ParameterInfo array
    ReturnType: TypeRef
    CallingConvention: CallConv
    OwnershipTransfer: Transfer
}

/// A generated listener field's native calling contract. The record and field
/// identify each FnPtr supplied by application code; the signature fixes its
/// C parameter and result representations independently of the Clef carrier.
type CallbackDescriptor = {
    Record: string
    Field: string
    Signature: FunctionDescriptor
}

/// A borrowed view's element representation. Schema names the opaque phantom
/// marker, not a C struct; its element width never changes ordinary Clef arrays.
type ViewLayoutDescriptor = {
    Schema: string
    Element: BAREWire.Hardware.Repr
    Alignment: int
    Access: BAREWire.Hardware.AccessKind
}

/// Values carried by one native acquisition. Output names a single declared
/// reference cell written by that call, including a nullable opaque cookie.
type MappedValue =
    | Input of string
    | Output of string

/// A generated scoped wrapper over an existing native map/unmap pair. Native
/// FunctionDescriptors retain their exact arities. The compiler owns output
/// cells, validates stride/extent/alignment, invokes the callback with a borrowed
/// view, and releases once after every callback use has retired. Failed native
/// acquisition never invokes the callback or the release binding.
type MappedReturnDescriptor = {
    Binding: string
    Acquire: string
    Release: string
    Layout: string
    CallbackParameter: string
    Owner: MappedValue
    RowStride: MappedValue
    RowCount: MappedValue
    RowWidth: MappedValue
    ReleaseArguments: MappedValue array
    FailureStatus: int
}

/// The binding promises that every use and capture of this callback retires
/// before it returns. This declaration is a trusted library obligation. The
/// compiler checks callers' scoped uses against that promise; it does not prove
/// an arbitrary scheduling implementation honors it. Native lifecycle tests
/// and implementation review supply separate retirement evidence.
type ScopedCallbackDescriptor = {
    Binding: string
    Parameter: string
}

/// Parameter constructors.
module Parameter =

    /// A parameter passed by value.
    let value (name: string) (t: TypeRef) : ParameterInfo =
        { Name = name; Type = t; PassBy = Value }

    /// A parameter passed by reference.
    let reference (name: string) (t: TypeRef) : ParameterInfo =
        { Name = name; Type = t; PassBy = Reference }

    /// A reference whose foreign contract excludes writes through this parameter.
    let readOnlyReference (name: string) (t: TypeRef) : ParameterInfo =
        { Name = name; Type = t; PassBy = ReadOnlyReference }

/// Function descriptor constructors. A descriptor is a declaration the compiler
/// reads (CCS8206 for a shape it cannot follow, CCS8207 for a width of no bits);
/// nothing here evaluates one.
module Function =

    /// A C-convention function whose result is borrowed.
    let cdecl (cname: string) (parameters: ParameterInfo array) (returnType: TypeRef) : FunctionDescriptor =
        { CName = cname; Parameters = parameters; ReturnType = returnType; CallingConvention = CDecl; OwnershipTransfer = Borrowed }

    /// The same descriptor with the ownership transfer declared.
    let withTransfer (f: FunctionDescriptor) (transfer: Transfer) : FunctionDescriptor =
        { CName = f.CName; Parameters = f.Parameters; ReturnType = f.ReturnType; CallingConvention = f.CallingConvention; OwnershipTransfer = transfer }
