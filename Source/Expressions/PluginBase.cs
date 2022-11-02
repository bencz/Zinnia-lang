using System;
using System.Collections.Generic;
using System.Linq;
using Zinnia.Base;
using Zinnia.Recognizers;

namespace Zinnia
{
    public enum PluginResult
    {
        Failed,
        Succeeded,
        Ready,
        Interrupt,
    }

    [Flags]
    public enum BeginEndMode
    {
        None = 0,
        Begin = 1,
        End = 2,
        Both = Begin | End,
    }

    public interface IPluginDeclarationHandler
    {
        bool OnIdentifierDeclared(Identifier Id);
    }

    public interface IExpressionPlugin
    {
        bool Begin();
        PluginResult NewNode(ref ExpressionNode Node);
        PluginResult End(ref ExpressionNode Node);
        PluginResult ForceContinue(ref ExpressionNode Node);
    }

    public class ExpressionPlugin : IExpressionPlugin
    {
        public PluginRoot Parent;
        public IdContainer Container;
        public CompilerState State;

        public virtual PluginResult ForceContinue(ref ExpressionNode Node)
        {
            return PluginResult.Succeeded;
        }

        public virtual PluginResult NewNode(ref ExpressionNode Node)
        {
            return PluginResult.Succeeded;
        }

        public virtual PluginResult End(ref ExpressionNode Node)
        {
            return PluginResult.Succeeded;
        }

        public virtual bool Begin()
        {
            Container = Parent.Container;
            return true;
        }

        public ExpressionPlugin(PluginRoot Parent)
        {
            this.Parent = Parent;
            this.Container = Parent.Container;
            this.State = Parent.State;
        }

        protected PluginResult ResolveNode(ref ExpressionNode Node)
        {
            if (Node.InterrupterPlugin != -1)
            {
                if (Node.InterrupterPlugin != Array.IndexOf(Parent.Plugins, this))
                    return PluginResult.Succeeded;

                var Res = ForceContinue(ref Node);
                if (Res != PluginResult.Succeeded) return Res;

                if (Parent.FinishNode(ref Node, false) == PluginResult.Failed)
                    return PluginResult.Failed;

                return PluginResult.Ready;
            }

            return PluginResult.Succeeded;
        }

        protected ExpressionNode ResolveNode(ExpressionNode Node)
        {
            var Res = ResolveNode(ref Node);
            return Res == PluginResult.Failed ? null : Node;
        }

        protected bool ResolveChildren(ExpressionNode Node)
        {
            return Node.ReplaceChildren(x => ResolveNode(x));
        }
    }

    public class EmptyPluginRoot : PluginRoot
    {
        public EmptyPluginRoot(IdContainer Container)
            : base(Container)
        {
            Plugins = new IExpressionPlugin[0];
        }
    }

    public class PluginRoot
    {
        public CompilerState State;
        public IdContainer Container;
        public IExpressionPlugin[] Plugins;
        public int CallNewNodeFrom = 0;
        public bool CurrentlyUsing = false;

        public Variable CreateVariable(Identifier Type, CodeString Name)
        {
            return Container.CreateVariable(Name, Type);
        }

        public bool DeclareIdentifier(Identifier Id)
        {
            if (Id.Container.FunctionScope != null)
            {
                if (!Id.Container.CanIdDeclared(Id))
                    return false;

                for (var i = 0; i < Plugins.Length; i++)
                {
                    var Plugin = Plugins[i] as IPluginDeclarationHandler;
                    if (Plugin != null && !Plugin.OnIdentifierDeclared(Id))
                        return false;
                }

                return true;
            }
            else
            {
                State.Messages.Add(MessageId.CannotDeclVar, Id.Name);
                return false;
            }
        }

        public Variable CreateAndDeclareVar(Identifier Type, CodeString Name)
        {
            var Ret = CreateVariable(Type, Name);
            if (Ret == null || !DeclareIdentifier(Ret)) return null;
            return Ret;
        }

        public ExpressionNode DeclareVarAndCreateIdNode(CodeString Code, Identifier Type, CodeString Name)
        {
            var Var = CreateAndDeclareVar(Type, Name);
            if (Var == null) return null;

            return NewNode(new IdExpressionNode(Var, Code, ExpressionFlags.IdMustBeAssigned));
        }

        public ExpressionNode DeclareVarAndCreateIdNode(CodeString Code, CodeString TypeName, CodeString Name)
        {
            var Type = Container.RecognizeIdentifier(TypeName, GetIdOptions.DefaultForType);
            if (Type == null) return null;

            return DeclareVarAndCreateIdNode(Code, Type, Name);
        }

        public ExpressionNode FinishNode(ExpressionNode Node, bool CallInterrupter = true)
        {
            var Res = FinishNode(ref Node, CallInterrupter);
            return Res == PluginResult.Failed ? null : Node;
        }

        public ExpressionNode NewNode(ExpressionNode Node)
        {
            if (NewNode(ref Node) == PluginResult.Failed)
                return null;
            else return Node;
        }

        public ExpressionNode End(ExpressionNode Node)
        {
            if (End(ref Node) == PluginResult.Failed)
                return null;
            else return Node;
        }

        public void Reset()
        {
            CurrentlyUsing = false;
        }

        public ExpressionNode Continue(ExpressionNode Node, bool CallInterrupter = true)
        {
            if (Continue(ref Node, CallInterrupter) == PluginResult.Failed)
                return null;

            return Node;
        }

