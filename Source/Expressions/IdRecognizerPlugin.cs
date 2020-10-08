using System;
using System.Collections.Generic;
using System.Linq;
using Zinnia.Recognizers;

namespace Zinnia
{
    public enum AutoDeclareMode
    {
        Disable,
        Enabled,
        Always,
    }

    public delegate ExpressionNode DeclarationHandler(PluginRoot Plugin, CodeString Code, Identifier Type, CodeString Name);

    struct ScopeResolutions
    {
        public ExpressionNode MemberOf;
        public ExpressionNode[] Scopes;
        public ExpressionNode Member;
        public CodeString Code;
        public bool Static;

        public ScopeResolutions(ExpressionNode MemberOf, ExpressionNode[] Scopes,
            ExpressionNode Member, CodeString Code, bool Static)
        {
            this.MemberOf = MemberOf;
            this.Scopes = Scopes;
            this.Member = Member;
            this.Code = Code;
            this.Static = Static;
        }

        public bool MakeMemberOf(PluginRoot Plugin, Identifier Id)
        {
            var Node = Plugin.NewNode(new IdExpressionNode(Id, Code));
            if (Node == null) return false;

            MakeMemberOf(Node);
            return true;
        }

        public void MakeMemberOf(ExpressionNode Node)
        {
            if (!Static) throw new InvalidOperationException();

            Static = false;
            Scopes = Scopes.Union(MemberOf).ToArray();
            MemberOf = Node;
        }

        public static ScopeResolutions Get(ExpressionNode Node)
        {
            var Ret = new ScopeResolutions();
            Ret.Code = Node.Code;

            var List = new List<ExpressionNode>();
            var First = true;

            while (true)
            {
                var Op = Expressions.GetOperator(Node);
                var Ch = Node.Children;

                if (Op != Operator.ScopeResolution)
                {
                    if (Op != Operator.Member || !(Ch[0].Type.RealId is Type))
                    {
                        Ret.Scopes = List.ToArray();
                        Ret.MemberOf = Node;
                        Ret.Static = true;
                        return Ret;
                    }

                    if (!First) List.Insert(0, Ch[1]);
                    else Ret.Member = Ch[1];

                    Ret.Scopes = List.ToArray();
                    Ret.MemberOf = Ch[0];
                    return Ret;
                }
                else
                {
                    if (!First) List.Insert(0, Ch[1]);
                    else Ret.Member = Ch[1];
                    First = false;
                }

                Node = Ch[0];
            }
        }

        public ExpressionNode GetNode(PluginRoot Plugin)
        {
            var MemberCh = new ExpressionNode[]
                {
                    MakeCasts(Plugin, MemberOf),
                    Member,
                };

            if (MemberCh[0] == null)
                return null;

            return Plugin.NewNode(new OpExpressionNode(Operator.Member,
                MemberCh, Code, ExpressionFlags.DisableVirtualMember));
        }

        public ExpressionNode MakeCasts(PluginRoot Plugin, ExpressionNode Node)
        {
            if (Static)
                throw new InvalidOperationException();

            Node = Plugin.FinishNode(Node);
            if (Node == null) return null;

            var State = Plugin.State;
            var CurrentType = MemberOf.Type;

            for (var i = 0; i < Scopes.Length; i++)
            {
                var Base = GetBaseForNode(CurrentType, Scopes[i]);
                if (Base == null) return null;

                var CastCh1 = Plugin.NewNode(new IdExpressionNode(Base, Node.Code));
                if (CastCh1 == null) return null;

                var CastCh = new ExpressionNode[] { Node, CastCh1 };
                Node = Plugin.NewNode(new OpExpressionNode(Operator.Cast, CastCh, Node.Code));
                if (Node == null) return null;

                CurrentType = Base;
            }

            return Node;
        }

        public static Identifier GetBaseForNode(Identifier CurrentType, ExpressionNode Node)
        {
            var State = CurrentType.Container.State;

            Identifier Base = null;
            if (Node is StrExpressionNode)
            {
                var SCurrentType = CurrentType.UnderlyingStructureOrRealId as StructuredType;
                if (SCurrentType != null)
                {
                    var List = SCurrentType.SearchBase(null, Node.Code.ToString());
                    Base = Identifiers.SelectIdentifier(State, List, Node.Code);
                    if (Base == null) return null;
                }
            }
            else if (Node is IdExpressionNode)
            {
                var IdScopesi = Node as IdExpressionNode;
                throw new NotImplementedException();
            }

            if (Base == null)
            {
                State.Messages.Add(MessageId.UnknownId, Node.Code);
                return null;
            }

            return Base;
        }
    }

