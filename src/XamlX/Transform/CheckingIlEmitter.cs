using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using XamlX.TypeSystem;

namespace XamlX.Transform
{
    public class CheckingIlEmitter : IXamlXEmitter
    {
        private readonly IXamlXEmitter _inner;

        private Dictionary<IXamlXLabel, string> _unmarkedLabels =
            new Dictionary<IXamlXLabel, string>();

        private Dictionary<IXamlXLabel, Instruction> _labels =
            new Dictionary<IXamlXLabel, Instruction>();
        
        private List<IXamlXLabel> _labelsToMarkOnNextInstruction = new List<IXamlXLabel>();
        private bool _paused;

        public CheckingIlEmitter(IXamlXEmitter inner)
        {
            _inner = inner;

        }        
        
        class Instruction
        {
            public int Offset { get; }
            public OpCode Opcode { get; set; }
            public object Operand { get; }
            public int BalanceChange { get; set; }
            public IXamlXLabel JumpTo { get; set; }
            public int? ExpectedBalance { get; set; }

            public Instruction(int offset, OpCode opcode, object operand)
            {
                Offset = offset;
                Opcode = opcode;
                Operand = operand;
                BalanceChange = GetInstructionBalance(opcode, operand);
                JumpTo = operand as IXamlXLabel;
            }

            public Instruction(int offset, int balanceChange)
            {
                Offset = offset;
                BalanceChange = balanceChange;
                Opcode = OpCodes.Nop;
            }

            public override string ToString() =>
                $"{Offset:0000}: {Opcode}; Expected {ExpectedBalance} Change {BalanceChange}";
        }
        private List<Instruction> _instructions = new List<Instruction>();


        public IXamlXTypeSystem TypeSystem => _inner.TypeSystem;


        private static readonly Dictionary<StackBehaviour, int> s_balance = new Dictionary<StackBehaviour, int>
        {
            {StackBehaviour.Pop0, 0},
            {StackBehaviour.Pop1, -1},
            {StackBehaviour.Pop1_pop1, -2},
            {StackBehaviour.Popi, -1},
            {StackBehaviour.Popi_pop1, -2},
            {StackBehaviour.Popi_popi, -2},
            {StackBehaviour.Popi_popi8, -2},
            {StackBehaviour.Popi_popi_popi, -3},
            {StackBehaviour.Popi_popr4, -2},
            {StackBehaviour.Popi_popr8, -2},
            {StackBehaviour.Popref, -1},
            {StackBehaviour.Popref_pop1, -2},
            {StackBehaviour.Popref_popi, -2},
            {StackBehaviour.Popref_popi_popi, -3},
            {StackBehaviour.Popref_popi_popi8, -3},
            {StackBehaviour.Popref_popi_popr4, -3},
            {StackBehaviour.Popref_popi_popr8, -3},
            {StackBehaviour.Popref_popi_popref, -3},
            {StackBehaviour.Push0, 0},
            {StackBehaviour.Push1, 1},
            {StackBehaviour.Push1_push1, 2},
            {StackBehaviour.Pushi, 1},
            {StackBehaviour.Pushi8, 1},
            {StackBehaviour.Pushr4, 1},
            {StackBehaviour.Pushr8, 1},
            {StackBehaviour.Pushref, 1},
            {StackBehaviour.Popref_popi_pop1, -3}
        };

        static int GetInstructionBalance(OpCode code, object operand)
        {
            var method = operand as IXamlXMethod;
            var ctor = operand as IXamlXConstructor;
            /*if (code.FlowControl == FlowControl.Branch || code.FlowControl == FlowControl.Cond_Branch)
                _hasBranches = true;*/
            var stackBalance = 0;
            if (method != null && (code == OpCodes.Call || code == OpCodes.Callvirt))
            {
                stackBalance -= method.Parameters.Count + (method.IsStatic ? 0 : 1);
                if (method.ReturnType.FullName != "System.Void")
                    stackBalance += 1;
            }
            else if (ctor!= null && (code == OpCodes.Call  || code == OpCodes.Newobj))
            {
                stackBalance -= ctor.Parameters.Count;
                if (code == OpCodes.Newobj)
                    // New pushes a value to the stack
                    stackBalance += 1;
                else
                {
                    if (!ctor.IsStatic)
                        // base ctor pops this from the stack
                        stackBalance -= 1;
                }
            }
            else
            {
                void Balance(StackBehaviour op)
                {
                    if (s_balance.TryGetValue(op, out var balance))
                        stackBalance += balance;
                    else
                        throw new Exception("Don't know how to track stack for " + code);
                }
                Balance(code.StackBehaviourPop);
                Balance(code.StackBehaviourPush);
            }

            return stackBalance;
        }
        
        public void Pause()
        {
            _paused = true;
        }

        public void Resume()
        {
            _paused = false;
        }

        public void ExplicitStack(int change)
        {
            if (_paused)
                return;
            _instructions.Add(new Instruction(_instructions.Count, change));
            (_inner as CheckingIlEmitter)?.ExplicitStack(change);
        }
        
