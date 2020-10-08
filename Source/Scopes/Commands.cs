using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;

namespace Zinnia
{
	[Flags]
	public enum CommandFlags : byte
	{
		None = 0,
		Unreachable = 1,
		Breakable = 2,
		Continueable = 4,
		TryHasCatchVariable = 8,
		CatchesAllException = 16
	}

	public enum CommandType : byte
	{
		Unknown,
		Expression,
		Cycle,
		While,
		DoWhile,
		For,
		If,
		Try,

		Return,
		Break,
		Continue,
		Goto,
		Label,
		Throw,
		Rethrow,
	}

	public static class Commands
	{
		public static bool IsJumpCommand(CommandType Type)
		{
			return Type == CommandType.Return || Type == CommandType.Break ||
				Type == CommandType.Continue || Type == CommandType.Goto;
		}

		public static bool IsLoopCommand(CommandType Type)
		{
			return Type == CommandType.Cycle || Type == CommandType.While ||
				Type == CommandType.DoWhile || Type == CommandType.For;
		}

		public static bool IsBreakableCommand(CommandType Type)
		{
			return IsLoopCommand(Type);
		}

		public static bool CreateCatchVariable(Command TryComm,
			Identifier Type = null, CodeString Name = new CodeString())
		{
			var State = TryComm.State;
			if (!Name.IsValid)
				Name = State.AutoVarName;

			if (Type == null)
			{
				Type = Identifiers.GetByFullNameFast<ClassType>(State, "System.Exception");
				if (Type == null) throw new ApplicationException();
			}

			var Var = TryComm.CatchScope.CreateVariable(Name, Type);
			if (Var == null) return false;

			if (!TryComm.CatchScope.CanIdDeclared(Var)) return false;
			TryComm.CatchScope.IdentifierList.Insert(0, Var);
			TryComm.Flags |= CommandFlags.TryHasCatchVariable;

			var Loc = Var as LocalVariable;
			Loc.PreAssigned = true;
			return true;
		}

		public static Identifier GetOrCreateCatchVariable(Command TryComm)
		{
			if ((TryComm.Flags & CommandFlags.TryHasCatchVariable) == 0)
			{
				if (!CreateCatchVariable(TryComm))
					return null;
			}

			return TryComm.CatchScope.IdentifierList[0];
		}

		static void CreateRootCatchScope(Command TryComm)
		{
			TryComm.CatchScope = new CodeScopeNode(TryComm, TryComm.Code);
			TryComm.Children.Add(TryComm.CatchScope);
		}

		static bool CopyCatchVar(CodeScopeNode Scope, Identifier From, Identifier To, CodeString Code)
		{
			var Plugin = Scope.GetPlugin();
			if (!Plugin.Begin()) return false;

			var Ch = new ExpressionNode[]
			{
				Plugin.NewNode(new IdExpressionNode(To, Code)),
				Plugin.NewNode(new IdExpressionNode(From, Code)),
			};

			if (Ch[0] == null || Ch[1] == null)
				return false;

			if (!To.Children[0].IsEquivalent(From.Children[0]))
			{
				var CastCh1 = Plugin.NewNode(new IdExpressionNode(To.Children[0], Code));
				if (CastCh1 == null) return false;

				var CastCh = new ExpressionNode[] { Ch[1], CastCh1 };
				Ch[1] = Plugin.NewNode(new OpExpressionNode(Operator.Cast, CastCh, Code));
				if (Ch[1] == null) return false;
			}

			var Node = Plugin.NewNode(new OpExpressionNode(Operator.Assignment, Ch, Code));
			if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
				return false;

			var Comm = new Command(Scope, Code, CommandType.Expression);
			Comm.Expressions = new List<ExpressionNode>() { Node };
			Scope.Children.Add(Comm);
			return true;
		}

