using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia.NativeCode;

namespace Zinnia.x86
{
	public class x86ReturnVariable : LocalVariable
	{
		public x86ReturnVariable(IdContainer Container, CodeString Name, Type Type)
			: base(Container, Name, Type)
		{
		}
	}

	public class x86IdContainerData
	{
		public IdContainer Container;
		public x86DataList UsedByParams;
		public x86DataAllocator Allocator;

		public x86IdContainerData(IdContainer Container)
		{
			this.Container = Container;
		}

		public void Reset()
		{
			if (Allocator == null) Allocator = new x86DataAllocator(Container);
			if (UsedByParams == null) UsedByParams = new x86DataList(Allocator.Arch);

			Allocator.Reset();
			UsedByParams.Reset();
		}
	}

    [Flags]
    public enum x86FuncScopeFlags : byte
    {
        None = 0,
		FunctionCalled = 1,
		SaveFramePointer = 2,
		SaveParameterPointer = 4,
        RetAddressInParams = 8,
		StackLocationsValid = 16,
    }

	public class x86FuncScopeData : x86IdContainerData
	{
		public x86ReturnVariable ReturnVariable;
		public x86DataList UsedToPassParams;
		public AutoAllocatedList<LocalVariable> AllParams;
        public x86FuncScopeFlags Flags;
		//public bool DisableAlwaysReturned;
		//public LocalVariable AlwaysReturned;

		public List<ExpressionNode> Expressions;
		public int PushedRegisters;
		public int CallParameters;
		public int SubractedFromESP;
		public int UnallocatedSpace;
		public int StackAlignment = 1;

		public x86DataList DisabledLocations;
		public x86GRegLocation StackPointer;
		public x86GRegLocation FramePointer;
		public x86GRegLocation ParameterPointer;

		public AutoAllocatedList<x86MoveStruct> FuncLeaveLoadRegs;
		public x86DataLocation SpaceForSaving;
		public int FuncLeaveInsCount;

		public x86FuncScopeData(FunctionScope Scope)
			: base(Scope)
		{
		}
	}

	public class x86FunctionData : x86IdentifierData
	{
		public string Assembly;
		public bool AssemblyOnly;

		public x86FunctionData(Function Function)
			: base(Function)
		{
		}
	}

	struct x86TestedLocation
	{
		public x86DataLocation Location;
		public bool Succeeded;

		public x86TestedLocation(x86DataLocation Location)
		{
			this.Location = Location;
			this.Succeeded = true;
		}
	}

	struct x86TestedIdLocations
	{
		public Identifier Identifier;
		public x86TestedLocation[] Locations;
		public int Index;

		public x86TestedIdLocations(Identifier Identifier, x86TestedLocation[] Locations)
		{
			this.Identifier = Identifier;
			this.Locations = Locations;
			this.Index = -1;
		}

		public x86TestedIdLocations(Identifier Identifier, x86DataLocation[] Locations)
		{
			this.Identifier = Identifier;
			this.Index = -1;

			this.Locations = new x86TestedLocation[Locations.Length];
			for (var i = 0; i < Locations.Length; i++)
				this.Locations[i] = new x86TestedLocation(Locations[i]);
		}

		public x86DataLocation[] GetSucceededLocations()
		{
			var Ret = new List<x86DataLocation>();
			for (var i = 0; i < Locations.Length; i++)
				if (Locations[i].Succeeded) Ret.Add(Locations[i].Location);

			return Ret.ToArray();
		}
	}

	struct x86CheckedIdentifier
	{
		public Identifier Identifier;
		public x86IdentifierData Data;
		public x86DataLocation Location;
		public bool[] Written;

		public x86CheckedIdentifier(Identifier Identifier)
		{
			this.Identifier = Identifier;
			this.Data = null;
			this.Location = null;
			this.Written = null;
		}

		public x86CheckedIdentifier(Identifier Identifier, x86IdentifierData Data, x86DataLocation Location)
		{
			this.Identifier = Identifier;
			this.Data = Data;
			this.Location = Location;
			this.Written = null;
		}

		public x86CheckedIdentifier Copy()
		{
			var Ret = this;
			Ret.Written = Written.ToArray();
			return Ret;
		}
	}

	[Flags]
	enum x86CheckerFlags : byte
	{
		None = 0,
		NoBraches = 1,
		NoLoopRepetation = 2,
	}

	struct x86CheckerState
	{
		public x86CheckedIdentifier[] Identifiers;
		public int[] ReachCount;

		public x86CheckerState(x86CheckedIdentifier[] Identifiers, int[] ReachCount = null)
		{
			this.Identifiers = Identifiers;
			this.ReachCount = ReachCount;
		}

		public x86CheckerState Copy()
		{
			var Identifiers = new x86CheckedIdentifier[this.Identifiers.Length];
			for (var i = 0; i < this.Identifiers.Length; i++)
				Identifiers[i] = this.Identifiers[i].Copy();

			var ReachCount = this.ReachCount;
			if (ReachCount != null) ReachCount = ReachCount.Copy();
			return new x86CheckerState(Identifiers, ReachCount);
		}

		public void Combine(x86CheckerState State)
		{
			if (State.Identifiers.Length != Identifiers.Length)
				throw new ArgumentException("State");

			if (State.ReachCount.Length != ReachCount.Length)
				throw new ArgumentException("State");

			for (var i = 0; i < Identifiers.Length; i++)
			{
				var Arr1 = State.Identifiers[i].Written;
				var Arr2 = Identifiers[i].Written;
				if (Arr1.Length != Arr2.Length)
					throw new ArgumentException("State");

				for (var j = 0; j < Arr1.Length; j++)
					if (Arr1[j]) Arr2[j] = true;
			}
		}
	}

	sealed class x86OverwritingChecker : CodeContext
	{
		struct x86LocationExtraction
		{
			public x86DataLocation Location;
			public Identifier Identifier;

			public x86LocationExtraction(x86DataLocation Location, Identifier Identifier = null)
			{
				this.Location = Location;
				this.Identifier = Identifier;
			}
		}

		public x86Architecture Arch;
		public x86CheckerFlags CheckerFlags;
		public x86CheckerState CheckerState;
		public x86TestedIdLocations TestedLocs;

		static x86LocationExtraction ExtractLocation(ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			return ExtractLocation(Data.Output);
		}

		static x86LocationExtraction ExtractLocation(x86DataLocation Location)
		{
			if (Location is x86PostCalcedLocation)
			{
				var PLoc = Location as x86PostCalcedLocation;

				if (PLoc is x86AssignVarLoc)
				{
					var AVLoc = PLoc as x86AssignVarLoc;
					var DstId = Expressions.GetIdentifier(AVLoc.Assigned);
					return new x86LocationExtraction(PLoc.Location, DstId);
				}
				else
				{
					return new x86LocationExtraction(PLoc.Location);
				}
			}

			return new x86LocationExtraction(Location);
		}

		void InitIdentifiers(x86CheckedIdentifier[] Identifiers, x86TestedIdLocations TestedLocs)
		{
			var TestedIdIndex = -1;
			for (var i = 0; i < Identifiers.Length; i++)
			{
				var Id = Identifiers[i].Identifier;
				if (Id == null || Identifiers.Where(x => x.Identifier == Id).Count() != 1)
					throw new ArgumentException();

				var Data = Id.Data.Get<x86IdentifierData>();
				if (Id == TestedLocs.Identifier) TestedIdIndex = i;

				Identifiers[i].Data = Data;
				Identifiers[i].Location = Data.Location;

				if (TestedLocs.Identifier != null)
					Identifiers[i].Written = new bool[TestedLocs.Locations.Length];
				else Identifiers[i].Written = new bool[1];
			}

			if (TestedLocs.Identifier != null)
			{
				if (TestedLocs.Index != -1)
				{
					var i = TestedLocs.Index;
					if (Identifiers[i].Identifier != TestedLocs.Identifier || Identifiers[i].Location != null)
						throw new ArgumentException();
				}
				else
				{
					if (TestedIdIndex == -1)
						throw new ArgumentException();

					TestedLocs.Index = TestedIdIndex;
				}

				for (var i = 0; i < TestedLocs.Locations.Length; i++)
					TestedLocs.Locations[i].Succeeded = true;
			}

			this.CheckerState.Identifiers = Identifiers;
			this.TestedLocs = TestedLocs;
		}

