using Mono.Cecil;

namespace XamlX.TypeSystem
{
    public partial class CecilTypeSystem
    {
        class UnresolvedCecilType : XamlXPseudoType, ITypeReference
        {
            public TypeReference Reference { get; }

            public UnresolvedCecilType(TypeReference reference) : base("Unresolved:" + reference.FullName)
            {
                Reference = reference;
            }
        }
    }
}
