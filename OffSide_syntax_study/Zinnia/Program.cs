using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

class Zinnia
{
    static void Main(string[] args)
    {
        Scanner scanner = null;
        using (TextReader input = File.OpenText("test.txt"))
        {
            scanner = new Scanner(input);
        }
        scanner.Tokens.ForEach(x => Console.WriteLine(x.Value));
        Parser parser = new Parser(scanner.Tokens);
        new CodeGenerator(parser.Result, Path.GetFileNameWithoutExtension("test.txt"));
    }
}

public enum TokenType { SYMB, NAME, LITERAL, INDENT, DEDENT };

public class Token
{
    public TokenType Type;
    public object Value;
    public int IdentationLevel;

    public Token(TokenType type, object val, int identationLevel)
    {
        Type = type;
        Value = val;
        IdentationLevel = identationLevel;
    }
}

class Scanner
{
    private List<Token> result;

    public Scanner(TextReader reader)
    {
        result = new List<Token>();
        string source = reader.ReadToEnd();
        Scan(source);
    }

    public List<Token> Tokens { get { return this.result; } }

    private void Scan(string src)
    {
        int ix, ixBgn;
        int indentationLevel = 0;
        int sourceCollumn = 0;

        for (ix = 0; ix < src.Length;)
        {
            if (ix + 1 < src.Length && src[ix] == '/' && src[ix + 1] == '/')
            {
                for (ix += 2; ix < src.Length && src[ix] != '\n';)
                    ix++;
            }
            else if (src[ix] == '\r' || src[ix] == '\n')
            {
                for (ixBgn = ix; ix < src.Length && (src[ix] == '\r' || src[ix] == '\n');)
                    ix++;

                sourceCollumn = 0;
            }
            else if (src[ix] == ' ')
            {
                ix++;
                sourceCollumn++;
            }
            else if (src[ix] == '\t')
            {
                for (ixBgn = ix; ix < src.Length && src[ix] == '\t';)
                    ix++;

                int level = ix - ixBgn;
                if (level > indentationLevel + 1)
                    throw new Exception("Multiple indents in a single line not allowed");
                else if (level == indentationLevel + 1)
                {
                    indentationLevel = level;
                    var tk = "(indent)";
                    result.Add(new Token(TokenType.INDENT, tk, indentationLevel));
                }
                else if (level == indentationLevel)
                { }
                else
                {
                    int dedents = indentationLevel - level;
                    if (dedents < 1)
                        throw new Exception("Error computing number of syntetic dedents to produce");

                    indentationLevel = level;
                    var tk = "(dedent)";
                    for (int i = 0; i < dedents; i++)
                        result.Add(new Token(TokenType.DEDENT, tk, indentationLevel));
                }
                sourceCollumn += level;
            }
            else if (char.IsLetter(src[ix]) || src[ix] == '_')
            {
                for (ixBgn = ix; ix < src.Length && (char.IsLetter(src[ix]) || char.IsDigit(src[ix]) || src[ix] == '_');)
                    ix++;

                var tetola = src.Substring(ixBgn, ix - ixBgn);
                if (sourceCollumn == 0 && indentationLevel > 0)
                    for (; indentationLevel > 0; indentationLevel--)
                        result.Add(new Token(TokenType.DEDENT, "(dedent)", indentationLevel));

                result.Add(new Token(TokenType.NAME, src.Substring(ixBgn, ix - ixBgn), indentationLevel));
                sourceCollumn += ix - ixBgn;
            }
            else if (src[ix] == '"')
            {
                ixBgn = ++ix;
                while (ix < src.Length && src[ix] != '"') ix++;
                if (ix >= src.Length || src[ix] != '"')
                {
                    throw new Exception("unterminated string literal");
                }
                result.Add(new Token(TokenType.LITERAL, src.Substring(ixBgn, ix - ixBgn), indentationLevel));
                ix++;
                sourceCollumn += ix - ixBgn;
            }
            else if (src[ix] == '\'')
            {
                ix++;
                if (ix + 1 >= src.Length || src[ix + 1] != '\'')
                {
                    throw new Exception("unterminated charcter literal");
                }
                result.Add(new Token(TokenType.LITERAL, src[ix++], indentationLevel));
                sourceCollumn += 3;
                ix++;
            }
            else if (char.IsDigit(src[ix]))
            {
                int nDot = 0;
                for (ixBgn = ix; ix < src.Length && (char.IsDigit(src[ix]) || src[ix] == '.'); ix++)
                {
                    if (src[ix] == '.') nDot++;
                }
                if (nDot == 0)
                {
                    result.Add(new Token(TokenType.LITERAL, int.Parse(src.Substring(ixBgn, ix - ixBgn)), indentationLevel));
                }
                else if (nDot == 1)
                {
                    result.Add(new Token(TokenType.LITERAL, double.Parse(src.Substring(ixBgn, ix - ixBgn)), indentationLevel));
                }
                else
                {
                    throw new Exception("Scanner: Invalid value");
                }

                sourceCollumn += ix - ixBgn;
            }
            else
            {
                int k = "+-*/=:;()[]{}.,".IndexOf(src[ix]);
                if (k >= 0)
                {
                    result.Add(new Token(TokenType.SYMB, src.Substring(ix, 1), indentationLevel));
                }
                else
                {
                    throw new Exception("Scanner: unrecognized character '" + src[ix] + "'");
                }
                ix++;
                sourceCollumn += 1;
            }
        }
        for (int i = 0; i < indentationLevel; i++)
            result.Add(new Token(TokenType.DEDENT, "(dedent)", indentationLevel));
    }
}

