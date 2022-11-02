using System;
using Zinnia.Base;

namespace Zinnia.Recognizers;

public interface IBuiltinFuncRecognizer
{
    ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out);
}

public class StackAllocRecognizer : IBuiltinFuncRecognizer
{
    public ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out)
    {
        if (Function.IsEqual("stackalloc"))
        {
            var State = Plugin.State;

            if (GenericParams != null && GenericParams.Length != 0)
            {
                State.Messages.Add(MessageId.NonGenericIdentifier, Function);
                return ExprRecResult.Failed;
            }

            if (Params.Length != 1)
            {
                State.Messages.Add(MessageId.ParamCount, Code);
                return ExprRecResult.Failed;
            }

            var Bytes = Expressions.Recognize(Params[0], Plugin, true);
            if (Bytes == null) return ExprRecResult.Failed;

            var Ch = new[] { Bytes };
            Out = new OpExpressionNode(Operator.StackAlloc, Ch, Code);
            return ExprRecResult.Succeeded;
        }

        return ExprRecResult.Unknown;
    }
}

public class IncBinRecognizer : IBuiltinFuncRecognizer
{
    public ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out)
    {
        var State = Plugin.State;
        var Container = Plugin.Container;

        int Func;
        if (Function.IsEqual("incbin_ptr")) Func = 0;
        else if (Function.IsEqual("incbin_size")) Func = 1;
        else return ExprRecResult.Unknown;

        if (GenericParams != null && GenericParams.Length != 0)
        {
            State.Messages.Add(MessageId.NonGenericIdentifier, Function);
            return ExprRecResult.Failed;
        }

        if (Params.Length != 1)
        {
            State.Messages.Add(MessageId.ParamCount, Code);
            return ExprRecResult.Failed;
        }

        var IncBin = GetIncBin(Params[0], Plugin);
        if (IncBin == null) return ExprRecResult.Failed;

        if (Func == 0)
        {
            Out = new DataPointerNode(Code, IncBin);
        }
        else if (Func == 1)
        {
            var Type = Container.GlobalContainer.CommonIds.UIntPtr;
            Out = new ConstExpressionNode(Type, new IntegerValue(IncBin.Length), Code);
        }
        else
        {
            throw new ApplicationException();
        }

        return ExprRecResult.Succeeded;
    }

    private IncludedBinary GetIncBin(CodeString Code, PluginRoot Plugin)
    {
        string String;
        if (!Constants.RecognizeString(Code, Plugin, out String))
            return null;

        var Global = Plugin.Container.GlobalContainer;
        var IncBin = Global.GetIncludedBinary(String);

        if (IncBin == null)
        {
            Plugin.State.Messages.Add(MessageId.UnknownId, Code);
            return null;
        }

        return IncBin;
    }
}

public class DataPointerRecognizer : IBuiltinFuncRecognizer
{
    public ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out)
    {
        var State = Plugin.State;
        if (Function.IsEqual("id_desc_ptr"))
        {
            if (GenericParams != null && GenericParams.Length != 0)
            {
                State.Messages.Add(MessageId.NonGenericIdentifier, Function);
                return ExprRecResult.Failed;
            }

            if (Params.Length != 1)
            {
                State.Messages.Add(MessageId.ParamCount, Code);
                return ExprRecResult.Failed;
            }

            var Container = Plugin.Container;
            var Id = Container.RecognizeIdentifier(Params[0]);
            if (Id == null) return ExprRecResult.Failed;

            Out = new DataPointerNode(Code, Id);
            return ExprRecResult.Succeeded;
        }

        if (Function.IsEqual("assembly_desc_ptr"))
        {
            var Global = Plugin.Container.GlobalContainer;
            if (GenericParams != null && GenericParams.Length != 0)
            {
                State.Messages.Add(MessageId.NonGenericIdentifier, Function);
                return ExprRecResult.Failed;
            }

            if (Params.Length == 0)
            {
                Out = new DataPointerNode(Code, Global.OutputAssembly);
                return ExprRecResult.Succeeded;
            }

            if (Params.Length != 1)
            {
                State.Messages.Add(MessageId.ParamCount, Code);
                return ExprRecResult.Failed;
            }

            string String;
            if (!Constants.RecognizeString(Params[0], Plugin, out String))
                return ExprRecResult.Failed;

            var Assembly = Global.GetAssembly(String);
            if (Assembly == null)
            {
                State.Messages.Add(MessageId.UnknownId, Params[0]);
                return ExprRecResult.Failed;
            }

            Out = new DataPointerNode(Code, Assembly);
            return ExprRecResult.Succeeded;
        }

        return ExprRecResult.Unknown;
    }
}

