using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;

namespace Zinnia.x86
{
	public class x86ExpressionPlugin : ExpressionPlugin
	{
		public x86Architecture Arch;

		public x86ExpressionPlugin(PluginRoot Parent)
			: base(Parent)
		{
			this.Arch = State.Arch as x86Architecture;
		}

		public override PluginResult NewNode(ref ExpressionNode Node)
		{
			var Data = Node.Data.Get<x86NodeData>();
			if (Data != null) Data.Reset();
			else Data = Node.Data.Create<x86NodeData>();

			DivisionToMultiplication(Node);
			if (!ResolveChildren(Node))
				return PluginResult.Failed;

			if (Node is ConstExpressionNode)
			{
				return PluginResult.Interrupt;
			}

			return PluginResult.Succeeded;
		}

		public override PluginResult ForceContinue(ref ExpressionNode Node)
		{
			return MoveConstantToGlobalIfNeeded(ref Node);
		}

		public void DivisionToMultiplication(ExpressionNode Node)
		{
			if (Node is OpExpressionNode)
			{
				var OpNode = Node as OpExpressionNode;
				var Ch = OpNode.Children;
				var Op = OpNode.Operator;

				if (Op == Operator.Divide && (OpNode.Flags & ExpressionFlags.ReverseOperation) == 0 &&
					Ch[1] is ConstExpressionNode && Ch[1].Type.RealId is FloatType)
				{
					var Ch1 = Ch[1] as ConstExpressionNode;
					var FloatVal = Ch1.Value as FloatValue;
					if (FloatVal != null)
					{
						FloatVal.Value = 1 / FloatVal.Value;
					}
					else
					{
						var DoubleVal = Ch1.Value as DoubleValue;
						if (DoubleVal != null) DoubleVal.Value = 1 / DoubleVal.Value;
						else throw new ApplicationException();
					}

					OpNode.Operator = Operator.Multiply;
				}
			}
		}

		ExpressionNode MoveConstantToGlobal(ConstExpressionNode Node)
		{
			var RetValue = Container.GlobalContainer.CreateConstNode(Node, Parent);
			return RetValue == null ? null : Parent.FinishNode(RetValue);
		}

		PluginResult MoveConstantToGlobalIfNeeded(ref ExpressionNode Node)
		{
			if (Node is ConstExpressionNode)
			{
				var CNode = Node as ConstExpressionNode;

				if (CNode.Type.RealId is FloatType)
				{
					if (Arch.FloatingPointMode == x86FloatingPointMode.FPU)
					{
						var Value = CNode.CDouble;
						if (Value != 0.0 && Value != 1.0 && Value != Math.PI)
						{
							Node = MoveConstantToGlobal(CNode);
							return Node == null ? PluginResult.Failed : PluginResult.Ready;
						}
					}
					else if (Arch.FloatingPointMode == x86FloatingPointMode.SSE)
					{
						Node = MoveConstantToGlobal(CNode);
						return Node == null ? PluginResult.Failed : PluginResult.Ready;
					}
					else
					{
						throw new NotImplementedException();
					}
				}
				else if (x86Expressions.NeedReturnPointer(CNode.Type))
				{
					Node = MoveConstantToGlobal(CNode);
					return Node == null ? PluginResult.Failed : PluginResult.Ready;
				}
			}

			return PluginResult.Succeeded;
		}
	}
}