    public class IdRecognizerPlugin : ExpressionPlugin
    {
        [Flags]
        enum ResolveNodeFlags
        {
            None = 0,
            Member = 1,
            Indexed = 2,
        }

        public AutoAllocatedList<CodeString> Dependencies;
        public bool DoNotFail;
        public EnumType ConvertEnums;

        public AutoDeclareMode AutoDeclareMode = AutoDeclareMode.Disable;
        public DeclarationHandler OnDeclaration;

        public IdRecognizerPlugin(PluginRoot Parent, bool DoNotFail = false)
            : base(Parent)
        {
            this.DoNotFail = DoNotFail;
        }

        public override PluginResult End(ref ExpressionNode Node)
        {
            var Res = ResolveNode(ref Node);
            if (Res == PluginResult.Ready)
            {
                Node = Parent.End(Node);
                return Node == null ? PluginResult.Failed : PluginResult.Ready;
            }

            return Res;
        }

        ExpressionNode GetId(CodeString Code, bool EnableMessages,
            OverloadSelectionData OverloadData = new OverloadSelectionData(), IdContainer Container = null)
        {
            if (Container == null)
                Container = this.Container;

            var Options = GetIdOptions.Default;
            Options.OverloadData = OverloadData;
            Options.EnableMessages = EnableMessages;

            var Id = Container.RecognizeIdentifier(Code, Options);
            if (Id == null) return null;

            if (Id is Constructor)
            {
                if (Container.Parent == null) throw new ApplicationException();
                return GetId(Code, EnableMessages, OverloadData, Container.Parent);
            }

            return Expressions.CreateReference(Container, Id, Parent, Code, EnableMessages);
        }

        ExpressionNode DeclareVar(CodeString Name, Identifier T)
        {
            if (OnDeclaration != null) return OnDeclaration(Parent, Name, T, Name);
            else return Parent.DeclareVarAndCreateIdNode(Name, T, Name);
        }

        ExpressionNode GetOrDeclareId(CodeString Name, bool EnableMessages, Identifier T = null,
            OverloadSelectionData OverloadData = new OverloadSelectionData())
        {
            if (AutoDeclareMode == AutoDeclareMode.Always)
                return DeclareVar(Name, T);

            if (T != null && Name.IsValidIdentifierName)
            {
                var Ret = GetId(Name, true, OverloadData);
                if (Ret != null) return Ret;

                if (AutoDeclareMode == AutoDeclareMode.Enabled)
                    return DeclareVar(Name, T);

                if (EnableMessages) GetId(Name, false, OverloadData);
                return null;
            }

            return GetId(Name, EnableMessages, OverloadData);
        }

        ExpressionNode ResolveNode(ExpressionNode Node, Identifier DeclarationType = null,
            OverloadSelectionData OverloadData = new OverloadSelectionData(), ResolveNodeFlags Flags = ResolveNodeFlags.None)
        {
            var Res = ResolveNode(ref Node, DeclarationType, OverloadData, Flags);
            return Res == PluginResult.Failed ? null : Node;
        }

        public override PluginResult ForceContinue(ref ExpressionNode Node)
        {
            return ForceContinue(ref Node, null, new OverloadSelectionData(), ResolveNodeFlags.None);
        }

        Type GetType(ExpressionNode Node, out bool Static)
        {
            var Type = (Type)null;
            if (Node.Type != null)
                Type = Node.Type.RealId as Type;

            if (Type == null && Node is IdExpressionNode)
            {
                var IdNode = Node as IdExpressionNode;
                Type = IdNode.Identifier.TypeOfSelf.RealId as Type;
            }

            if (Type is TypeOfType)
            {
                var IdNode = Node as IdExpressionNode;
                Static = true;
                return IdNode.Identifier.RealId as Type;
            }

            Static = false;
            return Type;
        }

