using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Zinnia.Base;

namespace Zinnia;

public enum OperatingSystem : byte
{
    Windows,
    Linux,
    MacOSX
}

public class MessageStringsException : Exception
{
    public string _Message;

    public MessageStringsException(string Message)
    {
        _Message = Message;
    }

    public override string Message => _Message;
}

public class MessageStrings
{
    public Dictionary<string, string> Strings;

    public MessageStrings()
    {
        Process(CultureInfo.CurrentCulture);
    }

    public string this[string Key] => Strings[Key];

    public void Read(CultureInfo Culture)
    {
        var Name = Culture.TwoLetterISOLanguageName;
        var f = "Messages." + Name + ".txt";
        if (!File.Exists(f))
        {
            f = "Messages.en.txt";
            if (!File.Exists(f))
                throw new MessageStringsException("Can't find message file");
        }

        Read(f);
    }

    public void Read(string FileName)
    {
        var Stream = File.OpenRead(FileName);
        Read(Stream);
        Stream.Dispose();
    }

    public void Read(Stream Stream)
    {
        var Reader = new StreamReader(Stream);
        Process(Reader.ReadToEnd());
        Reader.Dispose();
    }

    public void Process(CultureInfo Culture)
    {
        var f = "Messages_" + Culture.TwoLetterISOLanguageName;
        var Ret = Resource.ResourceManager.GetString(f);
        if (Ret == null && (Ret = Resource.ResourceManager.GetString("Messages_en")) == null)
            throw new MessageStringsException("Can't find message file");

        Process(Ret);
    }

    public void Process(string String)
    {
        Strings = new Dictionary<string, string>();
        var Start = 0;
        var LineIndex = 0;

        for (var i = 0; i < String.Length; i++)
        {
            var Chr = String[i];
            if (Chr == '\n')
            {
                LineIndex++;
                var Line = String.Substring(Start, i - Start).Trim();
                ProcessLine(Line, LineIndex);
                Start = i + 1;
            }
        }
    }

    public void ProcessLine(string Line, int LineIndex)
    {
        if (Line.Length == 0 || Line.StartsWith("//") || Line.StartsWith("'"))
            return;

        var Pos = Line.IndexOf('=');
        if (Pos == -1) throw new MessageStringsException("Missing '=' at line " + LineIndex);

        var Key = Line.Substring(0, Pos).Trim();
        var Value = Line.Substring(Pos + 1).Trim();
        if (Key.Length == 0 || Value.Length == 0)
            throw new MessageStringsException("Missing key or value at line " + LineIndex);

        if (Value.Length < 2 || Value[0] != '\"' || Value[Value.Length - 1] != '\"')
            throw new MessageStringsException("Invalid value at line " + LineIndex);
        Value = Value.Substring(1, Value.Length - 2);

        Strings.Add(Key, Value);
    }
}

public enum MessageSeverity : byte
{
    Error = 0,
    Warning = 1,
    Info = 2
}

public enum MessageId
{
    UnknownError = 1000,
    NoMacro,
    NotValidName,
    WrongParamList,
    MacroAlreadyDefined,
    NoParamName,
    ParamCount,
    NoEndif,
    UnknownType,
    WrongDefinition,

