using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections;

namespace Zinnia
{
	[Flags]
	public enum GetAssemblyMode
	{
		Code = 1,
		InitedValues = 2,
		UninitedValues = 4,
		ReflectionData = 8,
	}

	public delegate bool IdEventHandler<T>(T Id);

	public class IncludedBinary
	{
		public string Name;
		public string File;
		public string Label;
		public long Length;

		public IncludedBinary(string Name, string File, long Length)
		{
			this.Name = Name;
			this.File = File;
			this.Label = "_%IncBins_" + Name;
			this.Length = Length;
		}
	}

	public class MTCompiler
	{
		public CompilerState State;
		public List<Identifier> Identifiers;
		public List<string> GlobalPointers;

		public MTCompiler(CompilerState State, List<Identifier> Identifiers)
		{
			this.State = State;
			this.Identifiers = Identifiers;
		}

		public bool Run()
		{
			var Result = true;
			var Global = State.GlobalContainer;

			if (!State.Parallel)
				State.Messages.Add(MessageId.SingleThread);

			Action<Identifier, PluginRoot> VariableProcess = (Id, Plugin) =>
			{
				Plugin.Reset();
				Plugin.Container = Id.Container;

				var Var = Id as Variable;
				if (Var != null && !Var.CalcValue(Plugin)) Result = false;
				if (!State.Arch.ProcessIdentifier(Id)) Result = false;
			};

			Action<Identifier> FunctionProcess = Id =>
			{
				var Func = Id as Function;
				if (Func != null && Func.NeedsToBeCompiled)
				{
					Func.FunctionState = FunctionState.CodeProcessStarted;
					if (!Func.FunctionScope.ProcessCode()) Result = false;

					Func.FunctionState = FunctionState.CodeProcessEnded;
				}
			};

			Action<Identifier> CreateAssembly = Id =>
			{
				var Func = Id as Function;
				if (Func != null && Func.NeedsToBeCompiled)
				{
					Func.FunctionState = FunctionState.AssemblyGenerationStarted;
					if (!State.Arch.CreateAssembly(Func))  Result = false;

					Func.FunctionScope = null;
					Func.FunctionState = FunctionState.AssemblyGenerationEnded;
				}
			};

			if (State.Parallel)
			{
				Parallel.ForEach(Identifiers,
					() => Global.GetPlugin(),
					(Id, LoopState, Plugin) => { VariableProcess(Id, Plugin); return Plugin; },
					Plugin => { });
			}
			else
			{
				var Plugin = Global.GetPlugin();
				for (var i = 0; i < Identifiers.Count; i++)
					VariableProcess(Identifiers[i], Plugin);
			}

			if (!Result) return false;
			for (var i = 0; i < Identifiers.Count; i++)
				if (!Identifiers[i].CalculateLayout()) Result = false;

			if (!Result) return false;
			if (State.Parallel) Parallel.ForEach(Identifiers, FunctionProcess);
			else Identifiers.ForEach(x => FunctionProcess(x));

			if (!Result) return false;
			GlobalPointers = Global.GetGlobalPointers();
			var ADC = new AssemblyDescCreator();
			ADC.CreateAssemblyDesc(Global, State.LibOut);

			if (State.Parallel) Parallel.ForEach(Identifiers, CreateAssembly);
			else Identifiers.ForEach(x => CreateAssembly(x));
			return Result;
		}
	}

	[Flags]
	public enum GlobalContainerFlags : byte
	{
		None,
		HasUninitedValues = 1,
		StructureMembersParsed = 2,
	}

	public struct CommonIdentifiers
	{
		public List<Identifier> Predeclared;
		public List<BuiltinType> BuiltinTypes;
		public List<NumberType> NumTypes;
		public SignedType SByte;
		public UnsignedType Byte;
		public SignedType Int16;
		public UnsignedType UInt16;
		public SignedType Int32;
		public UnsignedType UInt32;
		public SignedType Int64;
		public UnsignedType UInt64;
		public Identifier IntPtr;
		public Identifier UIntPtr;
		public FloatType Single;
		public FloatType Double;

		public BooleanType Boolean;
		public VoidType Void;
		public CharType Char;
		public AutomaticType Auto;
		public NullType Null;
		public ObjectType Object;
		public StringType String;
		public TypeOfType TypeOfType;
		public NamespaceType NamespaceType;
		public Type BytePtr;
		public Type VoidPtr;

		public Identifier ValueType;
		public Identifier Enum;
		public Identifier Tuple;
		public Identifier Array;

		public StructureBase[] ValueTypeBase;
		public StructureBase[] EnumBase;
		public StructureBase[] TupleBase;
		public StructureBase[] ArrayBase;
		public StructureBase[] EmptyBase;

		public Identifier GetIdentifier(UndeclaredIdType Type)
		{
			for (var i = 0; i < Predeclared.Count; i++)
			{
				if (Predeclared[i].UndeclaredIdType == Type)
					return Predeclared[i];
			}

			return null;
		}