        PluginResult ForceContinue(ref ExpressionNode Node, Identifier DeclarationType = null,
            OverloadSelectionData OverloadData = new OverloadSelectionData(), ResolveNodeFlags Flags = ResolveNodeFlags.None)
        {
            if (Node is StrExpressionNode)
            {
                var StrNode = Node as StrExpressionNode;
                var Code = StrNode.Code;

                Node = GetOrDeclareId(Code, !DoNotFail, DeclarationType, OverloadData);

                if (Node == null)
                {
                    if (!DoNotFail)
                        return PluginResult.Failed;

                    Node = StrNode;
                    Node.InterrupterPlugin = -1;
                    Dependencies.Add(StrNode.Code);
                }
                else
                {
                    if (ResolveNode(ref Node) == PluginResult.Failed)
                        return PluginResult.Failed;

                    return PluginResult.Ready;
                }
            }
            else if (Node is IdExpressionNode)
            {
                var IdNode = Node as IdExpressionNode;
                var Id = IdNode.Identifier.RealId;

                if (!(Id is Constructor))
                {
                    if ((Flags & ResolveNodeFlags.Member) == 0)
                    {
                        var Lang = State.Language;
                        if ((Lang.Flags & LangaugeFlags.AllowMemberFuncStaticRef) == 0 ||
                            !(IdNode.Identifier.RealId is Function))
                        {
                            if (IdNode.Identifier.IsInstanceIdentifier)
                            {
                                State.Messages.Add(MessageId.NonStatic, Node.Code);
                                return PluginResult.Failed;
                            }
                        }
                    }
                    else
                    {
                        if (!IdNode.Identifier.IsInstanceIdentifier)
                        {
                            State.Messages.Add(MessageId.Static, Node.Code);
                            return PluginResult.Failed;
                        }
                    }
                }
            }
            else if (Node is LinkingNode)
            {
                var LinkingNode = Node as LinkingNode;
                var LinkedNode = LinkingNode.LinkedNode;

                LinkedNode.Node = ResolveNode(LinkedNode.Node, DeclarationType, OverloadData);
                if (LinkedNode.Node == null) return PluginResult.Failed;
            }
            else if (Node is OpExpressionNode)
            {
                var OpNode = Node as OpExpressionNode;
                var Ch = OpNode.Children;
                var Op = OpNode.Operator;

                if (Op == Operator.Tuple)
                {
                    var Failed = false;
                    var TupleDeclType = DeclarationType != null ? DeclarationType.RealId as TupleType : null;
                    for (var i = 0; i < Ch.Length; i++)
                    {
                        var T = (Identifier)null;
                        if (TupleDeclType != null)
                            T = TupleDeclType.StructuredScope.IdentifierList[i].TypeOfSelf;

                        if ((Ch[i] = ResolveNode(Ch[i], T)) == null) Failed = true;
                    }

                    if (Failed) return PluginResult.Failed;
                }
                else if (Op == Operator.Member)
                {
                    if (Ch[1] is StrExpressionNode)
                    {
                        var Res = ForceContinue_MemberNode(ref Node, OverloadData);
                        if (Res != PluginResult.Succeeded) return Res;
                    }

                    Ch[1] = ResolveNode(Ch[1], Flags: Flags | ResolveNodeFlags.Member);
                    if (Ch[1] == null) return PluginResult.Failed;
                }
                else if (Op == Operator.ScopeResolution)
                {
                    if (Ch[1] is StrExpressionNode)
                    {
                        var Res = ForceContinue_ScopeResolution(ref Node, OverloadData);
                        if (Res != PluginResult.Succeeded) return Res;
                    }

                    Ch[1] = ResolveNode(Ch[1], Flags: Flags | ResolveNodeFlags.Member);
                    if (Ch[1] == null) return PluginResult.Failed;
                }
                else
                {
                    throw new ApplicationException();
                }
            }
            else
            {
                throw new ApplicationException();
            }

            return PluginResult.Succeeded;
        }

        PluginResult ResolveNode(ref ExpressionNode Node, Identifier DeclarationType = null,
            OverloadSelectionData OverloadData = new OverloadSelectionData(), ResolveNodeFlags Flags = ResolveNodeFlags.None)
        {
            if (Node.InterrupterPlugin != -1)
            {
                if (Node.InterrupterPlugin != Array.IndexOf(Parent.Plugins, this))
                    return PluginResult.Succeeded;

                var Res = ForceContinue(ref Node, DeclarationType, OverloadData, Flags);
                if (Res != PluginResult.Succeeded) return Res;

                if (Parent.FinishNode(ref Node, false) == PluginResult.Failed)
                    return PluginResult.Failed;

                return PluginResult.Ready;
            }

            return PluginResult.Succeeded;
        }