    // ConstStringErr,
    ZNumErr,
    ConstNumErr,
    UnknownOp,
    CantCall,
    MustBeConst,
    CannotConvert,
    InvalidScopeIndent,
    UnknownId,
    OpsInGblScope,
    NotExpected,
    PreprocError,
    TypeNotSpecified,
    IdAlreadyDefined,
    DeficientExpr,
    DeficientDoWhile,
    LoopWithoutDo,
    CantOpApplied,
    CallingNotFunc,
    MissingParam,
    MissingThen,
    AsmNotRId,
    NotAllPathReturn,
    AssignRValue,
    UnknownCommand,
    ImplicitlyCast,
    ForToDownToUntil,
    NoForVar,
    MoreForVar,
    NoForInitValue,
    NeedThenElse,
    WrongSettings,
    EntryNotFound,
    EntryNotSpecified,
    ImpExpGlbScope,
    CannotConvertConst,
    UnassignedVar,
    AddressOfRValue,
    MustBeGlobal,
    Untyped,
    CannotDeclVar,
    CantOpApplied2,
    UnknownType2,
    SameEnumValue,
    DefFuncParamVal,
    UnBreakCountinueable,
    CaseWithoutSwitch,
    CaseSystaxErr,
    SwitchNoCaseDef,
    SwitchAlreadyHasDef,
    EnumValOverflow,
    LabelAlreadyDefined,
    UnknownLabel,
    ConstOutOfRange,
    ReadOnly,
    CallConvErr,
    UntypedFunction,
    CannotInherit,
    TypeCannotBeSpecified,
    NoConstructor,
    CantDeclare,
    Static,
    NonStatic,
    ExternNonMSCoff,
    FuncCannotHaveInnerScope,
    CannotGetSize,
    MacroWithoutValue,
    VarFuncRetType,
    NonStaticInStaticClass,

    //SealedErr,
    //StaticSealed,
    CannotInheritStatic,
    CannotInheritSealed,
    NoParamLessConstructor,
    HasntGotMembers,
    CycleInStructured,
    MustBeUnnamed,
    UnassignedVar2,
    CannotBeThisType,
    MustBeNamed,
    MustHaveInitVal,
    FileDoesntExists,
    CantLoadFile,
    EnumTypeError,
    CantBeConst,
    ConstsCantBeStatic,
    CantUseConstructors,
    CannotCalcConst,
    GuidCannotUsed,
    InvalidGuidFormat,
    SameOverloads,
    AmbiguousReference,
    MoreAccessModifier,
    CantAccessPrivate,
    CantAccessProtected,
    PrivateInOtherLib,
    ProtPrivInNonStructured,
    OverrideVirtual,
    StructorFuncModifier,
    VirtualAbstractContainer,
    NoOverridable,
    OverrideNonvirtual,
    PrivateVirtual,
    MustBeInteger,
    OverrideSealed,
    NobaseError,
    NobaseClassbase,
    ArrayLengthTooSmall,
    ArrayLengthTooBig,
    ArrayDifferentTypes,
    ArrayInvalidType,
    WithMustBeStructured,
    InvalidCast,
    CantAccessInternal,
    MissingTypeParams,
    ReinterpretSize,
    ArrayInvalidLength,
    CharInvalidLength,
    InvalidOp,
    ReturnTypeNotTuple,
    RetVarAlreadySpecified,
    CannotDeclFunc,
    CannotReturn,
    IncompatibleMods,
    AbstractInner,
    AbstractInNonAbstract,
    StaticCallAbstract,
    LessAccessable,
    StructParamLessCtor,
    InvalidNumberOfIds,
    ClassMustBeAbstract,
    MultipleRadix,
    InvalidEscapeSequence,

    //QuotationMark,
    InvalidAddressType,
    ExprVarDeclInitVal,
    NoPropertyGetter,
    NoPropertySetter,
    UnaccessableGetter,
    UnaccessableSetter,
    PropertyAccessLevel,
    ModifierCantBeUsed,
    ModifierCantBeUsed2,
    EntryMustBePublic,
    UnnamedIdentifier,
    AssemblyNotFound,
    ParamIsntOptional,
    CannotHaveInitVal,
    ParamAlreadySpecified,
    ParamNotSpecified,
    IdDescPtrFromLocal,
    ForInvalidTupleSize,
    ForInvalidOp,
    InvalidAlign,
    NotAlignedEnough,
    IndexOutOfRange,
    MissingPropertyIndices,
    ParamlessSelfIndexer,
    UnimplementedWithIndices,
    OperatorModifiers,
    UnknownOpFunc,
    CastOpInvalidTypes,
    NoncastOpInvalidTypes,
    ArrayInitializerLength,
    ArrayLengthNotSpecified,
    NoMatchingCommand,
    CannotLeaveFinally,
    MustBeType,
    MustBeClass,
    NoIdDataPointer,
    UnassignedReadonly,
    GenericParamCount,
    ParamArrayMustBeTheLast,
    UnnamedParamAfterNamed,
    CatchesAllException,
    NonGenericIdentifier,
    BaseIdNotImplemented,

