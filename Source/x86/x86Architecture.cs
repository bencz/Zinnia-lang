using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Numerics;
using Zinnia.Base;
using Zinnia.NativeCode;

namespace Zinnia.x86
{
	public struct x86IdReference
	{
		public int ContainerIndex;
		public int ExpressionIndex;
		public int ExecutionNumber;

		public x86IdReference(int ContainerIndex, int ExpressionIndex, int ExecutionNumber)
		{
			this.ContainerIndex = ContainerIndex;
			this.ExpressionIndex = ExpressionIndex;
			this.ExecutionNumber = ExecutionNumber;
		}

		public static int Compare(x86IdReference Ref1, x86IdReference Ref2)
		{
			if (Ref1.ContainerIndex < Ref2.ContainerIndex) return -1;
			if (Ref1.ContainerIndex > Ref2.ContainerIndex) return 1;

			if (Ref1.ExpressionIndex < Ref2.ExpressionIndex) return -1;
			if (Ref1.ExpressionIndex > Ref2.ExpressionIndex) return 1;

			if (Ref1.ExecutionNumber < Ref2.ExecutionNumber) return -1;
			if (Ref1.ExecutionNumber > Ref2.ExecutionNumber) return 1;
			return 0;
		}

		public static x86IdReference Min(x86IdReference Ref1, x86IdReference Ref2)
		{
			return Compare(Ref1, Ref2) < 0 ? Ref1 : Ref2;
		}

		public static x86IdReference Max(x86IdReference Ref1, x86IdReference Ref2)
		{
			return Compare(Ref1, Ref2) < 0 ? Ref2 : Ref1;
		}
	}

	public struct x86IdReferenceRange
	{
		public x86IdReference Min, Max;

		public void Set(x86IdReference Reference)
		{
			Min = Max = Reference;
		}

		public void Add(x86IdReference Reference)
		{
			Min = x86IdReference.Min(Min, Reference);
			Max = x86IdReference.Max(Max, Reference);
		}

		public bool Contains(x86IdReference Reference)
		{
			return x86IdReference.Compare(Min, Reference) >= 0 &&
				x86IdReference.Compare(Max, Reference) <= 0;
		}

		public bool Intersects(x86IdReferenceRange Range)
		{
			return Contains(Range.Min) || Contains(Range.Max);
		}
	}

	public class x86CallingConvention
	{
		public x86RegisterList SavedGRegs;
		public x86RegisterList SavedSSERegs;
		public x86SequenceOptions ParameterSequence;
		public x86SequenceOptions AllocationSequence;
		public bool StackCleanupByCaller;
		public int StackAlignment;
		public int ParameterAlignment;

		public bool IsSavedRegister(x86DataLocation Location)
		{
			if (Location is x86GRegLocation)
			{
				var GReg = Location as x86GRegLocation;
				return SavedGRegs[GReg.Index];
			}
			else if (Location is x86SSERegLocation)
			{
				var SSEReg = Location as x86SSERegLocation;
				return SavedSSERegs[SSEReg.Index];
			}
			else
			{
				return true;
			}
		}
	}

	public class x86AsmModifier : Modifier
	{
		public x86AsmModifier(CodeString Code)
			: base(Code)
		{
		}

		public override bool Apply(Identifier Id)
		{
			var State = Id.Container.State;
			if (!(Id is Function))
			{
				State.Messages.Add(MessageId.NotExpected, Code);
				return false;
			}

			var IdData = Id.Data.GetOrCreate<x86FunctionData>(Id);
			IdData.AssemblyOnly = true;
			return true;
		}
	}

	public class x86AsmModifierRecognizer : IModRecognizer
	{
		public static string String = "asm";

		public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Container.State;
				var ModCode = Code.Substring(0, String.Length);

				for (var i = 0; i < Out.Count; i++)
					if (Out[i] is x86AsmModifier)
					{
						State.Messages.Add(MessageId.NotExpected, ModCode);
						return SimpleRecResult.Failed;
					}

				Out.Add(new x86AsmModifier(ModCode));
				Code = Code.Substring(String.Length).Trim();
			}

