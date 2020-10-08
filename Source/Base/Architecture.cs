using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Zinnia
{
    public abstract class CondBranch
    {
    }

    public class JumpCodeBranch : CondBranch
    {
        public int Label;

        public JumpCodeBranch(int Label)
        {
            this.Label = Label;
        }
    }

    public class CodeCondBranch : CondBranch
    {
        public Action<CodeGenerator> GetCode;

        public CodeCondBranch(Action<CodeGenerator> GetCode)
        {
            this.GetCode = GetCode;
        }
    }

    public abstract class Instruction
    {
        public bool Skip = false;
    }

    public class LabelInstruction : Instruction
    {
        public int Label;
        public string Name;

        public LabelInstruction(int Label, string Name = null)
        {
            this.Label = Label;
            this.Name = Name;
        }

        public string LabelName
        {
            get { return Name == null ? "_" + Label : Name; }
        }
    }

    public class JumpInstruction : Instruction
    {
        public int Label;
        public string Name;

        public JumpInstruction(int Label, string Name = null)
        {
            this.Label = Label;
            this.Name = Name;
        }

        public string LabelName
        {
            get { return Name == null ? "_" + Label : Name; }
        }
    }

    public sealed class ReplaceableJumpInstruction : JumpInstruction
    {
        public ReplaceableJumpInstruction(int Label)
            : base(Label)
        {
        }
    }

    public class StrInstruction : Instruction
    {
        public string Instruction;

        public StrInstruction(string Instruction)
        {
            this.Instruction = Instruction;
        }
    }

    public class InstructionContainer
    {
        public List<Instruction> Instructions = new List<Instruction>();
        public Dictionary<int, int> LabelPositions;

        public InstructionContainer()
        {
        }

        public void Add(Instruction Instruction)
        {
            if (Instruction is LabelInstruction)
            {
                var LblIns = Instruction as LabelInstruction;
                if (LabelPositions == null) LabelPositions = new Dictionary<int, int>();
                LabelPositions.Add(LblIns.Label, Instructions.Count);
            }

            Instructions.Add(Instruction);
        }

        public void Add(InstructionContainer InsContainer)
        {
            var Count = Instructions.Count;
            Instructions.AddRange(InsContainer.Instructions);

            if (InsContainer.LabelPositions != null)
            {
                foreach (var e in InsContainer.LabelPositions)
                    LabelPositions.Add(e.Key, e.Value + Count);
            }
        }

        public void Add(string Str)
        {
            if (Str.Trim() == "mov dword[ebp - 12], _GetInt") {; }
            Instructions.Add(new StrInstruction(Str));
        }

        public void Jump(int Label)
        {
            if (Label < 0) throw new ArgumentException(null, "Label");
            Instructions.Add(new ReplaceableJumpInstruction(Label));
        }

        public void Jump(string Label)
        {
            Instructions.Add(new JumpInstruction(-1, Label));
        }

        public void Label(string Label)
        {
            Instructions.Add(new LabelInstruction(-1, Label));
        }

        public void Label(int Label)
        {
            if (LabelPositions == null) LabelPositions = new Dictionary<int, int>();
            LabelPositions.Add(Label, Instructions.Count);

            var Ins = new LabelInstruction(Label);
            Instructions.Add(Ins);
        }

        public void Reset()
        {
            Instructions = new List<Instruction>();
            LabelPositions = new Dictionary<int, int>();
        }

        public void Insert(int Index, InstructionContainer InsContainer)
        {
            var P = InsContainer.Instructions.Count - 1;
            if (LabelPositions != null)
            {
                foreach (var e in LabelPositions.Keys.ToArray())
                    if (LabelPositions[e] >= Index) LabelPositions[e] += P;
            }

            Instructions.InsertRange(Index, InsContainer.Instructions);
            if (InsContainer.LabelPositions != null)
            {
                foreach (var e in InsContainer.LabelPositions)
                    LabelPositions.Add(e.Key, e.Value + Index);
            }
        }

        public int JumpsTo(int Label)
        {
            var InsIndex = LabelPositions[Label];
            for (var i = InsIndex; i < Instructions.Count; i++)
            {
                var Ins = Instructions[i];
                if (Ins is JumpInstruction)
                {
                    var JumpIns = Ins as JumpInstruction;
                    return JumpsTo(JumpIns.Label);
                }
                else if (Ins is LabelInstruction)
                {
                    var LabelIns = Ins as LabelInstruction;
                    Label = LabelIns.Label;
                    continue;
                }

                break;
            }

            return Label;
        }

        public string EncodeToText(InstructionEncoder Encoder)
        {
            var StrBuilder = new StringBuilder();
            for (var i = 0; i < Instructions.Count; i++)
            {
                var Ins = Instructions[i];
                if (!Ins.Skip)
                    StrBuilder.Append(Encoder.EncodeToText(Ins));
            }

            return StrBuilder.ToString();
        }
    }

    public abstract class InstructionEncoder
    {
        public abstract string EncodeToText(Instruction Instruction);
    }

    public abstract class CodeGenerator
    {
        public CompilerState State;
        public IdContainer Container;

        public InstructionContainer InsContainer;
        public Dictionary<int, InstructionContainer> ReplaceJumps;

        public abstract void EmitExpression(ExpressionNode Node);
        public abstract void EmitCondition(ExpressionNode Node, CondBranch Then, CondBranch Else, int NextLabel = -1);
        public abstract void EmitCondition(ExpressionNode Node, int Then, int Else, bool ElseAfterCondition);

        public abstract void Align(int Align);
        public abstract void DeclareFile(string FileName);
        public abstract void DeclareLabelPtr(string Label);
        public abstract void DeclareLabelPtr(int Label);
        public abstract void DeclareUnknownBytes(int Count);
        public abstract void DeclareZeroBytes(int Count);
        public abstract void Declare(Identifier Type, ConstValue Data);
        public abstract void Store(string String, bool WideString = true, bool ZeroTerminated = true);

        public CodeGenerator(CompilerState State)
        {
            this.State = State;
            this.InsContainer = new InstructionContainer();
        }

        public void DeclareNull()
        {
            DeclareZeroBytes(State.Arch.RegSize);
        }

        public void Declare(ConstExpressionNode Value)
        {
            Declare(Value.Type.RealId as Type, Value.Value);
        }

        public void Declare(bool Value)
        {
            Declare(Container.GlobalContainer.CommonIds.Boolean, new BooleanValue(Value));
        }

        public void Declare(string Value)
        {
            Declare(Container.GlobalContainer.CommonIds.String, new StringValue(Value));
        }

        public void Declare(float Value)
        {
            Declare(Container.GlobalContainer.CommonIds.Single, new DoubleValue(Value));
        }

        public void Declare(double Value)
        {
            Declare(Container.GlobalContainer.CommonIds.Double, new DoubleValue(Value));
        }

        public void Declare(long Value)
        {
            Declare(Container.GlobalContainer.CommonIds.Int64, new IntegerValue(Value));
        }

        public void Declare(ulong Value)
        {
            Declare(Container.GlobalContainer.CommonIds.UInt64, new IntegerValue(Value));
        }

        public void Declare(int Value)
        {
            Declare(Container.GlobalContainer.CommonIds.Int32, new IntegerValue(Value));
        }

        public void Declare(uint Value)
        {
            Declare(Container.GlobalContainer.CommonIds.UInt32, new IntegerValue(Value));
        }

        public void Declare(short Value)
        {
            Declare(Container.GlobalContainer.CommonIds.Int32, new IntegerValue(Value));
        }

        public void Declare(ushort Value)
        {
            Declare(Container.GlobalContainer.CommonIds.UInt16, new IntegerValue(Value));
        }

        public void Declare(sbyte Value)
        {
            Declare(Container.GlobalContainer.CommonIds.SByte, new IntegerValue(Value));
        }

        public void Declare(byte Value)
        {
            Declare(Container.GlobalContainer.CommonIds.Byte, new IntegerValue(Value));
        }

        public InstructionContainer ExecuteOnTempInsContainer(Action Action)
        {
            var InsContainer = new InstructionContainer();
            var OldInsContainer = this.InsContainer;
            this.InsContainer = InsContainer;

            Action();
            this.InsContainer = OldInsContainer;
            return InsContainer;
        }

        public InstructionContainer SetJumpReplacing(int Label, Action Action)
        {
            var InsContainer = ExecuteOnTempInsContainer(Action);
            SetJumpReplacing(Label, InsContainer);
            return InsContainer;
        }

        public void SetJumpReplacing(int Label, InstructionContainer InsContainer)
        {
            if (ReplaceJumps == null) ReplaceJumps = new Dictionary<int, InstructionContainer>();
            ReplaceJumps.Add(Label, InsContainer);
        }

        public virtual void SkipUnnecessaryJumps()
        {
            for (var i = 0; i < InsContainer.Instructions.Count; i++)
                InsContainer.Instructions[i].Skip = false;

            for (var i = 0; i < InsContainer.Instructions.Count; i++)
            {
                var JumpIns = InsContainer.Instructions[i] as JumpInstruction;
                if (JumpIns != null && !JumpIns.Skip)
                {
                    var SkipInstructions = JumpIns is ReplaceableJumpInstruction;
                    JumpIns.Label = InsContainer.JumpsTo(JumpIns.Label);

                    for (var SkipPos = i + 1; SkipPos < InsContainer.Instructions.Count; SkipPos++)
                    {
                        var SIns = InsContainer.Instructions[SkipPos];
                        if (SIns is LabelInstruction)
                        {
                            var SLabelIns = SIns as LabelInstruction;
                            if (SLabelIns.Label == JumpIns.Label)
                            {
                                JumpIns.Skip = true;
                                break;
                            }
                        }
                        else if (SIns is JumpInstruction)
                        {
                            if (SkipInstructions) SIns.Skip = true;
                            if (SIns is ReplaceableJumpInstruction && !SkipInstructions)
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
            var FS = Container.FunctionScope;
            for (var i = 0; i < InsContainer.Instructions.Count; i++)
            {
                var Ins = InsContainer.Instructions[i] as LabelInstruction;
                if (Ins != null && !FS.NeverSkippedLabels.Contains(Ins.Label))
                    Ins.Skip = true;
            }

            for (var i = 0; i < InsContainer.Instructions.Count; i++)
            {
                var JumpIns = InsContainer.Instructions[i] as JumpInstruction;
                if (JumpIns != null && !JumpIns.Skip)
                {
                    var JumpsTo = InsContainer.LabelPositions[JumpIns.Label];
                    InsContainer.Instructions[JumpsTo].Skip = false;
                }
            }
        }

        public virtual void ProcessJumps()
        {
            SkipUnnecessaryJumps();

            if (ReplaceJumps != null)
            {
                var Instructions = InsContainer.Instructions;
                var Count = 0;
                Reset();

                for (var i = 0; i < Instructions.Count; i++)
                {
                    var Ins = Instructions[i];
                    if (!Ins.Skip)
                    {
                        var JIns = Ins as ReplaceableJumpInstruction;
                        if (JIns != null && ReplaceJumps.ContainsKey(JIns.Label))
                        {
                            InsContainer.Add(ReplaceJumps[JIns.Label]);
                            Count++;
                            continue;
                        }

                        InsContainer.Add(Ins);
                    }
                }

                if (Count > 0) ProcessJumps();
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

        bool ProcessIdentifier(Identifier Id);
        bool ProcessFunction(Function Func);
        bool CreateAssembly(Function Func);
        void GetAssembly(CodeGenerator CG, Function Function);
        bool Compile(CompilerState State, CodeFile[] CodeFiles);


        bool IsSimpleCompareNode(ExpressionNode Node);
        CondBranch[] GetBranches(GlobalContainer Global, Command Then,
            Command Else, ref ExpressionNode Condition);
    }
}
