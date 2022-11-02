using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia.Base;
using Zinnia.Languages.Zinnia;

namespace Zinnia.Recognizers
{
	public class TryCommRecognizer : LanguageNode, IFinishableCommRecognizer
	{
		public static string[] Strings = new string[] { "try", "catch", "finally" };

		public TryCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var State = Scope.State;
			var FindRes = Code.StartsWith(Strings, null, new IdCharCheck(true));
			if (FindRes.Index != -1)
			{
				var Result = State.Language.CommandInnerSeparator.Separate(State, Code);
				if (!Result.Command.IsValid) return SimpleRecResult.Failed;

				var CommandCode = Code.Substring(0, FindRes.String.Length);
				var Param = Result.Command.Substring(FindRes.String.Length).Trim();

				//----------------------------------------------------------------------------------
				if (FindRes.Index == 0)
				{
					if (Param.Length != 0)
					{
						State.Messages.Add(MessageId.NotExpected, Param);
						return SimpleRecResult.Failed;
					}

					var Comm = new Command(Scope, Code, CommandType.Try);
					var NewScope = new CodeScopeNode(Comm, Result.Inner);
					Comm.Children.Add(NewScope);

					if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;
					if (!Scope.AddFinishableCommand(Comm, this)) return SimpleRecResult.Failed;
					return SimpleRecResult.Succeeded;
				}

				//----------------------------------------------------------------------------------
				else if (FindRes.Index == 1)
				{
					var Comm = Scope.LastChild as Command;
					if (Comm == null || Comm.Type != CommandType.Try || Comm.FinallyScope != null ||
						(Comm.Flags & CommandFlags.CatchesAllException) != 0)
					{
						State.Messages.Add(MessageId.NoMatchingCommand, Result.Command);
						return SimpleRecResult.Failed;
					}

					CodeScopeNode NewScope;
					if (Param.Length != 0)
					{
						var VarDeclListFlags = VarDeclarationListFlags.EnableMessages;
						var VarDeclList = VarDeclarationList.Create(Scope, Param, null, VarDeclListFlags);
						if (VarDeclList == null) return SimpleRecResult.Failed;

						if (VarDeclList.Count != 1)
						{
							State.Messages.Add(MessageId.ParamCount, Param);
							return SimpleRecResult.Failed;
						}

						var Decl = VarDeclList[0];
						NewScope = Commands.CreateCatchScope(Comm, CommandCode, Result.Inner, Decl.Type, Decl.Name);
						if (NewScope == null) return SimpleRecResult.Failed;
					}
					else
					{
						NewScope = Commands.CreateCatchScope(Comm, CommandCode, Result.Inner);
						if (NewScope == null) return SimpleRecResult.Failed;
					}


					if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;
					return SimpleRecResult.Succeeded;
				}

				//----------------------------------------------------------------------------------
				else if (FindRes.Index == 2)
				{
					if (Param.Length != 0)
					{
						State.Messages.Add(MessageId.NotExpected, Param);
						return SimpleRecResult.Failed;
					}

					var Comm = Scope.LastChild as Command;
					if (Comm == null || Comm.Type != CommandType.Try || Comm.FinallyScope != null)
					{
						State.Messages.Add(MessageId.NoMatchingCommand, Result.Command);
						return SimpleRecResult.Failed;
					}

					Comm.FinallyScope = new CodeScopeNode(Comm, Result.Inner);
					Comm.Children.Add(Comm.FinallyScope);

					if (!Comm.FinallyScope.ProcessCode())
						return SimpleRecResult.Failed;

					return SimpleRecResult.Succeeded;
				}

				//----------------------------------------------------------------------------------
				else
				{
					throw new ApplicationException();
				}
			}

			return SimpleRecResult.Unknown;
		}

