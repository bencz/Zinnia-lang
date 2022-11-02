using System;
using System.Collections.Generic;
using Zinnia.Base;

namespace Zinnia.Recognizers;

public class AlignModifierRecognizer : LanguageNode, IModRecognizer
{
    public static string String = "align";

    public AlignModifierRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        var State = Container.State;
        CodeString Inside, Cutted;

        var Result = RecognizerHelper.ExtractBracket(State, String, '(', ref Code, out Inside, out Cutted);
        if (Result == SimpleRecResult.Succeeded)
        {
            var Params = RecognizerHelper.GetParamList(State, Inside, 1);
            if (Params == null) return SimpleRecResult.Failed;

            var Node = Constants.CreateCIntNode(Container, Params[0]);
            if (Node == null) return SimpleRecResult.Failed;

            var Align = (int)Node.Integer;
            if (!DataStoring.VerifyAlign(Align))
            {
                State.Messages.Add(MessageId.InvalidAlign, Cutted);
                return SimpleRecResult.Failed;
            }

            for (var i = 0; i < Out.Count; i++)
                if (Out[i] is AlignModifier)
                {
                    State.Messages.Add(MessageId.NotExpected, Cutted);
                    return SimpleRecResult.Failed;
                }

            Out.Add(new AlignModifier(Cutted, Align));
            return SimpleRecResult.Succeeded;
        }

        return Result;
    }
}

public class CallingConventionRecognizer : LanguageNode, IModRecognizer
{
    public static string[] Strings = { "stdcall", "cdecl", "zinniacall" };

    public CallingConventionRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        var Result = Code.StartsWith(Strings, null, new IdCharCheck(true));
        if (Result.Index != -1)
        {
            var State = Container.State;
            var ModCode = Code.Substring(0, Result.String.Length);

            for (var i = 0; i < Out.Count; i++)
                if (Out[i] is CallingConventionModifier)
                {
                    State.Messages.Add(MessageId.NotExpected, ModCode);
                    return SimpleRecResult.Failed;
                }

            CallingConvention Conv;
            if (Result.Index == 0) Conv = CallingConvention.StdCall;
            else if (Result.Index == 1) Conv = CallingConvention.CDecl;
            else if (Result.Index == 2) Conv = CallingConvention.ZinniaCall;
            else throw new NotImplementedException();

            Out.Add(new CallingConventionModifier(ModCode, Conv));
            Code = Code.Substring(Result.String.Length).Trim();
            return SimpleRecResult.Succeeded;
        }

        return SimpleRecResult.Unknown;
    }

    public string GetCallingConventionName(CallingConvention Conv)
    {
        if (Conv == CallingConvention.StdCall) return Strings[0];
        if (Conv == CallingConvention.CDecl) return Strings[1];
        if (Conv == CallingConvention.ZinniaCall) return Strings[2];
        throw new NotImplementedException();
    }
}

public class AccessModifierRecognizer : LanguageNode, IModRecognizer
{
    public static string[] Strings = { "public", "protected", "private", "internal" };

    public AccessModifierRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        var Result = Code.StartsWith(Strings, null, new IdCharCheck(true));
        if (Result.Index != -1)
        {
            var State = Container.State;
            var ModCode = Code.Substring(0, Result.String.Length);

            IdentifierAccess Access;
            if (Result.Index == 0) Access = IdentifierAccess.Public;
            else if (Result.Index == 1) Access = IdentifierAccess.Protected;
            else if (Result.Index == 2) Access = IdentifierAccess.Private;
            else if (Result.Index == 3) Access = IdentifierAccess.Internal;
            else throw new NotImplementedException();

            for (var i = 0; i < Out.Count; i++)
            {
                var AccessMod = Out[i] as AccessModifier;
                if (AccessMod != null)
                {
                    var Allowed = false;
                    if (Access == IdentifierAccess.Internal)
                        Allowed = AccessMod.Access == IdentifierAccess.Protected;
                    else if (Access == IdentifierAccess.Protected)
                        Allowed = AccessMod.Access == IdentifierAccess.Internal;

                    if (!Allowed)
                    {
                        State.Messages.Add(MessageId.NotExpected, ModCode);
                        return SimpleRecResult.Failed;
                    }
                }
            }

            Out.Add(new AccessModifier(ModCode, Access));
            Code = Code.Substring(Result.String.Length).Trim();
            return SimpleRecResult.Succeeded;
        }

        return SimpleRecResult.Unknown;
    }
}

public class FlagModifierRecognizer : LanguageNode, IModRecognizer
{
    public static string[] Strings =
    {
        "virtual", "override", "abstract", "sealed", "static", "extern", "readonly", "new"
    };

