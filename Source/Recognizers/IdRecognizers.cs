using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zinnia.Recognizers
{
	public class PointerTypeRecognizer : LanguageNode, IIdRecognizer, INameGenerator
	{
		public PointerTypeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineLeft = Operators = new string[] { "*" };
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			if (Code.Length > 0 && Code[Code.Length - 1] == '*')
			{
				var State = Container.State;
				var ChildC = Code.TrimmedSubstring(State, 0, Code.Length - 1, Options.EnableMessages);
				if (!ChildC.IsValid) return SimpleRecResult.Failed;

				var TOptions = Options;
				TOptions.Func = x => x.RealId is Type;

				var Child = Identifiers.Recognize(Container, ChildC, TOptions);
				if (Child == null) return SimpleRecResult.Failed;

				var RChild = Child.RealId as Type;
				if (RChild == null || (RChild.TypeFlags & TypeFlags.CanBePointer) == 0)
				{
					if (Options.EnableMessages)
						State.Messages.Add(MessageId.UnknownId, Code);

					return SimpleRecResult.Failed;
				}

				Ret = new PointerType(Container, Child);
				if (Options.Func == null || Options.Func(Ret))
					return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}

		public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
		{
			if (Id is PointerType)
			{
				var PointerType = Id as PointerType;
				Out = State.GenerateName(PointerType.Children[0]) + "*";
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class RefTypeRecognizer : LanguageNode, IIdRecognizer, INameGenerator
	{
		public RefTypeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "unsafe_ref", "ref", "out" };
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			var Result = Code.StartsWith(Operators, Skip, IdCharCheck: new IdCharCheck(true));
			if (Result.Index != -1)
			{
				var State = Container.State;
				var ChildC = Code.TrimmedSubstring(State, Result.String.Length, Options.EnableMessages);
				if (!ChildC.IsValid) return SimpleRecResult.Failed;

				var TOptions = Options;
				TOptions.Func = x => x.RealId is Type;

				var Child = Identifiers.Recognize(Container, ChildC, TOptions);
				if (Child == null) return SimpleRecResult.Failed;
				
				var RChild = Child.RealId as Type;
				if (RChild == null || (RChild.TypeFlags & TypeFlags.CanBeReference) == 0)
				{
					if (Options.EnableMessages)
						State.Messages.Add(MessageId.UnknownId, Code);

					return SimpleRecResult.Failed;
				}

				ReferenceMode Mode;
				if (Result.Index == 0) Mode = ReferenceMode.Unsafe;
				else if (Result.Index == 1) Mode = ReferenceMode.IdMustBeAssigned;
				else if (Result.Index == 2) Mode = ReferenceMode.IdGetsAssigned;
				else throw new ApplicationException();

				Ret = new ReferenceType(Container, Child, Mode);
				if (Options.Func == null || Options.Func(Ret))
					return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}

		public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
		{
			if (Id is ReferenceType)
			{
				var RefType = Id as ReferenceType;
				var ChildName = " " + State.GenerateName(RefType.Children[0]);
				if (RefType.Mode == ReferenceMode.Unsafe) Out = Operators[0] + ChildName;
				else if (RefType.Mode == ReferenceMode.IdMustBeAssigned) Out = Operators[1] + ChildName;
				else if (RefType.Mode == ReferenceMode.IdGetsAssigned) Out = Operators[2] + ChildName;
				else throw new ApplicationException();
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class MemberTypeRecognizer : LanguageNode, IIdRecognizer
	{
		public MemberTypeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "." };
			NewLineLeft = NewLineRight = Operators;
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			var State = Container.State;
			var Result = RecognizerHelper.Find(this, State, Code.String);

			if (Result.Position != -1)
			{
				var Left = Code.TrimmedSubstring(State, 0, Result.Position, Options.EnableMessages);
				var Right = Code.TrimmedSubstring(State, Result.Position + 1, Options.EnableMessages);
				if (!Left.IsValid || !Right.IsValid) return SimpleRecResult.Failed;

				Ret = Identifiers.GetMember(Container, Left, Right, Options);
				return Ret == null ? SimpleRecResult.Failed : SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class TupleTypeRecognizer : LanguageNode, IIdRecognizer, INameGenerator
	{
		public TupleTypeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = new string[] { "," };
			NewLineLeft = NewLineRight = Operators;
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			var State = Container.State;
			if (RecognizerHelper.Find(this, State, Code.String).Position != -1)
			{
				var DeclListFlags = VarDeclarationListFlags.EnableUnnamed;
				if (Options.EnableMessages) DeclListFlags |= VarDeclarationListFlags.EnableMessages;
				var DeclList = VarDeclarationList.Create(Container, Code, null, DeclListFlags);
				if (DeclList == null) return SimpleRecResult.Failed;

				Ret = DeclList.ToTupleType(Container, Options.EnableMessages);
				if (Ret == null) return SimpleRecResult.Failed;

				if (Options.Func == null || Options.Func(Ret))
					return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}

		public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
		{
			if (Id is TupleType)
			{
				var TupleType = Id as TupleType;
				var Members = TupleType.StructuredScope.IdentifierList;

				Out = "(";
				for (var i = 0; i < Members.Count; i++)
				{
					var Member = Members[i] as MemberVariable;
					if (TupleType.Named)
					{
						if (i == 0 || !(Members[i - 1] as MemberVariable).TypeOfSelf.IsEquivalent(Member.TypeOfSelf))
							Out += State.GenerateName(Member.TypeOfSelf) + " ";

						Out += Member.Name.ToString();
					}
					else
					{
						Out += State.GenerateName(Member.TypeOfSelf);
					}

					if (i < Members.Count - 1)
						Out += ", ";
				}

				Out += ")";
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class FunctionTypeRecognizer : LanguageNode, IIdRecognizer, INameGenerator
	{
		public FunctionTypeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineLeft = NewLineRight = Operators = new string[] { "->" };
		}


		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Out)
		{
			var State = Container.State;
			var Result = RecognizerHelper.Find(this, State, Code.String);

			if (Result.Position != -1)
			{
				var OldCode = Code;
				var Mods = Modifiers.Recognize(Container, ref Code);
				if (Mods == null) return SimpleRecResult.Failed;

				Result.Position -= Code.Index - OldCode.Index;

				var Left = Code.TrimmedSubstring(State, 0, Result.Position, Options.EnableMessages);
				if (!Left.IsValid) return SimpleRecResult.Failed;

				var Right = Code.TrimmedSubstring(State, Result.Position + Result.String.Length, Options.EnableMessages);
				if (!Right.IsValid) return SimpleRecResult.Failed;

				var Flags = VarDeclarationListFlags.EnableUnnamed | VarDeclarationListFlags.EnableInitValue
					| VarDeclarationListFlags.EnableVoidOnly;

				if (Options.EnableMessages) Flags |= VarDeclarationListFlags.EnableMessages;
				var ParamDeclList = VarDeclarationList.Create(Container, Left, null, Flags);
				if (ParamDeclList == null) return SimpleRecResult.Failed;

				if (ParamDeclList.Count == 1 && ParamDeclList[0].Type.RealId is VoidType)
					ParamDeclList.RemoveAt(0);

				var Params = ParamDeclList.ToFuncParams(Container.GetPlugin());
				if (Params == null || Params.Contains(null)) return SimpleRecResult.Failed;

				Flags &= ~VarDeclarationListFlags.EnableInitValue;
				var RetDeclList = VarDeclarationList.Create(Container, Right, null, Flags);
				if (RetDeclList == null) return SimpleRecResult.Failed;

				Identifier RetType;
				if (RetDeclList.Count == 1 && !RetDeclList[0].Name.IsValid)
				{
					RetType = RetDeclList[0].Type;
				}
				else
				{
					RetType = RetDeclList.ToTupleType(Container, Options.EnableMessages);
					if (RetType == null) return SimpleRecResult.Failed;
				}

				var CallConv = Container.DefaultCallConv;
				var Succeeded = true;
				var Static = false;

				Mods.ForEach(x =>
				{
					if (x is CallingConventionModifier)
					{
						var CCM = x as CallingConventionModifier;
						CallConv = CCM.CallingConvention;
					}
					else if (x is FlagModifier)
					{
						var FM = x as FlagModifier;
						if ((FM.Flags & IdentifierFlags.Static) != 0)
							Static = true;

						if ((FM.Flags & ~IdentifierFlags.Static) != 0)
						{
							if (Options.EnableMessages)
								State.Messages.Add(MessageId.ModifierCantBeUsed, x.Code);

							Succeeded = false;
						}
					}
					else
					{
						if (Options.EnableMessages)
							State.Messages.Add(MessageId.ModifierCantBeUsed, x.Code);

						Succeeded = false;
					}
				});

				if (!Succeeded)
					return SimpleRecResult.Failed;

				Identifier FuncType = new TypeOfFunction(Container, CallConv, RetType, Params);
				Out = Static ? FuncType : new NonstaticFunctionType(Container, FuncType);

				if (Options.Func == null || Options.Func(Out)) return SimpleRecResult.Succeeded;
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}

		public string GetFuncTypeName(CompilerState State, TypeOfFunction FuncType)
		{
			var FuncCh = FuncType.Children;
			var Rec = State.Language.Root.GetObject<CallingConventionRecognizer>();
			var Mod = Rec.GetCallingConventionName(FuncType.CallConv);

			var Out = Mod + " ";
			if (FuncCh.Length == 1)
			{
				Out += "void";
			}
			else
			{
				for (var i = 1; i < FuncCh.Length; i++)
				{
					Out += State.GenerateName(FuncCh[i].TypeOfSelf);
					if (i < FuncCh.Length - 1) Out += ", ";
				}
			}

			Out += " -> ";
			var RetType = FuncCh[0];
			if (RetType.RealId is TupleType)
			{
				var Tuple = RetType.RealId as TupleType;
				var Members = Tuple.StructuredScope.IdentifierList;
				for (var i = 0; i < Members.Count; i++)
				{
					Out += State.GenerateName(Members[i].TypeOfSelf);
					if (i < Members.Count - 1) Out += ", ";
				}
			}
			else
			{
				Out += State.GenerateName(RetType);
			}

			return Out;
		}

		public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
		{
			if (Id is TypeOfFunction)
			{
				var FuncType = Id as TypeOfFunction;
				Out = "static " + GetFuncTypeName(State, FuncType);
				return SimpleRecResult.Succeeded;
			}
			else if (Id is NonstaticFunctionType)
			{
				var NFuncType = Id as NonstaticFunctionType;
				var FuncType = NFuncType.Child.RealId as TypeOfFunction;
				Out = GetFuncTypeName(State, FuncType);
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class OldFunctionTypeRecognizer : LanguageNode, IIdRecognizer, INameGenerator
	{
		public OldFunctionTypeRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			NewLineRight = Operators = new string[] { "fun" };
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			if (Code.StartsWith(Operators, Skip, IdCharCheck: new IdCharCheck(true)).Index == 0)
			{
				var State = Container.State;
				if (Code[Code.Length - 1] != ')')
				{
					if (Options.EnableMessages)
						State.Messages.Add(MessageId.NotExpected, Code);

					return SimpleRecResult.Failed;
				}

				var BracketPos = Code.GetBracketPos(State, true, Options.EnableMessages);
				if (BracketPos == -1) return SimpleRecResult.Failed;

				var Left = Code.TrimmedSubstring(State, 3, BracketPos - 3, Options.EnableMessages);
				if (!Left.IsValid) return SimpleRecResult.Failed;

				CallingConvention CallConv;
				if (!Modifiers.GetCallConv(Container, ref Left, out CallConv))
					return SimpleRecResult.Failed;

				if (CallConv == CallingConvention.Unknown)
					CallConv = Container.DefaultCallConv;

				var TOptions = Options;
				TOptions.Func = x => x.RealId is Type;

				var RetType = Identifiers.Recognize(Container, Left, TOptions);
				var StrParams = Code.Substring(BracketPos + 1, Code.Length - BracketPos - 2).Trim();

				var Flags = VarDeclarationListFlags.EnableUnnamed | VarDeclarationListFlags.EnableInitValue;
				if (Options.EnableMessages) Flags |= VarDeclarationListFlags.EnableMessages;
				var DeclList = VarDeclarationList.Create(Container, StrParams, null, Flags);
				if (DeclList == null || RetType == null) return SimpleRecResult.Failed;

				if (!(RetType.RealId is Type))
				{
					State.Messages.Add(MessageId.UnknownType, Left);
					return SimpleRecResult.Failed;
				}

				var Params = DeclList.ToFuncParams(Container.GetPlugin());
				if (Params == null || Params.Contains(null)) return SimpleRecResult.Failed;

				Ret = new TypeOfFunction(Container, CallConv, RetType, Params);
				if (Options.Func == null || Options.Func(Ret)) return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}

		public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
		{
			if (Id is TypeOfFunction)
			{
				var FuncType = Id as TypeOfFunction;
				var FuncCh = FuncType.Children;
				var Rec = State.Language.Root.GetObject<CallingConventionRecognizer>();
				var Mod = Rec.GetCallingConventionName(FuncType.CallConv);

				Out = Mod + " " + FuncCh[0].Name.ToString() + "(";
				for (var i = 1; i < FuncCh.Length; i++)
				{
					Out += State.GenerateName(FuncCh[i].TypeOfSelf);
					if (i < FuncCh.Length - 1) Out += ", ";
				}

				Out += ")";
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class ArrayRecognizer : LanguageNode, IIdRecognizer, INameGenerator
	{
		public ArrayRecognizer(LanguageNode Parent)
			: base(Parent)
		{
			Operators = NewLineRight = new string[] { "[" };
			NewLineLeft = new string[] { "[", "]" };
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			var Position = Code.Length - 1;
			if (Position >= 0 && Code[Position] == ']')
			{
				var State = Container.State;
				var ZPos = Code.GetBracketPos(State, true, Options.EnableMessages);
				if (ZPos == -1) return SimpleRecResult.Failed;

				var Left = Code.TrimmedSubstring(State, 0, ZPos, Options.EnableMessages);
				if (!Left.IsValid) return SimpleRecResult.Failed;

				var TOptions = Options;
				TOptions.Func = x => x.RealId is Type;
				
				var TypeOfVals = Identifiers.Recognize(Container, Left, TOptions);
				if (TypeOfVals == null) return SimpleRecResult.Failed;

				var RTypeOfVals = TypeOfVals.RealId as Type;
				if (RTypeOfVals == null || (RTypeOfVals.TypeFlags & TypeFlags.CanBeArrayType) == 0)
				{
					if (Options.EnableMessages)
						State.Messages.Add(MessageId.UnknownId, Code);

					return SimpleRecResult.Failed;
				}

				var StrParams = Code.Substring(ZPos + 1, Position - ZPos - 1).Trim();
				if (StrParams.IsEqual("?"))
				{
					Ret = new NonrefArrayType(Container, TypeOfVals, null);
					if (Options.Func == null || Options.Func(Ret))
						return SimpleRecResult.Succeeded;
				}
				else if (StrParams.IsEqual("*"))
				{
					if ((RTypeOfVals.TypeFlags & TypeFlags.CanBePointer) == 0)
					{
						if (Options.EnableMessages)
							State.Messages.Add(MessageId.UnknownId, Code);

						return SimpleRecResult.Failed;
					}

					Ret = new PointerAndLength(Container, TypeOfVals);
					if (Options.Func == null || Options.Func(Ret))
						return SimpleRecResult.Succeeded;
				}

				var SplParams = RecognizerHelper.SplitToParameters(State, StrParams, 
					',', Options.EnableMessages, true);

				if (SplParams == null) return SimpleRecResult.Failed;

				if (SplParams.Length == 0 || SplParams[0].Length == 0)
				{
					for (var i = 0; i < SplParams.Length; i++)
					{
						if (SplParams[i].Length > 0)
						{
							State.Messages.Add(MessageId.NotExpected, SplParams[i]);
							return SimpleRecResult.Failed;
						}
					}

					var IndexCount = SplParams.Length == 0 ? 1 : SplParams.Length;
					Ret = new RefArrayType(Container, TypeOfVals, IndexCount);
					if (Options.Func == null || Options.Func(Ret))
						return SimpleRecResult.Succeeded;
				}
				else
				{
					var Plugin = Container.GetPlugin();
					Plugin.GetPlugin<EvaluatorPlugin>().MustBeConst = true;

					var Lengths = new int[SplParams.Length];
					for (var i = 0; i < SplParams.Length; i++)
					{
						var Node = Expressions.CreateExpression(SplParams[i], Plugin);
						var ConstCh = Node as ConstExpressionNode;
						if (ConstCh == null) return SimpleRecResult.Failed;

						if (!(ConstCh.Type is NonFloatType))
						{
							if (Options.EnableMessages)
								State.Messages.Add(MessageId.MustBeInteger, StrParams);

							return SimpleRecResult.Failed;
						}

						if (!VerifyArrayLength(State, ConstCh, StrParams, Options.EnableMessages))
							return SimpleRecResult.Failed;

						Lengths[i] = (int)ConstCh.Integer;
					}

					Ret = new NonrefArrayType(Container, TypeOfVals, Lengths);
					if (Options.Func == null || Options.Func(Ret))
						return SimpleRecResult.Succeeded;
				}
			}

			return SimpleRecResult.Unknown;
		}

		public bool VerifyArrayLength(CompilerState State, ConstExpressionNode ConstNode, CodeString Code, bool EnableMessages)
		{
			if (ConstNode.Integer < 0)
			{
				if (EnableMessages)
					State.Messages.Add(MessageId.ArrayLengthTooSmall, Code);

				return false;
			}
			else if (ConstNode.Integer > int.MaxValue)
			{
				if (EnableMessages)
					State.Messages.Add(MessageId.ArrayLengthTooBig, Code);

				return false;
			}

			return true;
		}

		public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
		{
			if (Id is PointerAndLength)
			{
				var PAndL = Id as PointerAndLength;
				Out = State.GenerateName(PAndL.Child) + "[*]";
				return SimpleRecResult.Succeeded;
			}
			else if (Id is NonrefArrayType)
			{
				var Arr = Id as NonrefArrayType;
				Out = State.GenerateName(Arr.TypeOfValues) + "[";
				if (Arr.Lengths == null) Out += "?";
				else Out += string.Join(", ", Arr.Lengths);

				Out += "]";
				return SimpleRecResult.Succeeded;
			}
			else if (Id is RefArrayType)
			{
				var Arr = Id as RefArrayType;
				Out = State.GenerateName(Arr.TypeOfValues) + "[";
				for (var i = 1; i < Arr.Dimensions; i++)
					Out += ",";

				Out += "]";
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}

	public class SimpleIdRegocnizer : LanguageNode, IIdRecognizer, INameGenerator
	{
		public SimpleIdRegocnizer(LanguageNode Parent)
			: base(Parent)
		{
		}

		public SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Ret)
		{
			if (Code.IsValidIdentifierName)
			{
				var List = Container.GetIdentifier(Code.ToString(), Options.Mode, Options.Func);
				Ret = Identifiers.SelectIdentifier(Container.State, List, Code, Options);
				if (Ret == null) return SimpleRecResult.Failed;

				if (!Identifiers.VerifyAccess(Container, Ret, Code, Options.EnableMessages))
					return SimpleRecResult.Failed;

				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}

		public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
		{
			if (Id.Name.IsValid)
			{
				Out = Id.Name.ToString();
				return SimpleRecResult.Succeeded;
			}

			return SimpleRecResult.Unknown;
		}
	}
}
