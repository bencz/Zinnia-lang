using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Zinnia.Base;

namespace Zinnia
{
	[Flags]
	public enum FunctionScopeFlags
	{
		None = 0,
		DisableParsing = 1,
		DisableCodeCheck = 2,
		DisableAddingRetCommand = 4,
	}

	public class FunctionScope : CodeScopeNode
	{
		public Function Function;
		public TypeOfFunction Type;
		public FunctionScopeFlags Flags;

		public Dictionary<string, Command> Labels = new Dictionary<string, Command>();
		public List<Command> Gotos = new List<Command>();

		public SelfVariable SelfVariable;
		public BaseVariable BaseVariable;
		public ExpressionNode ConstructorCall;
		public ExpressionNode ObjectSize;

		public int ReturnLabel;
		public Command ReturnLabelCommand;
		public AutoAllocatedList<ParamVariable> Parameters;

		public int ContainerLocalIndexCount;
		public List<Identifier> LocalIdentifiers = new List<Identifier>();
		public AutoAllocatedList<int> NeverSkippedLabels;

		public FunctionScope(IdContainer Parent, Function Function, CodeString Code)
			: base(Parent, Code)
		{
			this.Function = Function;
            this.FunctionScope = this;

			this.Type = Function.TypeOfSelf.RealId as TypeOfFunction;
			this.ReturnLabel = State.AutoLabel;
		}

		public override void ForEachId(Action<Identifier> Func)
		{
			IdentifierList.ForEach(Func);
			base.ForEachId(Func);
		}

		public override bool TrueForAllId(Predicate<Identifier> Func)
		{
			if (!Parameters.TrueForAll(Func)) return false;
			return base.TrueForAllId(Func);
		}

		public bool Initialize()
		{
			if (Type.Children.Length > 1)
			{
				Parameters = new List<ParamVariable>();
				for (var i = 1; i < Type.Children.Length; i++)
				{
					var e = Type.Children[i] as FunctionParameter;
					var Param = new ParamVariable(this, e.Name, e.TypeOfSelf);
					Param.LocalIndex = LocalIdentifiers.Count;
					LocalIdentifiers.Add(Param);
					Parameters.Add(Param);
				}
			}

			var StructuredParent = Parent.RealContainer as StructuredScope;
			if (StructuredParent != null && (Function.Flags & IdentifierFlags.Static) == 0)
			{
				var StructuredType = StructuredParent.StructuredType;
				SelfVariable = new SelfVariable(this, StructuredType);
				LocalIdentifiers.Add(SelfVariable);

				var ClassType = StructuredType as ClassType;
				if (ClassType != null && ClassType.BaseStructures.Length == 1)
				{
					BaseVariable = new BaseVariable(this, ClassType.BaseStructures[0].Base);
					LocalIdentifiers.Add(BaseVariable);
				}
			}

			return true;
		}

		public bool NeedsReturnVal
		{
			get
			{
				if (Type.RetType is VoidType) return false;

				var FS = FunctionScope;
				if (FS.Function is Constructor || FS.Function is Destructor)
					return false;

				return true;
			}
		}

		public bool ProcessLabels()
		{
			var RetValue = true;
			for (var i = 0; i < Gotos.Count; i++)
			{
				var Goto = Gotos[i];
				var LblCmd = GetLabelCmd(Goto.LabelName.ToString());
				if (LblCmd == null)
				{
					State.Messages.Add(MessageId.UnknownLabel, Goto.LabelName);
					RetValue = false;
				}
				else
				{
					Goto.JumpTo = LblCmd;
					Goto.Label = LblCmd.Label;
				}
			}

			return RetValue;
		}

		public Command GetLabelCmd(string Name)
		{
			foreach (var e in Labels.Keys)
				if (e == Name) return Labels[e];

			return null;
		}

		public override bool GetContainerId(string Name, List<IdentifierFound> Out, Predicate<Identifier> Func = null)
		{
			var RetValue = false;
			if (Name == null)
			{
				if (SelfVariable != null)
				{
					if (SelfVariable != null && (Func == null || Func(SelfVariable)))
					{
						Out.Add(new IdentifierFound(this, SelfVariable));
						RetValue = true;
					}

					if (BaseVariable != null && (Func == null || Func(BaseVariable)))
					{
						Out.Add(new IdentifierFound(this, BaseVariable));
						RetValue = true;
					}
				}

				for (var i = 0; i < Parameters.Count; i++)
				{
					var Id = Parameters[i];
					if (Id != null && (Func == null || Func(Id)))
					{
						Out.Add(new IdentifierFound(this, Id));
						RetValue = true;
					}
				}
			}
			else
			{
				if (SelfVariable != null)
				{
					var TSelf = SelfVariable;
					if (TSelf != null && TSelf.Name.IsEqual(Name))
						if (Func == null || Func(TSelf))
						{
							Out.Add(new IdentifierFound(this, TSelf));
							RetValue = true;
						}

					var TBase = BaseVariable;
					if (TBase != null && TBase.Name.IsEqual(Name))
						if (Func == null || Func(TBase))
						{
							Out.Add(new IdentifierFound(this, TBase));
							RetValue = true;
						}
				}

				for (var i = 0; i < Parameters.Count; i++)
				{
					var Id = Parameters[i];
					if (Id != null && Id.Name.IsEqual(Name))
						if (Func == null || Func(Id))
						{
							Out.Add(new IdentifierFound(this, Id));
							RetValue = true;
						}
				}
			}

			if (base.GetContainerId(Name, Out, Func))
				RetValue = true;

			return RetValue;
		}
		
		bool AddConstructorCommands()
		{
			if (Function is Constructor)
			{
				var Structured = Parent as StructuredScope;
				var Type = Structured.StructuredType;

#warning ERROR
				if (ConstructorCall == null)
				{
					for (var i = 0; i < Type.BaseStructures.Length; i++)
					{
						var Base = Type.BaseStructures[i].Base;
						var BaseStructure = Base.UnderlyingStructureOrRealId as StructuredType;

						if (!BaseStructure.HasParameterLessCtor)
						{
							State.Messages.Add(MessageId.NoParamLessConstructor, Function.Declaration);
							return false;
						}
					}
				}

				if (Type is ClassType)
				{
					var CtorInitializer = new CodeScopeNode(this, new CodeString());
					if (!CtorInitializer.BasicConstructorCommands()) return false;
					Children.Insert(0, CtorInitializer);
				}
			}

			return true;
		}

		public override bool ProcessCode()
		{
			if ((Flags & FunctionScopeFlags.DisableParsing) == 0)
			{
				var RetValue = base.ProcessCode();
				if (!AddConstructorCommands()) RetValue = false;

				if (!ProcessLabels()) RetValue = false;
				if (!CopyRetVal(Function.Declaration)) RetValue = false;
				if (!RetValue) return false;
			}

			if ((Flags & FunctionScopeFlags.DisableAddingRetCommand) == 0)
			{
				ReturnLabelCommand = new Command(this, new CodeString(), CommandType.Label);
				ReturnLabelCommand.Label = ReturnLabel;
				Children.Add(ReturnLabelCommand);
			}

			if ((Flags & FunctionScopeFlags.DisableCodeCheck) == 0)
			{
				if ((CodeChecker.Process(this) & CodeContextResult.Failed) != 0) 
					return false;
			}

			return State.Arch.ProcessFunction(Function);
		}
	}
}