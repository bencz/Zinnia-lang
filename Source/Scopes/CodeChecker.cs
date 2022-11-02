using System;
using Zinnia.Base;

namespace Zinnia;

[Flags]
public enum OnEnterLeaveResult : byte
{
    Succeeded = 0,
    Failed = 1,
    Cancel = 2,
    EnterNew = 4,
    LeaveNew = 8,
    EnterLeaveNew = EnterNew | LeaveNew,
    FailedAndCancel = Failed | Cancel
}

[Flags]
public enum CodeContextFlags : byte
{
    None = 0
}

public enum JumpMode : byte
{
    Enter,
    Leave
}

[Flags]
public enum CodeContextResult : byte
{
    Succeeded = 0,
    Failed = 1,
    JumpedOut = 2
}

public struct JumpDestination
{
    public IdContainer Container;
    public JumpMode Mode;

    public JumpDestination(IdContainer Container, JumpMode Mode = JumpMode.Enter)
    {
        this.Container = Container;
        this.Mode = Mode;
    }
}

public abstract class CodeContext
{
    public IdContainer Container;
    public IdContainer ExitAfter;
    private JumpDestination FinallyJump;
    public CodeContextFlags Flags;

    public CodeContext(IdContainer Container)
    {
        this.Container = Container;
    }

    public CompilerState State => Container.State;

    public bool IsOutsideOfExitAfter
    {
        get
        {
            var E = ExitAfter;
            return E != null && !(Container == E || Container.IsSubContainerOf(E));
        }
    }

    public abstract CodeContext Copy();

    public virtual OnEnterLeaveResult OnEnterContainer()
    {
        var RetValue = OnEnterLeaveResult.Succeeded;

        if (Container is Command)
        {
            var Comm = Container as Command;
            var Type = Comm.Type;
            if (Type == CommandType.If)
            {
                for (var i = 0; i < Comm.Children.Count; i++)
                {
                    if ((Copy(Comm.Children[i]).Process() & CodeContextResult.Failed) != 0)
                        RetValue |= OnEnterLeaveResult.Failed;

                    if (i == Comm.Expressions.Count)
                        return RetValue | OnEnterLeaveResult.Cancel;
                }
            }
            else if (Type == CommandType.Try)
            {
                if (Comm.CatchScope != null)
                    if ((Copy(Comm.CatchScope).Process() & CodeContextResult.Failed) != 0)
                        RetValue |= OnEnterLeaveResult.Failed;

                RetValue |= OnEnterLeaveResult.EnterNew;
                Container = Comm.Children[0];
            }
            else if (Commands.IsJumpCommand(Type))
            {
                return RetValue | Jump(Comm.GetJumpDestination());
            }
            else if (Commands.IsLoopCommand(Type))
            {
                var Res = Comm.WillRun;
                if (Res != ConditionResult.False)
                {
                    if (Res == ConditionResult.True)
                    {
                        Container = Comm.Children[0];
                        RetValue |= OnEnterLeaveResult.EnterNew;
                    }
                    else
                    {
                        if ((Copy(Comm.Children[0]).Process() & CodeContextResult.Failed) != 0)
                            RetValue |= OnEnterLeaveResult.Failed;
                    }
                }
            }
            else if (Type == CommandType.Throw || Type == CommandType.Rethrow)
            {
                return OnEnterLeaveResult.Cancel;
            }
        }
        else if (Container is CodeScopeNode)
        {
            if (Container.Children.Count > 0)
            {
                Container = Container.Children[0];
                RetValue |= OnEnterLeaveResult.EnterNew;
            }
        }

        return RetValue;
    }

    public virtual OnEnterLeaveResult OnLeaveContainer()
    {
        if (Container is FunctionScope) return OnEnterLeaveResult.Cancel;

        if (Container.Parent is Command)
        {
            var ParentComm = Container.Parent as Command;
            if (ParentComm.Type == CommandType.Try)
            {
                if (ParentComm.FinallyScope != Container && ParentComm.FinallyScope != null)
                {
                    Container = ParentComm.FinallyScope;
                    return OnEnterLeaveResult.EnterNew;
                }

                if (ParentComm.FinallyScope == Container && FinallyJump.Container != null)
                    return Jump(FinallyJump);
            }
        }
        else if (Container.Parent is CodeScopeNode)
        {
            var Scope = Container.Parent as CodeScopeNode;
            var NewCommPos = Scope.GetChildIndex(Container) + 1;

            if (NewCommPos < Scope.Children.Count)
            {
                Container = Scope.Children[NewCommPos];
                return OnEnterLeaveResult.EnterNew;
            }
        }

        return OnEnterLeaveResult.Succeeded;
    }