		void ConditionalAdd(x86FuncScopeData FSData, List<x86CheckedIdentifier> Identifiers, Identifier Id)
		{
			if (Id is LocalVariable)
			{
                if (!((FSData.Flags & x86FuncScopeFlags.RetAddressInParams) == 0 && Id is x86ReturnVariable))
                {
                    var IdData = Id.Data.Get<x86IdentifierData>();
                    if (IdData.Location != null)
                        Identifiers.Add(new x86CheckedIdentifier(Id));
                }
			}
		}

		x86OverwritingChecker(x86CheckerFlags Flags, IdContainer Container, x86CheckerState CheckerState, x86TestedIdLocations TestedLocs)
			: base(Container)
		{
			this.CheckerFlags = Flags;
			this.Arch = State.Arch as x86Architecture;
			this.CheckerState = CheckerState;
			this.TestedLocs = TestedLocs;
		}

		public x86OverwritingChecker(x86CheckerFlags Flags, IdContainer Container, x86TestedIdLocations TestedLocs = new x86TestedIdLocations())
			: base(Container)
		{
			var FS = Container.FunctionScope;
			var FSData = FS.Data.Get<x86FuncScopeData>();

			this.CheckerFlags = Flags;
			this.Arch = State.Arch as x86Architecture;
			if ((Flags & x86CheckerFlags.NoLoopRepetation) == 0)
				CheckerState.ReachCount = new int[FS.ContainerLocalIndexCount];

			var Identifiers = new List<x86CheckedIdentifier>();
			for (var i = 0; i < FS.LocalIdentifiers.Count; i++)
				ConditionalAdd(FSData, Identifiers, FS.LocalIdentifiers[i]);

			Identifiers.Add(new x86CheckedIdentifier(TestedLocs.Identifier));
			InitIdentifiers(Identifiers.ToArray(), TestedLocs);
		}

		public override CodeContext Copy()
		{
			return new x86OverwritingChecker(CheckerFlags, Container, CheckerState.Copy(), TestedLocs);
		}

		int GetStructIndex(string Name)
		{
			for (var i = 0; i < CheckerState.Identifiers.Length; i++)
			{
				if (CheckerState.Identifiers[i].Identifier.Name.IsEqual(Name))
					return i;
			}

			return -1;
		}

		int GetStructIndex(Identifier Id)
		{
			var Ids = CheckerState.Identifiers;
			Id = Id.RealId;

			for (var i = 0; i < Ids.Length; i++)
			{
				if (Ids[i].Identifier.RealId == Id)
					return i;
			}

			return -1;
		}

		void OnIdentifierWritten(Identifier Id)
		{
			var IdData = Id.Data.Get<x86IdentifierData>();
			var Index = GetStructIndex(Id);

			if (Index != -1)
			{
				if (TestedLocs.Identifier != null && TestedLocs.Index == Index)
				{
					for (var i = 0; i < TestedLocs.Locations.Length; i++)
					{
						var DstLoc = TestedLocs.Locations[i].Location;
						for (var j = 0; j < CheckerState.Identifiers.Length; j++)
						{
							var IdjLoc = CheckerState.Identifiers[j].Location;
							if (Index != j && DstLoc.Compare(IdjLoc, x86OverlappingMode.Partial))
								CheckerState.Identifiers[j].Written[i] = true;
						}
					}
				}
				else
				{
					OnLocationWritten(IdData.Location);
				}

				for (var j = 0; j < CheckerState.Identifiers[Index].Written.Length; j++)
					CheckerState.Identifiers[Index].Written[j] = false;
			}
			else if (IdData.Location != null)
			{
				OnLocationWritten(IdData.Location);
			}
		}

		bool HasSucceededLocation()
		{
			if (TestedLocs.Identifier == null)
				throw new InvalidOperationException();

			for (var i = 0; i < TestedLocs.Locations.Length; i++)
				if (TestedLocs.Locations[i].Succeeded) return true;

			return false;
		}

		bool OnIdentifierRead(Identifier Id)
		{
			var Index = GetStructIndex(Id);
			if (Index != -1)
			{
				if (TestedLocs.Identifier != null)
				{
					for (var i = 0; i < TestedLocs.Locations.Length; i++)
					{
						if (CheckerState.Identifiers[Index].Written[i])
							TestedLocs.Locations[i].Succeeded = false;
					}

					if (!HasSucceededLocation())
						return false;
				}
				else
				{
					return !CheckerState.Identifiers[Index].Written[0];
				}
			}

			return true;
		}

		void OnGRegLocationWritten(int Index, x86RegisterMask Mask)
		{
			OnLocationWritten(x =>
				!x86DataLocations.Check(x, y =>
				{
					var Gy = y as x86GRegLocation;
					return Gy == null || Gy.Index != Index || Gy.Mask.IsFree(Mask);
				})
			);
		}

		bool DisableLocation(x86DataList List)
		{
			return DisableLocation(x => !List.IsFree(x));
		}

		bool DisableLocation(x86DataLocation Location)
		{
			if (Location == null) throw new ArgumentNullException("Location");
			return DisableLocation(x => x.Compare(Location, x86OverlappingMode.Partial));
		}

		bool DisableGRegLocation(int Index, x86RegisterMask Mask)
        {
            return DisableLocation(x => 
				!x86DataLocations.Check(x, y =>
				{
					var Gy = y as x86GRegLocation;
					return Gy == null || Gy.Index != Index || Gy.Mask.IsFree(Mask);
				})
			);
        }

        bool DisableLocation(Predicate<x86DataLocation> Func)
        {
            for (var i = 0; i < CheckerState.Identifiers.Length; i++)
                if (TestedLocs.Identifier != null)
                {
                    if (TestedLocs.Index == i)
                    {
                        for (var j = 0; j < TestedLocs.Locations.Length; j++)
                        {
                            if (Func(TestedLocs.Locations[j].Location))
                                TestedLocs.Locations[j].Succeeded = false;
                        }

                        if (!HasSucceededLocation())
                            return false;
                    }
                    else
                    {
                        if (Func(CheckerState.Identifiers[i].Location))
                            return false;
                    }

                }
                else if (!CheckerState.Identifiers[i].Written[0])
                {
                    if (Func(CheckerState.Identifiers[i].Location))
                        return false;
                }

            return true;
        }

		void OnLocationWritten(Predicate<x86DataLocation> Func)
		{
			for (var i = 0; i < CheckerState.Identifiers.Length; i++)
				if (TestedLocs.Identifier != null)
				{
					if (TestedLocs.Index == i)
					{
						for (var j = 0; j < TestedLocs.Locations.Length; j++)
							if (!CheckerState.Identifiers[i].Written[j] && TestedLocs.Locations[j].Succeeded)
							{
								if (Func(TestedLocs.Locations[j].Location))
									CheckerState.Identifiers[i].Written[j] = true;
							}
					}
					else if (Func(CheckerState.Identifiers[i].Location))
					{
						for (var j = 0; j < TestedLocs.Locations.Length; j++)
							CheckerState.Identifiers[i].Written[j] = true;
					}
				}
				else if (!CheckerState.Identifiers[i].Written[0])
				{
					if (Func(CheckerState.Identifiers[i].Location))
						CheckerState.Identifiers[i].Written[0] = true;
				}
		}