//=========================================================================//
public class Class
{
    public bool fPublic;
    public string Name;
    public IList<Stmt> Stmts;
}

abstract public class Stmt { }

public enum Access { PUBLIC, PRIVATE, PROTECTED, INTERNAL }

public class Modifier
{
    public Access Access;	// public, private, protected, internal
    public bool fStatic;	// static
}

public class AType
{
    public IList<string> Name;	// System.IO.StreamReader

    public string GetTypeName()
    {
        string result = Name[0];
        for (int n = 1; n < Name.Count; n++)
            result += "." + Name[n];
        return result;
    }

    public Type GetAType()
    {
        string result = Name[0];
        if (result == "void") return typeof(void);
        if (result == "int") return typeof(int);
        if (result == "float") return typeof(float);
        if (result == "double") return typeof(double);
        if (result == "char") return typeof(char);
        if (result == "string") return typeof(string);
        for (int n = 1; n < Name.Count; n++)
            result += "." + Name[n];
        return Type.GetType(result);
    }
}

// <modifier> <type> <ident> = <expr> 
public class VarDeclaration : Stmt
{
    public Modifier Modifier;
    public AType Type;		// System.IO.StreamReader
    public string Name; 	// reader
    public Expr Expr; 		// new System.IO.StreamReader("test.txt", Encoding.GetEncoding("Shift_JIS"))
}

// <type> <ident> @@‰¼ˆø”
public class Argument
{
    public AType Type;		// System.IO.StreamReader
    public string Name; 	// reader
}

// <modifier> <type> <ident> ( <args> ) { <stmt>* } 
public class FuncDefinition : Stmt
{
    public Modifier Modifier;
    public AType Type;
    public string Name;
    public IList<Argument> Args;
    public IList<Stmt> Body;
}

// <ident> = <expr> 
public class Assign : Stmt
{
    public string Name;
    public Expr Expr;
}

// <expr>; 
public class ExprStmt : Stmt { public Expr Expr; }

// <stmt>*             
public class Compound { public IList<Stmt> Stmts; }

public abstract class Expr { }

public class Literal : Expr { public object Value; }
public class Variable : Expr { public IList<string> Name; }

// <arith_expr> := <expr> (+ | - | * | /) <expr> 
public class ArithExpr : Expr
{
    public Expr Left, Right;
    public string Op;
}

public class FuncCall : Expr
{
    public bool fNew;
    public IList<string> Name;
    public IList<Expr> Args;
}

//======================================== Parser ====================================//
public class Parser
{
    private IList<Class> result;
    private IList<Token> tokens;
    private int ix;

    public Parser(IList<Token> in_tokens)
    {
        tokens = in_tokens;
        result = new List<Class>();
        for (ix = 0; ix < tokens.Count;)
            result.Add(ParseClass());
    }

    public IList<Class> Result { get { return this.result; } }