    public CodeContext Copy(IdContainer Container)
    {
        var Ret = Copy();
        Ret.Container = Container;
        Ret.Flags = Flags;
        return Ret;
    }

    public CodeContextResult Process(IdContainer Container)
    {
        var OldExitAfter = ExitAfter;
        var OldContainer = Container;

        this.Container = Container;
        ExitAfter = Container;

        var Res = Process();
        this.Container = OldContainer;
        ExitAfter = OldExitAfter;
        return Res;
    }

    public CodeContextResult Process()
    {
        var RetValue = CodeContextResult.Succeeded;
        var State = Container.State;

        while (true)
        {
            var Result = OnEnterContainer();
            if ((Result & OnEnterLeaveResult.Failed) != 0) RetValue |= CodeContextResult.Failed;
            if ((Result & OnEnterLeaveResult.Cancel) != 0) return RetValue;

            if ((Result & OnEnterLeaveResult.EnterLeaveNew) != 0)
            {
                if (IsOutsideOfExitAfter) return RetValue | CodeContextResult.JumpedOut;
                if ((Result & OnEnterLeaveResult.EnterNew) != 0) continue;
            }

            while (true)
            {
                var OldContainer = Container;
                var LResult = OnLeaveContainer();
                if ((LResult & OnEnterLeaveResult.Failed) != 0) RetValue |= CodeContextResult.Failed;
                if ((LResult & OnEnterLeaveResult.Cancel) != 0) return RetValue;

                if ((LResult & OnEnterLeaveResult.EnterLeaveNew) != 0)
                {
                    if (IsOutsideOfExitAfter) return RetValue | CodeContextResult.JumpedOut;
                    if ((LResult & OnEnterLeaveResult.EnterNew) != 0) break;
                    if ((LResult & OnEnterLeaveResult.LeaveNew) != 0) continue;
                }
                else
                {
                    if (OldContainer == ExitAfter) return RetValue;
                }

                Container = Container.Parent;
            }
        }
    }

    public OnEnterLeaveResult Jump(JumpDestination JumpDst)
    {
        return Jump(JumpDst.Container, JumpDst.Mode);
    }

    public OnEnterLeaveResult Jump(IdContainer JumpTo, JumpMode Mode = JumpMode.Enter)
    {
        var Common = Container.GetCommonContainer(JumpTo);
        var TryComm = Container.GetParent<Command>(x =>
            x.Type == CommandType.Try && x.FinallyScope != null, Common);

        if (TryComm != null)
        {
            Container = TryComm.FinallyScope;
            FinallyJump = new JumpDestination(JumpTo, Mode);
            return OnEnterLeaveResult.EnterNew;
        }

        Container = JumpTo;
        if (Mode == JumpMode.Enter) return OnEnterLeaveResult.EnterNew;
        if (Mode == JumpMode.Leave) return OnEnterLeaveResult.LeaveNew;
        throw new ArgumentOutOfRangeException("Mode");
    }
}

public struct CodeCheckerRouteData
{
    public bool Assigned;
}

[Flags]
public enum CodeCheckerIdDataFlags : byte
{
    Read = 1,
    Used = 2,
    AddressUsed = 4
}

public struct CodeCheckerIdData
{
    public CodeCheckerIdDataFlags Flags;
}

public sealed class CodeCheckerContext : CodeContext
{
    public CodeCheckerIdData[] IdData;
    public CodeCheckerRouteData[] RouteData;

    public CodeCheckerContext(IdContainer Container, CodeCheckerIdData[] IdData = null)
        : base(Container)
    {
        var FS = Container.FunctionScope;
        var IdCount = FS.LocalIdentifiers.Count;

        if (IdData == null)
        {
            IdData = new CodeCheckerIdData[IdCount];
            for (var i = 0; i < IdCount; i++)
                if (FS.LocalIdentifiers[i].Used)
                    IdData[i].Flags |= CodeCheckerIdDataFlags.Used;
        }

        this.IdData = IdData;
        RouteData = new CodeCheckerRouteData[IdCount];
    }

    public bool VerifyAssigned(Identifier Id, CodeString Code)
    {
        if (!RouteData[Id.LocalIndex].Assigned)
        {
            var Local = Id as LocalVariable;
            if (Local != null && !Local.PreAssigned)
            {
                State.Messages.Add(MessageId.UnassignedVar, Code);
                return false;
            }
        }

        return true;
    }