        PluginResult ForceContinue_ScopeResolution(ref ExpressionNode Node,
            OverloadSelectionData OverloadData = new OverloadSelectionData())
        {
            var Ch = Node.Children;
            var ScopeReses = ScopeResolutions.Get(Node);

            if (ScopeReses.Static)
            {
                if (Ch[0] is StrExpressionNode)
                {
                    var Options = GetIdOptions.DefaultForType;
                    Options.OverloadData = OverloadData;

                    var Id = Container.RecognizeIdentifier(Ch[0].Code, Options);
                    if (Id == null) return PluginResult.Failed;

                    Ch[0] = Parent.NewNode(new IdExpressionNode(Id, Ch[0].Code));
                    if (Ch[0] == null) return PluginResult.Failed;
                }

                Ch[0] = ResolveNode(Ch[0]);
                if (Ch[0] == null) return PluginResult.Failed;

                if (!(Ch[0].Type.RealId is TypeOfType))
                {
                    State.Messages.Add(MessageId.MustBeType, Ch[0].Code);
                    return PluginResult.Failed;
                }

                var FScope = Container.FunctionScope;
                if (FScope != null && FScope.SelfVariable != null)
                {
                    var FuncContainer = FScope.Function.Container;
                    var StructuredScope = FuncContainer.RealContainer as StructuredScope;
                    var Structure = StructuredScope.StructuredType;
                    var Ch0Id = Expressions.GetIdentifier(Ch[0]);

                    if (Structure.IsEquivalent(Ch0Id) || Structure.IsSubstructureOf(Ch0Id))
                    {
                        if (!ScopeReses.MakeMemberOf(Parent, FScope.SelfVariable))
                            return PluginResult.Failed;
                    }
                }

                if (ScopeReses.Static)
                    return ForceContinue_MemberNode(ref Node, OverloadData);
            }

            Node = ScopeReses.GetNode(Parent);
            if (Node == null || ResolveNode(ref Node) == PluginResult.Failed)
                return PluginResult.Failed;

            return PluginResult.Ready;
        }

        PluginResult ForceContinue_MemberNode(ref ExpressionNode Node,
            OverloadSelectionData OverloadData = new OverloadSelectionData())
        {
            var Ch = Node.Children;

            bool Static;
            var Ch0Type = GetType(Ch[0], out Static);
            if (Ch0Type == null) return PluginResult.Succeeded;

            var MemberName = Ch[1].Code;
            var Options = GetIdOptions.Default;
            Options.OverloadData = OverloadData;

            if (Ch0Type is NamespaceType && !Static)
            {
                var IdCh0 = Ch[0] as IdExpressionNode;
                var Namespace = IdCh0.Identifier as Namespace;

                var List = Identifiers.SearchMember(Container, Namespace, MemberName.ToString());
                var Id = Identifiers.SelectIdentifier(State, List, MemberName, Options);
                if (Id == null) return PluginResult.Failed;

                Node = Parent.NewNode(new IdExpressionNode(Id, Node.Code));
                if (Node == null || ResolveNode(ref Node) == PluginResult.Failed)
                    return PluginResult.Failed;

                return PluginResult.Ready;
            }
            else if (Ch0Type is EnumType && Static)
            {
                var EnumType = Ch0Type as EnumType;
                var Ret = EnumType.GetValue(State, MemberName);
                if (Ret == null) return PluginResult.Failed;

                Node = Parent.NewNode(new IdExpressionNode(Ret, Node.Code));
                if (Node == null || ResolveNode(ref Node) == PluginResult.Failed)
                    return PluginResult.Failed;

                return PluginResult.Ready;
            }
            else
            {
                var List = Identifiers.SearchMember(Container, Ch0Type, MemberName.ToString());
                var Id = Identifiers.SelectIdentifier(State, List, MemberName, Options);
                if (Id == null) return PluginResult.Failed;

                if (!Identifiers.VerifyAccess(Container, Id, MemberName))
                    return PluginResult.Failed;

                var IdNode = Parent.NewNode(new IdExpressionNode(Id, Node.Code));
                if (IdNode == null) return PluginResult.Failed;

                if (Static)
                {
                    Node = IdNode;
                    if (ResolveNode(ref Node) == PluginResult.Failed)
                        return PluginResult.Failed;

                    return PluginResult.Ready;
                }
                else
                {
                    Ch[1] = IdNode;
                }
            }

            return PluginResult.Succeeded;
        }