    PreprocWarning = 2000,
    EmptyScope,
    SingleThread,
    AssignSameVar,
    UnreachableCode,
    UnusedId,
    CastToSameType,
    CmpSameVariable,
    AssignedButNeverUsed,
    ConstExpression,
    HidingRequired,
    HidingUnnecessary,

    NoMessage = 3000,
    PreprocInfo
}

public class MessageList
{
    public MessageStrings Strings;
    public List<string> Messages;
    public object LockObject;
    public bool CollectMessages = true;
#if DEBUG
    public bool ExceptionOnError = false;
#else
        public bool ExceptionOnError = false;
#endif

    public MessageList(MessageStrings Strings = null)
    {
        this.Strings = Strings;
        LockObject = new object();
    }

    public void Add(string Message, string Id)
    {
        Add(Id + ": " + Message + ".");
    }

    public void Add(string Message, string Id, string Extra)
    {
        Message = Id + ": " + Message + ": " + Extra + ".";
        Add(Message);
    }

    public void Add(string Message, string Id, string File, int Line, int Char, int Length)
    {
        var PosStr = File + ", " + Line + ", " + Char + ", " + Length;
        Message = Id + "(" + PosStr + "): " + Message + ".";
        Add(Message);
    }

    public void Add(string Message)
    {
        if (!CollectMessages) return;

        lock (LockObject)
        {
            if (Messages == null)
                Messages = new List<string>();

            Messages.Add(Message);
        }
    }

    public void Add(MessageId MessageId, CodeString Extra, params string[] Params)
    {
        if (MessageId == MessageId.NoMessage)
            throw new ArgumentOutOfRangeException("Invalid message", "Message");

        if (Strings == null)
            throw new InvalidOperationException("There's no instance of MessageStrings");

        var Severity = (MessageSeverity)((int)MessageId / 1000 - 1);
        var ErrId = Severity + " " + (int)MessageId;

        var Description = (string)null;
        if (MessageId != MessageId.PreprocError && MessageId != MessageId.PreprocInfo &&
            MessageId != MessageId.PreprocWarning)
            Description = string.Format(Strings[MessageId.ToString()], Params);

        if (MessageId != MessageId.EntryNotFound && Severity == MessageSeverity.Error && ExceptionOnError)
            throw new ApplicationException(Description);

        if (Extra.File != null)
        {
            var File = "'" + Path.GetFileName(Extra.File.Path) + "'";
            var Line = Extra.File.GetLineCount(0, Extra.Index) - 1;
            var Char = Extra.Index - Extra.File.GetLinePosition(Line);

            if (Description == null) Description = Extra.ToString();
            Add(Description, ErrId, File, Line, Char, Extra.Length);
        }
        else if (Extra.IsValid)
        {
            Add(Description, ErrId, Extra.ToString());
        }
        else
        {
            Add(Description, ErrId);
        }
    }

    public void Add(MessageId MessageId, string Extra, params string[] Params)
    {
        Add(MessageId, new CodeString(Extra), Params);
    }

    public void Add(MessageId MessageId)
    {
        Add(MessageId, new CodeString());
    }

    public void Add(MessageList List)
    {
        if (!CollectMessages) return;

        lock (LockObject)
        {
            if (Messages == null)
                Messages = new List<string>();

            Messages.AddRange(List.Messages);
        }
    }

    public void WriteToConsole()
    {
        if (Messages != null)
            for (var i = 0; i < Messages.Count; i++)
                Console.WriteLine(Messages[i]);
    }
}