        void Track(OpCode code, object operand)
        {
            if (_paused)
                return;
            var op = new Instruction(_instructions.Count, code, operand);
            _instructions.Add(op);
            foreach (var l in _labelsToMarkOnNextInstruction)
                _labels[l] = op;
            _labelsToMarkOnNextInstruction.Clear();
        }
        
        public IXamlXEmitter Emit(OpCode code)
        {
            Track(code, null);
            _inner.Emit(code);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, IXamlXField field)
        {
            Track(code, field);
            _inner.Emit(code, field);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, IXamlXMethod method)
        {
            Track(code, method);
            _inner.Emit(code, method);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, IXamlXConstructor ctor)
        {
            Track(code, ctor);
            _inner.Emit(code, ctor);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, string arg)
        {
            Track(code, arg);
            _inner.Emit(code, arg);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, int arg)
        {
            Track(code, arg);
            _inner.Emit(code, arg);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, long arg)
        {
            Track(code, arg);
            _inner.Emit(code, arg);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, IXamlXType type)
        {
            Track(code, type);
            _inner.Emit(code, type);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, float arg)
        {
            Track(code, arg);
            _inner.Emit(code, arg);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, double arg)
        {
            Track(code, arg);
            _inner.Emit(code, arg);
            return this;
        }

        public IXamlXLocal DefineLocal(IXamlXType type)
        {
            return _inner.DefineLocal(type);
        }

        public IXamlXLabel DefineLabel()
        {
            var label = _inner.DefineLabel();
            _unmarkedLabels.Add(label, null);//, Environment.StackTrace);
            return label;
        }

        public IXamlXEmitter MarkLabel(IXamlXLabel label)
        {
            if (!_unmarkedLabels.Remove(label))
                throw new InvalidOperationException("Attempt to mark undeclared label");
            _inner.MarkLabel(label);
            _labelsToMarkOnNextInstruction.Add(label);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, IXamlXLabel label)
        {
            Track(code, label);
            _inner.Emit(code, label);
            return this;
        }

        public IXamlXEmitter Emit(OpCode code, IXamlXLocal local)
        {
            Track(code, local);
            _inner.Emit(code, local);
            return this;
        }

        public void InsertSequencePoint(IFileSource file, int line, int position)
        {
            _inner.InsertSequencePoint(file, line, position);
        }

        public XamlXLocalsPool LocalsPool => _inner.LocalsPool;

        int? VerifyAndGetBalanceAtExit(bool expectReturn)
        {
            if (_instructions.Count == 0)
                return 0;
            var toInspect = new Stack<int>();
            toInspect.Push(0);
            _instructions[0].ExpectedBalance = 0;

            if (_labelsToMarkOnNextInstruction.Count != 0
                || _instructions.Last().Opcode != OpCodes.Nop
                || _instructions.Last().BalanceChange != 0)
                Track(OpCodes.Nop, null);
            int? returnBalance = null;
            while (toInspect.Count > 0)
            {
                var ip = toInspect.Pop();
                var currentBalance = _instructions[ip].ExpectedBalance.Value;
                while (ip < _instructions.Count)
                {
                    var op = _instructions[ip];
                    if (op.ExpectedBalance.HasValue && op.ExpectedBalance != currentBalance)
                        throw new InvalidProgramException(
                            $"Already have been at instruction offset {ip} ({op.Opcode}) with stack balance {op.ExpectedBalance}, current balance is {currentBalance}");
                    op.ExpectedBalance = currentBalance;
                    currentBalance += op.BalanceChange;
                    var control = op.Opcode.FlowControl;
                    if (control == FlowControl.Return)
                    {
                        if (!expectReturn)
                            throw new InvalidProgramException("Return flow control is not allowed for this emitter");
                        if (returnBalance.HasValue && currentBalance != returnBalance)
                            throw new InvalidProgramException(
                                $"Already have a return with different stack balance {returnBalance}, current stack balance is {currentBalance}");
                        returnBalance = currentBalance;
                        break;
                    }

                    if (op.JumpTo != null)
                    {
                        var jump = _labels[op.JumpTo];
                        if (jump.ExpectedBalance.HasValue && jump.ExpectedBalance != currentBalance)
                            throw new InvalidProgramException(
                                $"Already have been at instruction offset {jump.Offset} ({jump.Opcode}) with stack balance {jump.ExpectedBalance}, stack balance at jump from {op.Offset} is {currentBalance}");

                        if (jump.ExpectedBalance == null)
                        {
                            jump.ExpectedBalance = currentBalance;
                            toInspect.Push(jump.Offset);
                        }
                    }
                    
                    if (control == FlowControl.Break || control == FlowControl.Throw || control == FlowControl.Branch)
                        break;

                    ip++;
                }
            }

            return _instructions.Last().ExpectedBalance;
        }
        
        
        public void Check(int? expectedBalance, bool expectReturn)
        {
            if (_unmarkedLabels.Count != 0)
                throw new InvalidProgramException("Code block has unmarked labels defined at:\n" +
                                                    string.Join("\n", _unmarkedLabels.Values));


            var balance = VerifyAndGetBalanceAtExit(expectReturn);
            if (expectedBalance != balance)
                throw new InvalidProgramException($"Unbalanced stack, expected {expectedBalance} got {balance}");
        }
    }
}