        bool ResolveParams(ExpressionNode[] Ch)
        {
            for (var i = 1; i < Ch.Length; i++)
            {
                if (Ch[i] is NamedParameterNode)
                {
                    var ChiCh = Ch[i].Children;
                    ChiCh[0] = ResolveNode(ChiCh[0]);
                    if (ChiCh[0] == null) return false;
                }
                else
                {
                    Ch[i] = ResolveNode(Ch[i]);
                    if (Ch[i] == null) return false;
                }
            }

            return true;
        }

        PluginResult ProcessParams(ref ExpressionNode Node)
        {
            var Ch = Node.Children;
            var Type = Ch[0].Type;
            if (Type == null)
            {
                var IdCh0 = Ch[0] as IdExpressionNode;
                if (IdCh0 != null) Type = IdCh0.Identifier.TypeOfSelf;
                else return PluginResult.Succeeded;
            }

            var FuncType = Type.RealId as TypeOfFunction;
            if (FuncType == null) return PluginResult.Succeeded;

            if (FuncType.Children.Length < Ch.Length)
            {
                if ((State.Language.Flags & LangaugeFlags.ConvertParametersToTuple) != 0)
                {
                    if (FuncType.Children.Length == 2 && Ch.TrueForAll(x => !(x is NamedParameterNode)))
                    {
                        var Param0 = FuncType.Children[1] as FunctionParameter;
                        if (Param0.TypeOfSelf.RealId is TupleType)
                        {
                            var NewCh1 = Parent.NewNode(new OpExpressionNode(Operator.Tuple, Ch.Slice(1), Node.Code));
                            if (NewCh1 == null || ResolveNode(ref NewCh1) == PluginResult.Failed)
                                return PluginResult.Failed;

                            Node.Children = Ch = new ExpressionNode[] { Ch[0], NewCh1 };
                            return PluginResult.Succeeded;
                        }
                    }
                }
            }

            var ExtraParams = new AutoAllocatedList<ExpressionNode>();
            var Ret = new ExpressionNode[FuncType.Children.Length];
            Ret[0] = Ch[0];

            var RetValue = true;
            var NamedParameter = false;
            for (var i = 1; i < Ch.Length; i++)
            {
                if (Ch[i] is NamedParameterNode)
                {
                    var Chi = Ch[i] as NamedParameterNode;
                    var Param = FuncType.GetParameter(Chi.Name.ToString());

                    if (Param == null)
                    {
                        State.Messages.Add(MessageId.UnknownId, Chi.Name);
                        RetValue = false; continue;
                    }

                    var Index = FuncType.GetChildIndex(Param);
                    if (Ret[Index] != null)
                    {
                        State.Messages.Add(MessageId.ParamAlreadySpecified, Chi.Code);
                        RetValue = false; continue;
                    }

                    Ret[Index] = Chi.Children[0];
                    NamedParameter = true;
                }
                else
                {
                    if (NamedParameter)
                    {
                        State.Messages.Add(MessageId.UnnamedParamAfterNamed, Ch[i].Code);
                        RetValue = false; continue;
                    }

                    if (i >= Ret.Length) ExtraParams.Add(Ch[i]);
                    else Ret[i] = Ch[i];
                }
            }

            var LastParam = FuncType.Children[FuncType.Children.Length - 1] as FunctionParameter;
            if (LastParam != null && (LastParam.ParamFlags & ParameterFlags.ParamArray) != 0)
            {
                var First = Ret[Ret.Length - 1];
                if (ExtraParams.Count > 0 || NeedToCreateParamArray(First, LastParam))
                {
                    ExtraParams.Insert(0, First);

                    var ArrCh = ExtraParams.ToArray();
                    var Arr = Parent.NewNode(new OpExpressionNode(Operator.Array, ArrCh, Node.Code));
                    if (Arr == null) return PluginResult.Failed;

                    Ret[Ret.Length - 1] = Arr;
                }
            }
            else if (ExtraParams.Count > 0)
            {
                State.Messages.Add(MessageId.ParamCount, Node.Code);
                return PluginResult.Failed;
            }

            for (var i = 1; i < Ret.Length; i++)
                if (Ret[i] == null)
                {
                    var Param = FuncType.Children[i] as FunctionParameter;
                    if (Param.ConstInitValue != null)
                    {
                        Ret[i] = new ConstExpressionNode(Param.TypeOfSelf, Param.ConstInitValue, Node.Code);
                        if ((Ret[i] = Parent.NewNode(Ret[i])) == null)
                        {
                            RetValue = false;
                            continue;
                        }
                    }
                    else
                    {
                        State.Messages.Add(MessageId.ParamNotSpecified, Node.Code, Param.Name.ToString());
                        RetValue = false; continue;
                    }
                }

            Node.Children = Ret;
            return RetValue ? PluginResult.Succeeded : PluginResult.Failed;
        }

