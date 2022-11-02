using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zinnia.Base;

public abstract class CondBranch
{
}

public class JumpCodeBranch : CondBranch
{
    public int Label;

    public JumpCodeBranch(int label)
    {
        Label = label;
    }
}

public class CodeCondBranch : CondBranch
{
    public Action<CodeGenerator> GetCode;

    public CodeCondBranch(Action<CodeGenerator> getCode)
    {
        GetCode = getCode;
    }
}

public abstract class Instruction
{
    public bool Skip;
}

public class LabelInstruction : Instruction
{
    public int Label;
    public string Name;

    public LabelInstruction(int label, string name = null)
    {
        Label = label;
        Name = name;
    }

    public string LabelName
        => Name ?? "_" + Label;
}

public class JumpInstruction : Instruction
{
    public int Label;
    public string Name;

    public JumpInstruction(int label, string name = null)
    {
        Label = label;
        Name = name;
    }

    public string LabelName
        => Name ?? "_" + Label;
}

public sealed class ReplaceableJumpInstruction : JumpInstruction
{
    public ReplaceableJumpInstruction(int label)
        : base(label)
    {
    }
}

public class StrInstruction : Instruction
{
    public string Instruction;

    public StrInstruction(string instruction)
    {
        Instruction = instruction;
    }
}

public class InstructionContainer
{
    public List<Instruction> Instructions = new();
    public Dictionary<int, int> LabelPositions;

    public void Add(Instruction instruction)
    {
        if (instruction is LabelInstruction lblIns)
        {
            LabelPositions ??= new Dictionary<int, int>();
            LabelPositions.Add(lblIns.Label, Instructions.Count);
        }

        Instructions.Add(instruction);
    }

    public void Add(InstructionContainer insContainer)
    {
        var count = Instructions.Count;
        Instructions.AddRange(insContainer.Instructions);

        if (insContainer.LabelPositions == null)
            return;

        foreach (var e in insContainer.LabelPositions)
            LabelPositions.Add(e.Key, e.Value + count);
    }

    public void Add(string str)
    {
        if (str.Trim() == "mov dword[ebp - 12], _GetInt")
        {
        }

        Instructions.Add(new StrInstruction(str));
    }

    public void Jump(int label)
    {
        if (label < 0)
            throw new ArgumentException(null, "label");
        Instructions.Add(new ReplaceableJumpInstruction(label));
    }

    public void Jump(string label)
    {
        Instructions.Add(new JumpInstruction(-1, label));
    }

    public void Label(string label)
    {
        Instructions.Add(new LabelInstruction(-1, label));
    }

    public void Label(int label)
    {
        LabelPositions ??= new Dictionary<int, int>();
        LabelPositions.Add(label, Instructions.Count);

        var ins = new LabelInstruction(label);
        Instructions.Add(ins);
    }

    public void Reset()
    {
        Instructions = new List<Instruction>();
        LabelPositions = new Dictionary<int, int>();
    }

    public void Insert(int index, InstructionContainer insContainer)
    {
        var p = insContainer.Instructions.Count - 1;
        if (LabelPositions != null)
            foreach (var e in LabelPositions.Keys.ToArray())
                if (LabelPositions[e] >= index)
                    LabelPositions[e] += p;

        Instructions.InsertRange(index, insContainer.Instructions);
        if (insContainer.LabelPositions != null)
            foreach (var e in insContainer.LabelPositions)
                LabelPositions.Add(e.Key, e.Value + index);
    }

    public int JumpsTo(int label)
    {
        var insIndex = LabelPositions[label];
        for (var i = insIndex; i < Instructions.Count; i++)
        {
            var ins = Instructions[i];
            switch (ins)
            {
                case JumpInstruction jumpIns:
                    return JumpsTo(jumpIns.Label);
                case LabelInstruction labelIns:
                    label = labelIns.Label;
                    continue;
            }

            break;
        }

        return label;
    }

    public string EncodeToText(InstructionEncoder encoder)
    {
        var strBuilder = new StringBuilder();
        foreach (var ins in Instructions.Where(ins => !ins.Skip)) strBuilder.Append(encoder.EncodeToText(ins));

        return strBuilder.ToString();
    }
}

public abstract class InstructionEncoder
{
    public abstract string EncodeToText(Instruction instruction);
}

public abstract class CodeGenerator
{
    public IdContainer Container;

    public InstructionContainer InsContainer;
    public Dictionary<int, InstructionContainer> ReplaceJumps;
    public CompilerState State;

    public CodeGenerator(CompilerState state)
    {
        State = state;
        InsContainer = new InstructionContainer();
    }

    public abstract void EmitExpression(ExpressionNode node);
    public abstract void EmitCondition(ExpressionNode node, CondBranch then, CondBranch @else, int nextLabel = -1);
    public abstract void EmitCondition(ExpressionNode node, int then, int @else, bool elseAfterCondition);

    public abstract void Align(int align);
    public abstract void DeclareFile(string fileName);
    public abstract void DeclareLabelPtr(string label);
    public abstract void DeclareLabelPtr(int label);
    public abstract void DeclareUnknownBytes(int count);
    public abstract void DeclareZeroBytes(int count);
    public abstract void Declare(Identifier type, ConstValue data);
    public abstract void Store(string str, bool wideString = true, bool zeroTerminated = true);

    public void DeclareNull()
    {
        DeclareZeroBytes(State.Arch.RegSize);
    }