		void OnLocationWritten(x86DataLocation Location)
		{
			if (Location == null) throw new ArgumentNullException("Location");
			OnLocationWritten(x => x.Compare(Location, x86OverlappingMode.Partial));
		}

		void OnLocationWritten(x86DataList List)
		{
			OnLocationWritten(x => !List.IsFree(x));
		}

		void OnLocationWritten(x86TemporaryData TempData)
		{
			if (TempData.GRegs != null)
			{
				for (var i = 0; i < TempData.GRegs.Length; i++)
					OnLocationWritten(TempData.GRegs[i].Location);
			}

			if (TempData.SSERegs != null)
			{
				for (var i = 0; i < TempData.SSERegs.Length; i++)
					OnLocationWritten(TempData.SSERegs[i]);
			}

			if (TempData.Memory != null)
				OnLocationWritten(TempData.Memory);
		}

		bool CheckSaveChResults(ExpressionNode Node, x86NodeData Data)
		{
			if ((Data.Flags & x86NodeFlags.SaveChResults) == 0)
				return true;

			var Ch = Node.Children;
			for (var i = 0; i < Ch.Length; i++)
			{
				var Loci = ExtractLocation(Ch[i]);
				if (Loci.Identifier == null) continue;

				var Index = GetStructIndex(Loci.Identifier);
				if (Index == -1) continue;

				if (TestedLocs.Identifier != null && TestedLocs.Index == Index)
				{
					for (var LocIndex = 0; LocIndex < TestedLocs.Locations.Length; LocIndex++)
					{
						if (!TestedLocs.Locations[LocIndex].Succeeded) continue;

						var CurrIdLoc = TestedLocs.Locations[LocIndex].Location;
						for (var j = 0; j < Ch.Length; j++)
						{
							if (i == j) continue;

							x86DataLocCalcHelper.ForeachIndexMemberNodeAndChildren(Ch[j], x =>
							{
								var Locx = ExtractLocation(x);
								if (Locx.Location == null) return;

								if (CurrIdLoc.Compare(Locx.Location, x86OverlappingMode.Partial))
									TestedLocs.Locations[LocIndex].Succeeded = false;
							});
						}
					}
				}
				else if (Loci.Location != null)
				{
					for (var j = 0; j < Ch.Length; j++)
					{
						if (i == j) continue;

						var Failed = false;
						x86DataLocCalcHelper.ForeachIndexMemberNodeAndChildren(Ch[j], x =>
						{
							var Locx = ExtractLocation(x);
							if (Locx.Location == null) return;

							if (Loci.Location.Compare(Locx.Location, x86OverlappingMode.Partial))
								Failed = true;
						});

						if (Failed)
							return false;
					}
				}
			}

			return HasSucceededLocation();
		}

		public bool IsOverwritten(ExpressionNode Node, x86DataLocation MoveAndOpPos = null)
		{
			var Data = Node.Data.Get<x86NodeData>();
			var Calculated = false;

			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				if ((LNode.Flags & LinkedNodeFlags.PostComputation) != 0)
					continue;

				var LData = LNode.Data.Get<x86LinkedNodeData>();
				if (IsOverwritten(LNode.Node, LData.Location))
					return true;

				if ((LData.Flags & x86LinkedNodeFlags.AllocateData) != 0)
					OnLocationWritten(LData.Location);
			}

			if ((Data.Flags & x86NodeFlags.IndexMemberNode) == 0)
			{
				OnLocationWritten(Data.TempData);
				if (Node.Children != null)
				{
					for (var i = 0; i < Node.Children.Length; i++)
						x86DataLocCalcHelper.ForeachIndexMemberNode(Node.Children[i], x =>
						{
							var xData = x.Data.Get<x86NodeData>();
							OnLocationWritten(xData.TempData);
						});
				}
			}

			if (Node is IdExpressionNode)
			{
				var IdNode = Node as IdExpressionNode;
				if (!OnIdentifierRead(IdNode.Identifier)) return true;
			}
			else if (Node is OpExpressionNode)
			{
				var OpNode = Node as OpExpressionNode;
				var Op = OpNode.Operator;
				var Ch = OpNode.Children;

				if (!CheckSaveChResults(Node, Data))
					return true;
				
				if (Op == Operator.Assignment)
				{
					Calculated = true;
					var Dst = x86Expressions.GetLocation(Arch, Ch[0]) as x86MemoryLocation;
					var Ch0Data = Ch[0].Data.Get<x86NodeData>();
					var DstIsSimpleId = Ch[0] is IdExpressionNode &&
						(Ch0Data.Flags & x86NodeFlags.IdentifierByRef) == 0;

					if (!DstIsSimpleId && IsOverwritten(Ch[0])) return true;
					if (IsOverwritten(Ch[1], Dst)) return true;

					if (DstIsSimpleId)
					{
						var IdCh0 = Ch[0] as IdExpressionNode;
						OnIdentifierWritten(IdCh0.Identifier);
					}
					else if ((Ch0Data.Flags & x86NodeFlags.IndexMemberNode) != 0)
					{
						var Result = false;
						x86DataLocCalcHelper.ForeachIndexMemberNode(Ch[0], x =>
						{
							var Idx = x as IdExpressionNode;
							if (Idx != null && !OnIdentifierRead(Idx.Identifier)) Result = true;
						});

						if (Result)
							return true;
					}
				}
				else if (Op == Operator.Cast || Op == Operator.Reinterpret)
				{
					Calculated = true;
					var Pos = x86Expressions.GetLocation(Arch, Node);
					if (IsOverwritten(Ch[0], Pos as x86MemoryLocation)) return true;
				}
				else if (Op == Operator.Call || Op == Operator.NewObject)
				{
					Calculated = true;
					var FuncType = Ch[0].Type as TypeOfFunction;
					var CallConv = FuncType.CallConv;
					var x86CallConv = Arch.GetCallingConvention(CallConv);
					var ParameterSequence = x86CallConv.ParameterSequence;
					var DS = new x86DataSequence(Arch, ParameterSequence);

					var RetType = FuncType.RetType.RealId;
					if ((RetType is StructType || RetType is NonrefArrayType) && DS.CanAllocGReg())
						OnGRegLocationWritten(DS.GetGRegisterIndex(), Arch.RegisterMask);

					if (IsOverwritten(Ch[0]))
						return true;

					if (x86Expressions.NeedSelfParameter(Node) && DS.CanAllocGReg())
						OnGRegLocationWritten(DS.GetGRegisterIndex(), Arch.RegisterMask);

					var Types = FuncType.GetTypes();
					var RegList = new x86DataLocation[Ch.Length];
					Arch.ProcessRegisterParams(Types, CallConv, (i, Pos) => RegList[i + 1] = Pos, DS);

					for (var i = 1; i < Ch.Length; i++)
						if (RegList[i] == null && IsOverwritten(Ch[i])) return true;

					for (var i = 1; i < Ch.Length; i++)
					{
						if (RegList[i] == null) continue;
						if (IsOverwritten(Ch[i], RegList[i])) return true;
						OnLocationWritten(RegList[i]);
					}

					if (IsOverwritten(Ch[0])) return true;
				}
			}

			if (!Calculated && Node.Children != null)
			{
				for (var i = 0; i < Node.Children.Length; i++)
					if (IsOverwritten(Node.Children[i])) return true;
			}