public enum ImageFormat : byte
{
    GUI,
    Console,
    AsDLL,
    DLL,
    MSCoff
}

[Flags]
public enum CompilerStateFlags : byte
{
    DebugMode = 1,
    RuntimeChecks = 2
}

public class CompilerState
{
    public DataList Data = new();
    public CompilerStateFlags Flags;
    public ZinniaBuilder Builder;
    public MessageStrings Strings;
    public MessageList Messages;
    public OperatingSystem OperatingSystem = OperatingSystem.Windows;
    public ImageFormat Format = ImageFormat.GUI;
    public string CodeOutFile, LibOutFile;
    public Stream CodeOut, LibOut;
    public string AssemblyName = "Assembly";

    public GlobalContainer GlobalContainer;
    public IArchitecture Arch;
    public Language Language;

#if DEBUG
    public bool Parallel = false;
#else
        public bool Parallel = true;
#endif
    public string Entry;
    public CallingConvention DefaultCallConv = CallingConvention.ZinniaCall;
    public int TabSize = 4;

    public bool Compile(CodeFile[] CodeFiles, List<AssemblyPath> Assemblies = null,
        List<IncBinReference> IncBins = null)
    {
        Reset();
        InitializeScopes(CodeFiles);
        DefineMacroes();

        if (Assemblies != null)
        {
            var RetValue = true;
            for (var i = 0; i < Assemblies.Count; i++)
                if (GlobalContainer.GetLoadedAssembly(Assemblies[i].Name) == null)
                    if (GlobalContainer.LoadAssembly(Assemblies[i]) == null)
                        RetValue = false;

            if (!RetValue)
                return false;
        }

        if (IncBins != null)
        {
            var RetValue = true;
            for (var i = 0; i < IncBins.Count; i++)
            {
                var File = IncBins[i].File;
                var FileInfo = new FileInfo(File);

                if (!FileInfo.Exists)
                {
                    Messages.Add(MessageId.FileDoesntExists, new CodeString(File));
                    RetValue = false;
                    continue;
                }

                var IncBin = new IncludedBinary(IncBins[i].Name, File, FileInfo.Length);
                GlobalContainer.IncludedBinaries.Add(IncBin);
            }

            if (!RetValue)
                return false;
        }

        GlobalContainer.SearchCommonIdentifiers();
        return Arch.Compile(this, CodeFiles);
    }

    private void DefineMacroes()
    {
        var Preprocessor = GlobalContainer.Preprocessor;
        if (OperatingSystem == OperatingSystem.Windows) Preprocessor.Define("OPERATING_SYSTEM", "Windows");
        else if (OperatingSystem == OperatingSystem.Linux) Preprocessor.Define("OPERATING_SYSTEM", "Linux");
        else if (OperatingSystem == OperatingSystem.MacOSX) Preprocessor.Define("OPERATING_SYSTEM", "MacOSX");
        else throw new ApplicationException();
    }

    private void InitializeScopes(CodeFile[] CodeFiles)
    {
        if (string.IsNullOrEmpty(AssemblyName))
            throw new InvalidOperationException("Assembly hasn't been set up");

        if (CodeOut == null || LibOut == null)
            throw new InvalidOperationException("Output streams haven't been set up");

        var Random = new Random();
        var Assembly = new Assembly(this, AssemblyName);
        Assembly.Random = Random.Next();

        GlobalContainer = new GlobalContainer(this);
        GlobalContainer.OutputAssembly = Assembly;

        for (var i = 0; i < CodeFiles.Length; i++)
        {
            var AssemblyScope = new AssemblyScope(GlobalContainer, Assembly, CodeFiles[i]);
            GlobalContainer.GlobalNamespace.AddScope(AssemblyScope);
            GlobalContainer.Children.Add(AssemblyScope);
        }

        GlobalContainer.GlobalNamespaceScope = new AssemblyScope(GlobalContainer, Assembly);
        GlobalContainer.GlobalNamespace.AddScope(GlobalContainer.GlobalNamespaceScope);
        GlobalContainer.Children.Add(GlobalContainer.GlobalNamespaceScope);
    }

