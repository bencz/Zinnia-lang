using System.Collections.Generic;

namespace Zinnia.Base
{
    public class MacroArgPlugin : ExpressionPlugin
    {
        public List<string> Parameters;

        public MacroArgPlugin(PluginRoot Parent, List<string> Parameters)
            : base(Parent)
        {
            this.Parameters = Parameters;
        }

        public override PluginResult NewNode(ref ExpressionNode Node)
        {
            if (Node is StrExpressionNode)
            {
                var StrNode = Node as StrExpressionNode;
                if (Parameters != null)
                {
                    var Index = Parameters.IndexOf(Node.Code.ToString());
                    if (Index != -1)
                    {
                        Node = Parent.NewNode(new MacroArgNode(Index, Node.Code));
                        return PluginResult.Ready;
                    }
                }
            }

            return PluginResult.Succeeded;
        }
    }

    public class PluginForDefine : PluginRoot
    {
        public PluginForDefine(IdContainer Container, List<string> Parameters)
            : base(Container)
        {
            Plugins = new IExpressionPlugin[]
            {
                new MacroArgPlugin(this, Parameters),
                new PreProcPlugin(this, false),
                new IdRecognizerPlugin(this, true),
                new TypeMngrPlugin(this, null, TypeMngrPluginFlags.EnableUntypedNodes),
                new EvaluatorPlugin(this, false),
            };
        }
    }

    public class PreProcPlugin : ExpressionPlugin
    {
        public bool IfDef;

        public PreProcPlugin(PluginRoot Parent, bool IfDef)
            : base(Parent)
        {
            this.IfDef = IfDef;
        }

        public bool CheckMacroNodes(ExpressionNode Node)
        {
            return Node.CheckChildren(Ch =>
            {
                var MacroCh = Ch as MacroExpressionNode;
                if (MacroCh != null && MacroCh.Macro.Parameters.Count != 0)
                {
                    State.Messages.Add(MessageId.ParamCount, MacroCh.Code);
                    return false;
                }

                return true;
            });
        }