		static bool CopyCatchVar(Command TryComm, CodeScopeNode Scope, Identifier Type, CodeString Name, CodeString Code)
		{
			var CatchVar = GetOrCreateCatchVariable(TryComm);
			var NewVar = Scope.CreateAndDeclareVariable(Name, Type);
			if (NewVar == null || CatchVar == null) return false;

			return CopyCatchVar(Scope, CatchVar, NewVar, Code);
		}

		static ExpressionNode CreateCatchCondition(Command TryComm, Command Condition, Identifier Type, CodeString Code)
		{
			var CatchVar = GetOrCreateCatchVariable(TryComm);
			if (CatchVar == null) return null;

			var Plugin = Condition.GetPlugin();
			if (!Plugin.Begin()) return null;

			var Ch = new ExpressionNode[]
			{
				Plugin.NewNode(new IdExpressionNode(CatchVar, Code)),
				Plugin.NewNode(new IdExpressionNode(Type, Code)),
			};
			
			var Node = Plugin.NewNode(new OpExpressionNode(Operator.Is, Ch, Code));
			if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
				return null;

			return Node;
		}

		public static bool CreateDefaultCatchScope(Command TryComm)
		{
			if ((TryComm.Flags & CommandFlags.CatchesAllException) != 0)
				throw new ApplicationException();

			var Scope = CreateCatchScope(TryComm, TryComm.Code, TryComm.Code);
			var CatchVar = GetOrCreateCatchVariable(TryComm);
			if (Scope == null || CatchVar == null) return false;

			var Plugin = Scope.GetPlugin();
			if (!Plugin.Begin()) return false;

			var Node = Plugin.NewNode(new IdExpressionNode(CatchVar, TryComm.Code));
			if (Node == null || Plugin.End(ref Node) == PluginResult.Failed)
				return false;

			var Comm = new Command(Scope, TryComm.Code, CommandType.Rethrow);
			Comm.Expressions = new List<ExpressionNode>() { Node };
			Scope.Children.Add(Comm);
			return true;
		}

		public static CodeScopeNode CreateCatchScope(Command TryComm, CodeString CommandCode, CodeString Inner,
			Identifier Type = null, CodeString Name = new CodeString())
		{
			var State = TryComm.State;
			if ((TryComm.Flags & CommandFlags.CatchesAllException) != 0)
			{
				State.Messages.Add(MessageId.CatchesAllException, CommandCode);
				return null;
			}

			if (TryComm.CatchScope == null)
				CreateRootCatchScope(TryComm);

			var ExceptionClass = Identifiers.GetByFullNameFast<ClassType>(State, "System.Exception");
			if (ExceptionClass == null) throw new ApplicationException();

			if (Type == null || Type.IsEquivalent(ExceptionClass))
			{
				TryComm.Flags |= CommandFlags.CatchesAllException;
				if (TryComm.CatchScope.Children.Count == 0)
				{
					if ((TryComm.Flags & CommandFlags.TryHasCatchVariable) != 0)
						throw new ApplicationException();

					if (Type != null && !CreateCatchVariable(TryComm, Type, Name))
						return null;

					TryComm.CatchScope.Code = Inner;
					return TryComm.CatchScope;
				}
				else
				{
					var Cond = TryComm.CatchScope.Children[0] as Command;
					var ElseScope = new CodeScopeNode(Cond, Inner);
					Cond.Children.Add(ElseScope);

					if (Type != null && !CopyCatchVar(TryComm, ElseScope, Type, Name, CommandCode))
						return null;

					return ElseScope;
				}
			}
			else
			{
				if (!Identifiers.IsSubtypeOf(Type, ExceptionClass))
				{
					State.Messages.Add(MessageId.CannotBeThisType, Name);
					return null;
				}

				Command Cond;
				if (TryComm.CatchScope.Children.Count == 0)
				{
					Cond = new Command(TryComm.CatchScope, TryComm.Code, CommandType.If);
					Cond.Expressions = new List<ExpressionNode>();
					TryComm.CatchScope.Children.Add(Cond);
				}
				else
				{
					Cond = TryComm.CatchScope.Children[0] as Command;
				}

				var Condition = CreateCatchCondition(TryComm, Cond, Type, CommandCode);
				if (Condition == null) return null;

				var ThenScope = new CodeScopeNode(Cond, Inner);
				Cond.Expressions.Add(Condition);
				Cond.Children.Add(ThenScope);

				if (!CopyCatchVar(TryComm, ThenScope, Type, Name, CommandCode))
					return null;

				return ThenScope;
			}
		}
	}