        public PluginResult FinishNode(ref ExpressionNode Node, bool CallInterrupter = true)
        {
            while (true)
            {
                var Res = Continue(ref Node, CallInterrupter);
                if (Res != PluginResult.Ready && Res != PluginResult.Interrupt)
                    return Res;

                if (Node.InterrupterPlugin == -1) return Res;
                CallInterrupter = true;
            }
        }

        public PluginResult FinishNode(ref ExpressionNode Node, ExpressionPlugin Until, bool CallInterrupter = true)
        {
            var Index = Array.IndexOf(Plugins, Until);
            if (Index == -1) throw new ApplicationException();

            while (true)
            {
                var Res = Continue(ref Node, CallInterrupter);
                if (Res != PluginResult.Ready && Res != PluginResult.Interrupt)
                    return Res;

                if (Node.InterrupterPlugin == -1 || Node.InterrupterPlugin > Index)
                    return Res;

                CallInterrupter = true;
            }
        }

        public PluginResult Continue(ref ExpressionNode Node, bool CallInterrupter = true)
        {
            if (Node.InterrupterPlugin == -1) return PluginResult.Ready;
            //throw new ApplicationException("Node hasn't been interrupted");

            var From = Node.InterrupterPlugin;
            if (CallInterrupter)
            {
                var Res = Plugins[From].ForceContinue(ref Node);
                if (Res != PluginResult.Succeeded)
                {
                    if (Res == PluginResult.Interrupt)
                        throw new ApplicationException("Cannot interrupt");

                    return Res;
                }
            }

            Node.InterrupterPlugin = -1;
            return NewNode(ref Node, From + 1);
        }

        public PluginRoot(IdContainer Container)
        {
            this.Container = Container;
            this.State = Container.State;
        }

        internal PluginResult NewNode(ref ExpressionNode Node, int From)
        {
            if (!CurrentlyUsing)
                throw new InvalidOperationException("Begin hasn't been called");

            for (var i = From; i < Plugins.Length; i++)
            {
                var Res = Plugins[i].NewNode(ref Node);

                if (Res != PluginResult.Succeeded)
                {
                    if (Res == PluginResult.Interrupt)
                    {
                        if (Node.InterrupterPlugin != -1)
                            throw new InvalidOperationException("Already interrupted");

                        Node.InterrupterPlugin = i;
                    }

                    return Res;
                }
            }

            if (!Node.CheckChildren(x => x.InterrupterPlugin == -1))
                throw new ApplicationException("Unfinished node");

            return PluginResult.Succeeded;
        }

        public PluginResult NewNodeDontCallAll(ref ExpressionNode Node)
        {
            Node.InterrupterPlugin = -1;
            return NewNode(ref Node, CallNewNodeFrom);
        }

        public PluginResult NewNode(ref ExpressionNode Node)
        {
            Node.InterrupterPlugin = -1;
            return NewNode(ref Node, 0);
        }

        public bool Begin()
        {
            if (CurrentlyUsing)
                throw new InvalidOperationException("End hasn't been called");

            CurrentlyUsing = true;
            for (var i = 0; i < Plugins.Length; i++)
                if (!Plugins[i].Begin()) return false;

            return true;
        }

        public PluginResult End(ref ExpressionNode Node)
        {
            if (!CurrentlyUsing)
                throw new InvalidOperationException("Begin hasn't been called");

            for (var i = 0; i < Plugins.Length; i++)
            {
                var Res = Plugins[i].End(ref Node);
                if (Res != PluginResult.Succeeded)
                {
                    if (Res == PluginResult.Interrupt)
                        throw new ApplicationException("Can't interrupt");

                    return Res;
                }
            }

#if DEBUG
			if (!Expressions.CheckLinkedNodes(Node))
				throw new ApplicationException();
#endif
            CurrentlyUsing = false;
            return PluginResult.Succeeded;
        }

        public T GetPlugin<T>() where T : ExpressionPlugin
        {
            for (var i = 0; i < Plugins.Length; i++)
                if (Plugins[i] is T) return Plugins[i] as T;

            return null;
        }
    }

    public class PluginForGlobals : PluginRoot
    {
        public PluginForGlobals(IdContainer Container)
            : base(Container)
        {
            Plugins = new IExpressionPlugin[]
            {
                new PreProcPlugin(this, false),
                new IdRecognizerPlugin(this),
                new TypeMngrPlugin(this, null),
                new EvaluatorPlugin(this, true),
            };
        }
    }

    public class PluginForCodeScope : PluginRoot
    {
        public PluginForCodeScope(IdContainer Container)
            : base(Container)
        {
            Plugins = new IExpressionPlugin[]
            {
                new PreProcPlugin(this, false),
                new IdRecognizerPlugin(this),
                new TypeMngrPlugin(this, null, TypeMngrPluginFlags.CalculateLayouts),
                new EvaluatorPlugin(this, false),
                new CompilerPlugin(this),
            };
        }
    }

    public class PluginForConstants : PluginRoot
    {
        public PluginForConstants(IdContainer Container, bool DoNotFail = false)
            : base(Container)
        {
            Plugins = new IExpressionPlugin[]
            {
                new PreProcPlugin(this, false),
                new IdRecognizerPlugin(this, DoNotFail),
                new TypeMngrPlugin(this, null, TypeMngrPluginFlags.EnableUntypedNodes),
                new EvaluatorPlugin(this, false),
            };
        }
    }
}