    public bool ProcessExpression(ExpressionNode Expr)
    {
        var D = Expr.Data.Get<NodeVariables>();
        for (var i = 0; i < D.AssignedIds.Count; i++)
        {
            var Id = D.AssignedIds[i].Identifier;
            if (Id.LocalIndex != -1)
            {
                RouteData[Id.LocalIndex].Assigned = true;
                IdData[Id.LocalIndex].Flags |= CodeCheckerIdDataFlags.Used;
            }
        }

        for (var i = 0; i < D.UsedBeforeAssignIds.Count; i++)
        {
            var Node = D.UsedBeforeAssignIds[i];
            var Id = Node.Identifier;

            if (Id.LocalIndex != -1)
            {
                IdData[Id.LocalIndex].Flags |= CodeCheckerIdDataFlags.Read;
                IdData[Id.LocalIndex].Flags |= CodeCheckerIdDataFlags.Used;
                if (!VerifyAssigned(Id, Node.Code)) return false;
            }
        }

        for (var i = 0; i < D.AddressUsed.Count; i++)
        {
            var Id = D.AddressUsed[i].Identifier;
            if (Id.LocalIndex != -1)
            {
                RouteData[Id.LocalIndex].Assigned = true;
                IdData[Id.LocalIndex].Flags |= CodeCheckerIdDataFlags.Used;
                IdData[Id.LocalIndex].Flags |= CodeCheckerIdDataFlags.AddressUsed;
            }
        }

        return true;
    }

    public override CodeContext Copy()
    {
        var Ret = new CodeCheckerContext(Container, IdData);
        Ret.RouteData = new CodeCheckerRouteData[RouteData.Length];
        RouteData.CopyTo(Ret.RouteData, 0);
        return Ret;
    }

    public void ResetOutside(IdContainer Container)
    {
        var FS = Container.FunctionScope;
        for (var i = 0; i < RouteData.Length; i++)
            if (RouteData[i].Assigned)
            {
                var C = FS.LocalIdentifiers[i].Container;
                if (C != Container && !Container.IsSubContainerOf(C))
                {
                    RouteData[i].Assigned = false;
                    i--;
                }
            }
    }

    public override OnEnterLeaveResult OnEnterContainer()
    {
        var RetValue = OnEnterLeaveResult.Succeeded;
        ResetOutside(Container);

        if (Container is Command)
        {
            var Comm = Container as Command;
            if ((Comm.Flags & CommandFlags.Unreachable) == 0)
                return OnEnterLeaveResult.Cancel;

            var Type = Comm.Type;
            Comm.Flags &= ~CommandFlags.Unreachable;

            if (Commands.IsJumpCommand(Type))
            {
                var Result = true;
                Comm.ForEachJumpedOver<CodeScopeNode>(x =>
                {
                    var Cx = x.Parent as Command;
                    if (Cx != null && Cx.Type == CommandType.Try && Cx.FinallyScope == x)
                    {
                        State.Messages.Add(MessageId.CannotLeaveFinally, Comm.Code);
                        Result = false;
                    }
                });

                if (!Result)
                    return OnEnterLeaveResult.FailedAndCancel;
            }

            if (Type == CommandType.If)
            {
                for (var i = 0; i < Comm.Children.Count; i++)
                {
                    var Chi = Comm.Children[i];
                    if (i < Comm.Expressions.Count)
                    {
                        if (!ProcessExpression(Comm.Expressions[i]))
                            RetValue |= OnEnterLeaveResult.Failed;

                        if ((Copy(Chi).Process() & CodeContextResult.Failed) != 0)
                            RetValue |= OnEnterLeaveResult.Failed;
                    }
                    else
                    {
                        if ((Copy(Chi).Process() & CodeContextResult.Failed) != 0)
                            RetValue |= OnEnterLeaveResult.Failed;

                        RetValue |= OnEnterLeaveResult.Cancel;
                    }
                }

                return RetValue;
            }

            if (Type == CommandType.Return)
            {
                if (Comm.Expressions != null && Comm.Expressions.Count > 0)
                    if (!ProcessExpression(Comm.Expressions[0]))
                        RetValue |= OnEnterLeaveResult.Failed;
            }
            else if (Type == CommandType.Expression)
            {
                if (!ProcessExpression(Comm.Expressions[0]))
                    RetValue |= OnEnterLeaveResult.Failed;
            }
            else if (Type == CommandType.Throw || Type == CommandType.Rethrow)
            {
                if (!ProcessExpression(Comm.Expressions[0]))
                    RetValue |= OnEnterLeaveResult.Failed;
            }
            else if (Type == CommandType.For)
            {
                if (!ProcessExpression(Comm.Expressions[0]))
                    RetValue |= OnEnterLeaveResult.Failed;

                if (!ProcessExpression(Comm.Expressions[1]))
                    RetValue |= OnEnterLeaveResult.Failed;
            }
            else if (Type == CommandType.While)
            {
                if (!ProcessExpression(Comm.Expressions[0]))
                    RetValue |= OnEnterLeaveResult.Failed;
            }
        }

        return base.OnEnterContainer() | RetValue;
    }