		public T GetIdentifier<T>(int Size) where T : NumberType
		{
			for (var i = 0; i < NumTypes.Count; i++)
			{
				var Id = NumTypes[i] as T;
				if (Id != null && Id.Size == Size) return Id;
			}

			return null;
		}

		public Type GetIdentifier(System.Type Type, int Size)
		{
			for (var i = 0; i < NumTypes.Count; i++)
			{
				var Id = NumTypes[i];
				if (Id.Size == Size && Type.IsInstanceOfType(Id))
					return Id;
			}

			return null;
		}

		public Type GetLargestType(System.Type Type)
		{
			var RetIndex = -1;
			for (var i = 0; i < NumTypes.Count; i++)
				if (Type.IsInstanceOfType(NumTypes[i]))
				{
					if (RetIndex == -1 || NumTypes[i].Size > NumTypes[RetIndex].Size)
						RetIndex = i;
				}

			return NumTypes[RetIndex];
		}
	}

	public class GlobalContainer : IdContainer
	{
        public Preprocessor Preprocessor;
		public Namespace GlobalNamespace;
		public NamespaceScope GlobalNamespaceScope;
		public Assembly OutputAssembly;
		public Function AssemblyEntry;
		public AutoAllocatedList<Assembly> LoadedAssemblies;
		public CommonIdentifiers CommonIds;
		public List<GlobalVariable> ExprConsts = new List<GlobalVariable>();
		public List<IdentifierAlias> TypeAliases = new List<IdentifierAlias>();
		public AutoAllocatedList<IncludedBinary> IncludedBinaries;
		public List<string> GlobalPointers;
		public GlobalContainerFlags Flags;
		internal Dictionary<string, Identifier> FastIds = new Dictionary<string,Identifier>();

		public bool CreateAssemblyEntry()
		{
			var Name = new CodeString(OutputAssembly.DescName + "_AssemblyEntry");
			var FuncTypeCh = new Identifier[] { CommonIds.Void };
			var FuncType = new TypeOfFunction(this, CallingConvention.ZinniaCall, FuncTypeCh);
			var Func = GlobalNamespaceScope.CreateDeclaredFunctionAndScope(Name, FuncType, new CodeString());
			if (Func == null) return false;

			var FS = Func.FunctionScope;
			FS.Flags &= ~FunctionScopeFlags.DisableParsing;
			AssemblyEntry = Func;

			//--------------------------------------------------------------------------------------
			var Plugin = FS.GetPlugin();
#warning WARNING
			if ((Flags & GlobalContainerFlags.HasUninitedValues) != 0 || true)
			{
				if (!Plugin.Begin()) return false;

				var ZeroMemFuncOptions = GetIdOptions.Default;
				ZeroMemFuncOptions.OverloadData.Specified = true;
				ZeroMemFuncOptions.OverloadData.Unnamed = new List<Identifier>() { CommonIds.VoidPtr, CommonIds.UIntPtr };

				var ZeroMemFunc = Identifiers.GetFromMembers(GlobalNamespace,
					new CodeString("System.Memory.Zero"), ZeroMemFuncOptions);
				if (ZeroMemFunc == null) return false;

				var ZeroMemFuncNode = Plugin.NewNode(new IdExpressionNode(ZeroMemFunc, Name));
				var ZeroMemAddress = Plugin.NewNode(new LabelExpressionNode(Name, "_%UninitedValues_Begin"));
                var ZeroMemSizeTypeNode = Plugin.NewNode(new IdExpressionNode(CommonIds.UIntPtr, Name));
				if (ZeroMemFuncNode == null || ZeroMemAddress == null || ZeroMemSizeTypeNode == null) return false;

				var ZeroMemSizeString = "_%UninitedValues_End - _%UninitedValues_Begin";
				var ZeroMemSizeCh0 = Plugin.NewNode(new LabelExpressionNode(Name, ZeroMemSizeString));
				if (ZeroMemSizeCh0 == null) return false;

				var ZeroMemSizeCastCh = new ExpressionNode[] { ZeroMemSizeCh0, ZeroMemSizeTypeNode };
				var ZeroMemSize = Plugin.NewNode(new OpExpressionNode(Operator.Cast, ZeroMemSizeCastCh, Name));
				if (ZeroMemSize == null) return false;

				var ZeroMemCh = new ExpressionNode[] { ZeroMemFuncNode, ZeroMemAddress, ZeroMemSize };
				var ZeroMem = Plugin.NewNode(new OpExpressionNode(Operator.Call, ZeroMemCh, Name));
				if (ZeroMem == null || Plugin.End(ref ZeroMem) == PluginResult.Failed) return false;

				var ZeroMemComm = new Command(FS, Name, CommandType.Expression);
				ZeroMemComm.Expressions = new List<ExpressionNode>() { ZeroMem };
				FS.Children.Add(ZeroMemComm);
			}

			//--------------------------------------------------------------------------------------
			for (var i = 0; i < Children.Count; i++)
			{
				var Ch = Children[i] as AssemblyScope;
				if (Ch.Assembly != OutputAssembly) continue;
				if (!CallStaticConstructors(Ch, FS, Plugin, Name))
					return false;
			}

			//-----------------------------------------------------------------------------
			if (State.Entry != null)
			{
				var Entry = GetEntryFunction();
				if (Entry == null) return false;

				if (!Plugin.Begin()) return false;
				var EntryNode = Plugin.NewNode(new IdExpressionNode(Entry, Name));
				if (EntryNode == null) return false;

				var NodeCh = new ExpressionNode[] { EntryNode };
				var Node = Plugin.NewNode(new OpExpressionNode(Operator.Call, NodeCh, Name));
				if (Node == null || Plugin.End(ref Node) == PluginResult.Failed) return false;

				var Comm = new Command(FS, Name, CommandType.Expression);
				Comm.Expressions = new List<ExpressionNode>() { Node };
				FS.Children.Add(Comm);
			}

			return true;
		}

