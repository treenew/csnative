////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Apache License 2.0 (Apache)
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
namespace System
{
    using System.Reflection;
    using System.Collections;
    using System.Runtime.CompilerServices;

    [Serializable]
    public abstract class Enum : ValueType
    {
        public override String ToString()
        {
            throw new NotImplementedException();
        }
    }
}


