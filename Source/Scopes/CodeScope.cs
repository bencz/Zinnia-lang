using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Zinnia
{
	public class WithScopeNode : CodeScopeNode
	{
		public Dictionary<Identifier, PackedMemberId> Ids = new Dictionary<Identifier, PackedMemberId>();
		public ExpressionNode WithNode;

		public WithScopeNode(IdContainer Parent, CodeString Code, ExpressionNode Node)
			: base(Parent, Code)
		{
			WithNode = Node;
		}

		public override bool GetContainerId(string Name, List<IdentifierFound> Out, Predicate<Identifier> Func = null)
		{
			var RetValue = base.GetContainerId(Name, Out, Func);
			var Type = WithNode.Type as StructuredType;

			foreach (var e in Identifiers.SearchMember(this, Type, Name, Func))
			{
				PackedMemberId Id;
				if (!Ids.TryGetValue(e.Identifier, out Id))
				{
					Id = new PackedMemberId(this, new CodeString(),
						e.Identifier.TypeOfSelf, WithNode, e.Identifier);

					Ids.Add(e.Identifier, Id);
				}

				if (Func(Id))
				{
					Out.Add(new IdentifierFound(this, Id));
					RetValue = true;
				}
			}

			return RetValue;
		}
	}
	
	public class CodeScopeNode : ScopeNode
	{
		public bool DisableLastChild;
		public IFinishableCommRecognizer LastCommRecognizer;
		public IdContainer LastAddedContainer;
		public CheckingMode CheckingMode;

		public override bool IsAlreadyDefined(string Name, Predicate<Identifier> Func = null)
		{
			Predicate<IdContainer> ContainerFunc = x =>
				Identifiers.Search(x, x.IdentifierList, Name, Func).Count == 0;

			return !TrueForAllParent<IdContainer>(ContainerFunc, FunctionScope.Parent);
		}

		public bool AddFinishableCommand(IdContainer Container, IFinishableCommRecognizer Recognizer)
		{
			if (!FinishLastCommand())
				return false;

			LastAddedContainer = Container;
			LastCommRecognizer = Recognizer;

			DisableLastChild = false;
			Children.Add(Container);
			return true;
		}

		public bool AddCommand(IdContainer Container)
		{
			if (!FinishLastCommand())
				return false;

			DisableLastChild = false;
			Children.Add(Container);
			return true;
		}

		public bool FinishLastCommand()
		{
			if (LastCommRecognizer != null)
			{
				LastCommRecognizer.Finish(LastAddedContainer);
				MarkFinished();
			}

			return true;
		}

		public void MarkFinished()
		{
			LastAddedContainer = null;
			LastCommRecognizer = null;
		}

		public bool BasicConstructorCommands()
		{
			if (FunctionScope.ConstructorCall != null)
			{
				Children.Add(new Command(this, FunctionScope.ConstructorCall.Code, CommandType.Expression)
				{
					Expressions = new List<ExpressionNode>() { FunctionScope.ConstructorCall },
				});
			}

			return AssignInitValues();
		}

		public bool AssignInitValues()
		{
			var Plugin = GetPlugin();
			var RetValue = true;
			var Scope = FunctionScope.Parent;

			for (var i = 0; i < Scope.IdentifierList.Count; i++)
			{
				var Var = Scope.IdentifierList[i] as MemberVariable;
				if (Var != null)
				{
					if (!Var.InitString.IsValid) continue;

					Plugin.Reset();
					if (!Var.CalcValue(Plugin, BeginEndMode.Both, true))
						RetValue = false;

					Children.Add(new Command(this, Var.InitString, CommandType.Expression)
					{
						Expressions = new List<ExpressionNode>() { Var.InitValue }
					});
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

		public CodeScopeNode(IdContainer Parent, CodeString Code)
			: base(Parent, Code)
		{
			var CodeScopeParent = GetParent<CodeScopeNode>(FunctionScope);
			if (CodeScopeParent != null) CheckingMode = CodeScopeParent.CheckingMode;
		}

		public bool Return(ExpressionNode Node, CodeString Code)
		{
			var FS = FunctionScope;
			var VoidType = GlobalContainer.CommonIds.Void;
			var RetType = FS.Type.RetType;

			if (Node != null)
			{
				var Func = FunctionScope.Function;
				if (Func is Constructor || Func is Destructor)
				{
					State.Messages.Add(MessageId.CannotReturn, Code);
					return false;
				}

				if (RetType.RealId is VoidType)
				{
					var TypeStrs = new[] { Node.Type.Name.ToString(), RetType.Name.ToString() };
					State.Messages.Add(MessageId.CannotConvert, Code, TypeStrs);
					return false;
				}

				Children.Add(new Command(this, Code, CommandType.Return)
				{
					Expressions = new List<ExpressionNode>() { Node },
					Label = FS.ReturnLabel,
				});
			}
			else
			{
				if (FS.NeedsReturnVal)
				{
					var TypeStrs = new[] { VoidType.Name.ToString(), RetType.Name.ToString() };
					State.Messages.Add(MessageId.CannotConvert, Code, TypeStrs);
					return false;
				}

				if (!CopyRetVal(Code)) 
					return false;

				Children.Add(new Command(this, Code, CommandType.Return)
				{
					Label = FS.ReturnLabel,
				});
			}

			return true;
		}

		public IdContainer LastChild
		{
			get
			{
				var CommCount = Children.Count;
				if (CommCount > 0 && !DisableLastChild)
					return Children[CommCount - 1];
				else return null;
			}
		}

		public bool RecognizeCommand(CodeString Code)
		{
			var Recs = State.Language.CommRecognizers;
            for (var i = 0; i < Recs.Length; i++)
			{
				var Res = Recs[i].Recognize(this, Code);
				if (Res != SimpleRecResult.Unknown)
					return Res != SimpleRecResult.Failed;
			}

			State.Messages.Add(MessageId.NotExpected, Code);
			return false;
		}

		public bool CopyRetVal(CodeString Code, Identifier Id)
		{
			var Plugin = GetPlugin();
			if (!Plugin.Begin()) return false;

			var Node = Plugin.NewNode(new IdExpressionNode(Id, Code));
			if (Node == null || Plugin.End(ref Node) == PluginResult.Failed) 
				return false;

			Children.Add(new Command(this, Code, CommandType.Return)
			{
				Expressions = new List<ExpressionNode>() { Node },
				Label = FunctionScope.ReturnLabel,
			});

			return true;
		}

		public bool CopyRetVal(CodeString Code, ExpressionNode SrcVar)
		{
			var Plugin = GetPlugin();
			var Node = SrcVar.Copy(Plugin, Code: Code);
			if (Node == null) return false;

			Children.Add(new Command(this, Code, CommandType.Return)
			{
				Expressions = new List<ExpressionNode>() { Node },
				Label = FunctionScope.ReturnLabel,
			});

			return true;
		}

		public bool CopyRetVal(CodeString Code)
		{
			var FS = FunctionScope;
			if (!(FS.Type.RetType is VoidType))
			{
				if (FS.Function is Constructor) return CopyRetVal(Code, FS.SelfVariable);
			}

			return true;
		}

		public virtual bool ProcessCode()
		{
			if (!State.Language.CodeProcessor.Process(this))
				return false;

			return FinishLastCommand();
		}

		public override void GetAssembly(CodeGenerator CG, GetAssemblyMode Mode = GetAssemblyMode.Code)
		{
			CG.Container = this;
			for (var i = 0; i < Children.Count; i++)
				Children[i].GetAssembly(CG);
		}

		public override bool DeclareVariables(Variable[] Variables, GetIdMode IdMode = GetIdMode.Everywhere)
		{
			var Ret = base.DeclareVariables(Variables, IdMode);
            for (var i = 0; i < Variables.Length; i++)
			{
				var e = Variables[i];
				if (e != null && e.InitValue != null)
				{
					Children.Add(new Command(this, e.InitString, CommandType.Expression)
					{
						Expressions = new List<ExpressionNode>() { e.InitValue },
					});
				}
			}

			return Ret;
		}
	}
}