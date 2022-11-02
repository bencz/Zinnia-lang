using System;
using System.Collections.Generic;

namespace Zinnia.Base
{
    public enum IdRecResult : byte
    {
        Unknown,
        Succeeded,
        ErrorSyntax,
        ErrorNotFound,
        ErrorAmbiguous,
    }

    public enum SimpleRecResult : byte
    {
        Unknown,
        Failed,
        Succeeded,
    }

    public enum ExprRecResult : byte
    {
        Unknown,
        Ready,
        Succeeded,
        Failed,
    }

    public enum ConditionResult : byte
    {
        Unknown,
        False,
        True,
    }

    public struct SkippingHandlerResult
    {
        public int Index;
        public bool SkipCurrent;

        public SkippingHandlerResult(int Index, bool SkipCurrent = false)
        {
            this.Index = Index;
            this.SkipCurrent = SkipCurrent;
        }
    }

    public interface IResultSkippingHandler
    {
        SkippingHandlerResult SkipResult(ref ResultSkippingManager RSM);
    }

    public struct ResultSkippingManager
    {
        public DataList Data;
        public IList<IResultSkippingHandler> SkippingHandlers;
        public bool DoNotSkipBrackets;

        public StringSlice String;
        public int Current;
        public bool Back;
        private int ContinueAt;
        private bool DontCheckNext;

        public int RemainingLength
        {
            get { return Back ? Current + 1 : String.Length - Current; }
        }

        public char CurrentChar
        {
            get { return String[Current]; }
        }

        public ResultSkippingManager(IList<IResultSkippingHandler> SkippingHandlers, StringSlice String, bool Back = false)
        {
            this.Data = new DataList();
            this.SkippingHandlers = SkippingHandlers;
            this.DoNotSkipBrackets = false;

            this.String = String;
            this.Current = -1;
            this.Back = Back;
            this.ContinueAt = Back ? String.Length - 1 : 0;
            this.DontCheckNext = false;
        }

        public ResultSkippingManager(IList<IResultSkippingHandler> SkippingHandlers, CodeString String, bool Back = false)
            : this(SkippingHandlers, String.String, Back)
        {
        }

        public bool Loop()
        {
            if (ContinueAt == -1)
            {
                if (Back) Current--;
                else Current++;
            }
            else
            {
                Current = ContinueAt;
                ContinueAt = -1;
            }

            if (Back && Current < 0) return false;
            if (!Back && Current >= String.Length) return false;

            if (SkippingHandlers != null && CurrentChar != ' ' && !DontCheckNext)
            {
                for (var i = 0; i < SkippingHandlers.Count; i++)
                {
                    var Result = SkippingHandlers[i].SkipResult(ref this);
                    if (Result.Index != -1)
                    {
                        var P = Result.Index;
                        if ((Back && P > Current) || (!Back && P < Current))
                            throw new ApplicationException();

                        if (P < 0 || P >= String.Length)
                            return false;

                        if (Result.SkipCurrent) Current = P;
                        else { ContinueAt = P; DontCheckNext = true; }
                        return true;
                    }
                }
            }

            DontCheckNext = false;
            return true;
        }
    }

    public abstract class CodeProcessor : ICodeProcessor, INameGenerator
    {
        public abstract string SelfName { get; }
        public abstract string BaseName { get; }
        public abstract bool Process(CodeScopeNode Scope);