	public interface ICommandExtension
	{
		void GetAssembly(CodeGenerator CG);
	}

	public class Command : IdContainer
	{
		public CommandType Type;
		public CommandFlags Flags;
		public CodeString Code;

		public List<ExpressionNode> Expressions;
		public ICommandExtension Extension;

		// Breakable
		public int BreakLabel = -1;
		public int ContinueLabel = -1;

		// Jump, Label Command
		public int Label = -1;

		// Try
		public CodeScopeNode CatchScope;
		public int CatchLabel;
		public CodeScopeNode FinallyScope;
		public int FinallyLabel;

		// Goto Command
		public Command JumpTo;
		public CodeString LabelName;

		public Command(IdContainer Parent, CodeString Code, CommandType Type)
			: base(Parent)
		{
			this.Type = Type;
			this.Code = Code;

			if (Commands.IsLoopCommand(Type))
			{
				BreakLabel = State.AutoLabel;
				ContinueLabel = State.AutoLabel;

				Flags |= CommandFlags.Breakable;
				Flags |= CommandFlags.Continueable;
			}
			else if (Type == CommandType.Try)
			{
				CatchLabel = State.AutoLabel;
				FinallyLabel = State.AutoLabel;
			}
		}

		public void ForEachJumpedOver<T>(Action<T> Func)
			where T : IdContainer
		{
			if (Type == CommandType.Return)
			{
				ForEachParent<T>(Func, FunctionScope.Parent);
			}
			else if (Type == CommandType.Break || Type == CommandType.Continue)
			{
				var NeededFlag = Type == CommandType.Break ?
					CommandFlags.Breakable : CommandFlags.Continueable;

				var Loop = GetParent<Command>(x => (x.Flags & NeededFlag) != 0);
				ForEachParent<T>(Func, Loop.Children[0]);
			}
			else if (Type == CommandType.Goto)
			{
				ForEachParent<T>(Func, JumpTo.Parent);
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		public T JumpsOver<T>(Predicate<T> Func)
			where T : IdContainer
		{
			if (Type == CommandType.Return)
			{
				return GetParent<T>(Func, FunctionScope.Parent);
			}
			else if (Type == CommandType.Break || Type == CommandType.Continue)
			{
				var NeededFlag = Type == CommandType.Break ?
					CommandFlags.Breakable : CommandFlags.Continueable;

				var Loop = GetParent<Command>(x => (x.Flags & NeededFlag) != 0);
				return GetParent<T>(Func, Loop.Children[0]);
			}
			else if (Type == CommandType.Goto)
			{
				return GetParent<T>(Func, JumpTo.Parent);
			}
			else
			{
				throw new InvalidOperationException();
			}
		}

		public JumpDestination GetJumpDestination()
		{
			if (Type == CommandType.Break || Type == CommandType.Continue)
			{
				var Breakable = GetParent<Command>(x => (x.Flags & CommandFlags.Breakable) != 0);
				return new JumpDestination(Breakable, JumpMode.Leave);
			}
			else
			{
				if (Type == CommandType.Goto)
					return new JumpDestination(JumpTo);
				else if (Type == CommandType.Return)
					return new JumpDestination(FunctionScope, JumpMode.Leave);
				else
					throw new InvalidOperationException();
			}
		}

		public bool ReplaceExpressions(Func<ExpressionNode, ExpressionNode> Func)
		{
			var RetValue = true;
			if (Expressions != null)
			{
				for (var i = 0; i < Expressions.Count; i++)
				{
					Expressions[i] = Func(Expressions[i]);
					if (Expressions[i] == null) RetValue = false;
				}
			}

			return RetValue;
		}

		public override Variable OnCreateVariable(CodeString Name, Identifier Type, List<Modifier> Mods = null)
		{
			var Ret = CreateVariableHelper(Name, Type, Mods);
			if (Ret == null) Ret = new LocalVariable(this, Name, Type);
			return Ret;
		}

		public override PluginRoot GetPlugin()
		{
			return new PluginForCodeScope(this);
		}

		public override void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode = GetAssemblyMode.Code)
		{ 
			CG.Container = this;
			if (Extension != null)
			{
				Extension.GetAssembly(CG);
				return;
			}

			if (Type == CommandType.Expression)
			{
				CG.EmitExpression(Expressions[0]);
			}
			else if (Type == CommandType.Cycle)
			{
				CG.InsContainer.Label(ContinueLabel);
				Children[0].GetAssembly(CG);
				CG.InsContainer.Jump(ContinueLabel);
				CG.InsContainer.Label(BreakLabel);
			}
			else if (Type == CommandType.While || Type == CommandType.DoWhile)
			{
				GetWhileAssembly(CG, Expressions[0], Type == CommandType.DoWhile);
			}
			else if (Type == CommandType.For)
			{
				if (Expressions[0] != null)
					CG.EmitExpression(Expressions[0]);

				var DoWhile = WillRun == ConditionResult.True;
				GetWhileAssembly(CG, Expressions[1], DoWhile, Expressions[2]);
			}
			else if (Type == CommandType.If)
			{
				NextLabel = State.AutoLabel;
				CalcIfAssembly(CG, 0);
				CG.InsContainer.Label(NextLabel);
			}
			else if (Type == CommandType.Label)
			{
				CG.InsContainer.Label(Label);
			}
			else if (Type == CommandType.Try)
			{
				Children[0].GetAssembly(CG);
				if (CatchScope != null)
					CG.InsContainer.Jump(FinallyLabel);

				CG.InsContainer.Label(CatchLabel);
				if (CatchScope != null)
					CatchScope.GetAssembly(CG);

				CG.InsContainer.Label(FinallyLabel);
				if (FinallyScope != null)
					FinallyScope.GetAssembly(CG);
			}
			else if (Commands.IsJumpCommand(Type))
			{
				if (Type == CommandType.Return && Expressions != null && Expressions.Count > 0)
					CG.EmitExpression(Expressions[0]);

				CG.InsContainer.Jump(Label);
			}
			else if (Type != CommandType.Unknown)
			{
				throw new NotImplementedException();
			}
		}

		Command GetCommand(IdContainer Container)
		{
			var Scope = Container as CodeScopeNode;
			if (Scope == null) return null;

			if (Scope.Children.Count != 1) return null;
			else return Scope.Children[0] as Command;
		}

		int NextLabel;
		void CalcIfAssembly(CodeGenerator CG, int Pos)
		{
			for (var i = Pos; i < Children.Count; i++)
			{
				var Res = ConditionResult.True;
				if (Expressions.Count > i)
					Res = Expressions[i].ConditionResult;

				if (Res == ConditionResult.Unknown)
				{
					var Cond = Expressions[i];
					var Container = Children[i];

					var ThenCmd = GetCommand(Container);
					var HasCondLessElse = i + 1 < Children.Count && i + 1 >= Expressions.Count;
					var ElseCmd = HasCondLessElse ? GetCommand(Children[i + 1]) : null;

					var Branches = State.Arch.GetBranches(GlobalContainer, ThenCmd, ElseCmd, ref Cond);
					if (Branches[0] == null)
						Branches[0] = new CodeCondBranch(_CG => Container.GetAssembly(_CG));

					if (Branches[1] == null && i + 1 < Children.Count)
						Branches[1] = new CodeCondBranch(_CG => CalcIfAssembly(_CG, i + 1));

					CG.EmitCondition(Cond, Branches[0], Branches[1], NextLabel: NextLabel);
					break;
				}
				else if (Res == ConditionResult.True)
				{
					Children[i].GetAssembly(CG);
					break;
				}
			}
		}

		private void GetWhileAssembly(CodeGenerator CG, ExpressionNode Condition,
			bool DoWhile = false, ExpressionNode Loop = null)
		{
			var Then = State.AutoLabel;
			var CondLbl = State.AutoLabel;

			//--------------------------------------------------------
			if (State.Arch.IsSimpleCompareNode(Condition))
			{
				CG.SetJumpReplacing(CondLbl, () =>
				{
					CG.EmitCondition(Condition, Then, BreakLabel, true);
					CG.InsContainer.Jump(DoWhile ? Then : BreakLabel);
				});
			}

			//--------------------------------------------------------
			if (!DoWhile)
			{
				CG.InsContainer.Label(CondLbl);
				CG.EmitCondition(Condition, Then, BreakLabel, false);
			}

			CG.InsContainer.Label(Then);
			Children[0].GetAssembly(CG);

			CG.InsContainer.Label(ContinueLabel);
			if (Loop != null) CG.EmitExpression(Loop);

			if (DoWhile)
			{
				CG.InsContainer.Label(CondLbl);
				CG.EmitCondition(Condition, Then, BreakLabel, true);
			}
			else
			{
				CG.InsContainer.Jump(CondLbl);
			}

			CG.InsContainer.Label(BreakLabel);
		}

		ConditionResult _WillRun;
		bool _WillRun_Calced = false;
		public ConditionResult WillRun
		{
			get
			{
				if (!_WillRun_Calced)
				{
					if (Type == CommandType.Cycle)
					{
						_WillRun = ConditionResult.True;
					}
					else if (Type == CommandType.While || Type == CommandType.DoWhile)
					{
						_WillRun = Expressions[0].ConditionResult;
					}
					else if (Type == CommandType.For)
					{
						_WillRun = Expressions[1].ConditionResult;
						if (_WillRun == ConditionResult.Unknown)
						{
							ExpressionNode AssignTo = null;
							Variable AssignVar = null;

							if (Expressions[0] is OpExpressionNode)
							{
								var OpNode = Expressions[0] as OpExpressionNode;
								if (OpNode.Operator == Operator.Assignment)
								{
									var Ch = OpNode.Children;
									AssignTo = Ch[1];
									OpNode.GetAssignVar(ref AssignVar);
								}
							}

							if (AssignVar != null && !AssignTo.IdUsed())
							{
								var ConstAssignTo = AssignTo as ConstExpressionNode;
								if (ConstAssignTo == null) return ConditionResult.Unknown;

								var OpCondition = Expressions[1] as OpExpressionNode;
								if (OpCondition == null) return ConditionResult.Unknown;
								var Op = OpCondition.Operator;
								var Ch = OpCondition.Children;

								if (!Operators.IsRelEquality(Op)) return ConditionResult.Unknown;
								var IdCh0 = Ch[0] as IdExpressionNode;
								if (IdCh0 == null || IdCh0.Identifier != AssignVar) return ConditionResult.Unknown;
								var ConstCh1 = Ch[1] as ConstExpressionNode;
								if (ConstCh1 == null) return ConditionResult.Unknown;

								var Ret = ConstAssignTo.Value.DoOperation(ConstCh1.Value, Op);
								if (!(Ret is BooleanValue)) return ConditionResult.Unknown;

								var BoolRet = Ret as BooleanValue;
								_WillRun = BoolRet.Value ? ConditionResult.True : ConditionResult.False;
							}
						}
					}
					else
					{
						throw new NotImplementedException();
					}

					_WillRun_Calced = true;
				}

				return _WillRun;
			}
		}

		public bool HasExpressions
		{
			get { return Expressions != null && Expressions.Count > 0; }
		}

		public void AddExpression(ExpressionNode Node)
		{
			if (Expressions == null)
				Expressions = new List<ExpressionNode>();

			Expressions.Add(Node);
		}
	}
}