public class DefaultRecognizer : IBuiltinFuncRecognizer
{
    public ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out)
    {
        if (Function.IsEqual("default"))
        {
            var State = Plugin.State;
            if (GenericParams != null && GenericParams.Length != 0)
            {
                State.Messages.Add(MessageId.NonGenericIdentifier, Function);
                return ExprRecResult.Failed;
            }

            if (Params.Length != 1)
            {
                State.Messages.Add(MessageId.ParamCount, Code);
                return ExprRecResult.Failed;
            }

            var Container = Plugin.Container;
            var Type = Container.RecognizeIdentifier(Params[0], GetIdOptions.DefaultForType);
            if (Type == null) return ExprRecResult.Failed;

            Out = Constants.GetDefaultValue(Plugin, Type, Code);
            return Out == null ? ExprRecResult.Failed : ExprRecResult.Ready;
        }

        return ExprRecResult.Unknown;
    }
}

public class ReinterpretCastRecognizer : IBuiltinFuncRecognizer
{
    public ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out)
    {
        if (Function.IsEqual("reinterpret_cast"))
        {
            var State = Plugin.State;
            var Container = Plugin.Container;

            if (GenericParams == null || GenericParams.Length != 1)
            {
                State.Messages.Add(MessageId.GenericParamCount, Code);
                return ExprRecResult.Failed;
            }

            if (Params.Length != 1)
            {
                State.Messages.Add(MessageId.ParamCount, Code);
                return ExprRecResult.Failed;
            }

            var Options = GetIdOptions.DefaultForType;
            var Type = Identifiers.Recognize(Container, GenericParams[0], Options);
            if (Type == null) return ExprRecResult.Failed;

            var Child = Expressions.Recognize(Params[0], Plugin, true);
            var TypeNode = Plugin.NewNode(new IdExpressionNode(Type, GenericParams[0]));
            if (Child == null || TypeNode == null) return ExprRecResult.Failed;

            Out = new OpExpressionNode(Operator.Reinterpret, Code);
            Out.Children = new[] { Child, TypeNode };
            return ExprRecResult.Succeeded;
        }

        return ExprRecResult.Unknown;
    }
}

public class IsDefinedRecognizer : IBuiltinFuncRecognizer
{
    public ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out)
    {
        if (Function.IsEqual("defined"))
        {
            var State = Plugin.State;
            if (GenericParams != null && GenericParams.Length != 0)
            {
                State.Messages.Add(MessageId.NonGenericIdentifier, Function);
                return ExprRecResult.Failed;
            }

            if (Params.Length != 1)
            {
                State.Messages.Add(MessageId.ParamCount, Code);
                return ExprRecResult.Failed;
            }

            var Preprocessor = State.GlobalContainer.Preprocessor;
            var Defined = Preprocessor.GetMacro(Params[0].ToString()) != null;
            Out = Constants.GetBoolValue(Plugin.Container, Defined, Code);
            if (Out == null) return ExprRecResult.Failed;
            return ExprRecResult.Succeeded;
        }

        return ExprRecResult.Unknown;
    }
}

public class SizeOfRecognizer : IBuiltinFuncRecognizer
{
    public bool AllowVariables = false;

    public ExprRecResult Recognize(CodeString Code, CodeString Function, CodeString[] Params,
        CodeString[] GenericParams, PluginRoot Plugin, ref ExpressionNode Out)
    {
        if (Function.IsEqual("sizeof"))
        {
            var State = Plugin.State;
            if (GenericParams != null && GenericParams.Length != 0)
            {
                State.Messages.Add(MessageId.NonGenericIdentifier, Function);
                return ExprRecResult.Failed;
            }

            if (Params.Length != 1)
            {
                State.Messages.Add(MessageId.ParamCount, Code);
                return ExprRecResult.Failed;
            }

            var Options = GetIdOptions.Default;
            Options.Func = x => x.RealId is Type || (x.RealId is Variable && AllowVariables);

            var Id = Plugin.Container.RecognizeIdentifier(Params[0], Options);
            if (Id == null) return ExprRecResult.Failed;

            Type Type = null;
            if (Id.RealId is Variable && AllowVariables)
                Type = Id.TypeOfSelf.RealId as Type;
            else Type = Id.RealId as Type;

            if (Type == null)
            {
                State.Messages.Add(MessageId.CannotGetSize, Params[0]);
                return ExprRecResult.Failed;
            }

            Out = Constants.GetIntValue(Plugin.Container, Type.Size, Code, true);
            return ExprRecResult.Succeeded;
        }

        return ExprRecResult.Unknown;
    }
}