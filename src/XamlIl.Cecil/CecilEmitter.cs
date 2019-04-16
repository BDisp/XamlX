using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using SreOpCode = System.Reflection.Emit.OpCode;
using SreOpCodes = System.Reflection.Emit.OpCodes;

namespace XamlIl.TypeSystem
{
    public partial class CecilTypeSystem
    {
        public class CecilEmitter : IXamlIlEmitter
        {
            TypeReference Import(TypeReference r)
            {
                var rv = M.ImportReference(r);
                if (r is GenericInstanceType gi)
                {
                    for (var c = 0; c < gi.GenericArguments.Count; c++)
                        gi.GenericArguments[c] = Import(gi.GenericArguments[c]);
                }

                return rv;
            }

            FieldReference Import(FieldReference f)
            {
                var rv = M.ImportReference(f);
                rv.FieldType = Import(rv.FieldType);
                return rv;
            }
            
            private static Dictionary<SreOpCode, OpCode> Dic = new Dictionary<SreOpCode, OpCode>();
            private List<CecilLabel> _markedLabels = new List<CecilLabel>();
            static CecilEmitter()
            {

                foreach (var sreField in typeof(SreOpCodes)
                    .GetFields(BindingFlags.Static | BindingFlags.Public)
                    .Where(f=>f.FieldType == typeof(SreOpCode)))

                {
                    var sre = (SreOpCode) sreField.GetValue(null);
                    var cecilField = typeof(OpCodes).GetField(sreField.Name);
                    if(cecilField == null)
                        continue;
                    var cecil = (OpCode)cecilField.GetValue(null);
                    Dic[sre] = cecil;
                }       
            }
            
            
            private readonly MethodBody _body;

            public CecilEmitter(IXamlIlTypeSystem typeSystem, MethodBody body)
            {
                _body = body;
                TypeSystem = typeSystem;
            }


            public IXamlIlTypeSystem TypeSystem { get; }

            IXamlIlEmitter Emit(Instruction i)
            {
                _body.Instructions.Add(i);
                foreach (var ml in _markedLabels)
                {
                    foreach(var instruction in _body.Instructions)
                        if (instruction.Operand == ml.Instruction)
                            instruction.Operand = i;
                    ml.Instruction = i;
                }
                _markedLabels.Clear();
                return this;
            }

            ParameterDefinition GetParameter(int arg)
            {
                if (_body.Method.HasThis)
                {
                    if (arg == 0)
                        return _body.ThisParameter;
                    arg--;
                }

                return _body.Method.Parameters[arg];
            }
            
            private Instruction CreateI(OpCode code, int arg)
            {
                if (code.OperandType == OperandType.ShortInlineArg || code.OperandType == OperandType.InlineArg)
                    return Instruction.Create(code, GetParameter(arg));
                if (code.OperandType == OperandType.InlineVar || code.OperandType == OperandType.ShortInlineVar)
                    return Instruction.Create(code, _body.Variables[arg]);
                return Instruction.Create(code, arg);
            }

            private ModuleDefinition M => _body.Method.Module;

            public IXamlIlEmitter Emit(SreOpCode code)
                => Emit(Instruction.Create(Dic[code]));

            public IXamlIlEmitter Emit(SreOpCode code, IXamlIlField field)
            {
                return Emit(Instruction.Create(Dic[code], Import(((CecilField) field).Field)));
            }

            public IXamlIlEmitter Emit(SreOpCode code, IXamlIlMethod method)
                => Emit(Instruction.Create(Dic[code], M.ImportReference(((CecilMethod) method).IlReference)));

            public IXamlIlEmitter Emit(SreOpCode code, IXamlIlConstructor ctor)
                => Emit(Instruction.Create(Dic[code], M.ImportReference(((CecilConstructor) ctor).IlReference)));

            public IXamlIlEmitter Emit(SreOpCode code, string arg)
                => Emit(Instruction.Create(Dic[code], arg));

            public IXamlIlEmitter Emit(SreOpCode code, int arg)
                => Emit(CreateI(Dic[code], arg));
            
            public IXamlIlEmitter Emit(SreOpCode code, long arg)
                => Emit(Instruction.Create(Dic[code], arg));
            
            public IXamlIlEmitter Emit(SreOpCode code, IXamlIlType type)
                => Emit(Instruction.Create(Dic[code], Import(((ITypeReference) type).Reference)));

            public IXamlIlEmitter Emit(SreOpCode code, float arg)
                => Emit(Instruction.Create(Dic[code], arg));

            public IXamlIlEmitter Emit(SreOpCode code, double arg)
                => Emit(Instruction.Create(Dic[code], arg));


            class CecilLocal : IXamlIlLocal
            {
                public VariableDefinition Variable { get; set; }
            }

            class CecilLabel : IXamlIlLabel
            {
                public Instruction Instruction { get; set; } = Instruction.Create(OpCodes.Nop);
            }

            public IXamlIlLocal DefineLocal(IXamlIlType type)
            {
                var r = Import(((ITypeReference) type).Reference);
                var def = new VariableDefinition(r);
                _body.Variables.Add(def);
                return new CecilLocal {Variable = def};
            }

            public IXamlIlLabel DefineLabel() => new CecilLabel();

            public IXamlIlEmitter MarkLabel(IXamlIlLabel label)
            {
                _markedLabels.Add((CecilLabel) label);
                return this;
            }

            public IXamlIlEmitter Emit(SreOpCode code, IXamlIlLabel label)
                => Emit(Instruction.Create(Dic[code], ((CecilLabel) label).Instruction));

            public IXamlIlEmitter Emit(SreOpCode code, IXamlIlLocal local)
                => Emit(Instruction.Create(Dic[code], ((CecilLocal) local).Variable));
        }
    }
}