        public override PluginResult NewNode(ref ExpressionNode Node)
        {
            if (Node is StrExpressionNode)
            {
                var IdNode = Node as StrExpressionNode;
                var Preproc = State.GlobalContainer.Preprocessor;
                var Macro = Preproc.GetMacro(IdNode.Code.ToString());

                if (IfDef)
                {
                    Node = Parent.NewNode(Constants.GetBoolValue(Container, Macro != null, Node.Code));
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
                else if (Macro != null)
                {
                    if (Macro.Value == null)
                    {
                        State.Messages.Add(MessageId.MacroWithoutValue, Node.Code);
                        return PluginResult.Failed;
                    }

                    Macro.Used = true;
                    if (Macro.Parameters == null || Macro.Parameters.Count == 0)
                        Node = Macro.Value.Copy(Parent, Mode: BeginEndMode.None, Code: IdNode.Code);
                    else Node = Parent.NewNode(new MacroExpressionNode(Macro, Node.Code));
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
            }

            // ------------------------------------------------------------------------------------
            else if (Node is OpExpressionNode)
            {
                var OpNode = Node as OpExpressionNode;
                var Ch = OpNode.Children;
                var Op = OpNode.Operator;

                if (Op == Operator.Call && Ch[0] is MacroExpressionNode)
                {
                    var MFunc = Ch[0] as MacroExpressionNode;
                    var Macro = MFunc.Macro;
                    Macro.Used = true;

                    if (Ch.Length != Macro.Parameters.Count + 1)
                    {
                        State.Messages.Add(MessageId.ParamCount, Node.Code);
                        return PluginResult.Failed;
                    }

                    var Nodes = new AutoAllocatedList<ExpressionNode>();
                    var LnkNodes = new AutoAllocatedList<LinkedExprNode>();
                    for (var i = 1; i < Ch.Length; i++)
                    {
                        if (!(Ch[i] is OpExpressionNode))
                        {
                            Nodes.Add(Ch[i]);
                        }
                        else
                        {
                            var N = new LinkedExprNode(Ch[i]);
                            LnkNodes.Add(N);
                            Nodes.Add(new LinkingNode(N, Node.Code));
                        }
                    }

                    PluginFunc Func = (ref ExpressionNode x) =>
                    {
                        if (x is MacroArgNode)
                        {
                            var ArgIndex = (x as MacroArgNode).Index;
                            x = Nodes[ArgIndex].Copy(Parent, Mode: BeginEndMode.None);
                            return x == null ? PluginResult.Failed : PluginResult.Ready;
                        }

                        return PluginResult.Succeeded;
                    };

                    Node = Macro.Value.Copy(Parent, Func, BeginEndMode.None);
                    if (Node == null) return PluginResult.Failed;

                    Node.LinkedNodes.AddRange(LnkNodes);
                    Node = Parent.NewNode(Node);
                    return Node == null ? PluginResult.Failed : PluginResult.Ready;
                }
            }

            // ------------------------------------------------------------------------------------
            if (!CheckMacroNodes(Node)) return PluginResult.Failed;
            else return PluginResult.Succeeded;
        }

        public override PluginResult End(ref ExpressionNode Node)
        {
            if (Node != null && Node is MacroExpressionNode)
            {
                var MNode = Node as MacroExpressionNode;
                State.Messages.Add(MessageId.ParamCount, MNode.Code);
                return PluginResult.Failed;
            }

            return PluginResult.Succeeded;
        }
    }

    public class Macro
    {
        public CodeString Name;
        public ExpressionNode Value;
        public List<string> Parameters;
        public bool Used = false;

        public Macro(CodeString Name, ExpressionNode Value, List<string> Parameters)
        {
            this.Name = Name;
            this.Value = Value;
            this.Parameters = Parameters;
        }
    }

    public struct PreprocessorCondition
    {
        public CodeString Declaration;
        public bool Value;
        public bool WasTrue;

        public PreprocessorCondition(CodeString Declaration, bool Value)
        {
            this.Declaration = Declaration;
            this.Value = Value;
            this.WasTrue = Value;
        }
    }

    public enum PreprocConditionMode : byte
    {
        Normal,
        IfDef,
        IfNotDef,
    }

    public class Preprocessor
    {
        public CompilerState State;
        public IdContainer Container;

        public List<Macro> Macroes = new List<Macro>();
        public List<PreprocessorCondition> Conditions =
            new List<PreprocessorCondition>();

        public bool Define(Macro Macro)
        {
            if (IsDefined(Macro.Name.ToString()))
            {
                State.Messages.Add(MessageId.MacroAlreadyDefined, Macro.Name);
                return false;
            }

            Macroes.Add(Macro);
            return true;
        }

        public bool Define(string Name)
        {
            return Define(new Macro(new CodeString(Name), null, null));
        }

        public bool Define(string Name, int Value)
        {
            var CName = new CodeString(Name);
            var Node = Constants.GetIntValue(State.GlobalContainer, Value, CName);
            return Define(new Macro(CName, Node, null));
        }

        public bool Define(string Name, double Value)
        {
            var CName = new CodeString(Name);
            var Node = Constants.GetDoubleValue(State.GlobalContainer, Value, CName);
            return Define(new Macro(CName, Node, null));
        }

        public bool Define(string Name, string Value)
        {
            var CName = new CodeString(Name);
            var Node = Constants.GetStringValue(State.GlobalContainer, Value, CName);
            return Define(new Macro(CName, Node, null));
        }

        public void Redefine(Macro m)
        {
            RemoveMacro(m.Name.ToString());
            Macroes.Add(m);
        }

        public bool IsDefined(string Name)
        {
            for (var i = 0; i < Macroes.Count; i++)
                if (Macroes[i].Name.IsEqual(Name)) return true;

            return false;
        }

        public Macro GetMacro(string Name)
        {
            for (var i = 0; i < Macroes.Count; i++)
                if (Macroes[i].Name.IsEqual(Name)) return Macroes[i];

            return null;
        }

        public bool RemoveMacro(string Name)
        {
            var Pos = -1;
            var Count = Macroes.Count;
            for (int i = 0; i < Count; i++)
                if (Macroes[i].Name.IsEqual(Name))
                {
                    Pos = i;
                    break;
                }

            if (Pos != -1)
            {
                Macroes.RemoveAt(Pos);
                return true;
            }

            return false;
        }

        public bool IsInDefBlock
        {
            get
            {
                for (var i = 0; i < Conditions.Count; i++)
                    if (!Conditions[i].Value) return false;

                return true;
            }
        }

        public int GetLastCondition(CodeString Code)
        {
            var l = Conditions.Count - 1;
            if (l < 0)
            {
                State.Messages.Add(MessageId.NoMatchingCommand, Code);
                return -1;
            }

            return l;
        }

        public bool DoElse(CodeString Code)
        {
            var Last = GetLastCondition(Code);
            if (Last == -1) return false;

            var Cond = Conditions[Last];
            if (Cond.Value || !Cond.WasTrue)
            {
                Cond.Value = !Cond.Value;
                if (Cond.Value) Cond.WasTrue = true;

                Conditions[Last] = Cond;
            }

            return true;
        }

        public bool DoEndif(CodeString Code)
        {
            var l = GetLastCondition(Code);
            if (l == -1) return false;

            Conditions.RemoveAt(l);
            return true;
        }

        public bool CheckConditions()
        {
            if (Conditions.Count > 0)
            {
                State.Messages.Add(MessageId.NoEndif, Conditions[0].Declaration);
                return false;
            }

            return true;
        }

        public bool DoElseIf(CodeString Line, CodeString MLine, PreprocConditionMode CondMode)
        {
            if (!DoElse(Line))
                return false;

            var Last = Conditions.Count - 1;
            var Cond = Conditions[Last];

            if (Cond.Value)
            {
                Cond.Declaration = Line;
                if (!DoIf(Line, MLine, CondMode, out Cond.Value))
                    return false;

                Conditions[Last] = Cond;
            }

            return true;
        }

        public bool DoIf(CodeString Line, CodeString MLine, PreprocConditionMode CondMode, out bool Result)
        {
            if (IsInDefBlock)
            {
                var Plugin = new PluginForGlobals(Container);
                Plugin.GetPlugin<TypeMngrPlugin>().RetType = Container.GlobalContainer.CommonIds.Boolean;
                Plugin.GetPlugin<PreProcPlugin>().IfDef = CondMode != PreprocConditionMode.Normal;
                Plugin.GetPlugin<EvaluatorPlugin>().MustBeConst = true;

                var Node = Expressions.CreateExpression(MLine, Plugin);
                if (Node != null)
                {
                    var CNode = Node as ConstExpressionNode;
                    Result = CondMode == PreprocConditionMode.IfNotDef ? !CNode.Bool : CNode.Bool;
                    return true;
                }
                else
                {
                    Result = true;
                    return false;
                }
            }

            Result = true;
            return true;
        }

        public bool DoIf(CodeString Line, CodeString MLine, PreprocConditionMode CondMode)
        {
            bool Result;
            if (!DoIf(Line, MLine, CondMode, out Result))
                return false;

            Conditions.Add(new PreprocessorCondition(Line, Result));
            return true;
        }

        public Preprocessor(IdContainer Container)
        {
            this.State = Container.State;
            this.Container = Container;
        }

        Macro ProcMacro(CodeString MLine)
        {
            var MacroName = MLine.Word();
            if (!MacroName.IsValidIdentifierName)
            {
                State.Messages.Add(MessageId.NotValidName, MacroName);
                return null;
            }

            var Params = new CodeString();
            var Handlers = State.Language.GlobalHandlers;
            if (MLine.Length > 0 && MLine[0] == '(')
            {
                var zp = MLine.GetBracketPos(false, Handlers);
                if (zp > 0)
                {
                    Params = MLine.Substring(1, zp - 1).Trim();
                    MLine = MLine.Substring(zp + 1).Trim();
                }
            }

            List<string> ParamList = null;
            if (Params.IsValid)
            {
                var PStrList = RecognizerHelper.SplitToParameters(State, Params, ',');
                if (PStrList == null) return null;

                ParamList = new List<string>();
                for (var i = 0; i < PStrList.Length; i++)
                {
                    var String = PStrList[i].ToString();
                    if (!PStrList[i].IsValidIdentifierName)
                    {
                        State.Messages.Add(MessageId.WrongParamList, PStrList[i]);
                        return null;
                    }

                    if (ParamList.Contains(String))
                    {
                        State.Messages.Add(MessageId.IdAlreadyDefined, PStrList[i]);
                        return null;
                    }

                    ParamList.Add(String);
                }
            }

            MLine = MLine.Trim();
            if (MLine.Length > 0)
            {
                var Plugin = new PluginForDefine(Container, ParamList);
                var Node = Expressions.CreateExpression(MLine, Plugin);

                if (Node != null)
                    return new Macro(MacroName, Node, ParamList);
                else return null;
            }

            return new Macro(MacroName, null, ParamList);
        }

        private SimpleRecResult ProcCommands(string Order, CodeString Line, CodeString MLine)
        {
            CodeString Name;
            var ReDef = false;

            switch (Order)
            {
                case "error":
                    State.Messages.Add(MessageId.PreprocError, MLine);
                    return SimpleRecResult.Failed;

                case "warning":
                    State.Messages.Add(MessageId.PreprocWarning, MLine);
                    return SimpleRecResult.Succeeded;

                case "info":
                    State.Messages.Add(MessageId.PreprocInfo, MLine);
                    return SimpleRecResult.Succeeded;

                case "redef":
                    ReDef = true;
                    goto case "define";

                case "define":
                    var Macro = ProcMacro(MLine);
                    if (Macro != null)
                    {
                        if (ReDef) Redefine(Macro);
                        else if (!Define(Macro))
                            return SimpleRecResult.Failed;

                        return SimpleRecResult.Succeeded;
                    }
                    else
                    {
                        return SimpleRecResult.Failed;
                    }

                case "undef":
                    Name = MLine.Word();
                    if (!Name.IsValidIdentifierName)
                    {
                        State.Messages.Add(MessageId.NotValidName, Name);
                        return SimpleRecResult.Failed;
                    }

                    if (!RemoveMacro(Name.ToString()))
                    {
                        State.Messages.Add(MessageId.NoMacro, Name);
                        return SimpleRecResult.Failed;
                    }

                    if (MLine.Length > 0) State.Messages.Add(MessageId.ParamCount, MLine);
                    return SimpleRecResult.Succeeded;

                default:
                    State.Messages.Add(MessageId.UnknownCommand, Line);
                    return SimpleRecResult.Failed;
            }
        }

        public SimpleRecResult ProcessLine(CodeString Line)
        {
            Line = Line.Trim();
            if (Line.Length > 0 && Line[0] == '#')
            {
                var MLine = Line.Substring(1).Trim();
                var Order = MLine.Word().ToString();

                if (Order == "ifdef")
                {
                    if (!DoIf(Line, MLine, PreprocConditionMode.IfDef))
                        return SimpleRecResult.Failed;
                }
                else if (Order == "ifndef")
                {
                    if (!DoIf(Line, MLine, PreprocConditionMode.IfNotDef))
                        return SimpleRecResult.Failed;
                }
                else if (Order == "if")
                {
                    if (!DoIf(Line, MLine, PreprocConditionMode.Normal))
                        return SimpleRecResult.Failed;
                }
                else if (Order == "elif")
                {
                    if (!DoElseIf(Line, MLine, PreprocConditionMode.Normal))
                        return SimpleRecResult.Failed;
                }
                else if (Order == "elifdef")
                {
                    if (!DoElseIf(Line, MLine, PreprocConditionMode.IfDef))
                        return SimpleRecResult.Failed;
                }
                else if (Order == "elifndef")
                {
                    if (!DoElseIf(Line, MLine, PreprocConditionMode.IfNotDef))
                        return SimpleRecResult.Failed;
                }
                else if (Order == "else")
                {
                    if (!DoElse(Line))
                        return SimpleRecResult.Failed;
                }
                else if (Order == "endif")
                {
                    if (!DoEndif(Line))
                        return SimpleRecResult.Failed;
                }
                else if (IsInDefBlock)
                {
                    return ProcCommands(Order, Line, MLine);
                }

                return SimpleRecResult.Succeeded;
            }

            return IsInDefBlock ? SimpleRecResult.Unknown : SimpleRecResult.Succeeded;
        }
    }
}