    public override OnEnterLeaveResult OnLeaveContainer()
    {
        var RetValue = OnEnterLeaveResult.Succeeded;
        if (Container is Command)
        {
            var Comm = Container as Command;
            if (Comm.Type == CommandType.For)
            {
                if (!ProcessExpression(Comm.Expressions[2]))
                    RetValue |= OnEnterLeaveResult.Failed;

                if (!ProcessExpression(Comm.Expressions[1]))
                    RetValue |= OnEnterLeaveResult.Failed;
            }
            else if (Comm.Type == CommandType.DoWhile)
            {
                if (!ProcessExpression(Comm.Expressions[0]))
                    RetValue |= OnEnterLeaveResult.Failed;
            }
        }

        Container.ForEachId(x =>
        {
            var R = x.TypeOfSelf.RealId as ReferenceType;
            if (R != null && R.Mode == ReferenceMode.IdGetsAssigned && !VerifyAssigned(x, x.Declaration))
                RetValue |= OnEnterLeaveResult.Failed;
        });

        return base.OnLeaveContainer() | RetValue;
    }
}

public static class CodeChecker
{
    public static void Initialize(IdContainer Container)
    {
        if (Container is Command)
        {
            var Command = Container as Command;
            Command.Flags |= CommandFlags.Unreachable;
        }

        for (var i = 0; i < Container.Children.Count; i++)
            Initialize(Container.Children[i]);
    }

    public static CodeContextResult Process(FunctionScope Scope)
    {
        Initialize(Scope);
        var State = Scope.State;
        var Context = new CodeCheckerContext(Scope);
        var Ret = Context.Process();

        if (Scope.Code.Length > 0)
            for (var i = 0; i < Context.IdData.Length; i++)
            {
                var Local = Scope.LocalIdentifiers[i] as LocalVariable;
                if (Local == null) continue;

                var IdData = Context.IdData[i];
                if ((IdData.Flags & CodeCheckerIdDataFlags.Used) == 0 &&
                    (Local.Flags & IdentifierFlags.ReadOnly) == 0)
                    State.Messages.Add(MessageId.UnusedId, Local.Name);
                else if ((IdData.Flags & CodeCheckerIdDataFlags.Read) == 0 &&
                         (IdData.Flags & CodeCheckerIdDataFlags.AddressUsed) == 0 && !Local.PreAssigned)
                    State.Messages.Add(MessageId.AssignedButNeverUsed, Local.Name);
            }

        if ((Scope.ReturnLabelCommand.Flags & CommandFlags.Unreachable) == 0)
            if (Scope.NeedsReturnVal)
            {
                Scope.State.Messages.Add(MessageId.NotAllPathReturn, Scope.Function.Declaration);
                Ret |= CodeContextResult.Failed;
            }

        Scope.ReturnLabelCommand.Flags &= ~CommandFlags.Unreachable;
        if (!Finalize(Scope)) Ret |= CodeContextResult.Failed;
        return Ret;
    }

    public static bool Finalize(IdContainer Container, bool Unreachable = false)
    {
        var RetValue = true;
        var State = Container.State;

        if (Container is CodeScopeNode)
        {
            var Scope = Container as CodeScopeNode;
            for (var ChIndex = 0; ChIndex < Scope.Children.Count; ChIndex++)
            {
                var Ch = Scope.Children[ChIndex];
                if (Ch is Command)
                {
                    var Comm = Ch as Command;
                    if ((Comm.Flags & CommandFlags.Unreachable) != 0 && !Unreachable)
                        State.Messages.Add(MessageId.UnreachableCode, Comm.Code);

                    Unreachable = (Comm.Flags & CommandFlags.Unreachable) != 0;
                }

                Finalize(Ch, Unreachable);
            }
        }
        else if (Container is Command)
        {
            var Comm = Container as Command;
            for (var i = 0; i < Comm.Children.Count; i++)
                if (!Finalize(Comm.Children[i], Unreachable))
                    RetValue = false;
        }
        else
        {
            throw new NotImplementedException();
        }

        return RetValue;
    }
}