    public FlagModifierRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        var Result = Code.StartsWith(Strings, null, new IdCharCheck(true));
        if (Result.Index != -1)
        {
            var State = Container.State;
            var ModCode = Code.Substring(0, Result.String.Length);

            IdentifierFlags Flags;
            if (Result.Index == 0) Flags = IdentifierFlags.Virtual;
            else if (Result.Index == 1) Flags = IdentifierFlags.Override;
            else if (Result.Index == 2) Flags = IdentifierFlags.Abstract;
            else if (Result.Index == 3) Flags = IdentifierFlags.Sealed;
            else if (Result.Index == 4) Flags = IdentifierFlags.Static;
            else if (Result.Index == 5) Flags = IdentifierFlags.Extern;
            else if (Result.Index == 6) Flags = IdentifierFlags.ReadOnly;
            else if (Result.Index == 7) Flags = IdentifierFlags.HideBaseId;
            else throw new NotImplementedException();

            for (var i = 0; i < Out.Count; i++)
            {
                var FlagMod = Out[i] as FlagModifier;
                if (FlagMod != null && (FlagMod.Flags & Flags) != 0)
                {
                    State.Messages.Add(MessageId.NotExpected, ModCode);
                    return SimpleRecResult.Failed;
                }
            }

            Out.Add(new FlagModifier(ModCode, Flags));
            Code = Code.Substring(Result.String.Length).Trim();
            return SimpleRecResult.Succeeded;
        }

        return SimpleRecResult.Unknown;
    }
}

public class ParamFlagModifierRecognizer : LanguageNode, IModRecognizer
{
    public static string[] Strings = { "params" };

    public ParamFlagModifierRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        var Result = Code.StartsWith(Strings, null, new IdCharCheck(true));
        if (Result.Index != -1)
        {
            var State = Container.State;
            var ModCode = Code.Substring(0, Result.String.Length);

            ParameterFlags Flags;
            if (Result.Index == 0) Flags = ParameterFlags.ParamArray;
            else throw new NotImplementedException();

            for (var i = 0; i < Out.Count; i++)
            {
                var FlagMod = Out[i] as ParamFlagModifier;
                if (FlagMod != null && (FlagMod.Flags & Flags) != 0)
                {
                    State.Messages.Add(MessageId.NotExpected, ModCode);
                    return SimpleRecResult.Failed;
                }
            }

            Out.Add(new ParamFlagModifier(ModCode, Flags));
            Code = Code.Substring(Result.String.Length).Trim();
            return SimpleRecResult.Succeeded;
        }

        return SimpleRecResult.Unknown;
    }
}

public class ConstModifierRecognizer : LanguageNode, IModRecognizer
{
    public static string String = "const";

    public ConstModifierRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        if (Code.StartsWith(String, new IdCharCheck(true)))
        {
            var State = Container.State;
            var ModCode = Code.Substring(0, String.Length);

            for (var i = 0; i < Out.Count; i++)
                if (Out[i] is ConstModifier)
                {
                    State.Messages.Add(MessageId.NotExpected, ModCode);
                    return SimpleRecResult.Failed;
                }

            Out.Add(new ConstModifier(ModCode));
            Code = Code.Substring(String.Length).Trim();
            return SimpleRecResult.Succeeded;
        }

        return SimpleRecResult.Unknown;
    }
}

public class AssemblyNameRecognizer : LanguageNode, IModRecognizer
{
    public static string String = "asmname";

    public AssemblyNameRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        var State = Container.State;
        CodeString Inside, Cutted;

        var Result = RecognizerHelper.ExtractBracket(State, String, '(', ref Code, out Inside, out Cutted);
        if (Result == SimpleRecResult.Succeeded)
        {
            var Params = RecognizerHelper.GetParamList(State, Inside, 1);
            if (Params == null) return SimpleRecResult.Failed;

            var Node = Constants.CreateCStrNode(Container, Params[0]);
            if (Node == null) return SimpleRecResult.Failed;

            var AsmName = Node.String;
            if (string.IsNullOrEmpty(AsmName))
            {
                State.Messages.Add(MessageId.DeficientExpr, Params[0]);
                return SimpleRecResult.Failed;
            }

            for (var i = 0; i < Out.Count; i++)
                if (Out[i] is AssemblyNameModifier)
                {
                    State.Messages.Add(MessageId.NotExpected, Cutted);
                    return SimpleRecResult.Failed;
                }

            Out.Add(new AssemblyNameModifier(Cutted, AsmName));
            return SimpleRecResult.Succeeded;
        }

        return Result;
    }
}

public class NoBaseModifierRecognizer : LanguageNode, IModRecognizer
{
    public static string String = "nobase";

    public NoBaseModifierRecognizer(LanguageNode Parent)
        : base(Parent)
    {
    }

    public SimpleRecResult Recognize(IdContainer Container, ref CodeString Code, List<Modifier> Out)
    {
        if (Code.StartsWith(String, new IdCharCheck(true)))
        {
            var State = Container.State;
            var ModCode = Code.Substring(0, String.Length);

            for (var i = 0; i < Out.Count; i++)
                if (Out[i] is NoBaseModifier)
                {
                    State.Messages.Add(MessageId.NotExpected, ModCode);
                    return SimpleRecResult.Failed;
                }

            Out.Add(new NoBaseModifier(ModCode));
            Code = Code.Substring(String.Length).Trim();
            return SimpleRecResult.Succeeded;
        }

        return SimpleRecResult.Unknown;
    }
}