        bool NeedToCreateParamArray(ExpressionNode First, FunctionParameter Param)
        {
            var ParamType = Param.Children[0];
            var ParamBaseType = Identifiers.GetParamArrayBaseType(ParamType);
            return First.Type.CanConvert(ParamBaseType) != TypeConversion.Nonconvertable &&
                First.Type.CanConvert(ParamType) == TypeConversion.Nonconvertable;
        }

        public override PluginResult NewNode(ref ExpressionNode Node)
        {
            for (var i = 0; i < Node.LinkedNodes.Count; i++)
            {
                var LNode = Node.LinkedNodes[i];
                LNode.Node = ResolveNode(LNode.Node);
                if (LNode.Node == null) return PluginResult.Failed;
            }

            if (Node is StrExpressionNode)
            {
                return PluginResult.Interrupt;
            }
            else if (Node is LinkingNode)
            {
                return PluginResult.Interrupt;
            }
            else if (Node is IdExpressionNode)
            {
                var IdNode = Node as IdExpressionNode;
                var Id = IdNode.Identifier.RealId;

                if (Id is LocalVariable)
                {
                    if (!Container.IsSubContainerOf(Id.Container) && Container != Id.Container)
                        throw new ApplicationException();
                }

                if (Id is ConstVariable)
                {
                    var Const = Id as ConstVariable;
                    if (Const.ConstInitValue == null) throw new ApplicationException();

                    Node = Parent.NewNode(new ConstExpressionNode(Const.TypeOfSelf, Const.ConstInitValue, Node.Code));
                    if (Node == null) return PluginResult.Failed;

                    if (ConvertEnums != null && Const.TypeOfSelf == ConvertEnums)
                    {
                        Node = new OpExpressionNode(Operator.Cast, Node.Code)
                        {
                            Children = new ExpressionNode[] { Node },
                            Type = ConvertEnums.TypeOfValues,
                        };

                        if ((Node = Parent.NewNode(Node)) == null)
                            return PluginResult.Failed;
                    }

                    return PluginResult.Ready;
                }

                return PluginResult.Interrupt;
            }
            else if (Node is OpExpressionNode)
            {
                var OpNode = Node as OpExpressionNode;
                var Op = OpNode.Operator;
                var Ch = OpNode.Children;

                if (Op == Operator.Assignment)
                {
                    if ((Ch[1] = ResolveNode(Ch[1])) == null)
                        return PluginResult.Failed;

                    if ((Ch[0] = ResolveNode(Ch[0], Ch[1].Type)) == null)
                        return PluginResult.Failed;
                }
                else if (Op == Operator.Call)
                {
                    if (!ResolveParams(Ch))
                        return PluginResult.Failed;

                    var OverloadData = Expressions.GetOverloadSelectData(Ch);
                    Ch[0] = ResolveNode(Ch[0], OverloadData: OverloadData);
                    if (Ch[0] == null) return PluginResult.Failed;

                    var Res = ProcessParams(ref Node);
                    if (Res != PluginResult.Succeeded) return Res;

                    if ((State.Language.Flags & LangaugeFlags.AllowMemberFuncStaticRef) != 0)
                    {
                        var Ch0Id = Expressions.GetIdentifier(Ch[0]);
                        if (Ch0Id != null && Ch0Id.IsInstanceIdentifier)
                        {
                            State.Messages.Add(MessageId.NonStatic, Ch[0].Code);
                            return PluginResult.Failed;
                        }
                    }
                }
                else if (Op == Operator.NewArray)
                {
                    if (!ResolveParams(Ch))
                        return PluginResult.Failed;

                    if (Ch[0] is StrExpressionNode)
                    {
                        var Type = Container.RecognizeIdentifier(Ch[0].Code, GetIdOptions.DefaultForType);
                        if (Type == null) return PluginResult.Failed;

                        if (Ch.Length < 2)
                        {
                            State.Messages.Add(MessageId.ParamCount, Node.Code);
                            return PluginResult.Failed;
                        }

                        Node.Type = new RefArrayType(Container, Type, Ch.Length - 1);
                        Node.Children = Ch.Slice(1);
                        return PluginResult.Succeeded;
                    }

                    var Ch0Id = Expressions.GetIdentifier(Ch[0]);
                    if (Ch0Id != null && Ch0Id.RealId is Type)
                    {
                        if (Ch.Length < 2)
                        {
                            State.Messages.Add(MessageId.ParamCount, Node.Code);
                            return PluginResult.Failed;
                        }

                        Node.Type = new RefArrayType(Container, Ch0Id, Ch.Length - 1);
                        Node.Children = Ch.Slice(1);
                        return PluginResult.Succeeded;
                    }
                }
                else if (Op == Operator.NewObject)
                {
                    if (!ResolveParams(Ch))
                        return PluginResult.Failed;

                    Identifier ConstructType = null;
                    if (Ch[0] is StrExpressionNode)
                    {
                        ConstructType = Container.RecognizeIdentifier(Ch[0].Code, GetIdOptions.DefaultForType);
                        if (ConstructType == null) return PluginResult.Failed;
                    }
                    else if (Ch[0] is IdExpressionNode)
                    {
                        var IdCh0 = Ch[0] as IdExpressionNode;
                        if (IdCh0.Identifier.RealId is Type) ConstructType = IdCh0.Identifier;
                    }

                    if (ConstructType != null)
                    {
                        Node.Type = ConstructType;
                        if (ConstructType.RealId is AutomaticType)
                        {
                            if (!(Ch[0] is IdExpressionNode))
                            {
                                Ch[0] = Parent.NewNode(new IdExpressionNode(ConstructType, Node.Code));
                                if (Ch[0] == null) return PluginResult.Failed;
                            }

                            Ch[0] = ResolveNode(Ch[0]);
                            return Ch[0] == null ? PluginResult.Failed : PluginResult.Succeeded;
                        }

                        if (Ch.Length == 1 && !(ConstructType.UnderlyingClassOrRealId is ClassType))
                        {
                            OpNode.Children = Ch = null;
                            return PluginResult.Succeeded;
                        }

                        var Structured = ConstructType.UnderlyingClassOrRealId as StructuredType;
                        if (Structured == null)
                        {
                            State.Messages.Add(MessageId.ParamCount, Node.Code);
                            return PluginResult.Failed;
                        }

                        var Options = GetIdOptions.Default;
                        Options.OverloadData = Expressions.GetOverloadSelectData(Ch);
                        Options.Func = x => x is Constructor;

                        var Constructor = Identifiers.GetMember(State, Structured, null, Node.Code, Options);
                        if (Constructor == null) return PluginResult.Failed;

                        if (!Identifiers.VerifyAccess(Container, Constructor, Node.Code))
                            return PluginResult.Failed;

                        Ch[0] = Parent.NewNode(new IdExpressionNode(Constructor, Ch[0].Code));
                        if (Ch[0] == null) return PluginResult.Failed;
                    }

                    Ch[0] = ResolveNode(Ch[0]);
                    if (Ch[0] == null) return PluginResult.Failed;

                    var Res = ProcessParams(ref Node);
                    if (Res != PluginResult.Succeeded) return Res;
                }
                else if (Op == Operator.Member)
                {
                    if ((Ch[0] = ResolveNode(Ch[0])) == null)
                        return PluginResult.Failed;

                    return PluginResult.Interrupt;
                }
                else if (Op == Operator.Tuple || Op == Operator.ScopeResolution)
                {
                    return PluginResult.Interrupt;
                }
                else if (Ch != null)
                {
                    for (var i = 0; i < Ch.Length; i++)
                    {
                        Ch[i] = ResolveNode(Ch[i]);
                        if (Ch[i] == null) return PluginResult.Failed;
                    }

                    return PluginResult.Succeeded;
                }
            }
            else if (Node.Children != null)
            {
                for (var i = 0; i < Node.Children.Length; i++)
                {
                    Node.Children[i] = ResolveNode(Node.Children[i]);
                    if (Node.Children[i] == null) return PluginResult.Failed;
                }
            }

            return PluginResult.Succeeded;
        }
    }
}