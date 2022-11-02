using System;
using Zinnia.Base;

namespace Zinnia.Recognizers
{
    public class FullNameGenerator : IFullNameGenerator
    {
        public string Separator;

        public FullNameGenerator(string Separator = ".")
        {
            this.Separator = Separator;
        }

        public string GetFullName(Identifier Id, bool Overload = false)
        {
            var State = Id.Container.State;
            var StructuredParent = Id.Container as StructuredScope;

            string Name;
            if (Id is Constructor) 
                Name = StructuredParent.StructuredType.Name.ToString();
            else if (Id is Destructor) 
                Name = "~" + StructuredParent.StructuredType.Name.ToString();
            else Name = Id.Name.ToString();

            var Current = Id.Container;
            while (Current != null)
            {
                if (Current is IdentifierScope && !(Current is AssemblyScope))
                {
                    var IdScope = Current as IdentifierScope;
                    Name = IdScope.Identifier.Name.ToString() + Separator + Name;
                }

                Current = Current.Parent;
            }

            if (Id is Function)
            {
                var Func = Id as Function;
                var FuncType = Func.Children[0].RealId as TypeOfFunction;
                var FuncCh = FuncType.Children;
                var Params = "";

                for (var i = 1; i < FuncCh.Length; i++)
                {
                    Params += State.GenerateName(FuncCh[i].TypeOfSelf);
                    if (i < FuncCh.Length - 1) Params += ", ";
                }

                Name += "(" + Params + ")";
            }

            return Name;
        }
    }
}