    void Skip(TokenType type, string str)
    {
        if (ix == tokens.Count)
        {
            throw new Exception("expected '" + str + "', got EOF");
        }
        else if (tokens[ix].Type != type || (string)tokens[ix].Value != str)
        {
            throw new Exception("expected '" + str + "', got '" + tokens[ix].Value + "'");
        }
        ix++;
    }
    void Skip(string str) { Skip(TokenType.SYMB, str); }

    string GetName()
    {
        if (ix == tokens.Count || tokens[ix].Type != TokenType.NAME) return null;
        return (string)tokens[ix++].Value;
    }

    string GetSymbol()
    {
        if (ix == tokens.Count || tokens[ix].Type != TokenType.SYMB) return null;
        return (string)tokens[ix++].Value;
    }

    bool GetKeyword(string keyword)
    {
        if (ix == tokens.Count || tokens[ix].Type != TokenType.NAME) return false;
        if ((string)tokens[ix].Value == keyword) { ix++; return true; }
        return false;
    }

    bool Is(string s)
    {
        if (ix == tokens.Count) return false;
        if (tokens[ix].Type != TokenType.SYMB &&
            tokens[ix].Type != TokenType.NAME &&
            tokens[ix].Type != TokenType.INDENT &&
            tokens[ix].Type != TokenType.DEDENT)
            return false;
        return ((string)tokens[ix].Value == s);
    }

    // ("indent") <stmt>* ("dedent")
    IList<Stmt> ParseCompoundStmt()
    {
        IList<Stmt> stmts = new List<Stmt>();
        Skip(TokenType.INDENT, "(indent)");
        while (ix < tokens.Count && !Is("(dedent)"))
        {
            stmts.Add(ParseStmt());
            if (Is(";")) ix++;
        }
        Skip(TokenType.DEDENT, "(dedent)");
        return stmts;
    }

    // <ident> (. <ident>)*
    IList<string> ParseName()
    {
        IList<string> name = new List<string>();
        while (ix < tokens.Count && tokens[ix].Type == TokenType.NAME)
        {
            name.Add((string)tokens[ix++].Value);
            if (!Is(".")) break;
            Skip(".");
        }
        return name;
    }

    // 
    AType ParseType()
    {
        AType type = new AType();
        type.Name = ParseName();
        return type;
    }

    // ( <arg>* )
    IList<Argument> ParseArguments()
    {
        IList<Argument> args = new List<Argument>();
        while (ix < tokens.Count && GetSymbol() != ")")
        {
            Argument arg = new Argument();
            arg.Type = ParseType();
            arg.Name = (string)tokens[ix++].Value;
            args.Add(arg);
        }
        return args;
    }

    Class ParseClass()
    {
        Class cls = new Class();
        cls.fPublic = GetKeyword("public");
        Skip(TokenType.NAME, "class");
        cls.Name = GetName();
        cls.Stmts = ParseCompoundStmt();
        return cls;
    }

    Modifier ParseModifier()
    {
        bool fPublic = GetKeyword("public");
        bool fPrivate = GetKeyword("private");
        bool fStatic = GetKeyword("static");
        if (!fPublic && !fPrivate) fPrivate = true;
        Modifier mod = new Modifier();
        if (fPublic) mod.Access = fPublic ? Access.PUBLIC : Access.PRIVATE;
        mod.fStatic = fStatic;
        return mod;
    }

    Stmt ParseStmt()
    {
        Modifier modifier = ParseModifier();
        IList<string> name1 = ParseName();  // Type of Variable
        IList<string> name2 = ParseName();  // Variable 
        string symb = GetSymbol();
        if (name1.Count > 0 && name2.Count > 0 && symb == "(")
        {
            FuncDefinition result = new FuncDefinition();
            result.Modifier = modifier;
            result.Type = new AType();
            result.Type.Name = name1;
            result.Name = name2[0];
            result.Args = ParseArguments();
            Skip(":");
            result.Body = ParseCompoundStmt();
            return result;
        }
        else if (name1.Count > 0 && name2.Count > 0 && symb == "=")
        {
            VarDeclaration var = new VarDeclaration();
            var.Type = new AType();
            var.Type.Name = name1;
            var.Name = name2[0];
            var.Expr = ParseExpr();
            Skip(";");
            return var;
        }
        else if (name1.Count > 0 && name2.Count == 0 && symb == "=")
        {
            VarDeclaration result = new VarDeclaration();
            return result;
        }
        else if (name1.Count > 0 && name2.Count == 0 && symb == "(")
        {// Ex. 	hello.Print(str);
            ExprStmt stmt = new ExprStmt();
            stmt.Expr = ParseFuncCall(false, name1);
            return stmt;
        }
        else
        {
            ExprStmt stmt = new ExprStmt();
            stmt.Expr = ParseExpr();
            return stmt;
        }
    }