		static bool CallStaticConstructors(IdContainer Container, CodeScopeNode Scope, PluginRoot Plugin, CodeString Code)
		{
			for (var i = 0; i < Container.Children.Count; i++)
			{
				if (!CallStaticConstructors(Container.Children[i], Scope, Plugin, Code))
					return false;
			}

			for (var i = 0; i < Container.IdentifierList.Count; i++)
			{
				var Ctor = Container.IdentifierList[i] as Constructor;
				if (Ctor == null || (Ctor.Flags & IdentifierFlags.Static) == 0)
					continue;

				if (!Plugin.Begin())
					return false;

				var CtorNode = Plugin.NewNode(new IdExpressionNode(Ctor, Code));
				if (CtorNode == null) { Plugin.Reset(); return false; }

				var NodeCh = new ExpressionNode[] { CtorNode };
				var Node = Plugin.NewNode(new OpExpressionNode(Operator.Call, NodeCh, Code));
				if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
					{ Plugin.Reset(); return false; }

				Scope.Children.Add(new Command(Scope, Code, CommandType.Expression)
				{
					Expressions = new List<ExpressionNode>() { Node },
				});
			}

			return true;
		}

		public Identifier GetAliasOrUnderlying(Type Type)
		{
			var BuiltinType = Type as BuiltinType;
			if (BuiltinType != null)
			{
				var Underlying = BuiltinType.UnderlyingType;
				if (Underlying != null) return Underlying;
			}

			return GetTypeAlias(Type);
		}

		public IdentifierAlias GetTypeAlias(Type Type)
		{
			lock (TypeAliases)
			{
				for (var i = 0; i < TypeAliases.Count; i++)
				{
					if (TypeAliases[i].IsEquivalent(Type))
						return TypeAliases[i];
				}

				var Alias = new IdentifierAlias(GlobalNamespaceScope, Type.Name, Type);
				GlobalNamespaceScope.AddIdentifier(Alias);
				TypeAliases.Add(Alias);
				return Alias;
			}
		}

		public IncludedBinary GetIncludedBinary(string Name)
		{
			for (var i = 0; i < IncludedBinaries.Count; i++)
				if (IncludedBinaries[i].Name == Name) return IncludedBinaries[i];

			return null;
		}

		public override IdentifierAccess DefaultAccess
		{
			get { return IdentifierAccess.Internal; }
		}

		public Assembly GetAssembly(string Assembly)
		{
			var Ret = GetLoadedAssembly(Assembly);
			if (Ret != null) return Ret;

			if (OutputAssembly.Name == Assembly)
				return OutputAssembly;

			return null;
		}

		public Assembly GetLoadedOrOutputAssembly(string Name)
		{
			if (OutputAssembly.Name == Name)
				return OutputAssembly;

			return GetLoadedAssembly(Name);
		}

		public Assembly GetLoadedAssembly(string Name)
		{
			for (var i = 0; i < LoadedAssemblies.Count; i++)
			{
				if (LoadedAssemblies[i].Name == Name)
					return LoadedAssemblies[i];
			}

			return null;
		}

		public Assembly LoadAssembly(string AssemblyPath)
		{
			return LoadAssembly(new AssemblyPath(AssemblyPath));
		}

