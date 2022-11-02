using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia.Base;
using Zinnia.Languages.Zinnia;

namespace Zinnia.Recognizers
{
	public class IsAsToRecognizer : LanguageNode, IExprRecognizer
	{
		public IsAsToRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "is", "as", "to" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Out)
		{
			var Result = RecognizerHelper.Find(this, Plugin.State, Code.String);
			if (Result.Position != -1)
			{
				var State = Plugin.State;
				var LeftStr = Code.TrimmedSubstring(State, 0, Result.Position);
				var RightStr = Code.TrimmedSubstring(State, Result.NextChar);
				if (!LeftStr.IsValid || !RightStr.IsValid) return ExprRecResult.Failed;

				var TypeOptions = GetIdOptions.DefaultForType;
				var Type = Identifiers.Recognize(Plugin.Container, RightStr);
				if (Type == null) return ExprRecResult.Failed;

				var Left = Expressions.Recognize(LeftStr, Plugin, true);
				var Right = Plugin.NewNode(new IdExpressionNode(Type, RightStr));
				if (Left == null || Right == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.Is;
				else if (Result.Index == 1) Op = Operator.As;
				else if (Result.Index == 2) Op = Operator.Cast;
				else throw new ApplicationException();

				var Ch = new ExpressionNode[] { Left, Right };
				Out = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class CastRecognizer : LanguageNode, IExprRecognizer, IFind2Handler
	{
		public CastRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "(" };
		}

		public bool IsCastString(CompilerState State, StringSlice String)
		{
			if (String.StartsWith(Operators, Skip).Position != -1)
			{
				var ZPos = String.GetBracketPos(false, SkippingHandlers);
				var Right = String.Substring(ZPos + 1).Trim();
				if (ZPos == -1 || Right.Length == 0) return false;

				var Lang = State.Language;
                var Res = Right.StartsWith(Lang.Root.NewLineLeft, Lang.Root.OnlyRight, new IdCharCheck(true));
				return Res.Position == -1 || (Right.Length > 0 && Right[0] == '(');
			}

			return false;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (IsCastString(Plugin.State, Code.String))
			{
				var State = Plugin.State;
				var Container = Plugin.Container;
				var Pos = Code.GetBracketPos(State);
				if (Pos == -1) return ExprRecResult.Failed;

				var TypeName = Code.TrimmedSubstring(State, 1, Pos - 1);
				var ChildStr = Code.TrimmedSubstring(State, Pos + 1);
				if (!TypeName.IsValid || !ChildStr.IsValid) return ExprRecResult.Failed;

				var Type = Container.RecognizeIdentifier(TypeName, GetIdOptions.DefaultForType);
                if (Type == null) return ExprRecResult.Failed;

				var Child = Expressions.Recognize(ChildStr, Plugin, true);
                var TypeNode = Plugin.NewNode(new IdExpressionNode(Type, TypeName));
                if (Child == null || TypeNode == null) return ExprRecResult.Failed;

                var Ch = new ExpressionNode[] { Child, TypeNode };
                Ret = new OpExpressionNode(Operator.Cast, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}

		public bool IsAcceptable(IdContainer Container, StringSlice String)
		{
			var BracketPos = String.Length - 1;
			if (BracketPos > 0 && String[BracketPos] == ')')
			{
				var Handlers = Container.State.Language.GlobalHandlers;
				var ZPos = String.GetBracketPos(true, Handlers);
				if (ZPos == -1) return true;

				var Left = String.Substring(0, ZPos);
				if (RecognizerHelper.IsAcceptable(Container, Left))
					return true;

				var SubStr = new CodeString(String.Substring(ZPos + 1, BracketPos - ZPos - 1));
				var Options = GetIdOptions.DefaultForType;
				Options.EnableMessages = false;
				return Container.RecognizeIdentifier(SubStr, Options) == null;
			}

			return true;
		}
	}

	public class RangeRecognizer : LanguageNode, IExprRecognizer
	{
		public RangeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "...", ".." };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Out)
		{
			var Result = RecognizerHelper.Find(this, Plugin.State, Code.String);
			if (Result.Position != -1)
			{
				var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (Ch == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.RangeTo;
				else if (Result.Index == 1) Op = Operator.RangeUntil;
				else throw new ApplicationException();

				Out = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class VarDeclRecignizer : LanguageNode, IExprRecognizer
	{
		public VarDeclRecignizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Out)
		{
			var State = Plugin.State;
			var AutoType = State.GlobalContainer.CommonIds.Auto;

			if (Code.StartsWith(AutoType.Name.ToString(), new IdCharCheck(true)))
			{
				var VarDeclRec = State.Language.VarDeclRecognizer;
				var Decls = new VarDeclarationList();
				if (!VarDeclRec.Recognize(Plugin.Container, Code, true, Decls))
					return ExprRecResult.Failed;

				var Vars = Decls.ToVariables(Plugin, BeginEndMode.None,
					VarDeclConvMode.Assignment, true, true, true);

				if (Vars == null || Vars.Contains(null)) 
					return ExprRecResult.Failed;

                if (Vars.Length == 1)
				{
					Out = new IdExpressionNode(Vars[0], Decls[0].Declaration);
					return ExprRecResult.Succeeded;
				}

                var RetCh = new ExpressionNode[Vars.Length];
				var HaveInitVals = true;
                for (var i = 0; i < Vars.Length; i++)
				{
					var e = Vars[i];
					if (i == 0)
					{
						HaveInitVals = e.InitValue != null;
					}
					else if ((e.InitValue != null) != HaveInitVals)
					{
						State.Messages.Add(MessageId.ExprVarDeclInitVal, e.Name);
						return ExprRecResult.Failed;
					}

					var Node = e.InitValue;
					if (Node == null)
					{
						Node = Plugin.NewNode(new IdExpressionNode(e, Decls[i].Declaration));
						if (Node == null) return ExprRecResult.Failed;
					}

					RetCh[i] = Node;
				}

				Out = new OpExpressionNode(Operator.Tuple, RetCh, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class RefRecognizer : LanguageNode, IExprRecognizer
	{
		public RefRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "unsafe_ref", "ref", "out" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Result = Code.StartsWith(Operators, Skip, IdCharCheck: new IdCharCheck(true));
			if (Result.Index != -1)
			{
				var SubCode = Code.TrimmedSubstring(Plugin.State, Result.String.Length);
				if (!SubCode.IsValid) return ExprRecResult.Failed;

				var Child = Expressions.Recognize(SubCode, Plugin, true);
				if (Child == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.Reference_Unsafe;
				else if (Result.Index == 1) Op = Operator.Reference_IdMustBeAssigned;
				else if (Result.Index == 2) Op = Operator.Reference_IdGetsAssigned;
				else throw new ApplicationException();

				var Ch = new ExpressionNode[] { Child };
				Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class ZinniaNewRecognizer : LanguageNode, IExprRecognizer
	{
		public CallRecognizer CallRecognizer;
		public bool EnableWithoutBracket = true;

		public ZinniaNewRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

        public override void Init(LanguageInitData InitData)
		{
            CallRecognizer = Language.Root.GetObject<CallRecognizer>();
			base.Init(InitData);
		}

		public NodeGroup GetGroups(PluginRoot Plugin, CodeString Code, int DimensionCount)
		{
			var State = Plugin.State;
			var Container = Plugin.Container;
			var Ret = new NodeGroup(Code);

			var Result = ZinniaHelper.ForEachLine(State, Code, Line =>
			{
				var Rec = State.Language.ParameterRecognizer;
				var List = Rec.SplitToParameters(State, Line);
				if (List == null) return false;

				var LocalGroup = (NodeGroup)null;
				if (DimensionCount >= 2)
				{
					LocalGroup = new NodeGroup(Line);
					Ret.Children.Add(LocalGroup);
				}

				var RetValue = true;
				for (var i = 0; i < List.Length; i++)
				{
					var String = List[i];
					object Object;

					if (String.Length > 1 && String[0] == '(' && String[String.Length - 1] == ')')
					{
						var SubString = String.TrimmedSubstring(State, 1, String.Length - 2);
						if (!SubString.IsValid) { RetValue = false; continue; }

						var NewGroup = BracketGroupRecognizer.GetGroups(Plugin, SubString, '(', ')');
						if (NewGroup == null) { RetValue = false; continue; }

						if (NewGroup.MaxDepth + 1 == DimensionCount)
						{
							LocalGroup.Children.AddRange(NewGroup.Children);
							continue;
						}

						Object = NewGroup;
					}
					else
					{
						Object = Expressions.Recognize(String, Plugin, true);
						if (Object == null) { RetValue = false; continue; }
					}

					if (LocalGroup == null) Ret.Children.Add(Object);
					else LocalGroup.Children.Add(Object);
				}

				return RetValue;
			});

			return Result ? Ret : null;
		}

		public bool CheckGroups_DimensionsNotSpecified(PluginRoot Plugin,
			NodeGroup Group, ExpressionNode[] Ch, CodeString Code, int DimensionCount)
		{
			var State = Plugin.State;
			var Global = State.GlobalContainer;
			var Container = Plugin.Container;

			var Lengths = Expressions.GetArrayLengths(State, Group);
			if (Lengths == null) return false;

			if (DimensionCount != Lengths.Length)
			{
				State.Messages.Add(MessageId.ArrayInitializerLength, Code);
				return false;
			}

			var Succeeded = true;
			for (var i = 0; i < Lengths.Length; i++)
			{
				var Node = Plugin.NewNode(Constants.GetIntValue(Container, Lengths[i], Code));
				if (Node == null) { Succeeded = false; continue; }

				Ch[i + 1] = Node;
			}

			return Succeeded;
		}

		public bool CheckGroups_DimensionsSpecified(PluginRoot Plugin, 
			NodeGroup Group, ExpressionNode[] Ch, CodeString Code)
		{
			var State = Plugin.State;
			var Global = State.GlobalContainer;

			var Lengths = Expressions.GetArrayLengths(State, Group);
			if (Lengths == null) return false;

			if (Ch.Length - 1 != Lengths.Length)
			{
				State.Messages.Add(MessageId.ArrayInitializerLength, Code);
				return false;
			}

			var RetValue = true;
			for (var i = 0; i < Lengths.Length; i++)
			{
				var Node = Ch[i + 1];
				if (!Node.Type.IsEquivalent(Global.CommonIds.Int32))
				{
					var TypeMngrPlugin = Plugin.GetPlugin<TypeMngrPlugin>();
					if (TypeMngrPlugin == null)
					{
						State.Messages.Add(MessageId.ImplicitlyCast, Node.Code);
						RetValue = false;
						continue;
					}

					Node = TypeMngrPlugin.Convert(Node, Global.CommonIds.Int32, Node.Code);
					if (Node == null) { RetValue = false; continue; }

					Ch[i + 1] = Node;
				}

				var ConstNode = Node as ConstExpressionNode;
				if (ConstNode == null)
				{
					State.Messages.Add(MessageId.MustBeConst, Node.Code);
					RetValue = false;
					continue;
				}

				if (Lengths[i] != ConstNode.Integer)
				{
					State.Messages.Add(MessageId.ArrayInitializerLength, Node.Code);
					RetValue = false;
					continue;
				}
			}

			return RetValue;
		}

		private ExpressionNode CreateIndexNode(PluginRoot Plugin, Identifier Id, List<int> Indices, CodeString Code)
		{
			var Container = Plugin.Container;

			var Ch = new ExpressionNode[Indices.Count + 1];
			Ch[0] = Plugin.NewNode(new IdExpressionNode(Id, Code));
			if (Ch[0] == null) return null;

			for (var i = 0; i < Indices.Count; i++)
			{
				Ch[i + 1] = Plugin.NewNode(Constants.GetIntValue(Container, Indices[i], Code));
				if (Ch[i + 1] == null) return null;
			}

			return Plugin.NewNode(new OpExpressionNode(Operator.Index, Ch, Code));
		}

		public void CreateInitValue_ForArray(NodeGroup Group, List<ExpressionNode> Children, 
			List<ArrayIndices> Indices, List<int> CurrentIndices = null)
		{
			if (CurrentIndices == null)
				CurrentIndices = new List<int>();

			for (var i = 0; i < Group.Children.Count; i++)
			{
				CurrentIndices.Add(i);

				var Object = Group.Children[i];
				if (Object is ExpressionNode)
				{
					Children.Add(Object as ExpressionNode);
					Indices.Add(new ArrayIndices(CurrentIndices.ToArray()));
				}
				else if (Object is NodeGroup)
				{
					CreateInitValue_ForArray(Object as NodeGroup, Children, Indices, CurrentIndices);
				}
				else
				{
					throw new ApplicationException();
				}

				CurrentIndices.RemoveAt(CurrentIndices.Count - 1);
			}
		}

		public bool CreateInitValue_ForArray(PluginRoot Plugin, NodeGroup Group,
			ExpressionNode Node, CodeString Code)
		{
			var Children = new List<ExpressionNode>();
			var Indices = new List<ArrayIndices>();
			CreateInitValue_ForArray(Group, Children, Indices);

			var LNode = Plugin.NewNode(new ArrayInitNode(Children.ToArray(), Indices.ToArray(), Code));
			if (LNode == null) return false;

			Node.LinkedNodes.Add(new LinkedExprNode(LNode, LinkedNodeFlags.NotRemovable));
			return true;
		}

		public bool CreateInitValue_ForObject(PluginRoot Plugin, ExpressionNode Node, CodeString Code)
		{
			var State = Plugin.State;
			var Container = Plugin.Container;
			var SkippingHandlers = State.Language.GlobalHandlers;
			var Refs = new List<IdentifierReference>();
			var Nodes = new List<ExpressionNode>();

			var Result = ZinniaHelper.ForEachLine(State, Code, Line =>
			{
				var Rec = State.Language.ParameterRecognizer;
				var List = Rec.SplitToParameters(State, Line);
				if (List == null) return false;
				
				var RetValue = true;
				for (var i = 0; i < List.Length; i++)
				{
					var String = List[i];
					var Pos = String.Find('=', false, SkippingHandlers);
					if (Pos == -1) 
					{
						State.Messages.Add(MessageId.NotExpected, String);
						RetValue = false;
						continue;
					}
					
					var Left = String.TrimmedSubstring(State, 0, Pos);
					var Right = String.TrimmedSubstring(State, Pos + 1);
					if (!Left.IsValid || !Right.IsValid) { RetValue = false; continue; }

					if (!Left.IsValidIdentifierName)
					{
						State.Messages.Add(MessageId.NotValidName, Left);
						RetValue = false;
						continue;
					}

					var Value = Expressions.Recognize(Right, Plugin, true);
					if (Node == null) return false;

					Refs.Add(new IdentifierReference(Left));
					Nodes.Add(Value);
				}

				return RetValue;
			});

			var LNode = Plugin.NewNode(new ObjectInitNode(Refs.ToArray(), Nodes.ToArray(), Code));
			if (LNode == null) return false;

			Node.LinkedNodes.Add(new LinkedExprNode(LNode, LinkedNodeFlags.NotRemovable));
			return Result;
		}

		public int GetOnlyDimensions(CodeString Code)
		{
			var Ret = 1;
			for (var i = 0; i < Code.Length; i++)
			{
				var Char = Code[i];
				if (!char.IsWhiteSpace(Char) && Char != ',') return -1;
				else if (Char == ',') Ret++;
			}

			return Ret;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.StartsWith("new", new IdCharCheck(true)))
			{
				var State = Plugin.State;
				var Container = Plugin.Container;
				var Global = State.GlobalContainer;

				var EndingResult = State.Language.CommandInnerSeparator.Separate
					(State, Code, CommandInnerSeparatorFlags.InnerIsOptional);

				if (!EndingResult.Command.IsValid) return ExprRecResult.Failed;

				var SubCode = EndingResult.Command.Substring(3).Trim();
				var LChar = SubCode.Length == 0 ? '\0' : SubCode[SubCode.Length - 1];

				var Ch = (ExpressionNode[])null;
				var Op = Operator.Unknown;
				var StrParams = new CodeString();
				var DimensionCount = -1;
				var DimensionsSpecified = true;

				if (LChar == ']' || LChar == ')')
				{
					if (LChar == ']') Op = Operator.NewArray;
					else if (LChar == ')') Op = Operator.NewObject;
					else throw new ApplicationException();

					var ZPos = SubCode.GetBracketPos(State, Back: true);
					if (ZPos == -1) return ExprRecResult.Failed;

					StrParams = SubCode.Substring(ZPos + 1, SubCode.Length - ZPos - 2).Trim();
					SubCode = SubCode.Substring(0, ZPos).Trim();

					DimensionCount = GetOnlyDimensions(StrParams);
					if (Op == Operator.NewArray && DimensionCount != -1)
					{
						DimensionsSpecified = false;
						Ch = new ExpressionNode[DimensionCount + 1];
					}
					else
					{
						Ch = CallRecognizer.ProcessParameters(Plugin, new CodeString(), StrParams);
						if (Ch == null) return ExprRecResult.Failed;
					}

					if (SubCode.Length == 0)
					{
						var Auto = Container.GlobalContainer.CommonIds.Auto;
						Ch[0] = Plugin.NewNode(new IdExpressionNode(Auto, Code));
						if (Ch[0] == null) return ExprRecResult.Failed;
					}
					else
					{
						Ch[0] = Plugin.NewNode(new StrExpressionNode(SubCode));
						if (Ch[0] == null) return ExprRecResult.Failed;
					}
				}
				else if (SubCode.Length == 0)
				{
					if (!EnableWithoutBracket)
					{
						State.Messages.Add(MessageId.DeficientExpr, Code);
						return ExprRecResult.Failed;
					}

					var Auto = Container.GlobalContainer.CommonIds.Auto;
					Op = Operator.NewObject;

					Ch = new ExpressionNode[1];
					Ch[0] = Plugin.NewNode(new IdExpressionNode(Auto, Code));
					if (Ch[0] == null) return ExprRecResult.Failed;
				}
				else
				{
					if (!EnableWithoutBracket)
					{
						State.Messages.Add(MessageId.NotExpected, SubCode);
						return ExprRecResult.Failed;
					}

					Op = Operator.NewObject;
					Ch = new ExpressionNode[1];
					Ch[0] = Plugin.NewNode(new StrExpressionNode(SubCode));
					if (Ch[0] == null) return ExprRecResult.Failed;
				}

				Ret = new OpExpressionNode(Op, Ch, Code);

				if (EndingResult.Inner.IsValid)
				{
					if (Op == Operator.NewArray)
					{
						var Groups = GetGroups(Plugin, EndingResult.Inner, Ch.Length - 1);
						if (Groups == null) return ExprRecResult.Failed;

						if (DimensionsSpecified)
						{
							if (!CheckGroups_DimensionsSpecified(Plugin, Groups, Ch, Code))
								return ExprRecResult.Failed;
						}
						else
						{
							if (!CheckGroups_DimensionsNotSpecified(Plugin, Groups, Ch, Code, DimensionCount))
								return ExprRecResult.Failed;
						}

						if (!CreateInitValue_ForArray(Plugin, Groups, Ret, Code))
							return ExprRecResult.Failed;
					}
					else if (Op == Operator.NewObject)
					{
						if (!CreateInitValue_ForObject(Plugin, Ret, EndingResult.Inner))
							return ExprRecResult.Failed;
					}
					else
					{
						throw new ApplicationException();
					}
				}
				else if (Op == Operator.NewArray && !DimensionsSpecified)
				{
					State.Messages.Add(MessageId.ArrayLengthNotSpecified, StrParams);
					return ExprRecResult.Failed;
				}

				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class NewRecognizer : LanguageNode, IExprRecognizer
	{
		public CallRecognizer CallRecognizer;

		public NewRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

        public override void Init(LanguageInitData InitData)
		{
            CallRecognizer = Language.Root.GetObject<CallRecognizer>();
			base.Init(InitData);
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.StartsWith("new", new IdCharCheck(true)))
			{
				var State = Plugin.State;
				var Container = Plugin.Container;

				var SubCode = Code.TrimmedSubstring(State, 3);
				if (!SubCode.IsValid) return ExprRecResult.Failed;

				var Len = SubCode.Length;
				var LChar = SubCode[Len - 1];

				var Ch = (ExpressionNode[])null;
				var Op = Operator.Unknown;
				if (LChar == ']' || LChar == ')')
				{
					if (LChar == ']') Op = Operator.NewArray;
					else if (LChar == ')') Op = Operator.NewObject;
					else throw new ApplicationException();

					var ZPos = SubCode.GetBracketPos(State, Back: true);
					if (ZPos == -1) return ExprRecResult.Failed;

					var StrParams = SubCode.Substring(ZPos + 1, Len - ZPos - 2).Trim();
					SubCode = SubCode.Substring(0, ZPos).Trim();

					Ch = CallRecognizer.ProcessParameters(Plugin, SubCode, StrParams);
					if (Ch == null) return ExprRecResult.Failed;
				}
				else
				{
					Ch = new ExpressionNode[] { Plugin.NewNode(new StrExpressionNode(SubCode)) };
					if (Ch[0] == null) return ExprRecResult.Failed;

					Op = Operator.NewObject;
				}

				Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class IfThenRecognizer : LanguageNode, IExprRecognizer
	{
		public string[] ThenStrs = new string[] { ":", "then" };
		public string[] ElseStrs = new string[] { "else" };

		public IfThenRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.StartsWith("if", new IdCharCheck(true)))
			{
				var State = Plugin.State;
				var Container = Plugin.Container;
				var SkippingHandler = State.Language.GlobalHandlers;

				FindResult Result;
				var SCode = Code.Substring(2);
				if (!SCode.IsValid || (Result = SCode.Find(ThenStrs, IdCharCheck: new IdCharCheck(true), Handlers: SkippingHandler)).Position == -1)
				{
					State.Messages.Add(MessageId.NeedThenElse, Code);
					return ExprRecResult.Failed;
				}

				var ConditionStr = SCode.TrimmedSubstring(State, 0, Result.Position);
				SCode = SCode.Substring(Result.NextChar);

				if (!SCode.IsValid || (Result = SCode.Find(ElseStrs, Handlers: SkippingHandler, IdCharCheck: new IdCharCheck(true))).Position == -1)
				{
					State.Messages.Add(MessageId.NeedThenElse, Code);
					return ExprRecResult.Failed;
				}

				var ThenStr = SCode.TrimmedSubstring(State, 0, Result.Position);
				var ElseStr = SCode.TrimmedSubstring(State, Result.NextChar);
				if (!ThenStr.IsValid || !ElseStr.IsValid || !ConditionStr.IsValid)
					return ExprRecResult.Failed;

				var Condition = Expressions.Recognize(ConditionStr, Plugin, true);
				var Then = Expressions.Recognize(ThenStr, Plugin, true);
				var Else = Expressions.Recognize(ElseStr, Plugin, true);

				if (Condition == null || Then == null || Else == null)
					return ExprRecResult.Failed;

				var Ch = new ExpressionNode[] {Condition, Then, Else };
				Ret = new OpExpressionNode(Operator.Condition, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class ConditionRecognizer : LanguageNode, IExprRecognizer
	{
		public ConditionRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "?", ":" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var State = Plugin.State;
			var SkippingHandler = State.Language.GlobalHandlers;

			var Depth = 0;
			var Begin = new FindResult(-1, -1, null);
			var End = new FindResult(-1, -1, null);
			foreach (var e in Code.EnumFind(Operators, Skip, Handlers: SkippingHandler))
			{
				if (e.Index == 0)
				{
					if (Depth == 0) Begin = e;
					Depth++;
				}
				else if (e.Index == 1)
				{
					Depth--;
					if (Depth == 0) End = e;
				}
				else
				{
					throw new ApplicationException();
				}
			}

			if (Begin.Position != -1 && Begin.Index == 0 && End.Position != -1 && End.Index == 1)
			{
				var Left_Str = Code.TrimmedSubstring(State, 0, Begin.Position);
				var MidPos = Begin.Position + Begin.String.Length;
				var Mid_Str = Code.TrimmedSubstring(State, MidPos, End.Position - MidPos);
				var Right_Str = Code.TrimmedSubstring(State, End.Position + End.String.Length);

				if (!Left_Str.IsValid || !Mid_Str.IsValid || !Right_Str.IsValid)
					return ExprRecResult.Failed;

				var Left = Expressions.Recognize(Left_Str, Plugin, true);
				var Mid = Expressions.Recognize(Mid_Str, Plugin, true);
				var Right = Expressions.Recognize(Right_Str, Plugin, true);
				if (Left == null || Mid == null || Right == null)
					return ExprRecResult.Failed;

				var Ch = new ExpressionNode[] { Left, Mid, Right };
				Ret = new OpExpressionNode(Operator.Condition, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class AssignmentRecognizer : LanguageNode, IExprRecognizer
	{
		public AssignmentRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "+=", "-=", "*=", "/=", "%=", "<<=", ">>=", "|=", "&=", "^=", "=" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var State = Plugin.State;
			var Result = RecognizerHelper.Find(this, State, Code.String);
			if (Result.Position != -1)
			{
				var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (Ch == null) return ExprRecResult.Failed;

				var Op = Operator.Unknown;
				switch (Result.Index)
				{
					case 0: Op = Operator.Add; break;
					case 1: Op = Operator.Subract; break;
					case 2: Op = Operator.Multiply; break;
					case 3: Op = Operator.Divide; break;
					case 4: Op = Operator.Modolus; break;
					case 5: Op = Operator.ShiftLeft; break;
					case 6: Op = Operator.ShiftRight; break;
					case 7: Op = Operator.BitwiseOr; break;
					case 8: Op = Operator.BitwiseAnd; break;
					case 9: Op = Operator.BitwiseXor; break;

					case 10:
						Ret = new OpExpressionNode(Operator.Assignment, Ch, Code);
						break;

					default:
						throw new ApplicationException();
				}

				if (Op != Operator.Unknown)
				{
					var LinkedNode = new LinkedExprNode(Ch[0]);
					Ch[0] = Plugin.NewNode(new LinkingNode(LinkedNode, Code));
					if (Ch[0] == null) return ExprRecResult.Failed;

					Ret = Plugin.NewNode(new OpExpressionNode(Op, Ch, Code));
					var Dst = Plugin.NewNode(new LinkingNode(LinkedNode, Code));
					if (Ret == null || Dst == null) return ExprRecResult.Failed;

					Ch = new ExpressionNode[] { Dst, Ret };
					Ret = new OpExpressionNode(Operator.Assignment, Ch, Code);
					Ret.LinkedNodes.Add(LinkedNode);
				}

				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class TupleCreatingRecognizer : LanguageNode, IExprRecognizer
	{
		public TupleCreatingRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var SkippingHandler = Plugin.State.Language.GlobalHandlers;
			if (Code.Find(',', Handlers: SkippingHandler) != -1)
			{
				var Splitted = RecognizerHelper.SplitToParameters(Plugin.State, Code, ',');
				if (Splitted == null) return ExprRecResult.Failed;

				var Members = Expressions.Recognize(Splitted, Plugin);
				if (Members == null) return ExprRecResult.Failed;
				
				Ret = new OpExpressionNode(Operator.Tuple, Members, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class ArrayCreatingRecognizer : LanguageNode, IExprRecognizer
	{
		public ArrayCreatingRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "[" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length > 1 && Code[0] == '[' && Code[Code.Length - 1] == ']')
			{
				var SubStr = Code.TrimmedSubstring(Plugin.State, 1, Code.Length - 2);
				if (!SubStr.IsValid) return ExprRecResult.Failed;

				var Groups = Expressions.GetGroups(Plugin, SubStr);
				if (Groups == null) return ExprRecResult.Failed;

				var Nodes = Groups.GetNodes().ToArray();
				Ret = new OpExpressionNode(Operator.Array, Nodes, Code);
				Ret.Data.Set(Groups);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class LogicalRecognizer : LanguageNode, IExprRecognizer
	{
		public LogicalRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			this.Operators = new string[] { "and", "xor", "or" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			for (var i = 0; i < Operators.Length; i++)
			{
				var Pos = Code.Find(Operators[i], Skip, true, new IdCharCheck(true), SkippingHandlers);
				var Result = new FindResult(i, Pos, Operators[i]);

				if (Result.Position != -1)
				{
					var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
					if (Ch == null) return ExprRecResult.Failed;

					Operator Op;
					if (Result.Index == 0) Op = Operator.And;
					else if (Result.Index == 1) Op = Operator.Inequality;
					else if (Result.Index == 2) Op = Operator.Or;
					else throw new ApplicationException();

					Ret = new OpExpressionNode(Op, Ch, Code);
					return ExprRecResult.Succeeded;
				}
			}

			return ExprRecResult.Unknown;
		}
	}

	public class RefEqualityRecognizer : LanguageNode, IExprRecognizer
	{
		public RefEqualityRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "===", "!==" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Result = RecognizerHelper.Find(this, Plugin.State, Code.String);
			if (Result.Position != -1)
			{
				var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (Ch == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.RefEquality;
				else if (Result.Index == 1) Op = Operator.RefInequality;
				else throw new ApplicationException();

				Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

    [Flags]
    public enum RelEquRecognizerFlags : byte
    {
        None = 0,
        DisableOpposed = 1,
    }

	public class RelEquRecognizer : LanguageNode, IExprRecognizer
	{
        public RelEquRecognizerFlags RecFlags;

		public RelEquRecognizer(LanguageNode Parent, RelEquRecognizerFlags Flags)
			: base(Parent)
		{
			Operators = new string[] { "==", "!=", "<=", "<", ">=", ">" };
			NewLineLeft = NewLineRight = Operators;
            this.RecFlags = Flags;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Out)
		{
			var Results = Code.EnumFind(Operators, Skip: Skip, Handlers: SkippingHandlers).ToList();

            if ((RecFlags & RelEquRecognizerFlags.DisableOpposed) != 0)
			{
				for (var i = 0; i < Results.Count; i++)
				{
					if (Results[i].String[0] == '<')
					{
						var Right = Code.Substring(Results[i].Position);
						if (Right.GetBracketPos(false, SkippingHandlers) != -1)
						{
							Results.RemoveAt(i);
							i--;
						}
					}
					else if (Results[i].String[0] == '>')
					{
						var Left = Code.Substring(0, Results[i].Position + 1);
						if (Left.GetBracketPos(true, SkippingHandlers) != -1)
						{
							Results.RemoveAt(i);
							i--;
						}
					}
				}
			}

			if (Results.Count == 0) 
				return ExprRecResult.Unknown;

			var ChNodes = new List<ExpressionNode>(Results.Count + 1);
			for (var i = 0; i <= Results.Count; i++)
			{
				int Start, Length;
				if (i < Results.Count)
				{
					if (i == 0)
					{
						Start = 0;
						Length = Results[i].Position;
					}
					else
					{
						var PrevRes = Results[i - 1];
						Start = PrevRes.Position + PrevRes.String.Length;
						Length = Results[i].Position - Start;
					}
				}
				else
				{
					var PrevRes = Results[i - 1];
					Start = PrevRes.Position + PrevRes.String.Length;
					Length = Code.Length - Start;
				}

				var PStr = Code.TrimmedSubstring(Plugin.State, Start, Length);
				if (!PStr.IsValid) return ExprRecResult.Failed;

				var Node = Expressions.Recognize(PStr, Plugin, true);
				if (Node == null) return ExprRecResult.Failed;
				ChNodes.Add(Node);
			}

			var ResOperators = new List<Operator>(Results.Count);
			for (var i = 0; i < Results.Count; i++)
			{
				var Result = Results[i];
				Operator Op;

				switch (Result.Index)
				{
					case 0: Op = Operator.Equality; break;
					case 1: Op = Operator.Inequality; break;
					case 2: Op = Operator.LessEqual; break;
					case 3: Op = Operator.Less; break;
					case 4: Op = Operator.GreaterEqual; break;
					case 5: Op = Operator.Greater; break;
					default: throw new ApplicationException();
				}

				ResOperators.Add(Op);
			}

			Out = Expressions.ChainedRelation(Plugin, ChNodes, ResOperators, Code);
			return Out == null ? ExprRecResult.Failed : ExprRecResult.Ready;
		}
	}

	public class AdditiveRecognizer : LanguageNode, IExprRecognizer
	{
		public AdditiveRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "+", "-" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Result = RecognizerHelper.Find2(this, Plugin.Container, Code.String);
			if (Result.Position != -1)
			{
				var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (Ch == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.Add;
				else if (Result.Index == 1) Op = Operator.Subract;
				else throw new ApplicationException();

				Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class MultiplicativeRecognizer : LanguageNode, IExprRecognizer
	{
		public MultiplicativeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "*", "/", "%" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Result = RecognizerHelper.Find2(this, Plugin.Container, Code.String);
			if (Result.Position != -1)
			{
				var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (Ch == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.Multiply;
				else if (Result.Index == 1) Op = Operator.Divide;
				else if (Result.Index == 2) Op = Operator.Modolus;
				else throw new ApplicationException();

				Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class ShiftRecognizer : LanguageNode, IExprRecognizer
	{
		public ShiftRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "<<", ">>" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Result = RecognizerHelper.Find2(this, Plugin.Container, Code.String);
			if (Result.Position != -1)
			{
				var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (Ch == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.ShiftLeft;
				else if (Result.Index == 1) Op = Operator.ShiftRight;
				else throw new ApplicationException();

				Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class BitwiseRecognizer : LanguageNode, IExprRecognizer
	{
		public BitwiseRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "&", "|", "^" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Result = RecognizerHelper.Find2(this, Plugin.Container, Code.String);
			if (Result.Position != -1)
			{
				var Ch = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (Ch == null) return ExprRecResult.Failed;

				Operator Op;
				if (Result.Index == 0) Op = Operator.BitwiseAnd;
				else if (Result.Index == 1) Op = Operator.BitwiseOr;
				else if (Result.Index == 2) Op = Operator.BitwiseXor;
				else throw new ApplicationException();

				Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class NegateRecognizer : LanguageNode, IExprRecognizer
	{
		public NegateRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "-", "+" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length > 0 && Code[0] == '-')
			{
				Ret = RecognizerHelper.OneParamOpNode(Code, Plugin, Operator.Negation);
				return Ret == null ? ExprRecResult.Failed : ExprRecResult.Succeeded;
			}
			else if (Code.Length > 0 && Code[0] == '+')
			{
				Ret = RecognizerHelper.OneParamOpNode(Code, Plugin, Operator.UnaryPlus);
				return Ret == null ? ExprRecResult.Failed : ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class NotRecognizer : LanguageNode, IExprRecognizer
	{
		public NotRecognizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.StartsWith("not", new IdCharCheck(true)))
			{
				var SubStr = Code.TrimmedSubstring(Plugin.State, 3);
				if (SubStr.Length == 0) return ExprRecResult.Failed;

				Ret = Expressions.Recognize(SubStr, Plugin, true);
				if (Ret == null) return ExprRecResult.Failed;

				var RetCh = new ExpressionNode[] { Ret };
				Ret = new OpExpressionNode(Operator.Not, RetCh, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class CNotRecognizer : LanguageNode, IExprRecognizer
	{
		public CNotRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "!" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.StartsWith(Operators, Skip).Position != -1)
			{
				var SubStr = Code.TrimmedSubstring(Plugin.State, 1);
				if (SubStr.Length == 0) return ExprRecResult.Failed;

				Ret = Expressions.Recognize(SubStr, Plugin, true);
				if (Ret == null) return ExprRecResult.Failed;

				var RetCh = new ExpressionNode[] { Ret };
				Ret = new OpExpressionNode(Operator.Not, RetCh, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class AddressRecognizer : LanguageNode, IExprRecognizer
	{
		public AddressRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "&" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length > 0 && Code[0] == '&')
			{
				Ret = RecognizerHelper.OneParamOpNode(Code, Plugin, Operator.Address);
				if (Ret == null) return ExprRecResult.Failed;
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class ComplementRecognizer : LanguageNode, IExprRecognizer
	{
		public ComplementRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "~" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length > 0 && Code[0] == '~')
			{
				Ret = RecognizerHelper.OneParamOpNode(Code, Plugin, Operator.Complement);
				return Ret == null ? ExprRecResult.Failed : ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class IncDecRecognizer : LanguageNode, IExprRecognizer
	{
		public IncDecRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineLeft = Operators = new string[] { "++", "--" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			Operator Op;
			if (Code.EndsWith("++")) Op = Operator.Increase;
			else if (Code.EndsWith("--")) Op = Operator.Decrease;
			else return ExprRecResult.Unknown;
			
			var ChildStr = Code.TrimmedSubstring(Plugin.State, 0, Code.Length - 2);
			if (!ChildStr.IsValid) return ExprRecResult.Failed;

			var Child = Expressions.Recognize(ChildStr, Plugin, true);
			if (Child == null) return ExprRecResult.Failed;

			var Children = new ExpressionNode[] { Child };
			Ret = new OpExpressionNode(Op, Children, Code);
			return ExprRecResult.Succeeded;
		}
	}

	public class IndirectionRecognizer : LanguageNode, IExprRecognizer
	{
		public IndirectionRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "*" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length > 0 && Code[0] == '*')
			{
				var PtrCode = Code.TrimmedSubstring(Plugin.State, 1);
				if (!PtrCode.IsValid) return ExprRecResult.Failed;

				var PtrNode = Expressions.Recognize(PtrCode, Plugin, true);
				if (PtrNode == null) return ExprRecResult.Failed;

				Ret = Expressions.Indirection(Plugin, PtrNode, Code);
				return Ret == null ? ExprRecResult.Failed : ExprRecResult.Ready;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class CallRecognizer : LanguageNode, IExprRecognizer, ICallParamRecognizer
	{
		public List<IBuiltinFuncRecognizer> Recognizers;

		public CallRecognizer(LanguageNode Parent, List<IBuiltinFuncRecognizer> Recognizers)
			: base(Parent)
		{
			this.Recognizers = Recognizers;
			Operators = NewLineRight = new string[] { "(" };
			NewLineLeft = new string[] {  "(", ")" };
		}

		public ExpressionNode[] ProcessParameters(PluginRoot Plugin, CodeString Function, CodeString Parameters)
		{
			var ParamList = RecognizerHelper.GetParamList(Plugin.State, Parameters);
			if (ParamList == null) return null;

			return ProcessParameters(Plugin, Function, ParamList);
		}

		public ExpressionNode[] ProcessParameters(PluginRoot Plugin, CodeString Function, CodeString[] Parameters)
		{
			var RetValue = true;
			var Nodes = new ExpressionNode[Parameters.Length + 1];

			if (Function.IsValid)
			{
				Nodes[0] = Expressions.Recognize(Function, Plugin);
				if (Nodes[0] == null) RetValue = false;
			}

			for (var i = 0; i < Parameters.Length; i++)
			{
				var NamePos = Parameters[i].Find(':', false, SkippingHandlers);
				if (NamePos != -1)
				{
					var Name = Parameters[i].Substring(0, NamePos).Trim();
					var Value = Parameters[i].Substring(NamePos + 1).Trim();

					if (Name.IsValidIdentifierName)
					{
						var Node = Expressions.Recognize(Value, Plugin);
						if (Node == null) { RetValue = false; continue; }

						Nodes[i + 1] = Plugin.NewNode(new NamedParameterNode(Parameters[i], Node, Name));
						if (Nodes[i + 1] == null) { RetValue = false; continue; }
					}
				}

				Nodes[i + 1] = Expressions.Recognize(Parameters[i], Plugin);
				if (Nodes[i + 1] == null) { RetValue = false; continue; }
			}

			return RetValue ? Nodes : null;
		}

		public bool GetParameters(CompilerState State, CodeString Code, out CodeString Function, out CodeString Parameters)
		{
			if (Code.Length > 0 && Code[Code.Length - 1] == ')')
			{
				Function = Code;
				Parameters = RecognizerHelper.ExtractBracket(State, Code, ')', ref Function, true);
				return Parameters.IsValid && Parameters.VerifyNotEmpty(State, Code);
			}
			else
			{
				Function = new CodeString();
				Parameters = new CodeString();

				State.Messages.Add(MessageId.NotExpected, Code);
				return false;
			}
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length > 0 && Code[Code.Length - 1] == ')')
			{
				var State = Plugin.State;

				var Called = Code;
				var ParamStr = RecognizerHelper.ExtractBracket(State, Code, ')', ref Called, true);
				if (!ParamStr.IsValid || !Called.VerifyNotEmpty(State, Code)) 
					return ExprRecResult.Failed;

				var ParamList = RecognizerHelper.GetParamList(State, ParamStr);
				if (ParamList == null) return ExprRecResult.Failed;

				CodeString[] GenericParams;
				var GenericRec = State.Language.GenericRecognizer;
				var GRes = GenericRec.GetGenericParams(State, ref Called, out GenericParams);
				if (GRes == SimpleRecResult.Failed) return ExprRecResult.Failed;

				if (Recognizers != null)
				{
					var ParamArray = ParamList.ToArray();
					for (var i = 0; i < Recognizers.Count; i++)
					{
						var Res = Recognizers[i].Recognize(Code, Called, 
							ParamArray, GenericParams, Plugin, ref Ret);

						if (Res != ExprRecResult.Unknown) return Res;
					}
				}

				var Nodes = ProcessParameters(Plugin, Called, ParamList);
				if (Nodes == null) return ExprRecResult.Failed;

				Ret = new OpExpressionNode(Operator.Call, Nodes, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class IndexRecognizer : LanguageNode, IExprRecognizer
	{
		public IndexRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = NewLineRight = new string[] { "[" };
			NewLineLeft = new string[] { "[" , "]" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			if (Code.Length > 0 && Code[Code.Length - 1] == ']')
			{
				var State = Plugin.State;

				var ArrayStr = Code;
				var ParamStr = RecognizerHelper.ExtractBracket(State, Code, ']', ref ArrayStr, true);
				if (!ParamStr.IsValid || !ArrayStr.VerifyNotEmpty(State, Code))
					return ExprRecResult.Failed;

				var ParamList = RecognizerHelper.GetParamList(State, ParamStr);
				if (ParamList == null) return ExprRecResult.Failed;

				var ParamNodes = Expressions.Recognize(ParamList, Plugin);
				var ArrayNode = Expressions.Recognize(ArrayStr, Plugin);
				if (ParamNodes == null || ArrayNode == null) return ExprRecResult.Failed;

				var Ch = new ExpressionNode[ParamNodes.Length + 1];
				Ch[0] = ArrayNode;
				ParamNodes.CopyTo(Ch, 1);

				Ret = new OpExpressionNode(Operator.Index, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class NullCoalescingRecognizer : LanguageNode, IExprRecognizer
	{
		public NullCoalescingRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "??" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Container = Plugin.Container;
			var Result = RecognizerHelper.Find2(this, Plugin.Container, Code.String);
			if (Result.Position != -1)
			{
				var State = Plugin.State;
				var GlobalContainer = Container.GlobalContainer;

				var LeftRight = RecognizerHelper.GetLeftRightNode(Code, Result, Plugin);
				if (LeftRight == null) return ExprRecResult.Failed;
				var Linked = new LinkedExprNode(LeftRight[0]);

				var Null = Plugin.NewNode(Constants.GetNullValue(Plugin.Container, Code));
				var RelLnk = Plugin.NewNode(new LinkingNode(Linked, Code));
				if (RelLnk == null || Null == null) return ExprRecResult.Failed;

				var RelCh = new ExpressionNode[] {RelLnk, Null };
				var Rel = Plugin.NewNode(new OpExpressionNode(Operator.Inequality, RelCh, Code));
				if (Rel == null) return ExprRecResult.Failed;

				var RetLnk = Plugin.NewNode(new LinkingNode(Linked, Code));
				if (RetLnk == null) return ExprRecResult.Failed;
				var RetCh = new ExpressionNode[] {Rel, RetLnk, LeftRight[1] };
				Ret = new OpExpressionNode(Operator.Condition, RetCh, Code);
				Ret.LinkedNodes = new List<LinkedExprNode>() { Linked };
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class SafeNavigationRecognizer : LanguageNode, IExprRecognizer
	{
		public SafeNavigationRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "?.", "?->" };
			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Container = Plugin.Container;
			var Result = RecognizerHelper.Find2(this, Plugin.Container, Code.String);
			if (Result.Position != -1)
			{
				var State = Plugin.State;
				var GlobalContainer = Container.GlobalContainer;

				var Left_Str = Code.TrimmedSubstring(State, 0, Result.Position);
				var Right_Str = Code.TrimmedSubstring(State, Result.NextChar);
				if (!Left_Str.IsValid || !Right_Str.IsValid) return ExprRecResult.Failed;

				var Left = Expressions.Recognize(Left_Str, Plugin, true);
				if (Left == null) return ExprRecResult.Failed;
				var Linked = new LinkedExprNode(Left);

				//--------------------------------------------------------------------------------------
				var Null = Plugin.NewNode(Constants.GetNullValue(Container, Code));
				if (Null == null) return ExprRecResult.Failed;
				var RelLnk = Plugin.NewNode(new LinkingNode(Linked, Code));
				if (RelLnk == null) return ExprRecResult.Failed;

				var RelCh = new ExpressionNode[] {RelLnk, Null };
				var Rel = Plugin.NewNode(new OpExpressionNode(Operator.Inequality, RelCh, Code));
				if (Rel == null) return ExprRecResult.Failed;

				//--------------------------------------------------------------------------------------
				var ThenSrc = Plugin.NewNode(new LinkingNode(Linked, Code));
				if (ThenSrc == null) return ExprRecResult.Failed;

				if (Result.Index == 1)
				{
					ThenSrc = Expressions.Indirection(Plugin, ThenSrc, Code);
					if (ThenSrc == null) return ExprRecResult.Failed;
				}

				var MemberNode = Plugin.NewNode(new StrExpressionNode(Right_Str));
				if (MemberNode == null) return ExprRecResult.Failed;
				var ThenCh = new ExpressionNode[] {ThenSrc, MemberNode };
				var Then = Plugin.NewNode(new OpExpressionNode(Operator.Member, ThenCh, Code));
				if (Then == null) return ExprRecResult.Failed;

				//--------------------------------------------------------------------------------------
				var Else = Plugin.NewNode(Constants.GetNullValue(Container, Code));
				if (Else == null) return ExprRecResult.Failed;
				var RetCh = new ExpressionNode[] {Rel, Then, Else };
				Ret = new OpExpressionNode(Operator.Condition, RetCh, Code);
				Ret.LinkedNodes = new List<LinkedExprNode>() { Linked };
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

    [Flags]
    public enum MemberRecognizerFlags : byte
    {
        None = 0,
        AllowScopeResolution = 1,
    }

	public class MemberRecognizer : LanguageNode, IExprRecognizer
	{
		public MemberRecognizer(LanguageNode Parent, MemberRecognizerFlags Flags = MemberRecognizerFlags.None)
			: base(Parent)
		{
			if ((Flags & MemberRecognizerFlags.AllowScopeResolution) != 0)
                Operators = new string[] { ".", "->", "::" };
            else Operators = new string[] { ".", "->" };

			NewLineLeft = NewLineRight = Operators;
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Container = Plugin.Container;
			var Result = RecognizerHelper.Find2(this, Plugin.Container, Code.String);
			if (Result.Position == -1) return ExprRecResult.Unknown;

			var RightStr = Code.Substring(Result.NextChar).Trim();
			if (RightStr.IsValidIdentifierName || RightStr.IsNumber)
			{
				var State = Plugin.State;
				var LeftStr = Code.TrimmedSubstring(State, 0, Result.Position);
				if (!LeftStr.IsValid) return ExprRecResult.Failed;

                var Op = Operator.Member;
				var Left = Expressions.Recognize(LeftStr, Plugin, true);
				if (Left == null) return ExprRecResult.Failed;

                if (Result.Index == 2)
                {
                    Op = Operator.ScopeResolution;
                }
                else
                {
                    Op = Operator.Member;
                    if (Result.Index == 1)
                    {
                        Left = Expressions.Indirection(Plugin, Left, Code);
                        if (Left == null) return ExprRecResult.Failed;
                    }
                }

				var Right = Plugin.NewNode(new StrExpressionNode(RightStr));
				if (Right == null) return ExprRecResult.Failed;
				
				var Ch = new ExpressionNode[] { Left, Right };
                Ret = new OpExpressionNode(Op, Ch, Code);
				return ExprRecResult.Succeeded;
			}

			return ExprRecResult.Unknown;
		}
	}

	public class ExprVarDeclRecognizer : LanguageNode, IExprRecognizer
	{
		public ExprVarDeclRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { " ", "\t", "\r", "\n" };
		}

		public ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Ret)
		{
			var Result = RecognizerHelper.Find(this, Plugin.State, Code.String);
			if (Result.Position != -1)
			{
				var VarName = Code.Substring(Result.Position).Trim();
				if (!VarName.IsValidIdentifierName) return ExprRecResult.Unknown;

				var Container = Plugin.Container;
				var State = Plugin.State;

				var TypeName = Code.Substring(0, Result.Position).Trim();
				Ret = Plugin.DeclareVarAndCreateIdNode(Code, TypeName, VarName);
				if (Ret == null) return ExprRecResult.Failed;
				else return ExprRecResult.Ready;
			}

			return ExprRecResult.Unknown;
		} 
	}
}
