﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ObjectInfrastructure.cs" company="">
//   
// </copyright>
// <summary>
//   
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Il2Native.Logic.Gencode
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using CodeParts;

    using Il2Native.Logic.Gencode.SynthesizedMethods.Base;

    using PEAssemblyReader;
    using SynthesizedMethods;
    using OpCodesEmit = System.Reflection.Emit.OpCodes;

    /// <summary>
    /// </summary>
    public static class ObjectInfrastructure
    {
        /// <summary>
        /// </summary>
        public const int FunctionsOffsetInVirtualTable = 2;

        public static void WriteAllocateMemory(
            this CWriter cWriter,
            OpCodePart opCodePart,
            FullyDefinedReference size)
        {
            var writer = cWriter.Output;

            writer.Write("(Byte*) ");
            writer.Write(cWriter.GetAllocator(false, false));
            writer.Write("(");
            cWriter.WriteResult(size);
            writer.Write(")");
        }

        public static IlCodeBuilder GetAllocateMemoryCodeForObject(
            this ITypeResolver typeResolver, IType declaringClassType, bool doNotTestNullValue, bool enableStringFastAllocation = false)
        {
            IlCodeBuilder newAlloc;
            if (declaringClassType.IsMultiArray)
            {
                newAlloc = ArrayMultiDimensionGen.MultiDimArrayAllocationSizeMethodBody(typeResolver, declaringClassType);
            }
            else if (declaringClassType.IsArray)
            {
                newAlloc = ArraySingleDimensionGen.SingleDimArrayAllocationSizeMethodBody(typeResolver, declaringClassType);
            }
            else if (enableStringFastAllocation && declaringClassType.IsString)
            {
                newAlloc = StringGen.StringAllocationSizeMethodBody(typeResolver, declaringClassType, typeResolver.System.System_Char, true);
            }
            else
            {
                newAlloc = new IlCodeBuilder();
                newAlloc.SizeOf(declaringClassType);
            }

            // static is not part of class
            var isAtomicAllocation = typeResolver.CanBeAllocatedAtomically(declaringClassType);

            var localNumber = -1;
            if (isAtomicAllocation)
            {
                localNumber = newAlloc.Locals.Count;
                newAlloc.Locals.Add(typeResolver.System.System_Int32);

                newAlloc.Add(Code.Dup);
                newAlloc.SaveLocal(localNumber);
            }

#if NO_GC_MALLOC_IGNORE_OFF_PAGE
            newAlloc.Call(
                new SynthesizedMethod(
                    typeResolver.GetAllocator(isAtomicAllocation),
                    typeResolver.System.System_Void.ToPointerType(),
                    new[] { typeResolver.System.System_Int32.ToParameter("size") }));
#else
            newAlloc.Add(Code.Dup);
            // 100K
            newAlloc.LoadConstant(100 * 1024);
            var ifBigger100k = newAlloc.Branch(Code.Bge_Un, Code.Bge_Un_S);

            newAlloc.Call(
                new SynthesizedMethod(
                    typeResolver.GetAllocator(isAtomicAllocation, false),
                    typeResolver.System.System_Void.ToPointerType(),
                    new[] { typeResolver.System.System_Int32.ToParameter("size") }));

            var leave = newAlloc.Branch(Code.Br, Code.Br_S);

            newAlloc.Add(ifBigger100k);

            newAlloc.Call(
                new SynthesizedMethod(
                    typeResolver.GetAllocator(isAtomicAllocation, true),
                    typeResolver.System.System_Void.ToPointerType(),
                    new[] { typeResolver.System.System_Int32.ToParameter("size") }));

            newAlloc.Add(leave);
#endif

            if (isAtomicAllocation)
            {
                // if this is atomic, you need to init memory
                newAlloc.Add(Code.Dup);
                newAlloc.LoadConstant(0);
                newAlloc.LoadLocal(localNumber);
                newAlloc.Add(Code.Initblk);
            }

            newAlloc.Castclass(declaringClassType);

            return newAlloc;
        }

        private static bool CanBeAllocatedAtomically(this ITypeResolver typeResolver, IType declaringClassType)
        {
            if (declaringClassType.IsInterface)
            {
                return true;
            }

            return Logic.IlReader.Fields(declaringClassType, IlReader.DefaultFlags | BindingFlags.FlattenHierarchy, typeResolver)
                        .All(typeResolver.IsAtomicValue);
        }

        private static bool IsAtomicValue(this ITypeResolver typeResolver, IField field)
        {
            if (field.IsConst || field.IsVirtualTable || field.IsStatic || typeResolver.IsAtomicValue(field.FieldType))
            {
                return true;
            }

            if (field.IsFixed)
            {
                var elementType = field.FieldType.GetElementType();
                return typeResolver.IsAtomicValue(elementType);
            }

            return false;
        }

        private static bool IsAtomicValue(this ITypeResolver typeResolver, IType type)
        {
            if (!type.IsValueType || type.IsPointer)
            {
                return false;
            }

            if (type.IsStructureType())
            {
                return typeResolver.CanBeAllocatedAtomically(type);
            }

            return true;
        }

        public static IlCodeBuilder GetBoxMethod(
            this ITypeResolver typeResolver,
            IType type,
            bool doNotTestNullValue)
        {
            var declaringClassType = type.ToClass();
            var normal = type.ToNormal();
            var isStruct = normal.IsStructureType();

            var ilCodeBuilder = typeResolver.GetNewMethod(declaringClassType, true, doNotTestNullValue);

            ilCodeBuilder.Parameters.Add(normal.ToParameter("_value"));

            // we need to remove last code which is Code.Ret
            ilCodeBuilder.RemoveLast();

            if (!isStruct)
            {
                ilCodeBuilder.Add(Code.Dup);
                ilCodeBuilder.LoadArgument(0);
                ilCodeBuilder.SaveField(declaringClassType.GetFieldByFieldNumber(0, typeResolver));
            }
            else
            {
                // in case nullable does not have value just return null
                if (declaringClassType.TypeEquals(typeResolver.System.System_Nullable_T))
                {
                    ilCodeBuilder.LoadArgument(0);
                    ilCodeBuilder.LoadField(declaringClassType.GetFieldByFieldNumber(0, typeResolver));
                    var jump = ilCodeBuilder.Branch(Code.Brtrue, Code.Brtrue_S);
                    ilCodeBuilder.LoadNull();
                    ilCodeBuilder.Add(Code.Ret);
                    ilCodeBuilder.Add(jump);
                }

                // copy structure
                ilCodeBuilder.Add(Code.Dup);
                ilCodeBuilder.LoadArgument(0);
                ilCodeBuilder.CopyObject(declaringClassType);
            }

            ilCodeBuilder.Add(Code.Dup);
            ilCodeBuilder.Call(declaringClassType.GetFirstMethodByName(SynthesizedInitMethod.Name, typeResolver));

            ilCodeBuilder.Add(Code.Ret);

            return ilCodeBuilder;
        }

        public static IlCodeBuilder GetUnboxMethod(this ITypeResolver typeResolver, IType type)
        {
            var declaringClassType = type.ToClass();

            var ilCodeBuilder = new IlCodeBuilder();

            if (!type.IsStructureType())
            {
                var firstField = declaringClassType.GetFieldByFieldNumber(0, typeResolver);
                if (firstField != null)
                {
                    ilCodeBuilder.LoadArgument(0);
                    ilCodeBuilder.LoadField(firstField);
                }
                else
                {
                    ilCodeBuilder.LoadConstant(0);
                }
            }
            else
            {
                ilCodeBuilder.LoadArgument(0);
                ilCodeBuilder.LoadObject(type);
            }

            ilCodeBuilder.Add(Code.Ret);

            return ilCodeBuilder;
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        public static void WriteCallBoxObjectMethod(this IType type, CWriter cWriter, OpCodePart opCode)
        {
            var method = new SynthesizedBoxMethod(type, cWriter);
            cWriter.WriteCall(
                opCode,
                method,
                false,
                false,
                false,
                null,
                cWriter.tryScopes.Count > 0 ? cWriter.tryScopes.Peek() : null);
        }

        /// <summary>
        /// </summary>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCodeConstructorInfoPart">
        /// </param>
        public static void WriteCallConstructor(
            this CWriter cWriter,
            OpCodeConstructorInfoPart opCodeConstructorInfoPart)
        {
            cWriter.WriteCallConstructor(opCodeConstructorInfoPart, opCodeConstructorInfoPart.Operand);
        }

        /// <summary>
        /// </summary>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCodePart">
        /// </param>
        /// <param name="methodBase">
        /// </param>
        public static void WriteCallConstructor(
            this CWriter cWriter,
            OpCodePart opCodePart,
            IConstructor methodBase)
        {
            var resAlloc = opCodePart.Result;
            cWriter.WriteCall(
                opCodePart,
                methodBase,
                opCodePart.ToCode() == Code.Callvirt,
                true,
                true,
                resAlloc,
                cWriter.tryScopes.Count > 0 ? cWriter.tryScopes.Peek() : null);
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        public static void WriteCallGetTypeObjectMethod(this IType type, CWriter cWriter, OpCodePart opCode)
        {
            var method = new SynthesizedGetTypeStaticMethod(type, cWriter);
            var opCodeNope = OpCodePart.CreateNop;
            opCodeNope.OpCodeOperands = opCode.OpCodeOperands;
            opCodeNope.UsedBy = new UsedByInfo(opCode);
            cWriter.WriteCall(
                opCodeNope,
                method,
                false,
                false,
                false,
                opCode.Result,
                cWriter.tryScopes.Count > 0 ? cWriter.tryScopes.Peek() : null);
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        public static void WriteCallInitObjectMethod(this IType type, CWriter cWriter, OpCodePart opCode)
        {
            var method = new SynthesizedInitMethod(type, cWriter);
            var opCodeNope = OpCodePart.CreateNop;
            opCodeNope.OpCodeOperands = opCode.OpCodeOperands;
            opCodeNope.UsedBy = new UsedByInfo(opCode);
            cWriter.WriteCall(
                opCodeNope,
                method,
                false,
                true,
                false,
                opCode.Result,
                cWriter.tryScopes.Count > 0 ? cWriter.tryScopes.Peek() : null);
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        public static void WriteCallNewObjectMethod(this IType type, CWriter cWriter, OpCodePart opCode)
        {
            var method = new SynthesizedNewMethod(type, cWriter);
            var opCodeNope = OpCodePart.CreateNop;
            opCodeNope.UsedBy = new UsedByInfo(opCode);
            opCodeNope.OpCodeOperands = opCode.OpCodeOperands;
            cWriter.WriteCall(
                opCodeNope,
                method,
                false,
                false,
                false,
                opCode.Result,
                cWriter.tryScopes.Count > 0 ? cWriter.tryScopes.Peek() : null);

            cWriter.Output.WriteLine(";");
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        public static void WriteCallUnboxObjectMethod(this IType type, CWriter cWriter, OpCodePart opCode)
        {
            var method = new SynthesizedUnboxMethod(type, cWriter);
            cWriter.WriteCall(
                opCode,
                method,
                false,
                true,
                false,
                null,
                cWriter.tryScopes.Count > 0 ? cWriter.tryScopes.Peek() : null);
        }

        /// <summary>
        /// </summary>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        /// <param name="declaringType">
        /// </param>
        public static void WriteGetHashCodeObjectForEnum(
            this CWriter cWriter,
            OpCodePart opCode,
            IType declaringType)
        {
            var writer = cWriter.Output;

            var isStruct = declaringType.IsStructureType();

            writer.WriteLine("; Returning Hash Code");
            writer.WriteLine(string.Empty);

            writer.WriteLine("; Get data");

            if (!isStruct)
            {
                // write access to a field
                if (cWriter.WriteFieldAccess(
                        opCode,
                        declaringType.ToClass(),
                        declaringType.ToClass(),
                        0,
                        opCode.Result) == null)
                {
                    writer.WriteLine("// No data");
                    return;
                }
            }
            else
            {
                Debug.Fail("Not implemented yet");
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCodePart">
        /// </param>
        /// <param name="declaringTypeIn">
        /// </param>
        public static void WriteInit(
            this CWriter cWriter,
            OpCodePart opCodePart,
            IType declaringTypeIn)
        {
            var writer = cWriter.Output;

            var declaringTypeNormal = declaringTypeIn.ToNormal();
            if (declaringTypeNormal.IsValueType)
            {
                cWriter.WriteMemSet(declaringTypeNormal, opCodePart.OpCodeOperands[0]);
                if (declaringTypeNormal.IsStructureType())
                {
                    // and you need to call Init to allow to use Struct address in virtual calls for Code.Constrained
                    cWriter.Output.WriteLine(";");
                    declaringTypeNormal.WriteCallInitObjectMethod(cWriter, opCodePart);
                }
            }
            else
            {
                // this is type reference, initialize it with null
                cWriter.LoadIndirect(writer, opCodePart, declaringTypeNormal);
                writer.Write(" = 0");
            }
        }

        public static IlCodeBuilder GetInitMethod(this ITypeResolver typeResolver, IType declaringType)
        {
            var codeBuilder = new IlCodeBuilder();

            if (declaringType.HasAnyVirtualMethod(typeResolver))
            {
                // set virtual table
                codeBuilder.LoadArgument(0);
                codeBuilder.LoadToken(declaringType.ToVirtualTableImplementation());
                codeBuilder.SaveField(typeResolver.System.System_Object.GetFieldByName(CWriter.VTable, typeResolver));
            }

            // init all interfaces
            foreach (var @interface in declaringType.SelectAllTopAndAllNotFirstChildrenInterfaces())
            {
                // set virtual table
                codeBuilder.LoadArgument(0);
                // you need to have next line to select correct interface in case the same interface used many times
                codeBuilder.Castclass(@interface);
                codeBuilder.LoadToken(@interface.ToVirtualTableImplementation(declaringType));
                codeBuilder.SaveField(@interface.GetInterfaceVTable(typeResolver));
            }

            codeBuilder.Add(Code.Ret);

            return codeBuilder;
        }

        public static IlCodeBuilder GetGetTypeMethod(this ITypeResolver typeResolver, IType declaringType)
        {
            var codeBuilder = new IlCodeBuilder();

            codeBuilder.Call(declaringType.GetFirstMethodByName(SynthesizedGetTypeStaticMethod.Name, typeResolver));
            codeBuilder.Add(Code.Ret);

            return codeBuilder;
        }

        public static IlCodeBuilder GetGetTypeStaticMethod(this ITypeResolver typeResolver, IType declaringType)
        {
            var bytesArrayType = typeResolver.System.System_Byte.ToArrayType(1);

            var codeBuilder = new IlCodeBuilder();

            var typeStorageType = declaringType.GetFieldByName(".type", typeResolver);
            codeBuilder.LoadField(typeStorageType);
            var jumpIfNotNull = codeBuilder.Branch(Code.Brtrue, Code.Brtrue_S);

            codeBuilder.LoadFieldAddress(typeStorageType);
#if !MSCORLIB
            codeBuilder.LoadToken(new SynthesizedConstBytesField(new RuntimeTypeConstBytes(declaringType)));
            codeBuilder.New(Logic.IlReader.FindConstructor(typeResolver.System.System_RuntimeType, bytesArrayType, typeResolver));
#else
            codeBuilder.New(Logic.IlReader.FindConstructor(typeResolver.System.System_RuntimeType, typeResolver));
#endif
            codeBuilder.LoadNull();

            codeBuilder.Call(
                typeResolver.ResolveType("System.Threading.Interlocked")
                            .GetMethodsByName("CompareExchange", typeResolver)
                            .First(m => m.GetParameters().First().ParameterType.TypeEquals(typeResolver.System.System_Object.ToByRefType())));

            codeBuilder.Add(Code.Pop);

            codeBuilder.Add(jumpIfNotNull);

            codeBuilder.LoadField(typeStorageType);
            codeBuilder.Add(Code.Ret);

            return codeBuilder;
        }

        /// <summary>
        /// </summary>
        /// <param name="cWriter">
        /// </param>
        /// <param name="opCodeConstructorInfoPart">
        /// </param>
        /// <param name="declaringType">
        /// </param>
        public static void WriteNew(
            this CWriter cWriter,
            OpCodeConstructorInfoPart opCodeConstructorInfoPart,
            IType declaringType)
        {
            var @class = declaringType.ToClass();
            var objectReference = cWriter.WriteVariableForNew(opCodeConstructorInfoPart, @class);

            @class.WriteCallNewObjectMethod(cWriter, opCodeConstructorInfoPart);

            opCodeConstructorInfoPart.Result = objectReference;
            cWriter.WriteCallConstructor(opCodeConstructorInfoPart);
        }

        public static void WriteNew(
            this CWriter cWriter,
            OpCodePart opCodePart,
            IType declaringType)
        {
            var @class = declaringType.ToClass();
            var objectReference = cWriter.WriteVariableForNew(opCodePart, @class);
            @class.WriteCallNewObjectMethod(cWriter, opCodePart);

            opCodePart.Result = objectReference;
        }

        /// <summary>
        /// </summary>
        /// <param name="typeResolver">
        /// </param>
        /// <param name="opCodePart">
        /// </param>
        /// <param name="type">
        /// </param>
        /// <param name="doNotCallInit">
        /// </param>
        /// <param name="doNotTestNullValue">
        /// </param>
        public static IlCodeBuilder GetNewMethod(
            this ITypeResolver typeResolver,
            IType type,
            bool doNotCallInit = false,
            bool doNotTestNullValue = false,
            bool enableStringFastAllocation = false)
        {
            var declaringClassType = type.ToClass();

            var ilCodeBuilder = typeResolver.GetAllocateMemoryCodeForObject(declaringClassType, doNotTestNullValue, enableStringFastAllocation);

            if (!doNotCallInit)
            {
                ilCodeBuilder.Add(Code.Dup);
                ilCodeBuilder.Call(new SynthesizedInitMethod(declaringClassType, typeResolver));
            }

            if (!enableStringFastAllocation)
            {
                ilCodeBuilder.Add(Code.Ret);
            }

            return ilCodeBuilder;
        }

        public static IlCodeBuilder GetSizeMethod(
            this ITypeResolver typeResolver,
            IType type)
        {
            var ilCodeBuilder = new IlCodeBuilder();
            ilCodeBuilder.SizeOf(type.ToClass());
            ilCodeBuilder.Add(Code.Ret);
            return ilCodeBuilder;
        }
    }
}