		public Assembly LoadAssembly(AssemblyPath AssemblyPath)
		{
			string ZinniaLib;
			if (AssemblyPath.BuiltIn)
			{
				var Name = AssemblyPath.Name;
				var Dir = ZinniaBuilder.GetDirectory(ZinniaDirectory.Libraries);
				if (Dir == null || !Directory.Exists(Dir = Path.Combine(Dir, Name)))
				{
					State.Messages.Add(MessageId.AssemblyNotFound, new CodeString(Name));
					return null;
				}

				ZinniaLib = Path.Combine(Dir, Name + ".zlib");
				if (!File.Exists(ZinniaLib))
				{
					State.Messages.Add(MessageId.FileDoesntExists, new CodeString(ZinniaLib));
					return null;
				}
				
				var Arc = Path.Combine(Dir, Name + ".a");
				if (!File.Exists(Arc))
				{
					State.Messages.Add(MessageId.FileDoesntExists, new CodeString(Arc));
					return null;
				}

				if (State.Builder != null)
					State.Builder.Archives.Add(Arc);
				
				var Additional = Path.Combine(Dir, Name + ".link.txt");
				if (File.Exists(Additional))
				{
					var RetValue = true;
					foreach (var Line in File.ReadAllLines(Additional))
					{
						var NewLine = State.Builder.GetFilePath(Line, Dir);
						if (!File.Exists(NewLine))
						{
							State.Messages.Add(MessageId.FileDoesntExists, new CodeString(Line));
							RetValue = false;
						}

						var Ext = Path.GetExtension(NewLine);
						if (Ext == ".o" || Ext == ".obj")
						{
							State.Builder.ObjectFiles.Add(NewLine);
						}
						else if (Ext == ".a")
						{
							State.Builder.Archives.Add(NewLine);
						}
						else
						{
							State.Messages.Add(MessageId.CantLoadFile, new CodeString(NewLine));
							return null;
						}
					}

					if (!RetValue)
						return null;
				}
			}
			else
			{
				ZinniaLib = AssemblyPath.Name;
			}
			
#if !DEBUG
			try
			{
#endif
				var Stream = File.OpenRead(ZinniaLib);
				var ADL = new AssemblyDescLoader();

				var Ret = ADL.LoadAssemblyDesc(this, Stream);
				if (Ret == null) State.Messages.Add(MessageId.CantLoadFile, new CodeString(ZinniaLib));
				return Ret;
#if !DEBUG
			}
			catch (Exception)
			{
				State.Messages.Add(MessageId.CantLoadFile, new CodeString(ZinniaLib));
				return null;
			}
#endif
		}

		public GlobalVariable CreateExprConst(ConstValue Value, Identifier Type)
		{
			lock (ExprConsts)
			{
				var Res = ExprConsts.Find(x => x.TypeOfSelf == Type && Value.IsEqual(x.ConstInitValue));
				if (Res != null) return Res;
			}

			var Ret = CreateVariable(State.AutoVarName, Type) as GlobalVariable;
			if (Ret == null) return null;

			Ret.ConstInitValue = Value;

			lock (ExprConsts) ExprConsts.Add(Ret);
			if (!State.Arch.ProcessIdentifier(Ret)) return null;
			return Ret;
		}

		public GlobalVariable CreateExprConst(ConstExpressionNode Node)
		{
			return CreateExprConst(Node.Value, Node.Type);
		}

		public ExpressionNode CreateConstNode(ConstExpressionNode ConstNode, PluginRoot Plugin)
		{
			var Glb = CreateExprConst(ConstNode);
			if (Glb == null) return null;

			return Plugin.NewNode(new IdExpressionNode(Glb, ConstNode.Code));
		}

		public ExpressionNode CreateConstNode(CodeString Code, ConstValue Value, Type Type, PluginRoot Plugin)
		{
			var Glb = CreateExprConst(Value, Type);
			if (Glb == null) return null;

			return Plugin.NewNode(new IdExpressionNode(Glb, Code));
		}

		public Function GetEntryFunction()
		{
			if (State.Entry == null)
				throw new ApplicationException();

			var Options = new GetIdOptions();
			Options.Func = x => x is Function;

			var Name = new CodeString(State.Entry);
			var Fun = Identifiers.GetFromMembers(GlobalNamespace, Name, Options);

			if (Fun == null)
			{
				State.Messages.Add(MessageId.EntryNotFound, new CodeString(), State.Entry);
				return null;
			}

			return Fun as Function;
		}

		static bool ProcessClassBases(IdContainer Container)
		{
			var RetValue = true;
			if (Container.IsNotLoadedAssemblyScope())
			{
				var Scope = Container as StructuredScope;
				if (Scope != null && !Scope.ProcessBase())
					RetValue = false;

				for (var i = 0; i < Container.Children.Count; i++)
				{
					if (!ProcessClassBases(Container.Children[i]))
						RetValue = false;
				}
			}

			return RetValue;
		}

		static bool ProcessScopes(IdContainer Container, List<Identifier> Out)
		{
			var RetValue = true;
			if (Container.IsNotLoadedAssemblyScope())
			{
				if (Container is NonCodeScope)
				{
					var Scope = Container as NonCodeScope;
					if (!Scope.ProcessScope()) RetValue = false;

					if (Scope is IdentifierScope)
					{
						var IdScope = Scope as IdentifierScope;
						Out.Add(IdScope.Identifier);
					}

					Scope.GetMTProcIds(Out);
				}

				for (var i = 0; i < Container.Children.Count; i++)
				{
					if (!ProcessScopes(Container.Children[i], Out))
						RetValue = false;
				}
			}

			return RetValue;
		}