    FuncCall ParseFuncCall(bool fNew, IList<string> name)
    {
        FuncCall funcCall = new FuncCall();
        funcCall.fNew = fNew;
        funcCall.Name = name;
        funcCall.Args = new List<Expr>();
        while (ix < tokens.Count && !Is(")"))
        {
            funcCall.Args.Add(ParseExpr());
            if (Is(",")) ix++;
        }
        Skip(")");
        return funcCall;
    }

    Expr ParseExpr() { return ParseAddExpr(); }

    Expr ParseAddExpr()
    {
        Expr left = ParseMulExpr();
        if (!Is("+") && !Is("-")) return left;
        ArithExpr expr = new ArithExpr();
        expr.Op = (string)tokens[ix++].Value;
        expr.Left = left;
        expr.Right = ParseMulExpr();
        return expr;
    }

    Expr ParseMulExpr()
    {
        Expr left = ParsePrimExpr();
        if (!Is("*") && !Is("/")) return left;
        ArithExpr expr = new ArithExpr();
        expr.Op = (string)tokens[ix++].Value;
        expr.Left = left;
        expr.Right = ParsePrimExpr();
        return expr;
    }

    Expr ParsePrimExpr()
    {
        if (ix == tokens.Count) throw new Exception("expected expression, got EOF");
        if (tokens[ix].Type == TokenType.LITERAL)
        {
            Literal literal = new Literal();
            literal.Value = tokens[ix++].Value;
            return literal;
        }
        else if (tokens[ix].Type == TokenType.NAME)
        {
            bool fNew = false;
            if (Is("new")) { ix++; fNew = true; }
            IList<string> name = ParseName();
            if (Is("("))
            {
                Skip("(");
                return ParseFuncCall(fNew, name);
            }
            else
            {
                Variable var = new Variable();
                var.Name = name;
                return var;
            }
        }
        else
        {
            throw new Exception("expected literal or variable, Got " + tokens[ix].Value);
        }
    }

}

//======================================== CodeGem ====================================//
public class CodeGenerator
{
    Dictionary<string, TypeBuilder> typeTable;
    Dictionary<string, ConstructorBuilder> ctorTable;
    Dictionary<string, MethodBuilder> funcTable;
    Dictionary<string, object> symbolTable;
    bool fStaticFunc;