			return SimpleRecResult.Unknown;
		}
	}

	public struct x86ConstString
	{
		public string String;
		public int Label;

		public x86ConstString(string String, int Label)
		{
			this.String = String;
			this.Label = Label;
		}
	}

	public class x86GlobalContainerData
	{
		public GlobalContainer Scope;
		public List<x86ConstString> ConstStrings = new List<x86ConstString>();

		ClassType _x86HelperClass;
		public ClassType Getx86HelperClass()
		{
			lock (this)
			{
				if (_x86HelperClass != null) return _x86HelperClass;
				_x86HelperClass = Identifiers.GetByFullNameFast<ClassType>(Scope, "Internals._x86Helper");
				return _x86HelperClass;
			}
		}

		public Identifier GetHelperId(string Name, GetIdOptions Options)
		{
			var HelperClass = Getx86HelperClass();
			if (HelperClass == null) return null;

			var CName = new CodeString("_x86Helper." + Name);
			var Id = Identifiers.GetMember(Scope.State, HelperClass, Name, CName, Options);
			Id.Used = true;
			return Id;
		}

		public Identifier GetHelperId(string Name)
		{
			return GetHelperId(Name, GetIdOptions.Default);
		}

		public Function GetHelperFunction(string Name)
		{
			var Options = GetIdOptions.Default;
			Options.Func = x => x is Function;

			return GetHelperId(Name, Options) as Function;
		}

		public Variable GetHelperVariable(string Name)
		{
			var Options = GetIdOptions.Default;
			Options.Func = x => x is Variable;

			return GetHelperId(Name, Options) as Variable;
		}

		public x86GlobalContainerData(GlobalContainer Scope)
		{
			this.Scope = Scope;
		}
	}

	public class x86ConditionalJump : JumpInstruction
	{
		public string Condition;

		public x86ConditionalJump(int Label, string Condition)
			: base(Label)
		{
			this.Condition = Condition;
		}
	}

	public struct x86MoveStruct
	{
		public x86DataLocation Dst;
		public x86DataLocation Src;
		public x86TemporaryData TempData;
		public x86ExecutorType Executor;
		public x86StoredDataType StoredDataType;
		
		public x86MoveStruct(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, Identifier Type)

			: this(Dst, Src, TempData, Executor, x86Identifiers.GetStoredDataType(Type))
		{

		}

		public x86MoveStruct(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			this.Dst = Dst;
			this.Src = Src;
			this.TempData = TempData;
			this.Executor = Executor;
			this.StoredDataType = StoredDataType;
		}

		public x86MoveStruct Inverse
		{
			get { return new x86MoveStruct(Src, Dst, TempData, Executor, StoredDataType); }
		}
	}

	public class x86MoveCondBranch : CondBranch
	{
		public x86MoveStruct Struct;

		public x86MoveCondBranch(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, Identifier Type)
		{
			Struct = new x86MoveStruct(Dst, Src, TempData, Executor, Type);
		}

		public x86MoveCondBranch(x86DataLocation Dst, x86DataLocation Src, x86TemporaryData TempData,
			x86ExecutorType Executor, x86StoredDataType StoredDataType)
		{
			Struct = new x86MoveStruct(Dst, Src, TempData, Executor, StoredDataType);
		}

		public x86MoveCondBranch(x86MoveStruct Struct)
		{
			this.Struct = Struct;
		}
	}

	[Flags]
	public enum x86IdentifierFlags : byte
	{
		HasReferenceRange = 1,
		WholeContainerUsed = 2,
		CantBeInReg = 4,
		ParamLocUsable = 8,
		NeededToBeMem = 16,
	}

	public class x86IdentifierData
	{
		public Identifier Identifier;
		public LocalVariable PreferredIdForLocation;
		public x86DataLocation Location;
		public x86DataLocation ParamLocation;
		public x86DataList LocationCantBe;
		public x86IdReferenceRange ReferenceRange;
		public int ReferenceCount;
		public x86IdentifierFlags Flags;

		public x86IdentifierData(Identifier Identifier)
		{
			this.Identifier = Identifier;
		}

		public void CheckLocation()
		{
			if (Location == null)
				throw new InvalidOperationException();

            var Type = Identifier.TypeOfSelf.RealId as Type;
            if (Location.Size != Type.Size && !(Location is x86SSERegLocation))
				throw new ApplicationException();

			if (Location is x86MemoryLocation)
				Flags |= x86IdentifierFlags.NeededToBeMem;
			//else if ((Flags & x86IdentifierFlags.NeededToBeMem) != 0)
				//throw new ApplicationException();
		}

		public void Reset()
		{
			Location = null;
		}

		public void GetStartEnd(out int Start, out int End)
		{
			if ((Flags & x86IdentifierFlags.WholeContainerUsed) != 0)
			{
				Start = 0;
				End = Identifier.Container.Children.Count - 1;
			}
			else if ((Flags & x86IdentifierFlags.HasReferenceRange) != 0)
			{
				Start = ReferenceRange.Min.ContainerIndex;
				End = ReferenceRange.Max.ContainerIndex;
			}
			else
			{
				Start = 0;
				End = -1;
			}
		}

		public void SetParameterUsed()
		{
			var Data = Identifier.Container.Data.Get<x86IdContainerData>();
			Data.UsedByParams.SetUsed(ParamLocation);

			int Start, End;
			GetStartEnd(out Start, out End);

			for (var j = Start; j <= End; j++)
			{
				Identifier.Container.Children[j].ForEach(x =>
				{
					var xData = x.Data.Get<x86IdContainerData>();
					xData.UsedByParams.SetUsed(ParamLocation);
				});
			}
		}

		public void SetLocationUsed(bool OnlyChildContainers = false)
		{
			if (!OnlyChildContainers)
			{
				var Data = Identifier.Container.Data.Get<x86IdContainerData>();
				Data.Allocator.SetUsed(Location);
			}

			int Start, End;
			GetStartEnd(out Start, out End);

			for (var j = Start; j <= End; j++)
			{
				Identifier.Container.Children[j].ForEach(x =>
				{
					var xData = x.Data.Get<x86IdContainerData>();
					xData.Allocator.SetUsed(Location);
				});
			}
		}
	}

	public delegate void ProcessAsCallFunc(int Index, x86DataLocation Pos);

	[Flags]
	public enum x86Extensions
	{
		None = 0,
		LongMode = 1,
		FPU = 2,
		SSE = 4,
		SSE2 = 8,
		SSE3 = 16,
		SSSE3 = 32,
		SSE41 = 64,
		SSE42 = 128,
		SSE4ABM = 256,
		SSE4a = 512,
		AVX = 1024,
		CVT16 = 2048,
		XOP = 4096,
		FMA3 = 8192,
		FMA4 = 16384,
		AES = 32768,
		CLMUL = 65536,
		AVX2 = 131072,

		Default = FPU | SSE | SSE2 | SSE3,
		Default64 = LongMode | Default,
	}

	public enum x86FloatingPointMode : byte
	{
		Disabled,
		FPU,
		SSE
	}

	public class x86InstructionEncoder : InstructionEncoder
	{
		public static string EncodeToText(string Instruction, params object[] Parameters)
		{
			if (Parameters.Length > 0 && Parameters[0] == null)
				Parameters[0] = string.Empty;

			return "\t" + Instruction + " " + string.Join(", ", Parameters) + "\n";
		}

		public override string EncodeToText(Instruction Instruction)
		{
			if (Instruction is StrInstruction)
			{
				var StrIns = Instruction as StrInstruction;
				return StrIns.Instruction;
			}
			else if (Instruction is LabelInstruction)
			{
				var LabelIns = Instruction as LabelInstruction;
				return LabelIns.LabelName + ":\n";
			}
			else if (Instruction is JumpInstruction)
			{
				if (Instruction is x86ConditionalJump)
				{
					var JumpIns = Instruction as x86ConditionalJump;
					return EncodeToText("j" + JumpIns.Condition, JumpIns.LabelName);
				}
				else
				{
					var JumpIns = Instruction as JumpInstruction;
					return EncodeToText("jmp", JumpIns.LabelName);
				}
			}
			else
			{
				throw new NotImplementedException();
			}
		}
	}

	public class x86Architecture : IArchitecture, INCArchitecture
	{
		public struct CallingConventionsList
		{
			public x86CallingConvention CDecl;
			public x86CallingConvention StdCall;
			public x86CallingConvention ZinniaCall;
		}

		public CompilerState State;
		public x86Extensions Extensions;
		public x86FloatingPointMode FloatingPointMode;
		public CallingConventionsList CallingConventions;

		public int SSERegSize { get { return (Extensions & x86Extensions.AVX) != 0 ? 32 : 16; } }
		public int RegSize { get { return (Extensions & x86Extensions.LongMode) != 0 ? 8 : 4; } }
		public int RegCount { get { return (Extensions & x86Extensions.LongMode) != 0 ? 16 : 8; } }
		public int MaxStructPow2Size { get { return (Extensions & x86Extensions.SSE) != 0 ? SSERegSize : RegSize; } }
		public int ByteRegCount { get { return (Extensions & x86Extensions.LongMode) != 0 ? 14 : 4; } }
		public x86RegisterMask RegisterMask { get { return new x86RegisterMask(0, RegSize); } }

		public x86ExecutorType GetDefaultExecutor(Identifier Identifier)
		{
			var TypeKind = x86Identifiers.GetTypeKind(Identifier);
			return GetDefaultExecutor(TypeKind);
		}

		public x86ExecutorType GetDefaultExecutor(x86TypeKind TypeKind)
		{
			if (x86Identifiers.IsFloatTypeKind(TypeKind))
			{
				if (FloatingPointMode == x86FloatingPointMode.FPU)
					return x86ExecutorType.FPU;
				else if (FloatingPointMode == x86FloatingPointMode.SSE) 
					return x86ExecutorType.SSE;
				
				return x86ExecutorType.None;
			}

			return x86ExecutorType.SSE | x86ExecutorType.General;
		}

		public x86CallingConvention GetCallingConvention(CallingConvention Conv)
		{
			if (Conv == CallingConvention.ZinniaCall) return CallingConventions.ZinniaCall;
			else if (Conv == CallingConvention.StdCall) return CallingConventions.StdCall;
			else if (Conv == CallingConvention.CDecl) return CallingConventions.CDecl;
			else throw new ApplicationException();
		}

		public x86CallingConvention GetCallingConvention(Identifier Id)
		{
			var FuncType = Id.TypeOfSelf.RealId as TypeOfFunction;
			return GetCallingConvention(FuncType.CallConv);
		}

		public static x86CallingConvention GetCallingConvention(bool LongMode,
			OperatingSystem OperatingSystem, CallingConvention CallingConvention)
		{
			x86CallingConvention RetValue;

			if (LongMode)
			{
				if (OperatingSystem == OperatingSystem.Windows)
				{
					RetValue = new x86CallingConvention();
					RetValue.SavedGRegs = new x86RegisterList()
					{
						UsedRegs = new bool[]
						{ 
							false, false, false, true, false, true, true, true,
							false, false, false, false, true, true, true, true,
						}
					};

					RetValue.SavedSSERegs = new x86RegisterList()
					{
						UsedRegs = new bool[]
						{ 
							false, false, false, false, false, false, true, true,
							true, true, true, true, true, true, true, true,
						}
					};

					RetValue.AllocationSequence = new x86SequenceOptions()
					{
						GRegisters = new int[]
						{
							0, 2, 1, 8, 9, 10, 11, 
							3, 5, 6, 7, 12, 13, 14, 15
						},

						SSERegisters = new int[]
						{
							0, 1, 2, 3, 4, 5,
							6, 7, 8, 9, 10, 12, 13, 14, 15
						},

						AllowPartRegisters = true,
						Align = 1,
					};

					RetValue.ParameterSequence = new x86SequenceOptions()
					{
						GRegisters = new int[] { 2, 1, 8, 9 },
						SSERegisters = new int[] { 0, 1, 2, 3 },
						AllowPartRegisters = false,
						Align = 8,
					};

					RetValue.StackCleanupByCaller = false;
					RetValue.StackAlignment = LongMode ? 8 : 4;
					RetValue.ParameterAlignment = LongMode ? 8 : 4;
				}
				else
				{
					throw new NotImplementedException();
				}
			}
			else
			{
				var SavedGRegisters = new x86RegisterList()
				{
					UsedRegs = new bool[]
					{ 
						false, false, false, true, false, true, true, true,
					}
				};

				var GRegAllocationSequence = new int[]
				{
					0, 2, 1, 3, 5, 6, 7,
				};

				if (CallingConvention == CallingConvention.ZinniaCall)
				{
					RetValue = new x86CallingConvention();
					RetValue.SavedGRegs = SavedGRegisters;

					RetValue.SavedSSERegs = new x86RegisterList()
					{
						UsedRegs = new bool[]
						{
							false, false, false, false, true, true, true, true,
						}
					};

					RetValue.AllocationSequence = new x86SequenceOptions()
					{
						GRegisters = GRegAllocationSequence,
						SSERegisters = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 },
						AllowPartRegisters = true,
						Align = 1,
					};

					RetValue.ParameterSequence = new x86SequenceOptions()
					{
						GRegisters = new int[] { 0, 2, 1 },
						SSERegisters = new int[] { 0, 1, 2 },
						AllowPartRegisters = true,
						Align = 4,
					};

					RetValue.StackCleanupByCaller = false;
					RetValue.StackAlignment = LongMode ? 8 : 4;
					RetValue.ParameterAlignment = LongMode ? 8 : 4;
				}
				else
				{
					RetValue = new x86CallingConvention();
					RetValue.SavedGRegs = SavedGRegisters;
					RetValue.SavedSSERegs = new x86RegisterList() { UsedRegs = new bool[8] };

					RetValue.AllocationSequence = new x86SequenceOptions()
					{
						GRegisters = GRegAllocationSequence,
						SSERegisters = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 },
						AllowPartRegisters = true,
						Align = 1,
					};

					RetValue.ParameterSequence = new x86SequenceOptions()
					{
						GRegisters = new int[] { },
						SSERegisters = new int[] { },
						AllowPartRegisters = false,
						Align = 4,
					};

					RetValue.StackCleanupByCaller = CallingConvention == CallingConvention.CDecl;
					RetValue.StackAlignment = LongMode ? 8 : 4;
					RetValue.ParameterAlignment = LongMode ? 8 : 4;
				}
			}

			return RetValue;
		}

		void InitializeCallingConventions()
		{
			var OS = State.OperatingSystem;
			var LongMode = (Extensions & x86Extensions.LongMode) != 0;

			CallingConventions.ZinniaCall = GetCallingConvention(LongMode, OS, CallingConvention.ZinniaCall);
			CallingConventions.CDecl = GetCallingConvention(LongMode, OS, CallingConvention.CDecl);
			CallingConventions.StdCall = GetCallingConvention(LongMode, OS, CallingConvention.StdCall);
		}

		public bool SetupPlugin(PluginRoot Plugin)
		{
			var x86Plugins = new IExpressionPlugin[]
			{
				new x86ExpressionPlugin(Plugin),
				new x86Plugin(Plugin),
			};

			Plugin.Plugins = Plugin.Plugins.Union(x86Plugins).ToArray();
			return true;
		}

		public bool ProcessContainer(IdContainer Container)
		{
			return x86Functions.ProcessContainer(Container);
		}

		public bool Compile(CompilerState State, CodeFile[] CodeFiles)
		{
			this.State = State;
			Initialize();

			var Language = State.Language;
            var OldModRecs = Language.ModRecognizers;
            Language.ModRecognizers = OldModRecs.Union(new x86AsmModifierRecognizer()).ToArray();

			var Scope = State.GlobalContainer;
			var Data = new x86GlobalContainerData(Scope);
			Scope.Data.Set(Data);

            var Preprocessor = Scope.Preprocessor;
            DefineExtensions(Preprocessor, Extensions);
            Preprocessor.Define("FP_MODE", FloatingPointMode.ToString());
			if (!Scope.Process()) return false;

			Write(State.CodeOut, SW => GetAsmCode(State, SW));
			Scope.Data.Remove<x86GlobalContainerData>();

            Language.ModRecognizers = OldModRecs;
			return true;
		}

        public static bool DefineExtensions(Preprocessor Preprocessor, x86Extensions Extensions)
		{
			var RetValue = true;
			foreach (var e in Enum.GetValues(typeof(x86Extensions)))
			{
				var EnumVal = (x86Extensions)e;
				if ((Extensions & EnumVal) != 0)
				{
					string Name;
					if (EnumVal == x86Extensions.LongMode) Name = "LONG_MODE";
					else Name = EnumVal.ToString().ToUpper();

                    if (!Preprocessor.Define(Name))
						RetValue = false;
				}
			}

			return RetValue;
		}

		void Write(Stream Stream, Action<StreamWriter> Func)
		{
			var StreamWriter = new StreamWriter(Stream);
			Func(StreamWriter);
			StreamWriter.Flush();
		}

		public bool IsSimpleCompareNode(ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			if (OpNode == null) return true;

			var Op = OpNode.Operator;
			var Ch = OpNode.Children;

			for (var i = 0; i < Ch.Length; i++)
				if (Ch[i] is OpExpressionNode)
				{
					var OpChi = Ch[i] as OpExpressionNode;
					var ChiOp = OpChi.Operator;
					var ChiCh = OpChi.Children;

					if (ChiOp != Operator.Member && ChiOp != Operator.Index)
						return false;

					for (var j = 0; j < ChiCh.Length; j++)
						if (ChiCh[j] is OpExpressionNode) return false;
				}

			if (Operators.IsRelEquality(Op))
				return true;

			if (Op == Operator.Unknown)
			{
				var Data = Node.Data.Get<x86NodeData>();
				if (x86Expressions.IsConditionOp(Data.Operator))
					return true;
			}

			return false;
		}

		bool ProcessAssembly(Function Function)
		{
			var Container = Function.Container;
			var State = Container.State;
			var Handlers = State.Language.GlobalHandlers;
			var Data = Function.Data.Get<x86FunctionData>();
			var Scope = Function.FunctionScope;
			var Code = Scope.Code;

			var RetValue = true;
			var Assembly = new StringBuilder();
			var Labels = new Dictionary<string, int>();

			foreach (var Line in Code.EnumLines())
			{
				var NLine = Line;
				var CommentPos = NLine.Find(';', Handlers: Handlers);
				if (CommentPos != -1)
					NLine = NLine.Substring(0, CommentPos);

				NLine = NLine.Trim();
				if (NLine.Length == 0) continue;

				var LabelPos = NLine.Find(':', Handlers: Handlers);
				if (LabelPos != -1)
				{
					var LabelName = NLine.TrimmedSubstring(State, 0, LabelPos);
					if (!LabelName.IsValid) { RetValue = false; continue; }

					if (!LabelName.IsValidIdentifierName)
					{
						State.Messages.Add(MessageId.NotValidName, LabelName);
						RetValue = false; continue;
					}

					foreach (var e in Labels)
						if (LabelName.IsEqual(e.Key))
						{
							State.Messages.Add(MessageId.LabelAlreadyDefined, LabelName);
							RetValue = false;
							break;
						}

					if (!RetValue) continue;
					Labels.Add(LabelName.ToString(), State.AutoLabel);
				}

				var Pos = 0;
				if (LabelPos == -1)
					Assembly.Append("\t");

				foreach (var e in NLine.EnumFind("$["))
				{
					var FromBracket = NLine.Substring(e + 1);
					var BracketPos = FromBracket.GetBracketPos(State);
					if (BracketPos == -1) { RetValue = false; break; }

					var IdName = FromBracket.Substring(1, BracketPos - 1).Trim();
					var Id = Identifiers.Recognize(Container, IdName);
					if (Id == null) { RetValue = false; break; }
					
					Assembly.Append(NLine.Substring(Pos, e - Pos));
					Assembly.Append(x86Expressions.GetLocation(this, Id));
					Pos = e + BracketPos + 2;
				}

				Assembly.Append(NLine.Substring(Pos) + "\n");
			}

			if (Labels.Count > 0)
			{
				var Pos = 0;
				var String = Assembly.ToString();
				var StringRange = new StringSlice(String);
				Assembly.Clear();

				foreach (var Word in StringRange.EnumWords())
				{
					foreach (var Label in Labels)
						if (Word.IsEqual(Label.Key))
						{
							Assembly.Append(String.Substring(Pos, Word.Index - Pos));
							Assembly.Append("_" + Label.Value);
							Pos = Word.Index + Word.Length;
						}
				}

				Assembly.Append(String.Substring(Pos));
			}

			Data.Assembly = Assembly.ToString();
			return RetValue;
		}

		public void GetAssembly(CodeGenerator CG, Function Function)
		{
			var Data = Function.Data.Get<x86FunctionData>();
			CG.InsContainer.Add(Data.Assembly);
		}

		public bool ProcessFunction(Function Func)
		{
			var Data = Func.Data.Get<x86FunctionData>();
			var Scope = Func.FunctionScope;

			if (Data.AssemblyOnly) return ProcessAssembly(Func);
			else return x86Functions.ProcessFunction(Scope);
		}

		public bool CreateAssembly(Function Func)
		{
			var Data = Func.Data.Get<x86FunctionData>();
			var Scope = Func.FunctionScope;

			if (!Data.AssemblyOnly) 
				x86Functions.CreateAssembly(Scope);

			return true;
		}

		public bool ProcessIdentifier(Identifier Id)
		{
			if (Id is GlobalVariable)
			{
				var Var = Id as GlobalVariable;
				var Type = Var.TypeOfSelf.RealId as Type;

				var Data = Id.Data.GetOrCreate<x86IdentifierData>(Var);
				var LabelPos = new x86NamedLabelPosition(this, Id.AssemblyName);
				Data.Location = new x86IndexLocation(this, 0, Type.Size, LabelPos);
			}
			else if (Id is Function)
			{
				var Func = Id as Function;

				var Data = Id.Data.GetOrCreate<x86FunctionData>(Func);
				Data.Location = new x86NamedLabelPosition(this, Id.AssemblyName);
				if (Data.AssemblyOnly)
				{
					Func.FunctionScope.Flags |= FunctionScopeFlags.DisableParsing;
					Func.FunctionScope.Flags |= FunctionScopeFlags.DisableCodeCheck;
					Func.FunctionScope.Flags |= FunctionScopeFlags.DisableAddingRetCommand;
				}
			}

			return true;
		}

		private void GetSectionCode(IdContainer Container, string Section,
			TextWriter Out, Action<x86CodeGenerator> Func)
		{
			var Compiler = new x86CodeGenerator(Container);
			Func(Compiler);

			var InsEncoder = new x86InstructionEncoder();
			var Code = Compiler.InsContainer.EncodeToText(InsEncoder);
			if (Code.Length > 0)
			{
				Out.Write(Section);
				Out.Write(Code);
			}
		}

		private void GetAsmCode(CompilerState State, TextWriter Out)
		{
			var Scope = State.GlobalContainer;
			var Data = Scope.Data.Get<x86GlobalContainerData>();

			if (State.Format == ImageFormat.MSCoff)
			{
				if ((Extensions & x86Extensions.LongMode) != 0)
					Out.Write("\tformat MS64 COFF\n");
				else Out.Write("\tformat MS COFF\n");
			}
			else if (State.Format == ImageFormat.GUI)
			{
				if ((Extensions & x86Extensions.LongMode) != 0)
					Out.Write("\tformat PE64 GUI 4.0\n");
				else Out.Write("\tformat PE GUI 4.0\n");
			}
			else if (State.Format == ImageFormat.Console)
			{
				if ((Extensions & x86Extensions.LongMode) != 0)
					Out.Write("\tformat PE64 CONSOLE 4.0\n");
				else Out.Write("\tformat PE CONSOLE 4.0\n");
			}
			else if (State.Format == ImageFormat.DLL || State.Format == ImageFormat.AsDLL)
			{
				if ((Extensions & x86Extensions.LongMode) != 0)
					Out.Write("\tformat PE64 DLL 4.0\n");
				else Out.Write("\tformat PE DLL 4.0\n");
			}
			else
			{
				throw new ApplicationException();
			}

			var ExternCode = Scope.GetExternPublicCode();
			if (ExternCode.Length > 0) Out.Write(ExternCode);
			Out.Write("\t\n");

			// -----------------------------------------------------------------
			GetSectionCode(Scope, "section \".text\" code readable executable\n\n", Out,
				CG => Scope.GetAssembly(CG, GetAssemblyMode.Code));

			GetSectionCode(Scope, "section \".data\" data readable writeable align 32\n\n", Out,
				CG =>
				{
					Scope.GetAssembly(CG, GetAssemblyMode.InitedValues);

					for (var i = 0; i < Data.ConstStrings.Count; i++)
					{
						var e = Data.ConstStrings[i];
						CG.InsContainer.Label(e.Label);
						CG.DeclareValueString(e.String);
					}

					CG.InsContainer.Add("\n");
				});

			GetSectionCode(Scope, "section \".rdata\" data readable align 32\n\n", Out,
				CG => Scope.GetAssembly(CG, GetAssemblyMode.ReflectionData));

			GetSectionCode(Scope, "section \".bss\" data readable writeable align 32\n", Out,
				CG =>
				{
					var InsContainer = CG.ExecuteOnTempInsContainer(() =>
						Scope.GetAssembly(CG, GetAssemblyMode.UninitedValues));

#warning WARNING
					//if (InsContainer.Instructions.Count > 0)
					{
						Scope.Flags |= GlobalContainerFlags.HasUninitedValues;
						CG.InsContainer.Label("_%UninitedValues_Begin");
						CG.InsContainer.Add("\n");
						CG.InsContainer.Add(InsContainer);
						CG.Align(32);
						CG.InsContainer.Label("_%UninitedValues_End");
						CG.InsContainer.Add("\n");
					}

				});
		}

		public static bool CanBeIndexRegScale(ExpressionNode Node)
		{
			if (Node is ConstExpressionNode)
			{
				var ConstNode = Node as ConstExpressionNode;
				return CanBeIndexRegScale(ConstNode.Integer);
			}

			return false;
		}

		public static bool CanBeIndexRegScale(BigInteger Num)
		{
			return Num == 1 || Num == 2 || Num == 4 || Num == 8;
		}

		public static bool CanBeIndexRegScale(int Num)
		{
			return Num == 1 || Num == 2 || Num == 4 || Num == 8;
		}

		void ProcessRegisterParams_GReg(int Index, int Size, ProcessAsCallFunc Func, x86DataSequence DS)
		{
			if (!DS.CanAllocGReg(Size)) return;
			var Pos = (x86DataLocation)null;

			if (Size > RegSize)
			{
				var RSize = Size;
				var PartCount = (Size - 1) / RegSize + 1;
				var Positions = new x86DataLocation[PartCount];

				for (var i = 0; i < PartCount; i++)
				{
					var S = Size < RegSize ? Size : RegSize;
					var Reg = DS.GetGRegister(S);

					Positions[i] = Reg;
					Size -= S;
				}

				Pos = new x86MultiLocation(this, RSize, Positions);
			}
			else
			{
				Pos = DS.GetGRegister(Size);
			}

			Func(Index, Pos);
		}

		public static bool IsPointerType(Identifier Type)
		{
			var T = Type.RealId as Type;
			return T is ReferenceType || T is TypeOfFunction || T is PointerType || 
				(T.TypeFlags & TypeFlags.ReferenceValue) != 0;
		}

		public void ProcessRegisterParams(Type[] Params, CallingConvention CallConv, ProcessAsCallFunc Func, x86DataSequence DS = null)
		{
			if (DS == null)
			{
				var x86CallConv = GetCallingConvention(CallConv);
				DS = new x86DataSequence(this, x86CallConv.ParameterSequence);
			}

			if (CallConv == CallingConvention.ZinniaCall)
			{
				var Processed = new bool[Params.Length];

				for (var i = 0; i < Params.Length; i++)
					if (IsPointerType(Params[i]))
					{
						if ((x86Identifiers.GetPossibleLocations(Params[i]) & x86DataLocationType.General) != 0)
						{
							ProcessRegisterParams_GReg(i, Params[i].Size, Func, DS);
							Processed[i] = true;
						}
					}

				for (var i = 0; i < Params.Length; i++)
					if (!Processed[i])
					{
						if ((x86Identifiers.GetPossibleLocations(Params[i]) & x86DataLocationType.General) != 0)
						{
							ProcessRegisterParams_GReg(i, Params[i].Size, Func, DS);
							Processed[i] = true;
						}
					}
			}
			else
			{
				for (var i = 0; i < Params.Length; i++)
				{
					if ((x86Identifiers.GetPossibleLocations(Params[i]) & x86DataLocationType.General) != 0)
						ProcessRegisterParams_GReg(i, Params[i].Size, Func, DS);
				}
			}

			if (FloatingPointMode == x86FloatingPointMode.SSE)
			{
				for (var i = 0; i < Params.Length; i++)
					if (Params[i].RealId is FloatType)
					{
						if ((x86Identifiers.GetPossibleLocations(Params[i]) & x86DataLocationType.SSEReg) != 0)
						{
							if (!DS.CanAllocSSEReg()) break;
							Func(i, DS.GetSSERegister());
						}
					}
			}
		}

		public x86Architecture(x86Extensions Extensions = x86Extensions.Default)
		{
			this.Extensions = Extensions;

			if ((Extensions & x86Extensions.SSE2) != 0)
				FloatingPointMode = x86FloatingPointMode.SSE;
			else FloatingPointMode = x86FloatingPointMode.FPU;
		}

		void Initialize()
		{
			InitializeCallingConventions();
		}

		public static bool IsGRegister(x86DataLocation Pos, int Index)
		{
			var RegPos = Pos as x86GRegLocation;
			return RegPos != null && RegPos.Index == Index;
		}

		public bool IsGRegisterExists(int Index, int Offset, int Size)
		{
			var LongMode = (Extensions & x86Extensions.LongMode) != 0;
			if (Index < 0) throw new ArgumentOutOfRangeException("Index");

			if (Size == 1)
			{
				if (Index < 4) return Offset == 0 || Offset == 1;
				if (LongMode && Offset == 0 && Index >= 6 && Index < 16) return true;
			}
			else if (Size == 2 || Size == 4 || (Size == 8 && LongMode))
			{
				return Offset == 0 && (Index < 8 || (LongMode && Index < 16));
			}

			return false;
		}

		public bool IsGRegisterExists(int Index, x86RegisterMask Mask)
		{
			return IsGRegisterExists(Index, Mask.Offset, Mask.Size);
		}

		public static string GetTypeString(int Size)
		{
			switch (Size)
			{
				case 1: return "byte";
				case 2: return "word";
				case 4: return "dword";
				case 6: return "pword";
				case 8: return "qword";
				case 10: return "tword";
				case 16: return "dqword";
				default: return null;
			}
		}

		public static string GetDataTypeString(int Size)
		{
			switch (Size)
			{
				case 1: return "db";
				case 2: return "dw";
				case 4: return "dd";
				case 6: return "dp";
				case 8: return "dq";
				case 10: return "dt";
				default: return null;
			}
		}

		public static string GetGRegisterName(int Index, int Size)
		{
			return GetGRegisterName(Index, 0, Size);
		}

		public static string GetGRegisterName(int Index, int Offset, int Size)
		{
			if (Size == 1)
			{
				if (Offset != 0 && Offset != 1)
					return null;

				switch (Index)
				{
					case 0: return Offset == 1 ? "ah" : "al";
					case 1: return Offset == 1 ? "ch" : "cl";
					case 2: return Offset == 1 ? "dh" : "dl";
					case 3: return Offset == 1 ? "bh" : "bl";

					case 6: return Offset == 1 ? null : "sil";
					case 7: return Offset == 1 ? null : "dil";

					default:
						if (Index >= 8 && Index < 16)
							return Offset == 1 ? null : "r" + Index + "b";
						else return null;
				}
			}
			else if (Size == 2)
			{
				if (Offset != 0)
					return null;

				switch (Index)
				{
					case 0: return "ax";
					case 1: return "cx";
					case 2: return "dx";
					case 3: return "bx";
					case 4: return "sp";
					case 5: return "bp";
					case 6: return "si";
					case 7: return "di";

					default:
						if (Index >= 8 && Index < 16) return "r" + Index + "w";
						else return null;
				}
			}
			else if (Size == 4)
			{
				if (Offset != 0)
					return null;

				switch (Index)
				{
					case 0: return "eax";
					case 1: return "ecx";
					case 2: return "edx";
					case 3: return "ebx";
					case 4: return "esp";
					case 5: return "ebp";
					case 6: return "esi";
					case 7: return  "edi";

					default:
						if (Index >= 8 && Index < 16) return "r" + Index + "d";
						else return null;
				}
			}
			else if (Size == 8)
			{
				if (Offset != 0)
					return null;

				switch (Index)
				{
					case 0: return "rax";
					case 1: return "rcx";
					case 2: return "rdx";
					case 3: return "rbx";
					case 4: return "rsp";
					case 5: return "rbp";
					case 6: return "rsi";
					case 7: return "rdi";

					default:
						if (Index >= 8 && Index < 16) return "r" + Index;
						else return null;
				}
			}
			else
			{
				return null;
			}
		}

		public static string OpInstruction(x86Operator Op)
		{
			if (Op == x86Operator.IsCarryFlagSet) return "c";
			if (Op == x86Operator.IsCarryFlagZero) return "nc";
			if (Op == x86Operator.IsParityFlagSet) return "p";
			if (Op == x86Operator.IsParityFlagZero) return "np";
			if (Op == x86Operator.IsZeroFlagSet) return "z";
			if (Op == x86Operator.IsZeroFlagZero) return "nz";
			if (Op == x86Operator.IsSignFlagSet) return "s";
			if (Op == x86Operator.IsSignFlagZero) return "ns";
			if (Op == x86Operator.IsOverflowFlagSet) return "o";
			if (Op == x86Operator.IsOverflowFlagZero) return "no";
			return null;
		}

		public static string OpInstruction(Operator Op, bool Signed)
		{
			if (Op == Operator.Equality) return "e";
			if (Op == Operator.Inequality) return "ne";
			if (Op == Operator.Less) return Signed ? "l" : "b";
			if (Op == Operator.LessEqual) return Signed ? "le" : "be";
			if (Op == Operator.Greater) return Signed ? "g" : "a";
			if (Op == Operator.GreaterEqual) return Signed ? "ge" : "ae";
			return null;
		}

		public x86MoveCondBranch GetNodeMoveBranch(ExpressionNode Node)
		{
			var OpNode = Node as OpExpressionNode;
			if (OpNode == null) return null;

			var Op = OpNode.Operator;
			var Ch = OpNode.Children;

			if (Op == Operator.Assignment)
			{
				var To = x86Expressions.GetLocation(this, Ch[0]);
				var From = x86Expressions.GetLocation(this, Ch[1]);
				if (From == null || To == null || x86Expressions.NeedsInstructions(Ch[0]))
					return null;

				if (x86Expressions.NeedsInstructions(Ch[1]))
					return From.Compare(To) ? GetNodeMoveBranch(Ch[1]) : null;

				var Data = Node.Data.Get<x86NodeData>();
				return new x86MoveCondBranch(To, From, Data.TempData, x86ExecutorType.All, Node.Type);
			}
			else if (Op == Operator.Cast)
			{
				var Data = Node.Data.Get<x86NodeData>();
				var To = Expressions.GetIdentifier(Ch[1]);

				var StoredDataType = x86Identifiers.GetStoredDataType(To);
				if (!Ch[0].Type.IsEquivalent(To) || x86Identifiers.IsFloatTypeKind(StoredDataType.TypeKind))
					return null;

				var From = x86Expressions.GetLocation(this, Ch[0]);
				if (From != null && Data.Output != null && !x86Expressions.NeedsInstructions(Ch[0]))
				{
					return new x86MoveCondBranch(Data.ExtractedOutput, From,
						Data.TempData, x86ExecutorType.All, StoredDataType);
				}
			}

			return null;
		}

		public CondBranch GetNodeCondBrach(ExpressionNode Node,
			bool EnableMoveBranch = true, bool RetNull = true)
		{
			if (EnableMoveBranch)
			{
				var Ret = GetNodeMoveBranch(Node);
				if (Ret != null) return Ret;
			}

			if (RetNull) return null;
			return new CodeCondBranch(x => x.EmitExpression(Node));
		}

		public CondBranch GetCondBranchWithMove(x86DataLocation Dst, x86NodeData Data, ExpressionNode Node)
		{
			if (x86Expressions.NeedsInstructions(Node))
			{
				return new CodeCondBranch(CG =>
				{
					var x86CG = CG as x86CodeGenerator;
					x86CG.EmitMoveExpression(Data, Dst, Node);
				});
			}

			var Src = x86Expressions.GetLocation(this, Node);
			return new x86MoveCondBranch(Dst, Src, Data.TempData, x86ExecutorType.All, Node.Type);
		}

		private ExpressionNode GetExpr(Command Obj)
		{
			return Obj != null && Obj.Type == CommandType.Expression ? Obj.Expressions[0] : null;
		}

		void ProcessMoveCondBranch(GlobalContainer Global, x86MoveCondBranch Branch)
		{
			if (Branch.Struct.Dst != null && Branch.Struct.Src is x86ConstLocation &&
				Branch.Struct.Dst.Size > 1 && Branch.Struct.Dst is x86GRegLocation)
			{
				var ConstPos = Branch.Struct.Src as x86ConstLocation;
				var GlbVar = Global.CreateExprConst(ConstPos.Value, ConstPos.Type);
				if (GlbVar == null) throw new ApplicationException();

				var Data = GlbVar.Data.Get<x86IdentifierData>();
				Branch.Struct.Src = Data.Location;
			}
		}

		CondBranch GetJumpBranch(Command Comm)
		{
			if (Comm == null) return null;

			if (Comm.Extension is NCCommandExtension)
			{
				var NCExtension = Comm.Extension as NCCommandExtension;
				if (NCExtension.Type == NCCommandType.Jump)
				{/*
				var Src = Expressions.GetIdentifier(Comm.Expressions[0]);
				if (!(Comm.Expressions[0] is OpExpressionNode))
				{
					return new CodeCondBranch(CG =>
					{
						var x86CG = CG as x86CodeGenerator;
						var SrcLoc = x86Expressions.GetLocation(this, Comm.Expressions[0]);
						x86CG.ProcessIndexMoves(SrcLoc);
						x86CG.Append("\tjmp " + SrcLoc + "\n");
					});
				}*/
				}
			}

			if (Commands.IsJumpCommand(Comm.Type))
			{
				if (Comm.Type != CommandType.Return || Comm.Expressions == null || Comm.Expressions.Count == 0)
					return new JumpCodeBranch(Comm.Label);
			}
			else if (Comm.Type == CommandType.Unknown)
			{
			}

			return null;
		}

		public CondBranch[] GetBranches(GlobalContainer globalContainer, Command Then,
			Command Else, ref ExpressionNode Condition)
		{
			var ThenJump = GetJumpBranch(Then);
			var ElseJump = GetJumpBranch(Else);
			if (ThenJump != null || ElseJump != null)
				return new CondBranch[] { ThenJump, ElseJump };

			var OpCond = Condition as OpExpressionNode;
			if (OpCond != null && !Operators.IsRelEquality(OpCond.Operator))
				return new CondBranch[] { null, null };

			var ExprElse = GetNodeCondBrach(GetExpr(Else));
			var ExprThen = GetNodeCondBrach(GetExpr(Then));
			var MoveElse = ExprElse as x86MoveCondBranch;
			var MoveThen = ExprThen as x86MoveCondBranch;

			if (MoveThen != null && ExprElse != null && MoveThen.Struct.Dst.Compare(MoveElse.Struct.Dst))
			{
				if (MoveThen.Struct.Src is x86ConstLocation && !(MoveElse.Struct.Src is x86ConstLocation))
				{
					if (OpCond != null)
					{
						OpCond.Operator = Operators.Negate(OpCond.Operator);
					}
					else
					{
						var False = Constants.GetBoolValue(globalContainer, false, new CodeString());
						var Ch = new ExpressionNode[] {OpCond, False };
						OpCond = new OpExpressionNode(Operator.Equality, Ch, OpCond.Code);
					}

					return new CondBranch[] { ExprElse, ExprThen };
				}
				else
				{
					ProcessMoveCondBranch(globalContainer, MoveThen);
				}
			}
			else if (MoveThen != null && Else == null)
			{
				ProcessMoveCondBranch(globalContainer, MoveThen);
			}

			return new CondBranch[] { ExprThen, ExprElse };
		}

		public ExpressionNode OverflowCondition(PluginRoot Plugin, ExpressionNode Node, 
			CodeString Code, BeginEndMode BEMode = BeginEndMode.Both)
		{
			var Type = Node.Type.RealId as Type;
			var Signed = Type is SignedType;

			if ((BEMode & BeginEndMode.Begin) != 0 && !Plugin.Begin())
				return null;

			ExpressionNode Ret = new OpExpressionNode(Operator.Unknown, null, Code);
			Ret.Type = Plugin.Container.GlobalContainer.CommonIds.Boolean;

			var Op = Signed ? x86Operator.IsOverflowFlagSet : x86Operator.IsCarryFlagSet;
			Ret.Data.Create<x86NodeData>().Operator = Op;

			if (Plugin.NewNode(ref Ret) == PluginResult.Failed) return null;
			if ((BEMode & BeginEndMode.End) != 0 && Plugin.End(ref Ret) == PluginResult.Failed)
				return null;

			return Ret;
		}
	}
}