		static bool ProcessStructureIdentifiers(IdContainer Container)
		{
			var RetValue = true;
			if (Container.IsNotLoadedAssemblyScope())
			{
				if (Container is StructuredScope)
				{
					var Scope = Container as StructuredScope;
					if (!Scope.ProcessIdentifiers()) RetValue = false;
				}

				for (var i = 0; i < Container.Children.Count; i++)
				{
					if (!ProcessStructureIdentifiers(Container.Children[i]))
						RetValue = false;
				}
			}

			return RetValue;
		}

		bool ProcessTypes(List<Identifier> Out)
		{
			var Aliases = new AliasDeclarationList();
			var Namespaces = NamespaceDeclList.CreateAndDeclareRecursively(this);
			if (Namespaces == null) return false;

			if (!Aliases.RecognizeRecursively(this)) return false;
			if (!Aliases.Declare(false)) return false;
			
			var Types = TypeDeclarationList.CreateAndDeclareRecursively(this);
			if (Types == null) return false;

			SearchCommonIdentifiers();
			if (!Aliases.Recognize(Types)) return false;
			if (!Aliases.Declare(false)) return false;

			var Consts = ConstDeclarationList.CreateAndDeclareRecursively(this);
			if (Consts == null || !Aliases.Declare(false)) return false;

			if (!ProcessClassBases(this)) return false;
			if (!ProcessScopes(this, Out)) return false;

			Flags |= GlobalContainerFlags.StructureMembersParsed;
			if (!ProcessStructureIdentifiers(this)) return false;
			return Aliases.Declare();
		}

		public bool Process()
		{
			var GlobalContainerProcessor = State.Language.GlobalContainerProcessor;
			if (GlobalContainerProcessor != null && !GlobalContainerProcessor.Process(this))
				return false;

			var CodeFileProcessor = State.Language.CodeFileProcessor;
			if (CodeFileProcessor != null && GlobalNamespace.HasScopes)
			{
				foreach (var e in GlobalNamespace.EnumScopes)
				{
					var AssemblyScope = e as AssemblyScope;
					if (AssemblyScope != null && AssemblyScope.Assembly == OutputAssembly)
					{
						if (!CodeFileProcessor.Process(AssemblyScope))
							return false;
					}
				}
			}

			var List = new List<Identifier>(256);
			if (!ProcessTypes(List)) return false;

			if (GetLoadedOrOutputAssembly("ZinniaCore") != null)
			{
				if (!CreateAssemblyEntry()) return false;
				List.Add(AssemblyEntry);
			}

			var MTCompiler = new MTCompiler(State, List);
			if (!MTCompiler.Run()) return false;

			GlobalPointers = MTCompiler.GlobalPointers;
			return true;
		}

		public GlobalContainer(CompilerState State)
			: base(null)
		{
			this.State = State;
            this.Preprocessor = new Preprocessor(this);
			Initialize();
		}