			if (Data.Output != null)
			{
				/*
				if (Data.Output is x86AssignVarLoc)
				{
					var AVLoc = Data.Output as x86AssignVarLoc;
					var IdNode = AVLoc.Assigned as IdExpressionNode;
					if (IsIdentifierOverwritten(IdNode.Id)) return true;
				}
			  * 
				var Extracted = Data.ExtractedOutput;
				if (Extracted != null) OnLocationWritten(Extracted);
				*/

				var Extracted = ExtractLocation(Data.Output);
				if (TestedLocs.Identifier != null && Extracted.Identifier != null &&
					Extracted.Identifier.IsEquivalent(TestedLocs.Identifier))
				{
					for (var LocIndex = 0; LocIndex < TestedLocs.Locations.Length; LocIndex++)
					{
						for (var i = 0; i < CheckerState.Identifiers.Length; i++)
							if (i != TestedLocs.Index && !CheckerState.Identifiers[i].Written[LocIndex])
							{
								var DestLoc = TestedLocs.Locations[LocIndex].Location;
								if (DestLoc.Compare(CheckerState.Identifiers[i].Location, x86OverlappingMode.Partial))
									CheckerState.Identifiers[i].Written[LocIndex] = true;
							}
					}
				}
				else if (Extracted.Location != null)
				{
					OnLocationWritten(Extracted.Location);
				}
			}

			if (Data.UsedDataBySelf != null) OnLocationWritten(Data.UsedDataBySelf);

			for (var i = 0; i < Node.LinkedNodes.Count; i++)
			{
				var LNode = Node.LinkedNodes[i];
				if ((LNode.Flags & LinkedNodeFlags.PostComputation) != 0)
				{
					if (IsOverwritten(LNode.Node)) return true;
				}
			}

			return false;
		}

		public override OnEnterLeaveResult OnEnterContainer()
		{
			if (Container is Command)
			{
				var Comm = Container as Command;
				if (Comm.Type == CommandType.If)
				{
					var RetValue = OnEnterLeaveResult.Succeeded;
					if ((CheckerFlags & x86CheckerFlags.NoBraches) == 0)
					{
						for (var i = 0; i < Comm.Children.Count; i++)
						{
							var Chi = Comm.Children[i];
							if (i < Comm.Expressions.Count)
							{
								if (IsOverwritten(Comm.Expressions[i]))
									RetValue |= OnEnterLeaveResult.FailedAndCancel;
								else if ((Copy(Chi).Process() & CodeContextResult.Failed) != 0)
									RetValue |= OnEnterLeaveResult.FailedAndCancel;
							}
							else
							{
								if ((Copy(Chi).Process() & CodeContextResult.Failed) != 0)
									RetValue |= OnEnterLeaveResult.Failed;

								RetValue |= OnEnterLeaveResult.Cancel;
							}
						}
					}
					else
					{
						var NewStates = new List<x86CheckerState>();
						for (var i = 0; i < Comm.Children.Count; i++)
						{
							if (i < Comm.Expressions.Count)
							{
								if (IsOverwritten(Comm.Expressions[i]))
									RetValue |= OnEnterLeaveResult.FailedAndCancel;
							}

							var OldState = CheckerState;
							CheckerState = CheckerState.Copy();

							if ((Process(Comm.Children[i]) & CodeContextResult.Failed) != 0)
								RetValue |= OnEnterLeaveResult.FailedAndCancel;

							NewStates.Add(CheckerState);
							CheckerState = OldState;
						}

						NewStates.ForEach(x => CheckerState.Combine(x));
					}

					return RetValue;
				}
                else if (Commands.IsLoopCommand(Comm.Type))
                {
                    if (Comm.Type == CommandType.For)
                    {
                        if (IsOverwritten(Comm.Expressions[0]))
                            return OnEnterLeaveResult.FailedAndCancel;

                        if (IsOverwritten(Comm.Expressions[1]))
                            return OnEnterLeaveResult.FailedAndCancel;
                    }
                    else if (Comm.Type == CommandType.While)
                    {
                        if (IsOverwritten(Comm.Expressions[0]))
                            return OnEnterLeaveResult.FailedAndCancel;
                    }

                    if (Comm.WillRun == ConditionResult.Unknown && (CheckerFlags & x86CheckerFlags.NoBraches) != 0)
                    {
                        var RetValue = OnEnterLeaveResult.Succeeded;
                        var OldState = CheckerState;
                        CheckerState = CheckerState.Copy();

                        if ((Process(Comm.Children[0]) & CodeContextResult.Failed) != 0)
                            RetValue |= OnEnterLeaveResult.FailedAndCancel;

                        OldState.Combine(CheckerState);
                        CheckerState = OldState;
                        return RetValue;
                    }
                }
                else if (Comm.Type == CommandType.Expression)
                {
                    if (IsOverwritten(Comm.Expressions[0]))
                        return OnEnterLeaveResult.FailedAndCancel;
                }
                else if (Comm.Type == CommandType.Return)
                {
                    if (Comm.Expressions != null && Comm.Expressions.Count > 0)
                    {
                        if (IsOverwritten(Comm.Expressions[0]))
                            return OnEnterLeaveResult.FailedAndCancel;
                    }
                }
                else if (Commands.IsJumpCommand(Comm.Type))
                {
                    var DstContainer = Comm.GetJumpDestination().Container;
                    for (var i = 0; i < CheckerState.Identifiers.Length; i++)
                    {
                        var IdContainer = CheckerState.Identifiers[i].Identifier.Container;
                        if (DstContainer != IdContainer && !DstContainer.IsSubContainerOf(IdContainer))
                            continue;

                        if (TestedLocs.Identifier != null)
                        {
                            for (var Loc = 0; Loc < TestedLocs.Locations.Length; Loc++)
                            {
                                if (CheckerState.Identifiers[i].Written[Loc])
                                    TestedLocs.Locations[Loc].Succeeded = false;
                            }
                        }
                        else if (CheckerState.Identifiers[i].Written[0])
                        {
                            return OnEnterLeaveResult.FailedAndCancel;
                        }
                    }

                    if (!HasSucceededLocation())
                        return OnEnterLeaveResult.FailedAndCancel;
                }
                else if (Comm.Type == CommandType.Throw || Comm.Type == CommandType.Rethrow)
                {
                    if (IsOverwritten(Comm.Expressions[0]))
                        return OnEnterLeaveResult.FailedAndCancel;
                }
			}

			else if (Container is CodeScopeNode)
			{
				if (Container is FunctionScope)
				{
                    var FS = Container as FunctionScope;
                    var FSData = Container.Data.Get<x86FuncScopeData>();
					if (!DisableLocation(FSData.DisabledLocations))
						return OnEnterLeaveResult.FailedAndCancel;

					if (!CheckParameters(FS))
						return OnEnterLeaveResult.FailedAndCancel;
				}

				var Scope = Container as CodeScopeNode;
				var ParentComm = Scope.Parent as Command;

				if (ParentComm != null && ParentComm.Type == CommandType.Try)
				{
					if (ParentComm.FinallyScope == Scope || ParentComm.CatchScope == Scope)
					{
						var AsCall = Arch.CallingConventions.ZinniaCall;
						OnLocationWritten(x => !AsCall.IsSavedRegister(x));
					}
				}

			}

			return base.OnEnterContainer();
		}