    public CompilerState(ZinniaBuilder Builder, MessageStrings Strings, IArchitecture Arch, Language Language)
    {
        Messages = new MessageList(Strings);

        this.Builder = Builder;
        this.Arch = Arch;
        this.Language = Language;
        this.Strings = Strings;
    }

    public string GenerateName(Identifier Id)
    {
        if (Id == null)
            throw new ArgumentNullException("Id");

        var Out = (string)null;
        var Gens = Language.NameGenerators;
        for (var i = 0; i < Gens.Length; i++)
        {
            var Res = Gens[i].GenerateName(this, Id, ref Out);
            if (Res != SimpleRecResult.Unknown)
            {
                if (Res == SimpleRecResult.Succeeded)
                {
                    if (string.IsNullOrEmpty(Out))
                        throw new ApplicationException("Generated identifier name cannot be null or empty");

                    return Out;
                }

                if (Res == SimpleRecResult.Failed)
                    throw new ApplicationException("Failed to generate identifier name");
            }
        }

        throw new ApplicationException("None of the name generators can handle this identifier");
    }

    public void SetOutput(string CodeOut, string LibOut)
    {
        CodeOutFile = CodeOut = Path.GetFullPath(CodeOut);
        LibOutFile = LibOut = Path.GetFullPath(LibOut);

        this.CodeOut = new FileStream(CodeOut, FileMode.Create);
        this.LibOut = new FileStream(LibOut, FileMode.Create);
    }

    public void SetOutput(Stream CodeOut, Stream LibOut)
    {
        this.CodeOut = CodeOut;
        this.LibOut = LibOut;
    }

    public void DisposeOutput()
    {
        CodeOut.Dispose();
        LibOut.Dispose();
    }

    public int CalcPow2Size(int Size)
    {
        var M = Arch.MaxStructPow2Size;
        if (Size < M) return Helper.Pow2(Size);
        if (Size % M != 0) return Size + (M - Size % M);
        return Size;
    }

    public CompilerState(ZinniaBuilder Builder, IArchitecture Arch, Language Language)
        : this(Builder, new MessageStrings(), Arch, Language)
    {
    }

    public void Reset()
    {
        _AutoLabelIndex = 0;
    }

    private int _AutoLabelIndex;

    public int AutoLabel
    {
        get
        {
            if (!Parallel) _AutoLabelIndex++;
            else Interlocked.Increment(ref _AutoLabelIndex);
            return _AutoLabelIndex;
        }
    }

    public CodeString AutoVarName => new(AutoLabel.ToString());

    public CodeString GetInnerScope(CodeString Code)
    {
        if (Language.InnerScopeRecognizer == null) return Code;
        return Language.InnerScopeRecognizer.GetInnerScope(this, Code);
    }

    public CodeString GetInnerScope(CodeString Code, CodeString Command, bool Warning = true)
    {
        var Inner = GetInnerScope(Code);
        if (!Inner.IsValid) return new CodeString();

        Inner = Inner.Trim();
        if (Inner.Length == 0 && Warning)
            Messages.Add(MessageId.EmptyScope, Command);

        return Inner;
    }

    public CodeString GetInnerScope(CodeString Code, int Position, bool Warning = true)
    {
        return GetInnerScope(Code.Substring(Position).Trim(), Code.Substring(0, Position).Trim(), Warning);
    }

    public FunctionParameter[] GetParameters(IdContainer Container, CodeString Parameters)
    {
        var DeclList = VarDeclarationList.Create(Container, Parameters);
        if (DeclList == null) return null;

        var RetValue = DeclList.ToFuncParams(new PluginForGlobals(Container), VarDeclConvMode.Normal);
        if (RetValue == null || RetValue.Contains(null)) return null;
        return RetValue;
    }
}