		public bool SearchCommonIdentifiers()
		{
			if (CommonIds.EmptyBase == null)
				CommonIds.EmptyBase = new StructureBase[0];

			CommonIds.ValueTypeBase = CommonIds.EmptyBase;
			CommonIds.EnumBase = CommonIds.EmptyBase;
			CommonIds.TupleBase = CommonIds.EmptyBase;
			CommonIds.ArrayBase = CommonIds.EmptyBase;

			var System = Identifiers.GetByFullNameFast<Namespace>(this, "System", false);
			if (System == null) return false;

			var StructOptions = new GetIdOptions() { EnableMessages = false, Func = x => x is StructType };
			var ClassOptions = new GetIdOptions() { EnableMessages = false, Func = x => x is ClassType };

			CommonIds.SByte.UnderlyingType = Identifiers.GetMember(State, System, "SByte", StructOptions) as StructuredType;
			CommonIds.Int16.UnderlyingType = Identifiers.GetMember(State, System, "Int16", StructOptions) as StructuredType;
			CommonIds.Int32.UnderlyingType = Identifiers.GetMember(State, System, "Int32", StructOptions) as StructuredType;
			CommonIds.Int64.UnderlyingType = Identifiers.GetMember(State, System, "Int64", StructOptions) as StructuredType;

			CommonIds.Byte.UnderlyingType = Identifiers.GetMember(State, System, "Byte", StructOptions) as StructuredType;
			CommonIds.UInt16.UnderlyingType = Identifiers.GetMember(State, System, "UInt16", StructOptions) as StructuredType;
			CommonIds.UInt32.UnderlyingType = Identifiers.GetMember(State, System, "UInt32", StructOptions) as StructuredType;
			CommonIds.UInt64.UnderlyingType = Identifiers.GetMember(State, System, "UInt64", StructOptions) as StructuredType;

			CommonIds.Single.UnderlyingType = Identifiers.GetMember(State, System, "Single", StructOptions) as StructuredType;
			CommonIds.Double.UnderlyingType = Identifiers.GetMember(State, System, "Double", StructOptions) as StructuredType;

			CommonIds.Boolean.UnderlyingType = Identifiers.GetMember(State, System, "Boolean", StructOptions) as StructuredType;
			CommonIds.Object.UnderlyingType = Identifiers.GetMember(State, System, "Object", ClassOptions) as StructuredType;
			CommonIds.String.UnderlyingType = Identifiers.GetMember(State, System, "String", ClassOptions) as StructuredType;
			CommonIds.Char.UnderlyingType = Identifiers.GetMember(State, System, "Char", StructOptions) as StructuredType;
			CommonIds.Void.UnderlyingType = Identifiers.GetMember(State, System, "Void", StructOptions) as StructuredType;

			CommonIds.ValueType = Identifiers.GetMember(State, System, "ValueType", ClassOptions);
			CommonIds.Enum = Identifiers.GetMember(State, System, "Enum", ClassOptions);
			CommonIds.Tuple = Identifiers.GetMember(State, System, "Tuple", ClassOptions);
			CommonIds.Array = Identifiers.GetMember(State, System, "Array", ClassOptions);

			if (CommonIds.ValueType != null)
			{
				CommonIds.ValueTypeBase = new StructureBase[1]
				{
					new StructureBase(CommonIds.ValueType, StructureBaseFlags.Unreal),
				};
			}

			if (CommonIds.Enum != null)
			{
				CommonIds.EnumBase = new StructureBase[1]
				{
					new StructureBase(CommonIds.Enum, StructureBaseFlags.Unreal),
				};
			}

			if (CommonIds.Tuple != null)
			{
				CommonIds.TupleBase = new StructureBase[1]
				{
					new StructureBase(CommonIds.Tuple, StructureBaseFlags.Unreal),
				};
			}

			if (CommonIds.Array != null)
			{
				CommonIds.ArrayBase = new StructureBase[1]
				{
					new StructureBase(CommonIds.Array, StructureBaseFlags.Unreal),
				};
			}

			if (CommonIds.ValueType == null || CommonIds.Enum == null ||
				CommonIds.Tuple == null || CommonIds.Array == null) return false;

			return CommonIds.BuiltinTypes.TrueForAll(x =>
			{
				if (x.UnderlyingType == null)
					return false;

				x.UnderlyingType.RealId = x;
				return true;
			});
		}