    public CodeGenerator(IList<Class> classes, string moduleName)
    {
        AssemblyName name = new AssemblyName(moduleName);
        AssemblyBuilder asmb = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Save);
        ModuleBuilder moduleBuilder = asmb.DefineDynamicModule(moduleName + ".exe");
        typeTable = new Dictionary<string, TypeBuilder>();
        ctorTable = new Dictionary<string, ConstructorBuilder>();
        funcTable = new Dictionary<string, MethodBuilder>();
        foreach (Class cls in classes)
        {
            TypeBuilder typeBuilder = moduleBuilder.DefineType(cls.Name, TypeAttributes.Class);
            ConstructorBuilder ctorBuilder = typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
            typeTable[cls.Name] = typeBuilder;
            ctorTable[cls.Name] = ctorBuilder;
            foreach (Stmt stmt in cls.Stmts)
            {
                if (stmt is VarDeclaration)
                {
                    Console.WriteLine("VarDecl: todo");
                }
                else if (stmt is FuncDefinition)
                {
                    symbolTable = new Dictionary<string, object>();
                    FuncDefinition func = (FuncDefinition)stmt;
                    MethodAttributes attr = func.Modifier.Access == Access.PUBLIC
                              ? MethodAttributes.Public : MethodAttributes.Private;
                    if (func.Modifier.fStatic) attr |= MethodAttributes.Static;
                    fStaticFunc = func.Modifier.fStatic;
                    IList<Argument> args = func.Args;
                    Type[] types = new Type[args.Count];
                    for (int n = 0; n < args.Count; n++)
                    {
                        types[n] = args[n].Type.GetAType();
                        symbolTable[args[n].Name] = n;
                    }
                    MethodBuilder funcBuilder = typeBuilder.DefineMethod(func.Name, attr, func.Type.GetAType(), types);
                    //funcTable[cls.Name + "::" + func.Name] = funcBuilder;
                    funcTable[func.Name] = funcBuilder;
                    ILGenerator il = funcBuilder.GetILGenerator();
                    foreach (Stmt stmt1 in func.Body)
                    {
                        GenStmt(stmt1, il, types);
                    }
                    il.Emit(OpCodes.Ret);
                    if (func.Name == "Main")
                    {
                        asmb.SetEntryPoint(funcBuilder);
                    }
                }
                else
                {
                    throw new System.Exception("CodeGenerator: stmt is " + stmt);
                }
            }
            typeBuilder.CreateType();
        }
        moduleBuilder.CreateGlobalFunctions();
        asmb.Save(moduleName + ".exe");
    }

    private void GenStmt(Stmt stmt, ILGenerator il, Type[] argtypes)
    {
        if (stmt is VarDeclaration)
        {
            VarDeclaration var = (VarDeclaration)stmt;
            string tname = var.Type.GetTypeName();
            if (typeTable.ContainsKey(tname))
            {
                TypeBuilder typeBuilder = typeTable[tname];
                ConstructorBuilder ctorBuilder = ctorTable[tname];
                LocalBuilder localBuilder = il.DeclareLocal(typeBuilder);
                symbolTable[var.Name] = localBuilder;
                il.Emit(OpCodes.Newobj, ctorBuilder);
                il.Emit(OpCodes.Stloc, localBuilder);
            }
            else
            {
                Type vtype = var.Type.GetAType();
                symbolTable[var.Name] = il.DeclareLocal(vtype);
                GenExpr(var.Expr, TypeOfExpr(var.Expr, argtypes), il, argtypes);
                Store(var.Name, TypeOfExpr(var.Expr, argtypes), il);
            }
        }
        else if (stmt is Assign)
        {
            Assign assign = (Assign)stmt;
            GenExpr(assign.Expr, TypeOfExpr(assign.Expr, argtypes), il, argtypes);
            Store(assign.Name, TypeOfExpr(assign.Expr, argtypes), il);
        }
        else if (stmt is ExprStmt)
        {
            Expr expr = ((ExprStmt)stmt).Expr;
            if (expr is FuncCall)
            {
                FuncCall funcCall = (FuncCall)expr;
                if (funcCall.Name.Count > 1 && funcCall.Name[0] != "System")
                {
                    if (symbolTable.ContainsKey(funcCall.Name[0]))
                    {
                        LocalBuilder localBuilder = (LocalBuilder)symbolTable[funcCall.Name[0]];
                        il.Emit(OpCodes.Ldloc, localBuilder);
                    }
                }
                Type[] typeArgs = new Type[funcCall.Args.Count];
                for (int i = 0; i < funcCall.Args.Count; i++)
                {
                    typeArgs[i] = TypeOfExpr(funcCall.Args[i], argtypes);
                    GenExpr(funcCall.Args[i], typeArgs[i], il, argtypes);
                }
                string strFunc = funcCall.Name[funcCall.Name.Count - 1];    // "WriteLine"
                if (funcCall.Name[0] == "System")
                {
                    string strType = funcCall.Name[0];
                    for (int i = 1; i < funcCall.Name.Count - 1; i++)
                    {
                        strType += "." + funcCall.Name[i];
                    }
                    Type typeFunc = Type.GetType(strType);          // System.Console
                    il.Emit(OpCodes.Call, typeFunc.GetMethod(strFunc, typeArgs));
                }
                else if (funcCall.Name.Count > 1)
                {
                    MethodBuilder methodBuilder = funcTable[strFunc];
                    il.EmitCall(OpCodes.Call, methodBuilder, null);
                }
                else
                {
                    if (!funcTable.ContainsKey(strFunc))
                    {
                        throw new Exception("undeclared function '" + strFunc + "'");
                    }
                    MethodBuilder funcBuilder = funcTable[strFunc];
                    il.EmitCall(OpCodes.Call, funcBuilder, null);
                }
            }
        }
    }

    private void GenExpr(Expr expr, Type expectedType, ILGenerator il, Type[] argtypes)
    {
        Type deliveredType;

        if (expr is Literal)
        {
            object val = ((Literal)expr).Value;
            deliveredType = val.GetType();
            if (val is string) il.Emit(OpCodes.Ldstr, (string)val);
            else if (val is int) il.Emit(OpCodes.Ldc_I4, (int)val);
            else if (val is double) il.Emit(OpCodes.Ldc_R8, (double)val);
        }
        else if (expr is Variable)
        {
            string ident = ((Variable)expr).Name[0];
            deliveredType = TypeOfExpr(expr, argtypes);
            if (!symbolTable.ContainsKey(ident))
            {
                throw new Exception("undeclared variable '" + ident + "'");
            }
            object var = symbolTable[ident];
            if (var is LocalBuilder) il.Emit(OpCodes.Ldloc, (LocalBuilder)var);
            else if (var is int) il.Emit(OpCodes.Ldarg, (int)var + (fStaticFunc ? 0 : 1));
            else throw new System.Exception("invalid: " + var);
        }
        else if (expr is ArithExpr)
        {
            ArithExpr arithExpr = (ArithExpr)expr;
            GenExpr(arithExpr.Left, TypeOfExpr(arithExpr.Left, argtypes), il, argtypes);
            GenExpr(arithExpr.Right, TypeOfExpr(arithExpr.Right, argtypes), il, argtypes);
            if (arithExpr.Op == "+") il.Emit(OpCodes.Add);
            else if (arithExpr.Op == "-") il.Emit(OpCodes.Sub);
            else if (arithExpr.Op == "*") il.Emit(OpCodes.Mul);
            else if (arithExpr.Op == "/") il.Emit(OpCodes.Div);
            else throw new System.Exception("unsuportOperator '" + arithExpr.Op + "'");
            deliveredType = TypeOfExpr(arithExpr, argtypes);
        }
        else
        {
            throw new System.Exception("don't know how to generate " + expr.GetType().Name);
        }
        if (deliveredType != expectedType)
        {
            if (deliveredType == typeof(int) && expectedType == typeof(string))
            {
                il.Emit(OpCodes.Box, typeof(int));
                il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString"));
            }
            else
            {
                throw new Exception("can't coerce a " + deliveredType.Name + " to a " + expectedType.Name);
            }
        }
    }

    private Type TypeOfExpr(Expr expr, Type[] argtypes)
    {
        if (expr is Literal)
        {
            return ((Literal)expr).Value.GetType();
        }
        else if (expr is Variable)
        {
            Variable var = (Variable)expr;
            string vname = var.Name[0];
            if (symbolTable.ContainsKey(vname))
            {
                if (symbolTable[vname] is LocalBuilder)
                {
                    return ((LocalBuilder)symbolTable[vname]).LocalType;
                }
                else
                {
                    int ixArg = (int)symbolTable[vname];
                    return argtypes[ixArg];
                }
            }
            else
            {
                throw new Exception("undeclared variable '" + vname + "'");
            }
        }
        else if (expr is ArithExpr)
        {
            return TypeOfExpr(((ArithExpr)expr).Left, argtypes);
        }
        else
        {
            throw new Exception("don't know how to calculate the type of " + expr.GetType().Name);
        }
    }

    private void Store(string name, Type type, ILGenerator il)
    {
        if (symbolTable.ContainsKey(name))
        {
            LocalBuilder locb = (LocalBuilder)symbolTable[name];
            if (locb.LocalType == type || (locb.LocalType == typeof(double) && type == typeof(int)))
            {
                il.Emit(OpCodes.Stloc, locb);
            }
            else
            {
                throw new Exception("'" + name + "' is of type " + locb.LocalType.Name
                                        + " but attempted to store value of type " + type.Name);
            }
        }
        else
        {
            throw new Exception("undeclared variable '" + name + "'");
        }
    }

}
