using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zinnia.Recognizers
{
	public class TestScopeRecognizer : LanguageNode, IExprRecognizer
	{
		public TestScopeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "testscope" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Out)
		{
			var Result = Code.StartsWith(Operators, Skip, new IdCharCheck(true));
			if (Result.Position != -1)
			{
				var State = Plugin.State;
				var Right = Code.Substring(Result.String.Length).Trim();
				Right = Right.TrimOneBracket(SkippingHandlers);

				if (Right.Length == 0)
				{
					State.Messages.Add(MessageId.DeficientExpr, Code);
					return ExprRecResult.Failed;
				}

				var Scope = new CodeScopeNode(Plugin.Container, Right);
				if (!Scope.ProcessCode()) return ExprRecResult.Failed;

				var List = Scope.GetContainerId("retvar", x => x is LocalVariable);
				var RetVar = Identifiers.SelectIdentifier(State, List, Code) as LocalVariable;
				if (RetVar == null) return ExprRecResult.Failed;

				Out = new ScopeExpressionNode(Scope, Code)
				{
					ReturnVar = RetVar,
				};

				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class CheckedUncExprRecognizer : LanguageNode, IExprRecognizer
	{
		public CheckedUncExprRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "checked", "unchecked" };
			NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Out)
		{
			var Result = Code.StartsWith(Operators, Skip, new IdCharCheck(true));
			if (Result.Position != -1)
			{
				var State = Plugin.State;
				var Right = Code.Substring(Result.String.Length).Trim();
				Right = Right.TrimOneBracket(SkippingHandlers);

				if (Right.Length == 0)
				{
					State.Messages.Add(MessageId.DeficientExpr, Code);
					return ExprRecResult.Failed;
				}

				var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
				if (TypeMngrPlugin == null) return ExprRecResult.Unknown;

				var OldCheckingMode = TypeMngrPlugin.CheckingMode;
				if (Result.Index == 0) TypeMngrPlugin.CheckingMode = CheckingMode.Checked;
				else if (Result.Index == 1) TypeMngrPlugin.CheckingMode = CheckingMode.Unchecked;
				else throw new ApplicationException();

				Out = Expressions.Recognize(Right, Plugin);
				TypeMngrPlugin.CheckingMode = OldCheckingMode;

				if (Out == null) return ExprRecResult.Failed;
				else return ExprRecResult.Ready;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class SimpleArgRecognizer : LanguageNode, IParameterRecognizer
	{
		public SimpleArgRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "," };
			NewLineLeft = NewLineRight = Operators;
		}

		public CodeString[] SplitToParameters(CompilerState State, CodeString Self, bool EnableMessages = true)
		{
			return RecognizerHelper.SplitToParameters(State, Self, ',', EnableMessages);
		}
	}

	public class GenericRecognizer : LanguageNode, IGenericRecognizer
	{
		public GenericRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = new string[] { "<" };
			NewLineLeft = new string[] { "<", ">" };
		}

		public SimpleRecResult GetGenericParams(CompilerState State, ref CodeString Code, 
			 out CodeString[] Out, bool EnableMessages = true)
		{
			Out = null;
			if (Code.Length > 0 && Code[Code.Length - 1] == '>')
			{
				var BracketPos = Code.GetBracketPos(State, true);
				if (BracketPos == -1) return SimpleRecResult.Failed;

				var ParamStart = BracketPos + 1;
				var StrParams = Code.TrimmedSubstring(State, ParamStart, Code.Length - ParamStart - 1);
				if (!StrParams.IsValid) return SimpleRecResult.Failed;

				Out = RecognizerHelper.GetParamList(State, StrParams);
				if (Out == null) return SimpleRecResult.Failed;

				Code = Code.Substring(0, BracketPos).Trim();
			}

			return SimpleRecResult.Unknown;
		}

        public override void Init(LanguageInitData InitData)
		{
			base.Init(InitData);

            foreach (var e in Language.Root.GetObjects<RelEquRecognizer>())
				e.RecFlags |= RelEquRecognizerFlags.DisableOpposed;

			var ListDisableFind = new List<string>();
			var ListDisableSkip = new List<string>();
            foreach (var e in Language.Root.GetObjects<LogicalRecognizer>())
			{
				if (e.Operators != null) ListDisableFind.AddRange(e.Operators);
				if (e.Skip != null) ListDisableFind.AddRange(e.Skip);
			}

			var DisableFind = Helper.ToArrayWithoutSame(ListDisableFind);
			var DisableSkip = Helper.GetSkipList(DisableFind, ListDisableSkip);

            foreach (var e in Language.Root.GetObjects<BracketRecognizer>())
			{
				e.GenericBracketSkipOptions = new GenericBracketSkipOptions()
				{
					Enabled = true,
					DisableFind = DisableFind,
					DisableSkip = DisableSkip,
					SkippingHandlers = SkippingHandlers,
				};
			}
		}
	}

	public class BracketGroupRecognizer : LanguageNode, IGroupRecognizer
	{
		public char BracketLeft = '{';
		public char BracketRight = '}';

		public BracketGroupRecognizer(LanguageNode Parent, char BracketLeft = '{', char BracketRight = '}')
			: base(Parent)
		{
		}

		public static NodeGroup GetGroups(PluginRoot Plugin, CodeString Code, char BracketLeft = '{', char BracketRight = '}')
		{
			var Ret = new NodeGroup(Code);
			var State = Plugin.State;

			var List = RecognizerHelper.SplitToParameters(State, Code, ',');
			if (List == null) return null;

			for (var i = 0; i < List.Length; i++)
			{
				var Param = List[i];
				if (Param.Length > 1 && Param[0] == BracketLeft && Param[Param.Length] == BracketLeft)
				{
					Param = Param.Substring(1, Param.Length - 1).Trim();
					Ret.Children.Add(Expressions.GetGroups(Plugin, Param));
				}
				else
				{
					var Node = Expressions.Recognize(Param, Plugin);
					if (Node == null) return null;

					Ret.Children.Add(Node);
				}
			}

			return Ret;
		}

		public NodeGroup GetGroups(PluginRoot Plugin, CodeString Code)
		{
			return GetGroups(Plugin, Code, BracketLeft, BracketRight);
		}
	}

	public struct GenericBracketSkipOptions
	{
		public bool Enabled;
		public string[] DisableFind;
		public string[] DisableSkip;
		public IList<IResultSkippingHandler> SkippingHandlers;
	}

	public enum GenericBracketSkipMode : byte
	{
		Disabled,
		Enabled,
		IfNoLogicalOp,
	}

	public class BracketRecognizer : LanguageNode, IExprRecognizer, IIdRecognizer, IResultSkippingHandler
	{
		public GenericBracketSkipOptions GenericBracketSkipOptions;

		public BracketRecognizer(LanguageNode Parent, GenericBracketSkipOptions GenericBracketSkipMode = new GenericBracketSkipOptions())
			: base(Parent)
		{
			this.GenericBracketSkipOptions = GenericBracketSkipMode;
		}

		static bool Check(CompilerState State, CodeString Code)
		{
			if (Code[0] == '(' && Code.GetBracketPos(State)  == -1)
				return false;

            if (Code[0] == ')')
            {
                State.Messages.Add(MessageId.ZNumErr, Code.Substring(0, 1));
                return false;
            }

			var P = Code.Length - 1;
            if (Code[P] == '(')
            {
                State.Messages.Add(MessageId.ZNumErr, Code.Substring(P));
                return false;
            }

			if (Code[P] == ')' && Code.GetBracketPos(State, true) == -1)
				return false;

			return true;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (!Check(Plugin.State, Code)) return ExprRecResult.Failed;

			var NewCode = Code.TrimBrackets(Plugin.State);
			if (!NewCode.IsValid) return ExprRecResult.Failed;
			if (NewCode.Length == Code.Length) return ExprRecResult.Unknown;

			Ret = Expressions.Recognize(NewCode, Plugin, true);
			return Ret == null ? ExprRecResult.Failed : ExprRecResult.Ready;
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			if (!Check(Container.State, Code)) return SimpleRecResult.Failed;

			var NewCode = Code.TrimBrackets(Container.State);
			if (!NewCode.IsValid) return SimpleRecResult.Failed;
			if (NewCode.Length == Code.Length) return SimpleRecResult.Unknown;

			Ret = Identifiers.Recognize(Container, NewCode, Options);
			return Ret == null ? SimpleRecResult.Failed : SimpleRecResult.Succeeded;
		}

		SkippingHandlerResult SkipBracket(ref ResultSkippingManager RSM)
		{
			if (RSM.Back)
			{
				var NewString = RSM.String.Substring(0, RSM.Current + 1);
				var Pos = NewString.GetBracketPos(true, RSM.SkippingHandlers);
				if (Pos != -1) return new SkippingHandlerResult(Pos);
			}
			else
			{
				var NewString = RSM.String.Substring(RSM.Current);
				var Pos = NewString.GetBracketPos(false, RSM.SkippingHandlers);
				if (Pos != -1) return new SkippingHandlerResult(Pos + RSM.Current);
			}

			return new SkippingHandlerResult(-1);
		}

		public SkippingHandlerResult SkipResult(ref ResultSkippingManager RSM)
		{
			if (!RSM.DoNotSkipBrackets)
			{
				var Char = RSM.CurrentChar;
				if (GenericBracketSkipOptions.Enabled)
				{
					if ((Char == '<' && !RSM.Back) || (Char == '>' && RSM.Back))
					{
						var Result = SkipBracket(ref RSM);
						if (Result.Index == -1 || GenericBracketSkipOptions.DisableFind == null)
							return Result;

						var Substring = RSM.String.SubstringFromTo(RSM.Current + 1, Result.Index - 1);
						var FindRes = Substring.Find(GenericBracketSkipOptions.DisableFind, 
							GenericBracketSkipOptions.DisableSkip, false, new IdCharCheck(true),
							GenericBracketSkipOptions.SkippingHandlers);

						if (FindRes.Position == -1)
							return Result;
					}
				}

				if (Helper.GetBracket(Char, RSM.Back))
					return SkipBracket(ref RSM);
			}

			return new SkippingHandlerResult(-1);
		}
	}

	public class ExprIdRecognizer : LanguageNode, IExprRecognizer
	{
		public ExprIdRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.IsValidIdentifierName)
			{
				Ret = new StrExpressionNode(Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class RetValLessRecognizer : LanguageNode, IRetValLessRecognizer, ICallParamRecognizer
	{
		public IExprRecognizer[] RunBefore;
		CallRecognizer CallRecognizer;

		public RetValLessRecognizer(LanguageNode Parent, IExprRecognizer[] RunBefore = null)
			: base(Parent)
		{
			this.RunBefore = RunBefore;
		}

        public override void Init(LanguageInitData InitData)
		{
            CallRecognizer = Language.Root.GetObject<CallRecognizer>();
			base.Init(InitData);
		}

		public static int FindParamPosition(CompilerState State, StringSlice Code)
		{
			var Ok = false;
			var Handlers = State.Language.GlobalHandlers;
			var RSM = new ResultSkippingManager(Handlers, Code);

			var Language = State.Language;
			var LangRoot = Language.Root;
			var CastRecognizer = Language.Root.GetObject<CastRecognizer>(false);

			while (RSM.Loop())
			{
				var Pos = RSM.Current;
				var Chr = Code[Pos];
				if (!Helper.IsIdChar(Chr))
				{
					if (!Ok) continue;

					if (Chr == '(')
					{
						var Brackets = Code.Substring(Pos);
						if (CastRecognizer != null && CastRecognizer.IsCastString(State, Brackets))
							return Pos;

						if (Brackets.GetBracketPos(false, Handlers) == Brackets.Length - 1)
							return Pos;
					}
					else if (char.IsWhiteSpace(Chr))
					{
						// ambiguous: Print (Obj is object).ToString()
						var After = Code.Substring(Pos + 1).Trim();
						var AfterChr = After.Length > 0 ? After[0] : '\0';
						if (Helper.IsIdChar(AfterChr) || AfterChr == '(' || AfterChr == '[')
							return Pos;
					}

                    if (Code.SubstringEquals(Pos, LangRoot.OnlyRight, LangRoot.OnlyRightSkip,
                        false, new IdCharCheck(true)).Position != -1)
                    {
                        return Pos;
                    }

                    for (var i = 0; i < Language.ExprRecognizers.Length; i++)
					{
						var Rec = Language.ExprRecognizers[i] as SkippedBetweenRecognizer;
						if (Rec != null && Code.SubstringEquals(Pos, Rec.Operators, Rec.Skip).Index != -1)
							return Pos;
					}
				}
				else
				{
					Ok = true;
				}
			}

			return -1;
		}

		public bool GetParameters(CompilerState State, CodeString Code, out CodeString Function, out CodeString Parameters)
		{
			var Pos = FindParamPosition(State, Code.String);
			if (Pos != -1)
			{
				Function = Code.Substring(0, Pos).Trim();
				var Handlers = State.Language.GlobalHandlers;
				Parameters = Code.Substring(Pos).TrimOneBracket(Handlers);
			}
			else
			{
				Function = Code;
				Parameters = Code.Substring(Code.Length);
			}

			return true;
		}

		public ExpressionNode[] ProcessParameters(PluginRoot Plugin, CodeString Function, CodeString[] Parameters)
		{
			return CallRecognizer.ProcessParameters(Plugin, Function, Parameters);
		}

		public ExpressionNode[] ProcessParameters(PluginRoot Plugin, CodeString Function, CodeString Parameters)
		{
			return CallRecognizer.ProcessParameters(Plugin, Function, Parameters);
		}

		public ExpressionNode CreateFuncCallNode(PluginRoot Plugin, CodeString Code)
		{
			CodeString Function, Parameters;
			if (!GetParameters(Plugin.State, Code, out Function, out Parameters))
				return null;

			var Ch = CallRecognizer.ProcessParameters(Plugin, Function, Parameters);
			if (Ch == null) return null;

			return Plugin.NewNode(new OpExpressionNode(Operator.Call, Ch, Code));
		}

		public ExpressionNode Recognize(CodeString Code, PluginRoot Plugin)
		{
			ExpressionNode Out;
			var Res = Expressions.Recognize(Code, Plugin, RunBefore, out Out);
			if (Res == SimpleRecResult.Unknown) return CreateFuncCallNode(Plugin, Code);
			else return Res == SimpleRecResult.Succeeded ? Out : null;
		}
	}
}