		void Initialize()
		{
			GlobalNamespace = new Namespace(this, new CodeString());

			// ------------------------------------------------------------------------------------
			CommonIds.SByte = new SignedType(this, new CodeString("sbyte"), 1);
			CommonIds.Int16 = new SignedType(this, new CodeString("short"), 2);
			CommonIds.Int32 = new SignedType(this, new CodeString("int"), 4);
			CommonIds.Int64 = new SignedType(this, new CodeString("long"), 8);

			CommonIds.Byte = new UnsignedType(this, new CodeString("byte"), 1);
			CommonIds.UInt16 = new UnsignedType(this, new CodeString("ushort"), 2);
			CommonIds.UInt32 = new UnsignedType(this, new CodeString("uint"), 4);
			CommonIds.UInt64 = new UnsignedType(this, new CodeString("ulong"), 8);

            Identifier IntPtrBase = null;
            Identifier UIntPtrBase = null;

            if (State.Arch.RegSize == 8)
            {
                IntPtrBase = CommonIds.Int64;
                UIntPtrBase = CommonIds.UInt64;
            }
            else if (State.Arch.RegSize == 4)
            {
                IntPtrBase = CommonIds.Int32;
                UIntPtrBase = CommonIds.UInt32;
            }
            else
            {
                throw new ApplicationException();
            }

            CommonIds.IntPtr = new IdentifierAlias(this, new CodeString("int_ptr"), IntPtrBase);
            CommonIds.IntPtr.DeclaredIdType = DeclaredIdType.Unknown;
            CommonIds.IntPtr.UndeclaredIdType = UndeclaredIdType.IntPtr;

            CommonIds.UIntPtr = new IdentifierAlias(this, new CodeString("uint_ptr"), UIntPtrBase);
            CommonIds.UIntPtr.DeclaredIdType = DeclaredIdType.Unknown;
            CommonIds.UIntPtr.UndeclaredIdType = UndeclaredIdType.UIntPtr;

			CommonIds.Single = new FloatType(this, new CodeString("float"), 4);
			CommonIds.Double = new FloatType(this, new CodeString("double"), 8);

			CommonIds.Boolean = new BooleanType(this, new CodeString("bool"));
			CommonIds.Object = new ObjectType(this, new CodeString("object"));
			CommonIds.String = new StringType(this, new CodeString("string"));
			CommonIds.Char = new CharType(this, new CodeString("char"));
			CommonIds.Void = new VoidType(this, new CodeString("void"));
			CommonIds.Auto = new AutomaticType(this, new CodeString("var"));

			CommonIds.Null = new NullType(this, new CodeString("null"));
			CommonIds.TypeOfType = new TypeOfType(this, new CodeString("type"));
			CommonIds.NamespaceType = new NamespaceType(this, new CodeString("namespace"));
			CommonIds.VoidPtr = new PointerType(this, CommonIds.Void);
			CommonIds.BytePtr = new PointerType(this, CommonIds.Byte);

			// -----------------------------------------------------------------------------------
			var ConstRec = State.Language.Root.GetObject<Recognizers.NumberRecognizer>();
			ConstRec.TypeCodes = new Recognizers.NumberTypeCodeDefinition[]
			{
				new Recognizers.NumberTypeCodeDefinition("sb", CommonIds.SByte),
				new Recognizers.NumberTypeCodeDefinition("b", CommonIds.Byte),
				new Recognizers.NumberTypeCodeDefinition("s", CommonIds.Int16),
				new Recognizers.NumberTypeCodeDefinition("us", CommonIds.UInt16),
				new Recognizers.NumberTypeCodeDefinition("i", CommonIds.Int32),
				new Recognizers.NumberTypeCodeDefinition("u", CommonIds.UInt32),
				new Recognizers.NumberTypeCodeDefinition("l", CommonIds.Int64),
				new Recognizers.NumberTypeCodeDefinition("ul", CommonIds.UInt64),
				new Recognizers.NumberTypeCodeDefinition("p", CommonIds.IntPtr),
				new Recognizers.NumberTypeCodeDefinition("up", CommonIds.UIntPtr),
				new Recognizers.NumberTypeCodeDefinition("f", CommonIds.Single),
				new Recognizers.NumberTypeCodeDefinition("d", CommonIds.Double),
			};

			// -----------------------------------------------------------------------------------
			CommonIds.NumTypes = new List<NumberType>()
			{
				CommonIds.SByte,
				CommonIds.Int16,
				CommonIds.Int32,
				CommonIds.Int64,

				CommonIds.Byte,
				CommonIds.UInt16,
				CommonIds.UInt32,
				CommonIds.UInt64,

				CommonIds.Single,
				CommonIds.Double,
			};

			CommonIds.BuiltinTypes = new List<BuiltinType>(CommonIds.NumTypes)
			{
				CommonIds.Void,
				CommonIds.Boolean,
				CommonIds.Char,
				CommonIds.String,
				CommonIds.Object,
			};

			CommonIds.Predeclared = new List<Identifier>(CommonIds.BuiltinTypes)
			{
				CommonIds.Auto,
                CommonIds.IntPtr,
                CommonIds.UIntPtr,
			};

			CommonIds.Predeclared.ForEach(x => x.Access = IdentifierAccess.Public);
			CommonIds.Null.Access = IdentifierAccess.Public;
			CommonIds.TypeOfType.Access = IdentifierAccess.Public;
			CommonIds.NamespaceType.Access = IdentifierAccess.Public;
			CommonIds.VoidPtr.Access = IdentifierAccess.Public;
			CommonIds.BytePtr.Access = IdentifierAccess.Public;

			AddIdentifier(GlobalNamespace);
			//AddIdentifier(BasicCommonIds);
		}

		void GetExprConstsCode(CodeGenerator CG)
		{
			var Arch = State.Arch;
			for (var i = 0; i < ExprConsts.Count; i++)
				ExprConsts[i].GetAssembly(CG, GetAssemblyMode.InitedValues);
		}

		public string AssemblyDescLabel
		{
			get { return OutputAssembly.DescLabel; }
		}

		public string GlobalPointersLabel
		{
			get { return "_%" + OutputAssembly.DescName + "_GlobalPointers"; }
		}

		public override void GetGlobalPointers(List<string> Out)
		{
			base.GetGlobalPointers(Out);

			for (var i = 0; i < LoadedAssemblies.Count; i++)
			{
				LoadedAssemblies[i].GlobalPointer = Out.Count;
				Out.Add(LoadedAssemblies[i].DescLabel);
			}
		}