		bool CheckParameters(FunctionScope FS)
		{
			var FSData = FS.Data.Get<x86FuncScopeData>();
			for (var i = 0; i < FSData.AllParams.Count; i++)
			{
				var Id = FSData.AllParams[i];
				var IdData = Id.Data.Get<x86IdentifierData>();

				if (TestedLocs.Identifier.IsEquivalent(Id))
				{
					for (var LocIndex = 0; LocIndex < TestedLocs.Locations.Length; LocIndex++)
					{
						var Loc = TestedLocs.Locations[LocIndex].Location;
						for (var j = i + 1; j < FSData.AllParams.Count; j++)
						{
							var jIdData = FSData.AllParams[j].Data.Get<x86IdentifierData>();
							if (jIdData.ParamLocation.Compare(Loc, x86OverlappingMode.Partial))
								TestedLocs.Locations[LocIndex].Succeeded = false;
						}
					}

					if (!HasSucceededLocation())
						return false;
				}
				else if (IdData.Location != null && !IdData.ParamLocation.Compare(IdData.Location))
				{
					for (var j = i + 1; j < FSData.AllParams.Count; j++)
					{
						var jIdData = FSData.AllParams[j].Data.Get<x86IdentifierData>();
						if (jIdData.ParamLocation.Compare(IdData.Location, x86OverlappingMode.Partial))
							return false;
					}
				}

				OnIdentifierWritten(Id);
			}

			return true;
		}