		public bool Finish(IdContainer Container)
		{
			var Comm = Container as Command;
			if ((Comm.Flags & CommandFlags.CatchesAllException) == 0 && Comm.CatchScope != null)
			{
				if (!Commands.CreateDefaultCatchScope(Comm))
					return false;
			}

			return true;
		}
	}

	public class IfCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "if";
		public static string ElseFind = "else";

		public IfCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public static bool ProcIf(CodeScopeNode Scope, CodeString Code, int CommandLength, Command CCondComm = null)
		{
			var RetValue = true;
			var ElseInner = new CodeString();
			var State = Scope.State;

			var Result = State.Language.CommandInnerSeparator.Separate(State, Code);
			if (!Result.Command.IsValid) return false;

			if (Result.FindRes.Position != -1 && Result.Inner.LineCount == 1)
			{
				var SkippingHandlers = State.Language.GlobalHandlers;
				var ElseRes = Result.Inner.Find(ElseFind, null, false, new IdCharCheck(true), SkippingHandlers);
				if (ElseRes != -1)
				{
					var Else = Result.Inner.Substring(ElseRes).Trim();
					Result.Inner = Result.Inner.Substring(0, ElseRes).Trim();

					ElseInner = State.GetInnerScope(Else, ElseFind.Length);
					if (!ElseInner.IsValid) return false;
				}
			}

			var CondComm = CCondComm;
			if (CondComm == null)
				CondComm = new Command(Scope, Code, CommandType.If);

			var Plugin = CondComm.GetPlugin();
			Plugin.GetPlugin<TypeMngrPlugin>().RetType = Scope.GlobalContainer.CommonIds.Boolean;

			var ConditionStr = Result.Command.Substring(CommandLength).Trim();
			var Condition = Expressions.CreateExpression(ConditionStr, Plugin);
			if (Condition == null) RetValue = false;

			var ThenScope = new CodeScopeNode(CondComm, Result.Inner);
			if (!ThenScope.ProcessCode()) RetValue = false;

			CondComm.AddExpression(Condition);
			CondComm.Children.Add(ThenScope);

			if (ElseInner.IsValid)
			{
				var ElseScope = new CodeScopeNode(CondComm, ElseInner);
				CondComm.Children.Add(ElseScope);

				if (!ElseScope.ProcessCode())
					RetValue = false;
			}

			if (CondComm != CCondComm && !Scope.AddCommand(CondComm))
				return false;

			return RetValue;
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				if (!ProcIf(Scope, Code, String.Length))
					return SimpleRecResult.Failed;
				else return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ElseCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "else";

		public ElseCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public static bool ProcElse(CodeScopeNode Scope, CodeString Code, int CommandLength)
		{
			var State = Scope.State;
			var CondComm = Scope.LastChild as Command;

			var Command = Code.Substring(0, CommandLength).Trim();
			var Inner = State.GetInnerScope(Code.Substring(CommandLength).Trim(), Command);
			if (!Inner.IsValid) return false;

			if (CondComm == null || CondComm.Type != CommandType.If ||
				CondComm.Children.Count > CondComm.Expressions.Count)
			{
				State.Messages.Add(MessageId.NoMatchingCommand, Command);
				return false;
			}
			else
			{
				if (Inner.Line == Code.Line && Inner.StartsWith(IfCommRecognizer.String, new IdCharCheck(true)))
				{
					if (!IfCommRecognizer.ProcIf(Scope, Inner, IfCommRecognizer.String.Length, CondComm))
						return false;
				}
				else
				{
					var NewScope = new CodeScopeNode(CondComm, Inner);
					if (!NewScope.ProcessCode()) return false;
					CondComm.Children.Add(NewScope);
				}
			}

			return true;
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				if (!ProcElse(Scope, Code, String.Length))
					return SimpleRecResult.Failed;
				else return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class LabelCommRecognizer : LanguageNode, ICommRecognizer
	{
		public LabelCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { ":" };
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var State = Scope.State;
			var SkippingHandlers = State.Language.GlobalHandlers;

			var Result = Code.Find(Operators, Skip, Handlers: SkippingHandlers);
			if (Result.Position == -1) return SimpleRecResult.Unknown;

			var Name = Code.Substring(0, Result.Position).Trim();
			if (!Name.IsValidIdentifierName) return SimpleRecResult.Unknown;

			var StringName = Name.ToString();
			var FS = Scope.FunctionScope;
			if (FS.GetLabelCmd(StringName) != null)
			{
				State.Messages.Add(MessageId.LabelAlreadyDefined, Code);
				return SimpleRecResult.Failed;
			}

			var Comm = new Command(Scope, Code, CommandType.Label);
			Comm.Label = State.AutoLabel;
			if (!Scope.AddCommand(Comm)) return SimpleRecResult.Failed;

			var Inner = Code.Substring(Result.NextChar).Trim();
			if (Inner.Length != 0 && !Scope.RecognizeCommand(Inner))
				return SimpleRecResult.Failed;

			FS.Labels.Add(StringName, Comm);
			return SimpleRecResult.Succeeded;
		}
	}

	public class GotoCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "goto";

		public GotoCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				var Label = Code.Substring(String.Length).Trim();
				if (!Label.IsValidIdentifierName)
				{
					State.Messages.Add(MessageId.NotValidName, Label);
					return SimpleRecResult.Failed;
				}

				var Comm = new Command(Scope, Code, CommandType.Goto)
				{
					LabelName = Label,
				};

				Scope.FunctionScope.Gotos.Add(Comm);
				if (!Scope.AddCommand(Comm)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class BreakCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "break";

		public BreakCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				if (Code.Trim().Length > String.Length)
				{
					State.Messages.Add(MessageId.NotExpected, Code.Substring(String.Length).Trim());
					return SimpleRecResult.Failed;
				}

				var Comm = Scope.GetParent<Command>(x => (x.Flags & CommandFlags.Breakable) != 0);
				if (Comm == null)
				{
					State.Messages.Add(MessageId.UnBreakCountinueable, Code.Substring(0, String.Length));
					return SimpleRecResult.Failed;
				}

				var NewComm = new Command(Scope, Code, CommandType.Break);
				NewComm.Label = Comm.BreakLabel;
				if (!Scope.AddCommand(NewComm)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ContinueCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "continue";

		public ContinueCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				if (Code.Trim().Length > String.Length)
				{
					State.Messages.Add(MessageId.NotExpected, Code.Substring(String.Length).Trim());
					return SimpleRecResult.Failed;
				}

				var Comm = Scope.GetParent<Command>(x => (x.Flags & CommandFlags.Continueable) != 0);
				if (Comm == null)
				{
					State.Messages.Add(MessageId.UnBreakCountinueable, Code.Substring(0, String.Length));
					return SimpleRecResult.Failed;
				}

				var NewComm = new Command(Scope, Code, CommandType.Continue);
				NewComm.Label = Comm.ContinueLabel;
				if (!Scope.AddCommand(NewComm)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class SwitchCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string[] Strings = new string[] { "switch", "case", "default" };

		public SwitchCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var FindRes = Code.StartsWith(Strings, null, new IdCharCheck(true));
			if (FindRes.Index == -1) return SimpleRecResult.Unknown;

			var State = Scope.State;
			if (FindRes.Index != 0)
			{
				State.Messages.Add(MessageId.CaseWithoutSwitch, Code);
				return SimpleRecResult.Failed;
			}

			var EndingResult = State.Language.CommandInnerSeparator.Separate(State, Code);
			if (!EndingResult.Command.IsValid) return SimpleRecResult.Failed;

			var ExpressionString = EndingResult.Command.TrimmedSubstring(State, FindRes.String.Length);
			if (!ExpressionString.IsValid) return SimpleRecResult.Failed;

			var SwitchScope = new CodeScopeNode(Scope, EndingResult.Inner);
			if (!Scope.AddCommand(SwitchScope)) return SimpleRecResult.Failed;

			var Plugin = SwitchScope.GetPlugin();
			var Expression = Expressions.CreateExpression(ExpressionString, Plugin, BeginEndMode.Begin);
			if (Expression == null) return SimpleRecResult.Failed;

			if (Expression is OpExpressionNode)
			{
				var Variable = SwitchScope.CreateAndDeclareVariable(new CodeString(), Expression.Type);
				if (Variable == null) return SimpleRecResult.Failed;

				var AssignNode = Expressions.SetValue(Variable, Expression, Plugin, EndingResult.Command, true);
				if (AssignNode == null) return SimpleRecResult.Failed;

				var AssignCommand = new Command(SwitchScope, EndingResult.Command, CommandType.Expression);
				AssignCommand.Expressions = new List<ExpressionNode>() { AssignNode };
				SwitchScope.Children.Add(AssignCommand);

				Expression = new IdExpressionNode(Variable, Code);
			}

			var Condition = new Command(SwitchScope, Code, CommandType.If);
			SwitchScope.Children.Add(Condition);

			var DefaultAlreadySpecified = false;
			var Result = ZinniaHelper.ForEachLine(SwitchScope, Line =>
			{
				var LocalRes = Line.StartsWith(Strings, null, new IdCharCheck(true));
				if (LocalRes.Index != 1 && LocalRes.Index != 2)
				{
					State.Messages.Add(MessageId.UnknownCommand, Line);
					return false;
				}

				if (DefaultAlreadySpecified)
				{
					State.Messages.Add(MessageId.SwitchAlreadyHasDef, Line);
					return false;
				}

				if (LocalRes.Index == 1)
				{
					var CaseEndingResult = State.Language.CommandInnerSeparator.Separate(State, Code);
					if (!CaseEndingResult.Command.IsValid) return false;

					var CaseString = CaseEndingResult.Command.TrimmedSubstring(State, LocalRes.String.Length);
					if (!CaseString.IsValid) return false;
					
					var CaseScope = new CodeScopeNode(Condition, CaseEndingResult.Inner);
					Condition.Children.Add(CaseScope);

					var CasePlugin = CaseScope.GetPlugin();
					var Right = Expressions.CreateExpression(CaseString, CasePlugin, BeginEndMode.Begin);
					var Left = Expression.Copy(CasePlugin, Mode: BeginEndMode.None);
					if (Left == null || Right == null) return false;

					var CompareNode = (ExpressionNode)new OpExpressionNode(Operator.Equality, CaseEndingResult.Command);
					CompareNode.Children = new ExpressionNode[] { Left, Right };
					if ((CompareNode = CasePlugin.NewNode(CompareNode)) == null) return false;
					if ((CompareNode = CasePlugin.End(CompareNode)) == null) return false;

					Condition.AddExpression(CompareNode);
					if (!CaseScope.ProcessCode()) return false;
				}
				else
				{
					var DefaultEndingResult = State.Language.CommandInnerSeparator.Separate
						(State, Code, CommandInnerSeparatorFlags.InnerIsOptional);

					if (!DefaultEndingResult.Command.IsValid) return false;

					CodeString DefaultInner;
					if (DefaultEndingResult.FindRes.Position == -1)
						DefaultInner = State.GetInnerScope(Line, LocalRes.String.Length);
					else DefaultInner = DefaultEndingResult.Inner;

					var DefaultScope = new CodeScopeNode(Condition, DefaultInner);
					Condition.Children.Add(DefaultScope);

					if (!DefaultScope.ProcessCode()) return false;
				}

				return true;
			});

			if (!Result) return SimpleRecResult.Failed;
			return SimpleRecResult.Succeeded;
		}
	}

	public class DoCommRecognizer : LanguageNode, IFinishableCommRecognizer
	{
		public static string String = "do";

		public DoCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var Comm = new Command(Scope, Code, CommandType.DoWhile);
				var Inner = Scope.State.GetInnerScope(Code, String.Length);
				if (!Inner.IsValid) return SimpleRecResult.Failed;

				var NewScope = new CodeScopeNode(Comm, Inner);
				Comm.Children.Add(NewScope);

				if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;
				if (!Scope.AddFinishableCommand(Comm, this)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}

		public bool Finish(IdContainer Container)
		{
			var State = Container.State;
			var Comm = Container as Command;

			if (!Comm.HasExpressions)
			{
				State.Messages.Add(MessageId.DeficientDoWhile, Comm.Code);
				return false;
			}

			return true;
		}
	}

	public class RepeatCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "repeat";

		public RepeatCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				var RetValue = true;

				var Result = State.Language.CommandInnerSeparator.Separate(State, Code);
				if (!Result.Command.IsValid) return SimpleRecResult.Failed;

				var Comm = new Command(Scope, Code, CommandType.For);
				var NewScope = new CodeScopeNode(Comm, Result.Inner);
				Comm.Children.Add(NewScope);

				if (!NewScope.ProcessCode()) RetValue = false;

				var Plugin = Comm.GetPlugin();
				var RepeatStr = Result.Command.Substring(String.Length).Trim();
				var Condition = Expressions.CreateExpression(RepeatStr, Plugin, BeginEndMode.Begin);
				if (Condition == null) return SimpleRecResult.Failed;

				Condition = Plugin.FinishNode(Condition);
				if (Condition == null) return SimpleRecResult.Failed;

				var Type = Condition.Type;
				if (!(Type is NonFloatType))
				{
					State.Messages.Add(MessageId.MustBeInteger, RepeatStr);
					RetValue = false;
				}

				var Var = Comm.CreateVariable(new CodeString(), Type) as LocalVariable;
				Var.PreAssigned = true;
				Comm.DeclareIdentifier(Var);

				var IdNode = Plugin.NewNode(new IdExpressionNode(Var, Code));
				if (IdNode != null)
				{
					var CondCh = new ExpressionNode[] { IdNode, Condition };
					Condition = Plugin.NewNode(new OpExpressionNode(Operator.Less, CondCh, Code));
					if (Condition == null || (Condition = Plugin.End(Condition)) == null)
						RetValue = false;
				}

				var Init = Expressions.Zero(Plugin, Var, Code);
				var Loop = Expressions.Increase(Plugin, Var, 1, Code);
				if (Loop == null || Init == null) RetValue = false;

				Comm.Expressions = new List<ExpressionNode>() { Init, Condition, Loop };

				if (!Scope.AddCommand(Comm)) RetValue = false;
				if (RetValue) return SimpleRecResult.Succeeded;
				else return SimpleRecResult.Failed;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class CycleCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "cycle";

		public CycleCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var Inner = Scope.State.GetInnerScope(Code, String.Length);
				if (!Inner.IsValid) return SimpleRecResult.Failed;

				var Comm = new Command(Scope, Code, CommandType.Cycle);
				var NewScope = new CodeScopeNode(Comm, Inner);
				Comm.Children.Add(NewScope);

				if (!NewScope.ProcessCode())
					return SimpleRecResult.Failed;

				if (!Scope.AddCommand(Comm)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class CheckedUncheckedCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string[] Strings = { "checked", "unchecked" };

		public CheckedUncheckedCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var Res = Code.StartsWith(Strings, null, new IdCharCheck(true));
			if (Res.Index != -1)
			{
				var Inner = Scope.State.GetInnerScope(Code, Res.String.Length);
				if (!Inner.IsValid) return SimpleRecResult.Failed;

				var NewScope = new CodeScopeNode(Scope, Inner);
				if (Res.Index == 0) NewScope.CheckingMode = CheckingMode.Checked;
				else if (Res.Index == 1) NewScope.CheckingMode = CheckingMode.Unchecked;
				else throw new ApplicationException();

				if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;
				if (!Scope.AddCommand(NewScope)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class WhileCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "while";

		public WhileCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				var LastComm = Scope.LastChild as Command;
				var DoWhile = LastComm != null && LastComm.Type == CommandType.DoWhile && !LastComm.HasExpressions;

				var CISFlags = CommandInnerSeparatorFlags.None;
				if (DoWhile) CISFlags |= CommandInnerSeparatorFlags.NoEmptyScopeWarning;
				var Result = State.Language.CommandInnerSeparator.Separate(State, Code, CISFlags);
				if (!Result.Command.IsValid) return SimpleRecResult.Failed;

				Command WhileComm;
				CodeScopeNode NewScope = null;
				if (!DoWhile)
				{
					WhileComm = new Command(Scope, Code, CommandType.While);
					NewScope = new CodeScopeNode(WhileComm, Result.Inner);
					WhileComm.Children.Add(NewScope);
				}
				else
				{
					WhileComm = LastComm;
					Scope.MarkFinished();

					if (Result.Inner.IsValid)
					{
						State.Messages.Add(MessageId.NotExpected);
						return SimpleRecResult.Failed;
					}
				}

				var Plugin = WhileComm.GetPlugin();
				Plugin.GetPlugin<TypeMngrPlugin>().RetType = Scope.GlobalContainer.CommonIds.Boolean;

				var ConditionStr = Result.Command.Substring(String.Length).Trim();
				var Condition = Expressions.CreateExpression(ConditionStr, Plugin);
				if (Condition == null) return SimpleRecResult.Failed;

				if (!WhileComm.HasExpressions) WhileComm.AddExpression(Condition); 
				else WhileComm.Expressions[0] = Condition;

				if (!DoWhile)
				{
					if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;
					if (!Scope.AddCommand(WhileComm)) return SimpleRecResult.Failed;
				}

				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ForInCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "for";
		public static string[] Strings = new string[] { "in" };
		public static string[] StepStrings = new string[] { "step" };

		struct RangeNodePair
		{
			public ExpressionNode Init;
			public ExpressionNode Until;
			public ExpressionNode Step;
			public Operator Operator;

			public RangeNodePair(ExpressionNode Node)
			{
				this.Init = Node;
				this.Until = null;
				this.Step = null;
				this.Operator = Operator.Unknown;
			}

			public RangeNodePair(ExpressionNode Init, ExpressionNode Until, ExpressionNode Step, Operator Operator)
			{
				this.Init = Init;
				this.Until = Until;
				this.Step = Step;
				this.Operator = Operator;
			}
		}

		static ExpressionNode GetChild(ExpressionNode Node, int Index, CodeString Code)
		{
			if (Expressions.GetOperator(Node) == Operator.Tuple)
				return Node.Children[Index];

			var TType = Node.Type.RealId as TupleType;
			if (TType != null)
			{
				var Members = TType.StructuredScope.IdentifierList;
				var Member = new IdExpressionNode(Members[Index], Code);
				var RetCh = new ExpressionNode[] { Node, Member };
				return new OpExpressionNode(Operator.Member, RetCh, Code);
			}

			return Node;
		}

		static int GetTupleLength(ExpressionNode Node, int Default = 1)
		{
			if (Expressions.GetOperator(Node) == Operator.Tuple)
				return Node.Children.Length;

			var TType = Node.Type.RealId as TupleType;
			if (TType != null) return TType.StructuredScope.IdentifierList.Count;
			return Default;
		}

		static bool CheckLength(CompilerState State, ExpressionNode Node, ExpressionNode Step, int Count)
		{
			if (Count != 1 && GetTupleLength(Node, Count) != Count)
			{
				State.Messages.Add(MessageId.ForInvalidTupleSize, Node.Code);
				return false;
			}

			if (Count != 1 && GetTupleLength(Step, Count) != Count)
			{
				State.Messages.Add(MessageId.ForInvalidTupleSize, Step.Code);
				return false;
			}

			return true;
		}

		static RangeNodePair[] MapRangeNodes(CompilerState State, CodeString Code, ExpressionNode Node, ExpressionNode Step = null, int Count = -1)
		{
			if (Step == null)
			{
				var IntType = State.GlobalContainer.CommonIds.GetIdentifier(UndeclaredIdType.Int32);
				var IntValue = new IntegerValue(new System.Numerics.BigInteger(1));
				Step = new ConstExpressionNode(IntType, IntValue, new CodeString(), ExpressionFlags.AutoConvert);
			}

			var Op = Expressions.GetOperator(Node);
			var Ch = Node.Children;

			if (Zinnia.Operators.IsRange(Op))
			{
				if (Count == -1) Count = Math.Max(GetTupleLength(Ch[0]), GetTupleLength(Ch[1]));
				if (!CheckLength(State, Node, Step, Count)) return null;

				var Ret = new RangeNodePair[Count];
				if (Count != 1)
				{
					for (var i = 0; i < Count; i++)
					{
						Ret[i] = new RangeNodePair(GetChild(Ch[0], i, Code),
							GetChild(Ch[1], i, Code), GetChild(Step, i, Code), Op);
					}
				}
				else
				{
					Ret[0] = new RangeNodePair(Ch[0], Ch[1], Step, Op);
				}

				return Ret;
			}
			else if (Op == Operator.Tuple)
			{
				if (Count == -1) Count = Ch.Length;
				if (!CheckLength(State, Node, Step, Count))
					return null;

				var Ret = new RangeNodePair[Ch.Length];
				for (var i = 0; i < Ch.Length; i++)
				{
					var ChiOp = Expressions.GetOperator(Ch[i]);
					if (Zinnia.Operators.IsRange(ChiOp))
					{
						var ChiCh = Ch[i].Children;
						Ret[i] = new RangeNodePair(ChiCh[0], ChiCh[1], 
							GetChild(Step, i, Code), ChiOp);
					}
					else
					{
						Ret[i] = new RangeNodePair(Ch[i]);
					}
				}

				return Ret;
			}
			else
			{
				return new RangeNodePair[] { new RangeNodePair(Node) };
			}
		}

		ConditionResult IsNegative(ExpressionNode Node)
		{
			var CNode = Node as ConstExpressionNode;
			if (CNode == null) return ConditionResult.Unknown;

			if (CNode.Value is DoubleValue)
			{
				if ((CNode.Value as DoubleValue).Value < 0)
					return ConditionResult.True;
				else return ConditionResult.False;
			}
			else if (CNode.Value is FloatValue)
			{
				if ((CNode.Value as FloatValue).Value < 0)
					return ConditionResult.True;
				else return ConditionResult.False;
			}
			else if (CNode.Value is IntegerValue)
			{
				if ((CNode.Value as IntegerValue).Value < 0)
					return ConditionResult.True;
				else return ConditionResult.False;
			}
			else if (CNode.Value is ZeroValue)
			{
				return ConditionResult.False;
			}

			return ConditionResult.Unknown;
		}

		List<ExpressionNode> GetExpressions(Command Command, Identifier Variable,
			RangeNodePair RangeNode, CodeString Line, CodeString FLine)
		{
			var Plugin = Command.GetPlugin();
			var InitNode = RangeNode.Init.Copy(Plugin, Mode: BeginEndMode.Begin);
			if (InitNode == null) return null;

			InitNode = Expressions.SetValue(Variable, InitNode, Plugin, FLine, true);
			if (InitNode == null) return null;

			//-------------------------------------------------------------------------
			if (!Plugin.Begin()) return null;
			var CmpChildren = new ExpressionNode[]
			{
				Plugin.NewNode(new IdExpressionNode(Variable, Variable.Name)),
				RangeNode.Until.Copy(Plugin, Mode: BeginEndMode.None),
			};

			if (CmpChildren[0] == null || CmpChildren[1] == null)
				return null;

			Operator CmpOp;
			var NegRes = IsNegative(RangeNode.Step);
			if (NegRes == ConditionResult.False)
			{
				if (RangeNode.Operator == Operator.RangeUntil) CmpOp = Operator.Less;
				else if (RangeNode.Operator == Operator.RangeTo) CmpOp = Operator.LessEqual;
				else throw new ApplicationException();
			}
			else if (NegRes == ConditionResult.True)
			{
				if (RangeNode.Operator == Operator.RangeUntil) CmpOp = Operator.Greater;
				else if (RangeNode.Operator == Operator.RangeTo) CmpOp = Operator.GreaterEqual;
				else throw new ApplicationException();
			}
			else
			{
				throw new NotImplementedException();
			}

			var CmpNode = Plugin.NewNode(new OpExpressionNode(CmpOp, CmpChildren, FLine));
			if (CmpNode == null || (CmpNode = Plugin.End(CmpNode)) == null)
				return null;

			//-------------------------------------------------------------------------
			if (!Plugin.Begin()) return null;
			var IncChildren = new ExpressionNode[]
			{
				Plugin.NewNode(new IdExpressionNode(Variable, Variable.Name)),
				RangeNode.Step.Copy(Plugin, Mode: BeginEndMode.None),
			};

			if (IncChildren[0] == null || IncChildren[1] == null)
				return null;

			var IncNode = Plugin.NewNode(new OpExpressionNode(Operator.Add, IncChildren, FLine));
			if (IncNode == null) return null;

			IncNode = Expressions.SetValue(Variable, IncNode, Plugin, Line, true);
			if (IncNode == null) return null;

			return new List<ExpressionNode>() { InitNode, CmpNode, IncNode };
		}

		public ForInCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				var EndingResult = State.Language.CommandInnerSeparator.Separate(State, Code);
				if (!EndingResult.Command.IsValid) return SimpleRecResult.Failed;

				var FLine = EndingResult.Command;
				var Line = FLine.Substring(String.Length).Trim();

				//--------------------------------------------------------------------------------------
				var FindResult = Line.Find(Strings, null, false, new IdCharCheck(true));
				if (FindResult.Index == -1)
				{
					State.Messages.Add(MessageId.DeficientExpr, FLine);
					return SimpleRecResult.Failed;
				}

				var LeftStr = Line.TrimmedSubstring(State, 0, FindResult.Position);
				var RightStr = Line.TrimmedSubstring(State, FindResult.Position + FindResult.String.Length);
				if (!LeftStr.IsValid || !RightStr.IsValid) return SimpleRecResult.Failed;

				var StepStr = new CodeString();
				var StepResult = RightStr.Find(StepStrings, null, false, new IdCharCheck(true));
				if (StepResult.Index != -1)
				{
					StepStr = RightStr.TrimmedSubstring(State, StepResult.Position + StepResult.String.Length);
					RightStr = RightStr.TrimmedSubstring(State, 0, StepResult.Position);
					if (!StepStr.IsValid || !RightStr.IsValid) return SimpleRecResult.Failed;
				}

				//--------------------------------------------------------------------------------------
				var VarDecls = VarDeclarationList.Create(Scope, LeftStr);
				if (VarDecls == null) return SimpleRecResult.Failed;

				if (VarDecls.Count == 0)
				{
					State.Messages.Add(MessageId.NoForVar, LeftStr);
					return SimpleRecResult.Failed;
				}

				//--------------------------------------------------------------------------------------
				var Plugin = Scope.GetPlugin();
				var RangeNodes_Node = Expressions.CreateExpression(RightStr, Plugin);
				if (RangeNodes_Node == null) return SimpleRecResult.Failed;

				ExpressionNode RangeNodes_Step = null;
				if (StepStr.IsValid)
				{
					RangeNodes_Step = Expressions.CreateExpression(StepStr, Plugin);
					if (RangeNodes_Step == null) return SimpleRecResult.Failed;
				}

				var RangeNodes = MapRangeNodes(State, FLine, RangeNodes_Node, RangeNodes_Step, VarDecls.Count);
				if (RangeNodes == null) return SimpleRecResult.Failed;

				//--------------------------------------------------------------------------------------
				var LastContainer = (IdContainer)Scope;
				var ForCommands = new Command[VarDecls.Count];
				for (var i = 0; i < RangeNodes.Length; i++)
				{
					var Command = new Command(LastContainer, Code, CommandType.For);
					if (i > 0) Command.Flags &= ~CommandFlags.Breakable;

					if (LastContainer == Scope)
					{
						if (!Scope.AddCommand(Command))
							return SimpleRecResult.Failed;
					}
					else
					{
						LastContainer.Children.Add(Command);
					}

					LastContainer = Command;
					ForCommands[i] = Command;

					//-------------------------------------------------------------------------
					var Variable = VarDecls[i].ToVariable(Command, Declare: true);
					if (Variable == null) return SimpleRecResult.Failed;

					var RangeNode = RangeNodes[i];
					if (!Zinnia.Operators.IsRange(RangeNode.Operator))
					{
						State.Messages.Add(MessageId.ForInvalidOp, RightStr);
						return SimpleRecResult.Failed;
					}
					
					//-------------------------------------------------------------------------
					Command.Expressions = GetExpressions(Command, Variable, RangeNode, Line, FLine);
				}

				var NewScope = new CodeScopeNode(LastContainer, EndingResult.Inner);
				LastContainer.Children.Add(NewScope);

				if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ForToCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "for";
		public static string[] Strings = new string[] { "to", "downto", "until", "eachin" };
		public static string[] StepString = new string[] { "step" };

		public ForToCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				var Result = State.Language.CommandInnerSeparator.Separate(State, Code);
				if (!Result.Command.IsValid) return SimpleRecResult.Failed;
				var Line = Result.Command.Substring(String.Length).Trim();

				//--------------------------------------------------------------------------------------
				var SkippingHandlers = State.Language.GlobalHandlers;
				var AssignmentPos = Line.Find('=', Handlers: SkippingHandlers);
				if (AssignmentPos == -1)
				{
					State.Messages.Add(MessageId.NoForVar, Code);
					return SimpleRecResult.Failed;
				}

				var LeftStr = Line.TrimmedSubstring(State, 0, AssignmentPos);
				var RightStr = Line.TrimmedSubstring(State, AssignmentPos + 1);
				if (!LeftStr.IsValid || !RightStr.IsValid) return SimpleRecResult.Failed;

				var VarDecls = VarDeclarationList.Create(Scope, LeftStr);
				if (VarDecls == null) return SimpleRecResult.Failed;

				if (VarDecls.Count != 1)
				{
					if (VarDecls.Count == 0) State.Messages.Add(MessageId.NoForVar, Code);
					else State.Messages.Add(MessageId.MoreForVar, Code);
					return SimpleRecResult.Failed;
				}

				var ForCommand = new Command(Scope, Code, CommandType.For);
				var NewScope = new CodeScopeNode(ForCommand, Result.Inner);
				ForCommand.Children.Add(NewScope);
				
				var Plugin = ForCommand.GetPlugin();
				var ForVar = VarDecls[0].ToVariable(ForCommand, Declare: true);
				if (ForVar == null) return SimpleRecResult.Failed;
				
				//--------------------------------------------------------------------------------------
				var FindRes = RightStr.Find(Strings, IdCharCheck: new IdCharCheck(true), Handlers: SkippingHandlers);
				if (FindRes.Index == -1)
				{
					State.Messages.Add(MessageId.ForToDownToUntil, Code);
					return SimpleRecResult.Failed;
				}

				//--------------------------------------------------------------------------------------
				else if (FindRes.Index < 3)
				{
					var FromStr = RightStr.TrimmedSubstring(State, 0, FindRes.Position);
					var UntilToStr = RightStr.TrimmedSubstring(State, FindRes.NextChar);
					if (!FromStr.IsValid || !UntilToStr.IsValid) return SimpleRecResult.Failed;

					var StepStr = new CodeString();
					var StepRes = UntilToStr.Find(StepString, IdCharCheck: new IdCharCheck(true), Handlers: SkippingHandlers);
					if (StepRes.Index != -1)
					{
						StepStr = UntilToStr.TrimmedSubstring(State, StepRes.Position + StepRes.String.Length);
						UntilToStr = UntilToStr.TrimmedSubstring(State, 0, StepRes.Position);
						if (!StepStr.IsValid || !UntilToStr.IsValid) return SimpleRecResult.Failed;
					}

					var From = Expressions.CreateExpression(FromStr, Plugin, BeginEndMode.Begin);
					if (From == null) return SimpleRecResult.Failed;

					var InitNode = Expressions.SetValue(ForVar, From, Plugin, FromStr, true);
					if (InitNode == null) return SimpleRecResult.Failed;

					var UntilTo = Expressions.CreateExpression(UntilToStr, Plugin, BeginEndMode.Begin);
					var ForVarNode = Plugin.NewNode(new IdExpressionNode(ForVar, Code));
					if (ForVarNode == null || UntilTo == null) return SimpleRecResult.Failed;

					Operator CmpOp;
					if (FindRes.Index == 0) CmpOp = Operator.LessEqual;
					else if (FindRes.Index == 1) CmpOp = Operator.GreaterEqual;
					else if (FindRes.Index == 2) CmpOp = Operator.Less;
					else throw new ApplicationException();

					var CmpList = new ExpressionNode[] { ForVarNode, UntilTo };
					var CmpNode = Plugin.NewNode(new OpExpressionNode(CmpOp, CmpList, RightStr));
					if (CmpNode == null || (CmpNode = Plugin.End(CmpNode)) == null)
						return SimpleRecResult.Failed;

					var StepNode = (ExpressionNode)null;
					if (StepStr.IsValid)
					{
						StepNode = Expressions.CreateExpression(StepStr, Plugin, BeginEndMode.Begin);
						if (StepNode == null) return SimpleRecResult.Failed;

						StepNode = Expressions.Increase(Plugin, ForVar, StepNode, StepStr);
						if (StepNode == null) return SimpleRecResult.Failed;
					}
					else
					{
						var StepOp = FindRes.Index == 1 ? Operator.Subract : Operator.Add;
						StepNode = Expressions.Increase(Plugin, ForVar, 1, Code, StepOp);
						if (StepNode == null) return SimpleRecResult.Failed;
					}

					ForCommand.Expressions = new List<ExpressionNode>() { InitNode, CmpNode, StepNode };
				}

				//--------------------------------------------------------------------------------------
				else if (FindRes.Index == 3)
				{
					throw new NotImplementedException();
				}

				//--------------------------------------------------------------------------------------
				else
				{
					throw new ApplicationException();
				}

				if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;
				if (!Scope.AddCommand(ForCommand)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ThrowCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string[] Strings = new string[] { "throw", "rethrow" };

		public ThrowCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var Res = Code.StartsWith(Strings, null, new IdCharCheck(true));
			if (Res.Index != -1)
			{
				var State = Scope.State;
				var Global = State.GlobalContainer;
				var Str = Code.Substring(Res.String.Length).Trim();

				var Plugin = Scope.GetPlugin();
				var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
				TypeMngrPlugin.RetType = Identifiers.GetByFullNameFast<ClassType>(Global, "System.Exception");
				if (TypeMngrPlugin.RetType == null) return SimpleRecResult.Failed;

				var Node = Expressions.CreateExpression(Str, Plugin);
				if (Node == null) return SimpleRecResult.Failed;

				CommandType CommType;
				if (Res.Index == 0) CommType = CommandType.Throw;
				else if (Res.Index == 1) CommType = CommandType.Rethrow;
				else throw new ApplicationException();

				var Comm = new Command(Scope, Code, CommType);
				Comm.Expressions = new List<ExpressionNode>() { Node };
				if (!Scope.AddCommand(Comm)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}
	
	public class ReturnCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "return";

		public ReturnCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var Node = (ExpressionNode)null;
				if (Code.Trim().Length > String.Length)
				{
					var RetStr = Code.Substring(String.Length).Trim();
					var Plugin = Scope.GetPlugin();
					var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
					TypeMngrPlugin.RetType = Scope.FunctionScope.Type.RetType;

					Node = Expressions.CreateExpression(RetStr, Plugin);
					if (Node == null) return SimpleRecResult.Failed;
				}

				if (!Scope.FinishLastCommand()) return SimpleRecResult.Failed;
				if (!Scope.Return(Node, Code)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}
	
	public class WithCommRecognizer : LanguageNode, ICommRecognizer
	{
		public static string String = "with";

		public WithCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public static bool IsNewVariableNeeded(ExpressionNode Node)
		{
			if (Node is IdExpressionNode)
			{
				return false;
			}
			else if (Node is OpExpressionNode)
			{
				var OpNode = Node as OpExpressionNode;
				var Op = OpNode.Operator;
				var Ch = OpNode.Children;

				if (Op == Operator.Index)
				{
					var T = Ch[0].Type.RealId;
					if (T is NonrefArrayType || T is PointerType)
						return false;
				}
			}

			return true;
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				var State = Scope.State;
				var Result = State.Language.CommandInnerSeparator.Separate(State, Code);
				if (!Result.Command.IsValid) return SimpleRecResult.Failed;

				var Plugin = Scope.GetPlugin();
				var NodeStr = Code.Substring(String.Length).Trim();
				var Node = Expressions.CreateExpression(NodeStr, Plugin, BeginEndMode.Begin);
				if (Node == null) return SimpleRecResult.Failed;

				if (!(Node.Type.RealId is StructuredType))
				{
					State.Messages.Add(MessageId.WithMustBeStructured, NodeStr);
					return SimpleRecResult.Failed;
				}

				var NewScope = new WithScopeNode(Scope, Result.Inner, null);
				if (!NewScope.ProcessCode()) return SimpleRecResult.Failed;

				if (!IsNewVariableNeeded(Node))
				{
					NewScope.WithNode = Node;
				}
				else
				{
					var Type = Node.Type;
					var Ref = Type is StructType;

					if (Ref)
					{
						Type = new PointerType(Scope, Type);
						var NewCh = new ExpressionNode[] { Node };
						Node = Plugin.NewNode(new OpExpressionNode(Operator.Address, NewCh, NodeStr));
						if (Node == null) return SimpleRecResult.Failed;
					}

					var Var = NewScope.CreateVariable(new CodeString(), Type);
					NewScope.DeclareIdentifier(Var);

					Node = Expressions.SetValue(Var, Node, Plugin, NodeStr, true);
					if (Node == null) return SimpleRecResult.Failed;

					NewScope.Children.Add(new Command(Scope, NodeStr, CommandType.Expression)
					{
						Expressions = new List<ExpressionNode>() { Node },
					});

					NewScope.WithNode = (ExpressionNode)new IdExpressionNode(Var, NodeStr);
					if (Ref)
					{
						NewScope.WithNode = Expressions.Indirection(Plugin, NewScope.WithNode, NodeStr);
						if (NewScope.WithNode == null || Plugin.End(ref NewScope.WithNode) == PluginResult.Failed)
							return SimpleRecResult.Failed;
					}
				}


				if (!Scope.AddCommand(NewScope)) return SimpleRecResult.Failed;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}
	
	public class VarDeclCommRecognizer : LanguageNode, ICommRecognizer
	{
		public VarDeclCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public bool StartsWithType(IdContainer Container, CodeString Str)
		{
			if (Str.Length == 0) return false;

			var SkippingHandler = Container.State.Language.GlobalHandlers;
			var T = Str.Word(ModThis: false, WordStart: true, Handlers: SkippingHandler);
			if (T.IsEqual("var") || T.IsEqual("unsafe_ref") || T.IsEqual("ref") ||
				T.IsEqual("out") || T.IsEqual("fun")) return true;

			var Options = GetIdOptions.DefaultForType;
			Options.EnableMessages = false;
			Options.Func = x =>
			{
				var Typex = x.RealId as Type;
				return Typex != null && (Typex.TypeFlags & TypeFlags.CanBeVariable) != 0;
			};

			return Container.RecognizeIdentifier(T, Options) != null;
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var Mods = Modifiers.Recognize(Scope, ref Code);
			if (Mods == null) return SimpleRecResult.Failed;

			if (Mods.Count > 0 || StartsWithType(Scope, Code))
			{
				var Mode = VarDeclConvMode.Assignment;
				if (Modifiers.Contains<ConstModifier>(Mods))
					Mode = VarDeclConvMode.Normal;

				if (!Scope.DeclareVariables(Code, Mods, Mode, GetIdMode.Function))
					return SimpleRecResult.Failed;

				Scope.DisableLastChild = true;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ExprCommRecognizer : LanguageNode, ICommRecognizer
	{
		public ExprCommRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var Node = Expressions.CreateExpression_RetValLess(Code, Scope.GetPlugin());
			if (Node == null) return SimpleRecResult.Failed;

			var Comm = new Command(Scope, Code, CommandType.Expression);
			Comm.Expressions = new List<ExpressionNode>() { Node };
			if (!Scope.AddCommand(Comm)) return SimpleRecResult.Failed;
			return SimpleRecResult.Succeeded;
		}
	}

	public class AfterDeclarationData
	{
		public CodeString AfterDeclaration;
		public bool InAfterDeclaration;
	}

	public class AfterDeclarationRecognizer : LanguageNode, ICommRecognizer
	{
		public AfterDeclarationRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public virtual SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var FS = Scope as FunctionScope;
			if (FS == null) return SimpleRecResult.Unknown;

			var FSData = FS.Data.Get<AfterDeclarationData>();
			if (FSData == null || !FSData.InAfterDeclaration)
				return SimpleRecResult.Unknown;

			return SimpleRecResult.Succeeded;
		}
	}

	public class CtorCallRecognizer : AfterDeclarationRecognizer
	{
		public CtorCallRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		Constructor GetConstructor(CodeScopeNode Scope, Identifier Class, ExpressionNode[] Ch, CodeString Code)
		{
			var List = Identifiers.SearchMember(Scope, Class, null, x => x is Constructor);
			if (List.Count == 0) throw new ApplicationException("Classes must have constructors");

			var Options = GetIdOptions.Default;
			Options.OverloadData = Expressions.GetOverloadSelectData(Ch);

			var Constructor = Identifiers.SelectIdentifier(Scope.State, List, Code, Options);
			if (Constructor == null || !Identifiers.VerifyAccess(Scope, Constructor, Code))
				return null;

			return Constructor as Constructor;
		}

		ExpressionNode GetConstructorNode(PluginRoot Plugin, Identifier Class, ExpressionNode[] Ch, CodeString Code)
		{
			var Scope = Plugin.Container as FunctionScope;
			if (Scope == null) throw new InvalidOperationException();

			var Constructor = GetConstructor(Scope, Class, Ch, Code);
			if (Constructor == null) return null;

			var ConstructorNode = Plugin.NewNode(new IdExpressionNode(Constructor, Code));
			var SelfNode = Plugin.NewNode(new IdExpressionNode(Scope.SelfVariable, Code));
			if (SelfNode == null || ConstructorNode == null) return null;

			var MemberCh = new ExpressionNode[] { SelfNode, ConstructorNode };
			return Plugin.NewNode(new OpExpressionNode(Operator.Member, MemberCh, Code));
		}

		ExpressionNode GetConstructorCaller(PluginRoot Plugin, Identifier Class, ExpressionNode[] Ch, CodeString Code)
		{
			Ch[0] = GetConstructorNode(Plugin, Class, Ch, Code);
			if (Ch[0] == null) return null;

			var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
			TypeMngrPlugin.Flags |= TypeMngrPluginFlags.AllowConstructorCalls;

			var RetValue = Plugin.NewNode(new OpExpressionNode(Operator.Call, Ch, Code));
			if (RetValue == null) return null;

			return Plugin.End(RetValue);
		}

		public override SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var BaseRes = base.Recognize(Scope, Code);
			if (BaseRes != SimpleRecResult.Succeeded) return BaseRes;

			var State = Scope.State;
			var Language = State.Language;
			var CodeProcessor = Language.CodeProcessor;
			var FScope = Scope as FunctionScope;

			if (!(FScope.Function is Constructor))
				return SimpleRecResult.Unknown;

			if (Code.StartsWith(CodeProcessor.SelfName, new IdCharCheck(true)) ||
				Code.StartsWith(CodeProcessor.BaseName, new IdCharCheck(true)))
			{
				CodeString Function, Parameters;
                var CallParamRec = Language.Root.GetObject<ICallParamRecognizer>();
				if (!CallParamRec.GetParameters(State, Code, out Function, out Parameters))
					return SimpleRecResult.Failed;

				var StructuredScope = Scope.Parent as StructuredScope;
				var Type = StructuredScope.StructuredType as ClassType;

				if (Type == null)
				{
					State.Messages.Add(MessageId.NotExpected, Code);
					return SimpleRecResult.Failed;
				}

				var TypeToConstruct = (Identifier)null;
				if (Function.IsEqual(CodeProcessor.SelfName))
				{
					TypeToConstruct = Type;
				}
				else if (Function.IsEqual(CodeProcessor.BaseName))
				{
					if (Type.BaseStructures.Length == 0)
					{
						State.Messages.Add(MessageId.UnknownId, Function);
						return SimpleRecResult.Failed;
					}
					else if (Type.BaseStructures.Length > 1)
					{
						State.Messages.Add(MessageId.AmbiguousReference, Function);
						return SimpleRecResult.Failed;
					}

					TypeToConstruct = Type.BaseStructures[0].Base;
				}
				else
				{
					return SimpleRecResult.Unknown;
				}

				var Plugin = Scope.GetPlugin();
				if (!Plugin.Begin()) return SimpleRecResult.Failed;

				var Ch = CallParamRec.ProcessParameters(Plugin, new CodeString(), Parameters);
				if (Ch == null) return SimpleRecResult.Failed;

				if (!Expressions.FinishParameters(Plugin, Ch))
					return SimpleRecResult.Failed;

				var CtorCaller = GetConstructorCaller(Plugin, TypeToConstruct, Ch, Code);
				if (CtorCaller == null) return SimpleRecResult.Failed;

				FScope.ConstructorCall = CtorCaller;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ExtraStorageRecognizer : AfterDeclarationRecognizer
	{
		public static string String = "_set_extra_storage";

		public ExtraStorageRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public override SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var BaseRes = base.Recognize(Scope, Code);
			if (BaseRes != SimpleRecResult.Succeeded) return BaseRes;

			var State = Scope.State;
			var Language = State.Language;
			var FScope = Scope as FunctionScope;

			if (!(FScope.Function is Constructor))
				return SimpleRecResult.Unknown;

			if (Code.StartsWith(String, new IdCharCheck(true)))
			{
				CodeString Function, Parameters;
                var CallParamRec = Language.Root.GetObject<ICallParamRecognizer>();
				if (!CallParamRec.GetParameters(State, Code, out Function, out Parameters))
					return SimpleRecResult.Failed;

				if (!Function.IsEqual(String))
					return SimpleRecResult.Unknown;

				var Plugin = Scope.GetPlugin();
				if (!Plugin.Begin()) return SimpleRecResult.Failed;

				var Ch = CallParamRec.ProcessParameters(Plugin, Function, Parameters);
				if (Ch == null) return SimpleRecResult.Failed;

				if (Ch.Length != 2)
				{
					State.Messages.Add(MessageId.ParamCount, Code);
					return SimpleRecResult.Failed;
				}

				var StructureScope = Scope.Parent as StructuredScope;
				var Structure = StructureScope.StructuredType;

				Ch[0] = Plugin.NewNode(Constants.GetIntValue(Scope, Structure.InstanceSize, Code, true));
				var Node = Plugin.NewNode(new OpExpressionNode(Operator.Add, Ch, Code));
				if (Node == null) return SimpleRecResult.Failed;

				FScope.ObjectSize = Node;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class NonAfterDeclBlocker : AfterDeclarationRecognizer
	{
		public NonAfterDeclBlocker(LanguageNode Parent)
			: base(Parent)
		{
		}

		public override SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var BaseRes = base.Recognize(Scope, Code);
			if (BaseRes == SimpleRecResult.Succeeded)
			{
				Scope.State.Messages.Add(MessageId.NotExpected, Code);
				return SimpleRecResult.Failed;
			}

			return BaseRes;
		}
	}

	public class LineSplittingRecognizer : LanguageNode, ICommRecognizer
	{
		public LineSplittingRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code)
		{
			var SkippingHandlers = Scope.State.Language.GlobalHandlers;
			if (Code.Find(';', Handlers: SkippingHandlers) != -1)
			{
				var Spl = Code.Split(';', StringSplitOptions.RemoveEmptyEntries, true, SkippingHandlers);
				var RetValue = true;

				for (var i = 0; i < Spl.Count; i++)
					if (!Scope.RecognizeCommand(Spl[i])) RetValue = false;

				if (!RetValue) return SimpleRecResult.Failed;
				else return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}
}