    public void Declare(ConstExpressionNode value)
    {
        Declare(value.Type.RealId as Type, value.Value);
    }

    public void Declare(bool value)
    {
        Declare(Container.GlobalContainer.CommonIds.Boolean, new BooleanValue(value));
    }

    public void Declare(string value)
    {
        Declare(Container.GlobalContainer.CommonIds.String, new StringValue(value));
    }

    public void Declare(float value)
    {
        Declare(Container.GlobalContainer.CommonIds.Single, new DoubleValue(value));
    }

    public void Declare(double value)
    {
        Declare(Container.GlobalContainer.CommonIds.Double, new DoubleValue(value));
    }

    public void Declare(long value)
    {
        Declare(Container.GlobalContainer.CommonIds.Int64, new IntegerValue(value));
    }

    public void Declare(ulong value)
    {
        Declare(Container.GlobalContainer.CommonIds.UInt64, new IntegerValue(value));
    }

    public void Declare(int value)
    {
        Declare(Container.GlobalContainer.CommonIds.Int32, new IntegerValue(value));
    }

    public void Declare(uint value)
    {
        Declare(Container.GlobalContainer.CommonIds.UInt32, new IntegerValue(value));
    }

    public void Declare(short value)
    {
        Declare(Container.GlobalContainer.CommonIds.Int32, new IntegerValue(value));
    }

    public void Declare(ushort value)
    {
        Declare(Container.GlobalContainer.CommonIds.UInt16, new IntegerValue(value));
    }

    public void Declare(sbyte value)
    {
        Declare(Container.GlobalContainer.CommonIds.SByte, new IntegerValue(value));
    }

    public void Declare(byte value)
    {
        Declare(Container.GlobalContainer.CommonIds.Byte, new IntegerValue(value));
    }

    public InstructionContainer ExecuteOnTempInsContainer(Action action)
    {
        var insContainer = new InstructionContainer();
        var oldInsContainer = InsContainer;
        InsContainer = insContainer;

        action();
        InsContainer = oldInsContainer;
        return insContainer;
    }

    public InstructionContainer SetJumpReplacing(int label, Action action)
    {
        var insContainer = ExecuteOnTempInsContainer(action);
        SetJumpReplacing(label, insContainer);
        return insContainer;
    }

    public void SetJumpReplacing(int label, InstructionContainer insContainer)
    {
        ReplaceJumps ??= new Dictionary<int, InstructionContainer>();
        ReplaceJumps.Add(label, insContainer);
    }

    public virtual void SkipUnnecessaryJumps()
    {
        foreach (var t in InsContainer.Instructions)
            t.Skip = false;

        for (var i = 0; i < InsContainer.Instructions.Count; i++)
        {
            var jumpIns = InsContainer.Instructions[i] as JumpInstruction;
            if (jumpIns is { Skip: false })
            {
                var skipInstructions = jumpIns is ReplaceableJumpInstruction;
                jumpIns.Label = InsContainer.JumpsTo(jumpIns.Label);

                for (var skipPos = i + 1; skipPos < InsContainer.Instructions.Count; skipPos++)
                {
                    var sIns = InsContainer.Instructions[skipPos];
                    if (sIns is LabelInstruction sLabelIns)
                    {
                        if (sLabelIns.Label == jumpIns.Label)
                        {
                            jumpIns.Skip = true;
                            break;
                        }
                    }
                    else if (sIns is JumpInstruction)
                    {
                        if (skipInstructions)
                            sIns.Skip = true;
                        if (sIns is ReplaceableJumpInstruction && !skipInstructions)
                            break;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }

    public virtual void SkipUnnecessaryLabels()
    {
        var fs = Container.FunctionScope;
        foreach (var t in InsContainer.Instructions)
            if (t is LabelInstruction ins && !fs.NeverSkippedLabels.Contains(ins.Label))
                ins.Skip = true;

        foreach (var t in InsContainer.Instructions)
            if (t is JumpInstruction { Skip: false } jumpIns)
            {
                var jumpsTo = InsContainer.LabelPositions[jumpIns.Label];
                InsContainer.Instructions[jumpsTo].Skip = false;
            }
    }

    public virtual void ProcessJumps()
    {
        SkipUnnecessaryJumps();

        if (ReplaceJumps != null)
        {
            var instructions = InsContainer.Instructions;
            var count = 0;
            Reset();

            foreach (var ins in instructions.Where(ins => !ins.Skip))
            {
                if (ins is ReplaceableJumpInstruction jIns && ReplaceJumps.ContainsKey(jIns.Label))
                {
                    InsContainer.Add(ReplaceJumps[jIns.Label]);
                    count++;
                    continue;
                }

                InsContainer.Add(ins);
            }

            if (count > 0)
                ProcessJumps();
        }
    }

    public virtual void Optimize()
    {
        ProcessJumps();
        SkipUnnecessaryLabels();
    }

    public void Reset()
    {
        InsContainer.Reset();
    }
}

public interface IArchitecture
{
    int RegSize { get; }
    int MaxStructPow2Size { get; }

    bool ProcessIdentifier(Identifier id);
    bool ProcessFunction(Function func);
    bool CreateAssembly(Function func);
    void GetAssembly(CodeGenerator cg, Function function);
    bool Compile(CompilerState state, CodeFile[] codeFiles);


    bool IsSimpleCompareNode(ExpressionNode node);

    CondBranch[] GetBranches(GlobalContainer globalContainer, Command then, Command @else,
        ref ExpressionNode condition);
}