		public override OnEnterLeaveResult OnLeaveContainer()
		{
			if (Container.Parent is Command)
			{
				var Comm = Container.Parent as Command;
				if (Comm.Type == CommandType.For)
				{
					if (IsOverwritten(Comm.Expressions[2]))
						return OnEnterLeaveResult.FailedAndCancel;

					if (IsOverwritten(Comm.Expressions[1]))
						return OnEnterLeaveResult.FailedAndCancel;
				}
				else if (Comm.Type == CommandType.DoWhile)
				{
					if (IsOverwritten(Comm.Expressions[0]))
						return OnEnterLeaveResult.FailedAndCancel;
				}

				if ((CheckerFlags & x86CheckerFlags.NoLoopRepetation) == 0)
				{
					if (Commands.IsLoopCommand(Comm.Type))
					{
						CheckerState.ReachCount[Comm.LocalIndex]++;
						if (CheckerState.ReachCount[Comm.LocalIndex] < 2)
							return OnEnterLeaveResult.EnterNew;
					}
				}
			}

			return base.OnLeaveContainer();
		}
	}

	public class x86VariableLocCalcer
	{
		public x86Architecture Arch;
		public x86DataAllocator TempAllocator;
		public List<LocalVariable> List;

		void GetAllIdentifiers(IdContainer Container)
		{
			for (var i = 0; i < Container.IdentifierList.Count; i++)
			{
				var Id = Container.IdentifierList[i] as LocalVariable;
				if (Id != null) List.Add(Id);
			}

			for (var i = 0; i < Container.Children.Count; i++)
				GetAllIdentifiers(Container.Children[i]);
		}

		int GetIdPriority(Identifier Id)
		{
			var IdData = Id.Data.Get<x86IdentifierData>();
			var Ret = IdData.ReferenceCount;

			if (x86Architecture.IsPointerType(Id.TypeOfSelf))
			{
				if (Ret < (int.MaxValue >> 1)) Ret <<= 1;
				else Ret = int.MaxValue;
			}

			return Ret;
		}

		void ConstructAllocationList(IdContainer Container)
		{
			List = new List<LocalVariable>();
			GetAllIdentifiers(Container);

			List.Sort((x, y) =>
			{
				var xPriority = GetIdPriority(x);
				var yPriority = GetIdPriority(y);

				if (xPriority > yPriority) return -1;
				else if (xPriority < yPriority) return 1;
				else return 0;
			});
		}

		public x86VariableLocCalcer(x86Architecture Arch)
		{
			this.Arch = Arch;
		}

		public void Calc(IdContainer Container)
		{
			ConstructAllocationList(Container);
			TempAllocator = new x86DataAllocator(Container);
			//DoAllocations(RootList, Container, UseExistingLocs);
			//DoAllocations(Container, AllocatePointers);
			//DoAllocations(Container, AllocateBytes);
			DoAllocations(Container, AllocateRemaining);
		}

		private void AllocateRemaining(LocalVariable Id, x86IdentifierData IdData)
		{
			if (IdData.Location == null) 
				AllocateVariable(Id, IdData);
		}

		private void AllocateBytes(LocalVariable Id, x86IdentifierData IdData)
		{
            var Type = Id.TypeOfSelf.RealId as Type;
            if (IdData.Location == null && Type.Size == 1)
				AllocateVariable(Id, IdData);
		}

		private void AllocatePointers(LocalVariable Id, x86IdentifierData IdData)
		{
			if (IdData.Location == null && x86Architecture.IsPointerType(Id.TypeOfSelf))
				AllocateVariable(Id, IdData);
		}

		private void AllocateVariable(LocalVariable Id, x86IdentifierData IdData)
		{
			var State = Id.Container.State;
			if ((State.Flags & CompilerStateFlags.DebugMode) == 0)
				x86Functions.GetRegisterForVariable(Id);

			if (IdData.Location == null)
			{
				var T = Id.TypeOfSelf.RealId as Type;
				var DataLocType = x86Identifiers.GetPossibleLocations(Id, IdData);

				PrepareTempAllocator(Id, IdData);
				IdData.Location = TempAllocator.Allocate(T.Size, T.Align, DataLocType, IdData.LocationCantBe);
			}

			SetUsedInParents(Id.Container, IdData.Location);
			IdData.SetLocationUsed(true);
			IdData.CheckLocation();
		}

		private void SetUsedInParents(IdContainer Container, x86DataLocation Location)
		{
			Container.ForEachParent<IdContainer>(x =>
			{
				var xData = x.Data.Get<x86IdContainerData>();
				xData.Allocator.SetUsed(Location);
			}, Container.FunctionScope.Parent);
		}

		private void PrepareTempAllocator(LocalVariable Id, x86IdentifierData IdData)
		{
			int Start, End;
			IdData.GetStartEnd(out Start, out End);
			TempAllocator.Reset();
			
			var ContainerData = Id.Container.Data.Get<x86IdContainerData>();
			TempAllocator.SetUsed(ContainerData.Allocator);
			/*
			if (Id.Container is Command)
			{
				var Comm = Id.Container as Command;
				if (Comm.Expressions != null)
				{
					for (var i = 0; i < Comm.Expressions.Count; i++)
					{
						var Node = Comm.Expressions[i];
						var Data = Node.Data.Get<x86NodeData>();
						TempAllocator.SetUsed(Data.AllAllocated);
					}
				}
			}
			*/
			for (var i = Start; i <= End; i++)
			{
				var Ch = Id.Container.Children[i];
				var ChData = Ch.Data.Get<x86IdContainerData>();
				TempAllocator.SetUsed(ChData.Allocator);
			}
		}

		private void DoAllocations(IdContainer Container, Action<LocalVariable, x86IdentifierData> Action)
		{
			for (var i = 0; i < List.Count; i++)
			{
				var IdData = List[i].Data.Get<x86IdentifierData>();
				Action(List[i], IdData);
			}
		}
	}

	public static class x86Functions
	{
		public static bool ProcessContainer(IdContainer Container)
		{
			var FS = Container.FunctionScope;
			var FSData = FS.Data.Get<x86FuncScopeData>();
			var Data = Container.Data.GetOrCreate<x86IdContainerData>(Container);

			if (Container is Command)
			{
				var Command = Container as Command;
				if (!ProcessCommand(Command)) return false;
			}

			for (var i = 0; i < Container.IdentifierList.Count; i++)
			{
				var Id = Container.IdentifierList[i] as LocalVariable;
				if (Id != null)
				{
					if (!Id.Data.Contains<x86IdentifierData>())
						Id.Data.Create<x86IdentifierData>(Id);

					var Type = Id.TypeOfSelf.RealId as Type;
					FSData.StackAlignment = Math.Max(FSData.StackAlignment, Type.Align);
				}
			}

			return true;
		}

		public static bool ProcessCommand(Command Command)
		{
			if (Command.Type == CommandType.Return)
			{
				var FScope = Command.FunctionScope;
				var FSData = FScope.Data.Get<x86FuncScopeData>();
				if (Command.Expressions != null && Command.Expressions.Count > 0)
				{
					var Node = Command.Expressions[0];
					var Plugin = Command.GetPlugin();
					if (!Plugin.Begin()) return false;

					Node = Expressions.SetValue(FSData.ReturnVariable, Node, Plugin, Command.Code, true);
					if (Node == null) return false;

					Command.Expressions[0] = Node;
				}
			}

			return true;
		}

		public static bool ProcessFunction(FunctionScope Scope)
		{
			var Arch = Scope.State.Arch as x86Architecture;
			var Data = new x86FuncScopeData(Scope);
			Data.Expressions = new List<ExpressionNode>(64);
			Scope.Data.Set(Data);

			InitFunction(Scope);
			CalcParamDataLoc(Scope);
			return NCProcessor.ProcessCode(Scope);
		}

		static void MakeRefType(Variable Var)
		{
			var Type = Var.TypeOfSelf.RealId as Type;
			if ((Type.TypeFlags & TypeFlags.ReferenceValue) == 0)
				Type = new ReferenceType(Var.Container, Var.TypeOfSelf, ReferenceMode.Unsafe);

			Var.Children[0] = Type;
		}

		static void InitFunction(FunctionScope Scope)
		{
			var Arch = Scope.State.Arch as x86Architecture;
			var FuncType = Scope.Type;
			var RetType = FuncType.RetType.RealId as Type;
			if (RetType is EnumType) RetType = (RetType as EnumType).TypeOfValues;

			var Data = Scope.Data.Get<x86FuncScopeData>();
			Data.AllParams = Scope.Parameters.Change<LocalVariable>();
			Data.DisabledLocations = new x86DataList(Arch);
			Data.StackPointer = new x86GRegLocation(Arch, 4, Arch.RegisterMask);
			Data.DisabledLocations.SetUsed(Data.StackPointer);

			x86ReturnVariable RetVar = null;
			if (!(FuncType.RetType.RealId is VoidType))
			{
				RetVar = new x86ReturnVariable(Scope, new CodeString(), FuncType.RetType);
				RetVar.LocalIndex = Scope.LocalIdentifiers.Count;
				Scope.LocalIdentifiers.Add(RetVar);
				Data.ReturnVariable = RetVar;
			}

			if (RetType is StructType || RetType is NonrefArrayType)
			{
				Data.Flags |= x86FuncScopeFlags.RetAddressInParams;
				MakeRefType(RetVar);
				RetVar.Data.Set(new x86IdentifierData(RetVar));
				RetVar.PreAssigned = true;
			}
			else
			{
				if (RetType is NonFloatType || RetType is PointerType || RetType is BooleanType ||
					RetType is ReferenceType || RetType is ClassType || RetType is EnumType ||
					RetType is CharType || RetType is StringType || RetType is ObjectType || RetType is RefArrayType)
				{
					var Size = RetType.Size;
					var RetVarData = new x86IdentifierData(RetVar);

					if (Size > Arch.RegSize)
					{
						if (Size == 2 * Arch.RegSize) 
							RetVarData.Location = new x86MultiLocation(Arch, 0, 2);
						else throw new NotImplementedException();
					}
					else
					{
						if (FuncType.CallConv == CallingConvention.ZinniaCall)
							RetVarData.Location = new x86GRegLocation(Arch, 0, RetType.Size);
						else RetVarData.Location = new x86GRegLocation(Arch, 0, Arch.RegSize);
					}

					RetVar.Data.Set(RetVarData);
				}
				else if (RetType is FloatType)
				{
					var IdData = new x86IdentifierData(RetVar);
					if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
						IdData.Location = new x86SSERegLocation(Arch, 0);
					else if (Arch.FloatingPointMode != x86FloatingPointMode.FPU)
						throw new NotImplementedException();

					RetVar.Data.Set(IdData);
				}
				else if (!(RetType is VoidType))
				{
					throw new ApplicationException();
				}
			}

			if (Scope.SelfVariable != null)
			{
				MakeRefType(Scope.SelfVariable);
				Data.AllParams.Insert(0, Scope.SelfVariable);
				Scope.SelfVariable.Data.Set(new x86IdentifierData(Scope.SelfVariable));

				if (Scope.BaseVariable != null)
				{
					MakeRefType(Scope.BaseVariable);
					Scope.BaseVariable.Data.Set(new x86IdentifierData(Scope.BaseVariable));
				}
			}

			if (RetType is StructType)
				Data.AllParams.Insert(0, RetVar);

			for (var i = 0; i < Scope.Parameters.Count; i++)
			{
				var Param = Scope.Parameters[i];
				Param.Data.Set(new x86IdentifierData(Param));
			}
		}

		static void UpdateParamBaseRegisters(FunctionScope Scope)
		{
			var FSData = Scope.Data.Get<x86FuncScopeData>();
			for (var i = 0; i < FSData.AllParams.Count; i++)
			{
				var Param = FSData.AllParams[i];
				var ParamData = Param.Data.Get<x86IdentifierData>();
				var Loc = ParamData.ParamLocation as x86StackLocation;
				if (Loc != null) Loc.UpdateBaseRegister();
			}
		}

		static void Reset(FunctionScope Scope)
		{
			var FSData = Scope.Data.Get<x86FuncScopeData>();
			FSData.DisabledLocations.Reset();

			for (var i = 0; i < FSData.AllParams.Count; i++)
			{
				var Param = FSData.AllParams[i];
				var ParamData = Param.Data.Get<x86IdentifierData>();
				ParamData.Reset();
			}

			Reset((IdContainer)Scope);
		}

		static void Reset(IdContainer Container)
		{
			Container.Data.Get<x86IdContainerData>().Reset();
			for (var i = 0; i < Container.IdentifierList.Count; i++)
			{
				var Id = Container.IdentifierList[i] as LocalVariable;
				if (Id != null) Id.Data.Get<x86IdentifierData>().Reset();
			}

			for (var i = 0; i < Container.Children.Count; i++)
				Reset(Container.Children[i]);
		}

		static int GetNeededSpaceForSaving(FunctionScope Scope, x86DataAllocator Allocator)
		{
			var Ret = 0;
			var Arch = Allocator.Arch;
			var SSERegSize = Arch.SSERegSize;
			var Conv = Scope.Type.CallConv;
			var x86CallConv = Arch.GetCallingConvention(Conv);

			for (var i = 0; i < Allocator.SSERegisters.Size; i++)
			{
				if (Allocator.SSERegisters[i] && x86CallConv.SavedSSERegs.Contains(i))
					Ret += SSERegSize;
			}

			return Ret;
		}

		public static void CreateAssembly(FunctionScope FS)
		{
			var State = FS.State;
			var Arch = State.Arch as x86Architecture;
			var FSData = FS.Data.Get<x86FuncScopeData>();
			var FuncData = FS.Function.Data.Get<x86FunctionData>();
			var x86CallConv = Arch.GetCallingConvention(FS.Type.CallConv);

			var OldFSFlags = FSData.Flags;
			if (FSData.StackAlignment > Arch.RegSize)
				FSData.Flags |= x86FuncScopeFlags.SaveParameterPointer;

			var CG = new x86CodeGenerator(FS);
			var ToCopy = CalcLocations(FS);
			var Allocator = FSData.Allocator;

			var NeededSpaceForSaving = GetNeededSpaceForSaving(FS, Allocator);
			if (NeededSpaceForSaving == 0) FSData.SpaceForSaving = null;
			else FSData.SpaceForSaving = Allocator.AllocMemory(NeededSpaceForSaving, Arch.SSERegSize);

			if (Allocator.StackOffset == 0 && (FSData.Flags & x86FuncScopeFlags.FunctionCalled) == 0)
			{
				FSData.StackAlignment = x86CallConv.StackAlignment;

				if (FSData.StackAlignment <= Arch.RegSize &&
					(OldFSFlags & x86FuncScopeFlags.SaveParameterPointer) == 0)
				{
					FSData.Flags = OldFSFlags;
					UpdateParamBaseRegisters(FS);
				}
			}

			CG.BeginFunction(FS, Allocator);
			if (ToCopy != null)	CG.MoveData(ToCopy);

			FS.GetAssembly(CG);
			if (FSData.FuncLeaveInsCount < 4)
			{
				Action Func = () => CG.LeaveFunction(FS, Allocator);
				var InsContainer = CG.SetJumpReplacing(FS.ReturnLabel, Func);
				CG.InsContainer.Add(InsContainer);
			}
			else
			{
				CG.LeaveFunction(FS, Allocator);
			}

			CG.Optimize();

			var InsEncoder = new x86InstructionEncoder();
			FuncData.Assembly = CG.InsContainer.EncodeToText(InsEncoder);
		}

		static void CalcExprRegs(List<ExpressionNode> Expressions, x86DataLocCalcer DataLocCalcer)
		{
			for (var i = 0; i < Expressions.Count; i++)
				DataLocCalcer.Calc(Expressions[i]);
		}

		static bool CheckMem2Mem(List<ExpressionNode> Expressions, x86DataLocChecker DataLocChecker)
		{
			for (var i = 0; i < Expressions.Count; i++)
			{
				if (!DataLocChecker.Check(Expressions[i]))
					return false;
			}

			return true;
		}

		static void CalcStackPointers(FunctionScope FS)
		{
			var Arch = FS.State.Arch as x86Architecture;
			var FSData = FS.Data.Get<x86FuncScopeData>();

			//FSData.Flags |= x86FuncScopeFlags.SaveFramePointer;
			//FSData.Flags |= x86FuncScopeFlags.SaveParameterPointer;
			//FSData.StackAlignment = 16;

			if ((FSData.Flags & x86FuncScopeFlags.SaveFramePointer) != 0)
			{
				FSData.FramePointer = new x86GRegLocation(Arch, 5, Arch.RegisterMask);
				FSData.DisabledLocations.SetUsed(FSData.FramePointer);
			}

			if ((FSData.Flags & x86FuncScopeFlags.SaveParameterPointer) != 0)
			{
				FSData.ParameterPointer = new x86GRegLocation(Arch, 3, Arch.RegisterMask);
				FSData.DisabledLocations.SetUsed(FSData.ParameterPointer);
			}
		}

		static private List<x86MoveStruct> CalcLocations(FunctionScope FS)
		{
			var Arch = FS.State.Arch as x86Architecture;
			var FSData = FS.Data.Get<x86FuncScopeData>();
			var Params = FSData.AllParams;
			var Expressions = FSData.Expressions;

			var VarLocCalcer = new x86VariableLocCalcer(Arch);
			var DataLocCalcer = new x86DataLocCalcer(Arch);
			var DataLocChecker = new x86DataLocChecker(Arch);
			var ToCopy = new List<x86MoveStruct>();
			var ReCalc = false;
			var CalcCount = 0;

			do
			{
				ReCalc = false;
				Reset(FS);
				CalcStackPointers(FS);
				UpdateParamBaseRegisters(FS);

				if (Params.Count > 0)
				{
					ToCopy.Clear();
					for (var i = 0; i < Params.Count; i++)
					{
						var ParamData = Params[i].Data.Get<x86IdentifierData>();
						if ((ParamData.Flags & x86IdentifierFlags.ParamLocUsable) == 0)
							ParamData.SetParameterUsed();
					}
				}

				CalcExprRegs(Expressions, DataLocCalcer);
				VarLocCalcer.Calc(FS);

				if (Params.Count > 0)
				{
					for (var i = 0; i < Params.Count; i++)
						ProcessParam(Params[i], ToCopy);
				}

                if (FSData.Allocator.StackOffset > 0)
                {
                    if ((FSData.Flags & x86FuncScopeFlags.SaveFramePointer) == 0)
                    {
                        FSData.Flags |= x86FuncScopeFlags.SaveFramePointer;
                        ReCalc = true;
                    }
                }

				CalcCount++;
			} while (ReCalc || !CheckMem2Mem(Expressions, DataLocChecker));
			/*
			if (CalcCount > 8)
				Console.WriteLine("CalcCount(" + Scope.Function.AssemblyName + "): " + CalcCount + ", ");
			*/

			return ToCopy;
		}

		static x86DataLocation[] GetPossibleLocations(FunctionScope Scope,
			x86DataLocationType Type, int Size, x86DataList CantBe = null)
		{
			var State = Scope.State;
			var Arch = State.Arch as x86Architecture;
			var Data = Scope.Data.Get<x86FuncScopeData>();
			var Ret = new List<x86DataLocation>();

			if ((Type & x86DataLocationType.General) != 0)
			{
				var Mask = new x86RegisterMask(Size);
				var HighMask = new x86RegisterMask(1, 1);

				for (var i = 0; i < Arch.RegCount; i++)
					if (i != 4)
					{
						if (Arch.IsGRegisterExists(i, Mask))
						{
							if (CantBe == null || CantBe.GRegisters.IsFree(i, new x86RegisterMask(Size)))
								Ret.Add(new x86GRegLocation(Arch, i, Size));
						}

						if (Size == 1 && Arch.IsGRegisterExists(i, HighMask))
						{
							if (CantBe == null || CantBe.GRegisters.IsFree(i, HighMask))
								Ret.Add(new x86GRegLocation(Arch, i, HighMask));
						}
					}
			}

			else if ((Type & x86DataLocationType.SSEReg) != 0)
			{
				for (var i = 0; i < Arch.RegCount; i++)
				{
					if (CantBe == null || CantBe.SSERegisters.IsFree(i))
						Ret.Add(new x86SSERegLocation(Arch, i, Math.Max(16, Size)));
				}
			}

			return Ret.ToArray();
		}

		static void ProcessParam(LocalVariable Id, List<x86MoveStruct> ToCopy)
		{
			var IdData = Id.Data.Get<x86IdentifierData>();
			if (!ProcessRegParam(Id, IdData))
				ProcessMemoryParam(Id, IdData);

			IdData.CheckLocation();
            if (IdData.Location != IdData.ParamLocation)
            {
                ToCopy.Add(new x86MoveStruct(IdData.Location, IdData.ParamLocation, 
					new x86TemporaryData(), x86ExecutorType.All, Id.TypeOfSelf));
            }
		}

		static void ProcessMemoryParam(LocalVariable Id, x86IdentifierData IdData)
		{
			var FS = Id.Container.FunctionScope;
			var FSData = FS.Data.Get<x86FuncScopeData>();
			var Allocator = FSData.Allocator;
			var Arch = Id.Container.State.Arch as x86Architecture;
			var FuncType = FS.Type;

			if (IdData.ParamLocation.IsMemory(x86OverlappingMode.Partial) &&
				(IdData.Flags & x86IdentifierFlags.CantBeInReg) == 0)
			{
				var DataCalcPos = x86Identifiers.GetPossibleLocations(Id, IdData);
				DataCalcPos &= ~x86DataLocationType.Memory;

				if (DataCalcPos != x86DataLocationType.None)
				{
					if (IdData.ReferenceCount > 1 && GetRegisterForVariable(Id))
					{
						Allocator.SetUsed(IdData.Location);
						return;
					}

					if (IdData.ReferenceCount > 2)
					{
                        var Type = Id.TypeOfSelf.RealId as Type;
                        IdData.Location = Allocator.Allocate(Type, DataCalcPos);
						if (IdData.Location != null) return;
					}
				}
			}

			IdData.Location = IdData.ParamLocation;
		}

		static bool ProcessRegParam(LocalVariable Id, x86IdentifierData IdData)
		{
			var FS = Id.Container.FunctionScope;
			var FSData = FS.Data.Get<x86FuncScopeData>();
			var Allocator = FSData.Allocator;
			var Arch = Id.Container.State.Arch as x86Architecture;
			var UsedToPassParams = FSData.UsedToPassParams;
			var FuncType = FS.Type;

			if (IdData.ParamLocation is x86MemoryLocation)
				return false;

			if ((IdData.Flags & x86IdentifierFlags.CantBeInReg) == 0)
			{
				if (!GetRegisterForVariable(Id, IdData.ParamLocation) || IdData.Location != IdData.ParamLocation)
					IdData.Flags |= x86IdentifierFlags.ParamLocUsable;
			}
			else
			{
				IdData.Flags |= x86IdentifierFlags.ParamLocUsable;
			}

			if (IdData.Location == null)
			{
                var Type = Id.TypeOfSelf.RealId as Type;
				if (IdData.ReferenceCount <= 2)
				{
                    IdData.Location = Allocator.Allocate(Type, x86DataLocationType.Memory);
				}
				else
				{
					var DataCalcPos = x86Identifiers.GetPossibleLocations(Id, IdData);
                    IdData.Location = Allocator.Allocate(Type, DataCalcPos);
				}
			}
			else
			{
				Allocator.SetUsed(IdData.Location);
			}

			if (Id == FS.SelfVariable && FS.BaseVariable != null)
			{
				var BaseIdData = FS.BaseVariable.Data.Get<x86IdentifierData>();
				BaseIdData.Location = IdData.Location;
			}

			return true;
		}

		public static bool GetRegisterForVariable(LocalVariable Id, x86DataLocation Preferred)
		{
			return GetRegisterForVariable(Id, new List<x86DataLocation>() { Preferred },
				x => x86DataLocations.Select(x, Preferred));
		}

		public static bool GetRegisterForVariable(LocalVariable Id, List<x86DataLocation> List = null,
			Func<x86DataLocation[], x86DataLocation> Selector = null)
		{
			var Arch = Id.Container.State.Arch as x86Architecture;
			var IdData = Id.Data.Get<x86IdentifierData>();
			if (List == null) List = new List<x86DataLocation>();

			int SingleLocSize;
			var DataCalcLoc = x86Identifiers.GetPossibleLocations(Id, IdData);
			if ((DataCalcLoc & x86DataLocationType.SSEReg) != 0) SingleLocSize = Arch.SSERegSize;
			else if ((DataCalcLoc & x86DataLocationType.General) != 0) SingleLocSize = Arch.RegSize;
			else SingleLocSize = int.MaxValue;

            var Type = Id.TypeOfSelf.RealId as Type;
            var PossibleLocSizes = Math.Min(Type.Size, SingleLocSize);
			var Possible = GetPossibleLocations(Id.Container.FunctionScope,
				DataCalcLoc, PossibleLocSizes, IdData.LocationCantBe);

            if (Type.Size > SingleLocSize)
			{
                var PartCount = (Type.Size - 1) / SingleLocSize + 1;
				var MemberLocs = new x86DataLocation[PartCount];

				for (var i = 0; i < Possible.Length; i++)
				{
					for (var j = 0; j < MemberLocs.Length - 1; j++)
						MemberLocs[j] = MemberLocs[j + 1];

					MemberLocs[MemberLocs.Length - 1] = Possible[i];
					if (MemberLocs[0] != null)
					{
                        var Loc = new x86MultiLocation(Arch, Type.Size, MemberLocs.ToArray());
						if (!x86DataLocations.Contains(List, Loc)) List.Add(Loc);
					}
				}
			}
			else
			{
				for (var i = 0; i < Possible.Length; i++)
				{
					if (!x86DataLocations.Contains(List, Possible[i]))
						List.Add(Possible[i]);
				}
			}

			return TestLocations(Id, List.ToArray(), Selector);
		}

		public static bool TestLocations(LocalVariable Id, x86DataLocation[] Possible,
			Func<x86DataLocation[], x86DataLocation> Selector = null)
		{
			var FS = Id.Container.FunctionScope;
			var IdData = Id.Data.Get<x86IdentifierData>();
			var TestedLocs = new x86TestedIdLocations(Id, Possible);
			var Checker = new x86OverwritingChecker(x86CheckerFlags.NoBraches, FS, TestedLocs);
			if ((Checker.Process() & CodeContextResult.Failed) != 0) return false;

			var Locations = TestedLocs.GetSucceededLocations();
			if (Locations.Length == 0) return false;
			else if (Selector == null) IdData.Location = Locations[0];
			else IdData.Location = Selector(Locations);
			return IdData.Location != null;;
		}

		static void CalcParamDataLoc(FunctionScope Scope)
		{
			var Arch = Scope.State.Arch as x86Architecture;
			var Data = Scope.Data.Get<x86FuncScopeData>();
			var CallConv = Scope.Type.CallConv;
			var x86CallConv = Arch.GetCallingConvention(CallConv);
			var Params = Scope.Parameters;
			var RegSize = Arch.RegSize;

			var DS = new x86DataSequence(Arch, x86CallConv.ParameterSequence);
			DS.StoredDataList = new x86DataList(Arch);

			if ((Data.Flags & x86FuncScopeFlags.RetAddressInParams) != 0)
			{
				var RetVarData = Data.ReturnVariable.Data.Get<x86IdentifierData>();
				RetVarData.ParamLocation = DS.GetPosition(Scope, Arch.RegSize);
				Data.ReturnVariable.Data.Set(RetVarData);
			}

			if (Scope.SelfVariable != null)
			{
				var SelfIdData = Scope.SelfVariable.Data.Get<x86IdentifierData>();
				SelfIdData.ParamLocation = DS.GetPosition(Scope, Arch.RegSize);
				Scope.SelfVariable.Data.Set(SelfIdData);

				if (Scope.BaseVariable != null)
				{
					var BaseIdData = Scope.BaseVariable.Data.Get<x86IdentifierData>();
					BaseIdData.ParamLocation = SelfIdData.ParamLocation;
				}
			}

			if (Params.Count > 0)
			{
				var Processed = new bool[Params.Count];
				var Types = Scope.Type.GetTypes();

				Arch.ProcessRegisterParams(Types, CallConv, (Index, Pos) =>
				{
					var IdData = Params[Index].Data.Get<x86IdentifierData>();
					IdData.ParamLocation = Pos;
					Processed[Index] = true;
				}, DS);

				for (var i = 0; i < Params.Count; i++)
					if (!Processed[i])
					{
						var IdData = Params[i].Data.Get<x86IdentifierData>();
                        var ParamType = Params[i].TypeOfSelf.RealId as Type;
                        IdData.ParamLocation = DS.GetPosition(Scope, ParamType);
						Params[i].Data.Set(IdData);
					}
			}

			DS.AlignStack(Arch.RegSize);
			Data.UsedToPassParams = DS.StoredDataList;
		}

	}

}