		public override void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode)
		{
			var Arch = State.Arch;
			base.GetAssembly(CG, Mode);

			if ((Mode & GetAssemblyMode.InitedValues) != 0)
			{
				var AssemblyData = Identifiers.GetByFullNameFast<StructType>(
					this, "Internals.Reflection.AssemblyData", false);

				if (AssemblyData != null)
				{
					CG.Align(AssemblyData.Align);
					CG.InsContainer.Label(AssemblyDescLabel + "_AssemblyData");
					CG.Declare(AssemblyData, new ZeroValue());
				}

				for (var i = 0; i < IncludedBinaries.Count; i++)
				{
					var Bin = IncludedBinaries[i];
					CG.InsContainer.Label(Bin.Label);
					CG.DeclareFile(Bin.File);
				}

				var IncBinLabel = -1;
				if (IncludedBinaries.Count > 0)
				{
					IncBinLabel = State.AutoLabel;
					CG.Align(Arch.RegSize);
					CG.InsContainer.Label(IncBinLabel);

					for (var i = 0; i < IncludedBinaries.Count; i++)
						CG.DeclareLabelPtr(IncludedBinaries[i].Label);
				}

				if (State.Builder != null && State.Builder.Format == AssemblyFormat.Application)
				{
					CG.Align(Arch.RegSize);
					CG.InsContainer.Label("_Internals_Reflection_Reflection_EntryAssembly");
					CG.DeclareLabelPtr(AssemblyDescLabel);
				}

				GetExprConstsCode(CG);
			}
			
			if ((Mode & GetAssemblyMode.ReflectionData) != 0)
			{
				if (State.LibOutFile != null)
				{
					CG.Align(Arch.RegSize);
					CG.InsContainer.Label(AssemblyDescLabel);

					var AssemblyData = Identifiers.GetByFullNameFast<StructType>(
						this, "Internals.Reflection.AssemblyData", false);

					if (AssemblyData == null) CG.DeclareNull();
					else CG.DeclareLabelPtr(AssemblyDescLabel + "_AssemblyData");

					CG.DeclareLabelPtr(GlobalPointersLabel);

					//if (IncBinLabel != -1)
					//	CG.DeclareLabelPtr(IncBinLabel);

					string File;
					if (State.CodeOutFile == null) File = State.LibOutFile;
					else File = Helper.GetRelativePath(State.CodeOutFile, State.LibOutFile);

					CG.DeclareFile(File);
					CG.InsContainer.Add("\n");

					CG.Align(Arch.RegSize);
					CG.InsContainer.Label(GlobalPointersLabel);
					for (var i = 0; i < GlobalPointers.Count; i++)
						CG.DeclareLabelPtr(GlobalPointers[i]);

					CG.InsContainer.Add("\n");
				}
			}
		}

		bool HasReferencableAssemblyName(Identifier Id)
		{
			if ((Id.Flags & IdentifierFlags.Abstract) != 0) return false;
			return Id is Function || Id is GlobalVariable;
		}

		void GetExternPublicCode(StringBuilder Out, List<string> AlreadyExported, AutoAllocatedList<Identifier> List)
		{
			for (var i = 0; i < List.Count; i++)
			{
				var Id = List[i];
				if (State.Builder != null && State.Builder.Format == AssemblyFormat.Application)
				{
					if (Id.AssemblyName == "_Internals_Reflection_Reflection_EntryAssembly")
						continue;
				}

				if (Id.HasScopes && !(Id is IdentifierAlias))
				{
					foreach (var Scope in Id.EnumScopes)
						GetExternPublicCode(Out, AlreadyExported, Scope.IdentifierList);
				}

				if (!HasReferencableAssemblyName(Id))
					continue;

				var DeclaredInOutputAssembly = Id.Container.IsScopeOfAssembly();
				if (!DeclaredInOutputAssembly || (Id.Flags & IdentifierFlags.Extern) != 0)
				{
					if ((DeclaredInOutputAssembly && (Id.Flags & IdentifierFlags.Extern) != 0) ||
						(!DeclaredInOutputAssembly && Id.Used))
					{
						var Name = Id.AssemblyName;
						if (!AlreadyExported.Contains(Name))
							Out.Append("\textrn " + Name + "\n");

						if ((Id.Flags & IdentifierFlags.SpecialName) != 0) 
							AlreadyExported.Add(Name);
					}
				}
				else
				{
					if (Id.Access != IdentifierAccess.Internal && Id.Access != IdentifierAccess.Private)
						Out.Append("\tpublic " + Id.AssemblyName + "\n");
				}
			}
		}

		public StringBuilder GetExternPublicCode()
		{
			var Out = new StringBuilder();
			var AlreadyExported = new List<string>();
			GetExternPublicCode(Out, AlreadyExported, IdentifierList);

			for (var i = 0; i < LoadedAssemblies.Count; i++)
			{
				var Assembly = LoadedAssemblies[i];
				Out.Append("\textrn " + Assembly.DescLabel + "\n");
			}

			Out.Append("\tpublic " + OutputAssembly.DescLabel + "\n");
			if (State.Builder != null && State.Builder.Format == AssemblyFormat.Application)
				Out.Append("\tpublic _Internals_Reflection_Reflection_EntryAssembly\n");

			return Out;
		}

	}
}