        public SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out)
        {
            if (Id is SelfVariable)
            {
                Out = SelfName;
                return SimpleRecResult.Succeeded;
            }
            else if (Id is BaseVariable)
            {
                Out = BaseName;
                return SimpleRecResult.Succeeded;
            }

            return SimpleRecResult.Unknown;
        }
    }

    [Flags]
    public enum LanguageNodeFlags
    {
        None = 0,
        NotOpRecongizer = 1,
        FindSkipFromAll = 2,
    }

    public struct LanguageInitData
    {
        public List<string> SkipList;
        public string[] SkipFromAll;
    }

    public class LanguageNode
    {
        public DataList Data = new DataList();
        public Language Language;
        public LanguageNode Parent;
        public IList<LanguageNode> Children;
        public IList<IResultSkippingHandler> SkippingHandlers;
        public LanguageNodeFlags Flags;

        public string[] Operators;
        public string[] Skip;
        public string[] NewLineLeft;
        public string[] NewLineRight;
        public string[] NewLineLeftSkip;
        public string[] NewLineRightSkip;
        public string[] OnlyLeft;
        public string[] OnlyLeftSkip;
        public string[] OnlyRight;
        public string[] OnlyRightSkip;

        public void ForEach(Action<LanguageNode> Func)
        {
            Func(this);

            if (Children != null)
            {
                for (var i = 0; i < Children.Count; i++)
                    Children[i].ForEach(Func);
            }
        }

        public T GetParent<T>(Predicate<T> Func = null) where T : LanguageNode
        {
            var Node = this;
            while (Node != null)
            {
                var TNode = Node as T;
                if (TNode != null && (Func == null || Func(TNode)))
                    return TNode;

                Node = Node.Parent;
            }

            return null;
        }

        public LanguageNode(Language Language, LanguageNodeFlags Flags = LanguageNodeFlags.None)
        {
            this.Language = Language;
            this.SkippingHandlers = new List<IResultSkippingHandler>();
            this.Flags = Flags;
        }

        public LanguageNode(LanguageNode Parent, LanguageNodeFlags Flags = LanguageNodeFlags.None)
        {
            this.Parent = Parent;
            this.Language = Parent.Language;
            this.SkippingHandlers = new List<IResultSkippingHandler>();
            this.Flags = Flags;
        }

        void GetObjects<T>(List<T> List) where T : class
        {
            ForEach(x =>
            {
                if (x is T)
                    List.Add(x as T);
            });
        }

        public T[] GetObjects<T>() where T : class
        {
            var List = new List<T>();
            GetObjects(List);
            return List.ToArray();
        }

        public T GetObject<T>(bool MustContain = true) where T : class
        {
            var Array = GetObjects<T>();
            if (Array.Length == 0)
            {
                if (MustContain)
                    throw new ApplicationException("Recognizers are not valid");

                return null;
            }

            return Array[0];
        }

        public T[] GetObjectsStoreable<T>(bool Store = true) where T : class
        {
            if (!Store)
            {
                return GetObjects<T>();
            }
            else
            {
                var Objects = Data.Get<T[]>();
                if (Objects == null)
                {
                    Objects = GetObjects<T>();
                    Data.Set(Objects);
                }

                return Objects;
            }
        }

        protected void InitRecognizers(LanguageInitData InitData)
        {
            var AllOperators = new List<string>();
            var AllNewLineLeft = new List<string>();
            var AllNewLineRight = new List<string>();
            var AllOnlyLeft = new List<string>();
            var AllOnlyRight = new List<string>();

            for (var i = Children.Count - 1; i >= 0; i--)
            {
                var Rec = Children[i];
                if (Rec.Operators != null)
                {
                    if ((Flags & LanguageNodeFlags.FindSkipFromAll) != 0)
                        Rec.Skip = Helper.GetSkipList(Rec.Operators, InitData.SkipFromAll);
                    else Rec.Skip = Helper.GetSkipList(Rec.Operators, InitData.SkipList);

                    InitData.SkipList.AddRange(Rec.Operators);
                    if ((Rec.Flags & LanguageNodeFlags.NotOpRecongizer) == 0)
                        AllOperators.AddRange(Rec.Operators);
                }

                if (Rec.NewLineLeft != null) AllNewLineLeft.AddRange(Rec.NewLineLeft);
                if (Rec.NewLineRight != null) AllNewLineRight.AddRange(Rec.NewLineRight);
                if (Rec.OnlyLeft != null) AllOnlyLeft.AddRange(Rec.OnlyLeft);
                if (Rec.OnlyRight != null) AllOnlyRight.AddRange(Rec.OnlyRight);
            }

            Operators = Helper.ToArrayWithoutSame(AllOperators);
            NewLineLeft = Helper.ToArrayWithoutSame(AllNewLineLeft);
            NewLineRight = Helper.ToArrayWithoutSame(AllNewLineRight);
            OnlyLeft = Helper.ToArrayWithoutSame(AllOnlyLeft);
            OnlyRight = Helper.ToArrayWithoutSame(AllOnlyRight);
        }

        public virtual void Init(LanguageInitData InitData)
        {
            Language.GlobalHandlers.Foreach(x => SkippingHandlers.Add(x));

            if (Children != null)
            {
                for (var i = Children.Count - 1; i >= 0; i--)
                    Children[i].Init(InitData);
            }

            if (NewLineLeft != null)
                OnlyLeft = Helper.GetStrings(NewLineLeft, NewLineRight);

            if (NewLineRight != null)
                OnlyRight = Helper.GetStrings(NewLineRight, NewLineLeft);

            if (Children != null)
                InitRecognizers(InitData);

            OnlyLeftSkip = Helper.GetSkipList(OnlyLeft, Operators);
            OnlyRightSkip = Helper.GetSkipList(OnlyRight, Operators);
            NewLineLeftSkip = Helper.GetSkipList(NewLineLeft, Operators);
            NewLineRightSkip = Helper.GetSkipList(NewLineRight, Operators);
        }
    }

    public interface IInnerScopeRecognizer
    {
        CodeString GetInnerScope(CompilerState State, CodeString Code);
    }

    public interface IDeclarationRecognizer
    {
        bool Recognize(NonCodeScope Scope);
    }

    public interface IIdRecognizer
    {
        SimpleRecResult Recognize(IdContainer Container, CodeString Code, GetIdOptions Options, ref Identifier Out);
    }

    public interface IFullNameGenerator
    {
        string GetFullName(Identifier Id, bool Overload = false);
    }

    public interface INameGenerator
    {
        SimpleRecResult GenerateName(CompilerState State, Identifier Id, ref string Out);
    }

    public interface INamespaceDeclRecognizer
    {
        bool Recognize(NamespaceScope Scope, NamespaceDeclList Out);
    }

    public interface IConstDeclRecognizer
    {
        bool Recognize(NonCodeScope Scope, ConstDeclarationList Out);
    }

    public interface ITypeDeclRecognizer
    {
        bool Recognize(NonCodeScope Scope, TypeDeclarationList Out);
    }

    public interface IVarDeclRecognizer
    {
        bool Recognize(IdContainer Container, CodeString Code, bool EnableMessages, VarDeclarationList Out);
    }

    public interface IAliasDeclRecognizer
    {
        bool Recognize(NonCodeScope Scope, AliasDeclarationList Out);
    }

    public interface ICodeProcessor
    {
        string SelfName { get; }
        string BaseName { get; }
        bool Process(CodeScopeNode Scope);
    }

    public interface IGlobalContainerProcessor
    {
        bool Process(GlobalContainer Scope);
    }

    public interface ICodeFileProcessor
    {
        bool Process(AssemblyScope Scope);
    }

    public interface IExprRecognizer
    {
        ExprRecResult Recognize(CodeString Code, PluginRoot Plugin, ref ExpressionNode Out);
    }

    public interface ICallParamRecognizer
    {
        ExpressionNode[] ProcessParameters(PluginRoot Plugin, CodeString Function, CodeString[] Parameters);
        ExpressionNode[] ProcessParameters(PluginRoot Plugin, CodeString Function, CodeString Parameters);
        bool GetParameters(CompilerState State, CodeString Code, out CodeString Function, out CodeString Parameters);
    }

    public interface IParameterRecognizer
    {
        CodeString[] SplitToParameters(CompilerState State, CodeString Code, bool EnableMessages = true);
    }

    public interface IGenericRecognizer
    {
        SimpleRecResult GetGenericParams(CompilerState State, ref CodeString Code,
             out CodeString[] Out, bool EnableMessages = true);
    }

    public interface IGroupRecognizer
    {
        NodeGroup GetGroups(PluginRoot Plugin, CodeString Code);
    }

    public interface ICommRecognizer
    {
        SimpleRecResult Recognize(CodeScopeNode Scope, CodeString Code);
    }

    public interface IFinishableCommRecognizer : ICommRecognizer
    {
        bool Finish(IdContainer Container);
    }

    public interface IRetValLessRecognizer
    {
        ExpressionNode Recognize(CodeString Code, PluginRoot Plugin);
    }

    public interface IModRecognizer
    {
        SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out);
    }

    public struct CommandInnerSeparatorResult
    {
        public CodeString Command;
        public CodeString Inner;
        public FindResult FindRes;

        public CommandInnerSeparatorResult(CodeString Command, CodeString Inner, FindResult FindRes)
        {
            this.Command = Command;
            this.Inner = Inner;
            this.FindRes = FindRes;
        }
    }

    [Flags]
    public enum CommandInnerSeparatorFlags : byte
    {
        None = 0,
        NoEmptyScopeWarning = 1,
        InnerIsOptional = 2,
    }

    public interface ICommandInnerSeparator
    {
        CommandInnerSeparatorResult Separate(CompilerState State, CodeString Code,
            CommandInnerSeparatorFlags Flags = CommandInnerSeparatorFlags.None);
    }

    public interface ICommentRecognizer
    {
        bool Process(CompilerState State, CodeString Code);
    }

    [Flags]
    public enum LangaugeFlags : byte
    {
        None = 0,
        ConvertParametersToTuple = 1,
        AllowMemberFuncStaticRef = 2,
    }

    public abstract class Language
    {
        public LanguageNode Root;
        public LangaugeFlags Flags;
        public IExprRecognizer[] ExprRecognizers;
        public IIdRecognizer[] IdRecognizers;
        public ICommRecognizer[] CommRecognizers;
        public IModRecognizer[] ModRecognizers;
        public INameGenerator[] NameGenerators;
        public IResultSkippingHandler[] GlobalHandlers;

        public IParameterRecognizer ParameterRecognizer;
        public IGenericRecognizer GenericRecognizer;
        public IGroupRecognizer GroupRecognizer;

        public IRetValLessRecognizer RetValLessRecognizer;
        public IInnerScopeRecognizer InnerScopeRecognizer;
        public IVarDeclRecognizer VarDeclRecognizer;
        public ITypeDeclRecognizer TypeDeclRecognizer;
        public IConstDeclRecognizer ConstDeclRecognizer;
        public IAliasDeclRecognizer AliasDeclRecognizer;
        public INamespaceDeclRecognizer NamespaceDeclRecognizer;
        public IDeclarationRecognizer DeclarationRecognizer;
        public ICommandInnerSeparator CommandInnerSeparator;
        public IFullNameGenerator FullNameGenerator;

        public IGlobalContainerProcessor GlobalContainerProcessor;
        public ICodeFileProcessor CodeFileProcessor;
        public ICodeProcessor CodeProcessor;

        public Language()
        {
        }

        public void Init()
        {
            var InitData = new LanguageInitData();
            InitData.SkipList = new List<string>();

            var AllOps = new List<string>();
            Root.ForEach(x =>
            {
                if (x.Operators != null)
                    AllOps.AddRange(x.Operators);
            });

            InitData.SkipFromAll = Helper.ToArrayWithoutSame(AllOps);
            Init(InitData);
        }

        public void Init(LanguageInitData InitData)
        {
            ExprRecognizers = Root.GetObjectsStoreable<IExprRecognizer>(false);
            IdRecognizers = Root.GetObjectsStoreable<IIdRecognizer>(false);
            CommRecognizers = Root.GetObjectsStoreable<ICommRecognizer>(false);
            ModRecognizers = Root.GetObjectsStoreable<IModRecognizer>(false);
            NameGenerators = Root.GetObjectsStoreable<INameGenerator>(false);
            GlobalHandlers = Root.GetObjectsStoreable<IResultSkippingHandler>(false);

            ParameterRecognizer = Root.GetObject<IParameterRecognizer>();
            GenericRecognizer = Root.GetObject<IGenericRecognizer>();
            GroupRecognizer = Root.GetObject<IGroupRecognizer>();
            Root.Init(InitData);
        }
    }
}
