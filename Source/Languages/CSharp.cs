using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Zinnia;
using Zinnia.Recognizers;
using Zinnia.Languages;
using Zinnia.Languages.Zinnia;

namespace Zinnia.Languages.CSharp
{/*
	public struct CSharpCommand
	{
		public CodeString Code;
		public bool Processed;

		public CSharpCommand(CodeString Code)
		{
			this.Code = Code;
			this.Processed = false;
		}
	}

	public class CSharpScopeData
	{
		public bool Computed;
		public CSharpCommand[] List;
	}

	public static class CSharpHelper
	{
		public static bool ComputeScopeData(ScopeNode Scope)
		{
			var RetValue = true;
			var Code = Scope.Code;
			var File = Code.File;
			var Start = 0;

			var Data = Scope.Data.GetOrCreate<CSharpScopeData>();
			var List = new List<CSharpCommand>();
			Data.Computed = true;

			for (var i = 0; i < Code.Length; i++)
			{
				var Char = Code[i];
				if (Char == ';')
				{
					var SubCode = Code.Substring(Start, i - Start).Trim();
					List.Add(new CSharpCommand(SubCode));
					Start = i + 1;
				}
				else if (Char == '{')
				{
					var Command = Code.Substring(i);
					var Pos = Command.GetBracketPos(Scope.State);
					if (Pos == -1)
					{
						RetValue = false;
					}
					else
					{
						var SubCode = Code.Substring(Start, i + Pos - Start + 1).Trim();
						List.Add(new CSharpCommand(SubCode));
						Start = i + 1;
					}
				}
			}

			Data.List = List.ToArray();
			return RetValue;
		}

		public static bool ForEachLine(ScopeNode Scope, Predicate<CodeString> Func)
		{
			return ForEachLine(Scope, (Code, CommIndex) => Func(Code));
		}

		public static bool ForEachLine(ScopeNode Scope, Func<CodeString, int, bool> Func)
		{
			var Data = Scope.Data.GetOrCreate<CSharpScopeData>();
			if (!Data.Computed && !ComputeScopeData(Scope))
				return false;

			return ForEachLine(Data, Func);
		}

		public static bool ForEachLine(CSharpScopeData Data, Func<CodeString, int, bool> Func)
		{
			var RetValue = true;
			for (var i = 0; i < Data.List.Length; i++)
			{
				var Comm = Data.List[i];
				if (!Comm.Processed && !Func(Comm.Code, i))
					RetValue = false;
			}

			return RetValue;
		}
	}

	public class CSharpInnerScopeRecognizer : IInnerScopeRecognizer
	{
		public CodeString GetInnerScope(CompilerState State, CodeString Code)
		{
			if (Code[0] == '{' && Code[Code.Length - 1] == '}')
				return Code;

			State.Messages.Add(MessageId.MissingInnerScope, Code);
			return new CodeString();
		}
	}

	public class CSharpTypeDeclRecognizer : ITypeDeclRecognizer
	{
		public static string[] Strings = new string[] { "class", "struct", "enum", "flag" };

		public bool Recognize(NonCodeScope Scope, TypeDeclarationList Out)
		{
			var State = Scope.State;
			var File = Scope.Code.File;
			var ScopeData = Scope.Data.GetOrCreate<CSharpScopeData>();

			var RetValue = CSharpHelper.ForEachLine(Scope, (Code, CommIndex) =>
			{
				var Mods = Modifiers.Recognize(Scope, ref Code);
				if (Mods == null) return false;

				var Result = Code.StartsWith(Strings, IdCharCheck: new IdCharCheck(true));
				if (Result.Index == -1) return true;

				TypeDeclType Type;
				if (Result.Index == 0) Type = TypeDeclType.Class;
				else if (Result.Index == 1) Type = TypeDeclType.Struct;
				else if (Result.Index == 2) Type = TypeDeclType.Enum;
				else if (Result.Index == 3) Type = TypeDeclType.Flag;
				else throw new ApplicationException();

				var Command = Code.Substring(0, Result.String.Length).Trim();
				Code = Code.Substring(Result.NextChar).Trim();
				ScopeData.List[CommIndex].Processed = true;

				var Name = Code.Word();
				if (Name.Length == 0)
				{
					State.Messages.Add(MessageId.UnnamedIdentifier, Command);
					return false;
				}
				else
				{
					var Base = new CodeString();
					var FirstLine = Code.FirstLine;

					if (FirstLine.IsValid && FirstLine.Length > 0 && FirstLine[0] == ':')
					{
						Code = Code.Substring(FirstLine.Length).Trim();
						Base = FirstLine.TrimmedSubstring(State, 1);
						if (!Base.IsValid) return false;
					}

					var Inner = State.GetInnerScope(Code, Name);
					if (!Inner.IsValid) return false;

					Out.Add(new TypeDeclaration(Scope, Name, Type, Base, Inner, Mods));
				}

				return true;
			});

			return RetValue;
		}
	}

	public class CSharpAliasDeclRecognizer : IAliasDeclRecognizer
	{
		public static string String = "using";

		public bool Recognize(NonCodeScope Scope, AliasDeclarationList Out)
		{
			var State = Scope.State;
			var File = Scope.Code.File;
			var ScopeData = Scope.Data.GetOrCreate<CSharpScopeData>();
			var SkipHandlers = State.Language.GlobalHandlers;

			var RetValue = CSharpHelper.ForEachLine(Scope, (Code, CommIndex) =>
			{
				var Mods = Modifiers.Recognize(Scope, ref Code);
				if (Mods == null) return false;

				if (!Code.StartsWith(String, IdCharCheck: new IdCharCheck(true)))
					return true;

				if (Code.Find('=', Handlers: SkipHandlers) != -1)
					return true;

				Code = Code.Substring(String.Length).Trim();
				ScopeData.List[CommIndex].Processed = true;

				var SplString = RecognizerHelper.SplitToParameters(State, Code, ',');
				if (SplString == null) return false;

				for (var i = 0; i < SplString.Length; i++)
				{
					var e = SplString[i];
					var p = e.Find('=', Handlers: SkipHandlers);
					if (p == -1)
					{
						State.Messages.Add(MessageId.DeficientExpr, e);
						return false;
					}

					var Val = Code.TrimmedSubstring(State, p + 1);
					if (!Val.IsValid) return false;

					Code = Code.Substring(0, p).Trim();
					var Name = Code.Word(Back: true);

					if (Code.Length > 0)
					{
						State.Messages.Add(MessageId.TypeCannotBeSpecified, Code);
						return false;
					}

					Out.Add(new AliasDeclaration(Scope, Name, Val, Mods));
				}

				return true;
			});

			return RetValue;
		}
	}

	public class CSharpCodeFileProcessor : ICodeFileProcessor
	{
		public static string[] Remarks = new string[] { "/*", "//" };

		public bool Process(AssemblyScope Scope)
		{
			var RetValue = true;
			var State = Scope.State;
			var Code = Scope.Code;
			var File = Code.File;
			var LineCount = File.GetLineCount();
			var SkippingHandlers = State.Language.SkippingHandlers;

			for (var i = 0; i < LineCount; i++)
			{
				if (File.IsEmptyLine(i)) continue;

				var Line = File.GetLines(i);
				var Result = Line.Find(Remarks, Handlers: SkippingHandlers);
				if (Result.Index == 0)
				{
					var Position = Line.Index + Result.Position;
					var Right = Code.Substring(Position);
					var EndPos = Right.Find("*//*", Handlers: SkippingHandlers); asdasd

					if (EndPos == -1)
					{
						var ErrStr = Line.Substring(Result.Position, Result.String.Length);
						State.Messages.Add(MessageId.ZNumErr, ErrStr);
						RetValue = false;
					}

					File.RemoveCode(Position, EndPos);
				}
				else if (Result.Index == 1)
				{
					var Position = Line.Index + Result.Position;
					var Length = Line.FirstLineLength - Result.Position;
					File.RemoveCode(Position, Length, i);
				}
			}

			if (!RetValue) return false;

			var Preprocessor = new Preprocessor(Scope);
			for (var i = 0; i < LineCount; i++)
			{
				if (File.IsEmptyLine(i)) continue;

				var Line = File.GetLines(i);
				var Res = Preprocessor.PreprocessLine(Line);
				if (Res != SimpleRecResult.Unknown)
				{
					if (Res == SimpleRecResult.Failed) RetValue = false;
					else File.RemoveCode(Line);
				}
			}

			if (!Preprocessor.CheckConditions())
				RetValue = false;

			return RetValue;
		}
	}

	public class CSharpNamespaceDeclRecognizer : INamespaceDeclRecognizer
	{
		public static string[] Strings = new string[] { "namespace", "using" };

		public bool Recognize(NamespaceScope Scope, NamespaceDeclList Out)
		{
			var State = Scope.State;
			var File = Scope.Code.File;
			var ScopeData = Scope.Data.GetOrCreate<CSharpScopeData>();
			var SkippingHandlers = State.Language.SkippingHandlers;

			return CSharpHelper.ForEachLine(Scope, (Code, CommIndex) =>
			{
				var Result = Code.StartsWith(Strings, null, new IdCharCheck(true));
				if (Result.Index == -1) return true;

				var Command = Code.Substring(0, Result.String.Length).Trim();
				Code = Code.Substring(Result.NextChar).Trim();
				ScopeData.List[CommIndex].Processed = true;

				int Pos;
				if (Result.Index == 0)
				{
					Pos = Code.Find('{', Handlers: SkippingHandlers);
					if (Pos == -1)
					{
						State.Messages.Add(MessageId.MissingInnerScope, Code);
						return false;
					}
				}
				else if (Result.Index == 1)
				{
					Pos = Code.Length;
				}
				else
				{
					throw new ApplicationException();
				}

				var Name = Code.Substring(0, Pos);
				if (Name.Length == 0)
				{
					State.Messages.Add(MessageId.UnnamedIdentifier, Command);
					return false;
				}

				var Names = new List<CodeString>();
				if (!RecognizerHelper.SplitToParameters(State, Name, '.', Names))
					return false;

				for (var i = 0; i < Names.Count; i++)
					Names[i] = Names[i].Trim();

				if (Result.Index == 0)
				{
					Code = Code.Substring(Name.Length).Trim();
					var Inner = State.GetInnerScope(Code, Name);
					if (!Inner.IsValid) return false;

					Out.Add(new NamespaceDecl(Scope, NamespaceDeclType.Declare, Names, Inner));
				}
				else if (Result.Index == 1)
				{
					Out.Add(new NamespaceDecl(Scope, NamespaceDeclType.Use, Names));
				}
				else
				{
					throw new ApplicationException();
				}

				return true;
			});
		}
	}

	public class CSharpConstDeclRecognizer : IConstDeclRecognizer
	{
		public bool Recognize(NonCodeScope Scope, ConstDeclarationList Out)
		{
			var State = Scope.State;
			var RetValue = true;

			if (Scope is EnumScope)
			{
				var EScope = Scope as EnumScope;
				var Rec = State.Language.ParameterRecognizer;
				var List = Rec.SplitToParameters(State, Scope.Code);
				if (List == null) return false;

				for (var i = 0; i < List.Length; i++)
				{
					var DeclStr = List[i];
					var Pos = DeclStr.Find('=', true);
					var Name = Pos == -1 ? DeclStr : DeclStr.TrimmedSubstring(State, 0, Pos);
					var Value = Pos == -1 ? new CodeString() : DeclStr.TrimmedSubstring(State, Pos + 1);

					if (!Name.IsValidIdentifierName)
					{
						State.Messages.Add(MessageId.NotValidName, Name);
						return false;
					}

					Out.Add(new ConstDeclaration(Scope, Name, EScope.EnumType, Value, null));
				}
			}
			else
			{
				var VarRec = State.Language.VarDeclRecognizer;
				var File = Scope.Code.File;
				var ScopeData = Scope.Data.GetOrCreate<CSharpScopeData>();

				RetValue = CSharpHelper.ForEachLine(Scope, (Code, CommIndex) =>
				{
					var Mods = Modifiers.Recognize(Scope, ref Code);
					if (Mods != null && Modifiers.Contains<ConstModifier>(Mods))
					{
						var VarDecls = VarDeclarationList.Create(Scope, Code);
						if (VarDecls == null || !VarDecls.VerifyInitVal(Scope))
							return false;

						Out.AddRange(VarDecls.ToConstDecls(Scope, Mods));
						ScopeData.List[CommIndex].Processed = true;
					}

					return true;
				});
			}

			return RetValue;
		}
	}

	public class CSharpDeclarationRecognizer : BasicDeclarationRecognizer
	{
		public CSharpDeclarationRecognizer()
			: base(CSharpHelper.ForEachLine)
		{
		}
	}

	public class CSharpCodeProcessor : CodeProcessor
	{
		public override string SelfName
		{
			get { return "this"; }
		}

		public override string BaseName
		{
			get { return "base"; }
		}

		public override bool Process(CodeScopeNode Scope)
		{
			var State = Scope.State;
			if (Scope is FunctionScope)
			{
				var FSData = Scope.Data.Get<AfterDeclarationData>();
				if (FSData != null && FSData.AfterDeclaration.IsValid)
				{
					FSData.InAfterDeclaration = true;
					if (!ZinniaHelper.ForEachLine(State, FSData.AfterDeclaration, Code =>
						Scope.RecognizeCommand(Code))) return false;

					FSData.InAfterDeclaration = false;
				}
			}
			
			return CSharpHelper.ForEachLine(Scope, (Code, CommIndex) =>
				Scope.RecognizeCommand(Code));
		}
	}

	public class CSharpIdRecognizers : LanguageNode
	{
		public CSharpIdRecognizers(LanguageNode Parent)
			: base(Parent)
		{
			Children = new LanguageNode[]
			{
				new TupleTypeRecognizer(this),
				new RefTypeRecognizer(this),
				new ArrayRecognizer(this),
				new MemberTypeRecognizer(this),
				new FunctionTypeRecognizer(this),
				new PointerTypeRecognizer(this),
				new SimpleIdRegocnizer(this),
			};
		}
	}

	public class CSharpModRecognizers : LanguageNode
	{
		public CSharpModRecognizers(LanguageNode Parent)
			: base(Parent)
		{
			Children = new LanguageNode[]
			{
				new AlignModifierRecognizer(this),
				new CallingConventionRecognizer(this),
				new AccessModifierRecognizer(this),
				new FlagModifierRecognizer(this),
				new ParamFlagModifierRecognizer(this),
				new ConstModifierRecognizer(this),
				new AssemblyNameRecognizer(this),
				new NoBaseModifierRecognizer(this),
			};
		}
	}

	public class CSharpExprRecognizers : LanguageNode
	{
		public CSharpExprRecognizers(LanguageNode Parent)
			: base(Parent)
		{
			Children = new LanguageNode[]
			{
				new KeywordExprRecognizer(this),
				new NumberRecognizer(this),
				new StringRecognizer(this),
				new CharRecognizer(this),
				new RefRecognizer(this),
				new NewRecognizer(this),
				new IfThenRecognizer(this),
				new VarDeclRecignizer(this),
				new IsAsRecognizer(this),
				new LogicalRecognizer(this),
				new NotRecognizer(this),
				new AssignmentRecognizer(this),
				new RelEquRecognizer(this, true),
				new RefEqualityRecognizer(this),
				new TupleCreatingRecognizer(this),
				new ArrayCreatingRecognizer(this),
				new RangeRecognizer(this),
				new AdditiveRecognizer(this),
				new MultiplicativeRecognizer(this),
				new BitwiseRecognizer(this),
				new ShiftRecognizer(this),
				new NullCoalescingRecognizer(this),

				new NegateRecognizer(this),
				new ComplementRecognizer(this),
				new AddressRecognizer(this),
				new IncDecRecognizer(this),
				new IndirectionRecognizer(this),
				new CastRecognizer(this),
				new IndexRecognizer(this),

				new CallRecognizer(this, new List<IBuiltinFuncRecognizer>()
				{
					new DefaultRecognizer(),
					new IsDefinedRecognizer(),
					new SizeOfRecognizer(),
					new ReinterpretCastRecognizer(),
					new DataPointerRecognizer(),
					new IncBinRecognizer(),
				}),

				new SafeNavigationRecognizer(this),
				new MemberRecognizer(this),
				new ExprIdRecognizer(this),

				new SimpleArgRecognizer(this),
				new GenericRecognizer(this),
				new BracketGroupRecognizer(this)
			};
		}
	}

	public class CSharpCommRecognizers : LanguageNode
	{
		public CSharpCommRecognizers(LanguageNode Parent)
			: base(Parent)
		{
			Children = new LanguageNode[]
			{
				new CtorCallRecognizer(this),
				new ExtraStorageRecognizer(this),
				new AutoRetRecognizer(this),
				new NonAfterDeclBlocker(this),

				new IfCommRecognizer(this),
				//new ForCommRecognizer(this),
				new ForInCommRecognizer(this),
				new ElseCommRecognizer(this),
				new BreakCommRecognizer(this),
				new ContinueCommRecognizer(this),
				new SwitchCommRecognizer(this),
				new WhileCommRecognizer(this),
				new DoCommRecognizer(this),
				new RepeatCommRecognizer(this),
				new CycleCommRecognizer(this),
				new TryCommRecognizer(this),
				new CheckedUncheckedCommRecognizer(this),

				new ReturnCommRecognizer(this),
				new WithCommRecognizer(this),
				new GotoCommRecognizer(this),
				new LabelCommRecognizer(this),
				new ThrowCommRecognizer(this),

				new VarDeclCommRecognizer(this),
				new ExprCommRecognizer(this),
			};
		}
	}

	public class CSharpLanguage : Language
	{
		public CSharpLanguage()
		{
			Children = new LanguageNode[]
			{
				new CSharpCommRecognizers(this),
				new BracketRecognizer(this),
				new CSharpModRecognizers(this),
				new CSharpExprRecognizers(this),
				new CSharpIdRecognizers(this),
			};

			InnerScopeRecognizer = new CSharpInnerScopeRecognizer();
			VarDeclRecognizer = new ZinniaVarDeclRecognizer();
			NamespaceDeclRecognizer = new CSharpNamespaceDeclRecognizer();
			TypeDeclRecognizer = new CSharpTypeDeclRecognizer();
			ConstDeclRecognizer = new CSharpConstDeclRecognizer();
			DeclarationRecognizer = new CSharpDeclarationRecognizer();
			AliasDeclRecognizer = new CSharpAliasDeclRecognizer();

			CodeProcessor = new CSharpCodeProcessor();
			CodeFileProcessor = new CSharpCodeFileProcessor();
			Init();
		}
	}*/
}
