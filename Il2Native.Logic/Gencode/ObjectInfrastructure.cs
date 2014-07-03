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
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Text;

    using Il2Native.Logic.CodeParts;

    using PEAssemblyReader;

    /// <summary>
    /// </summary>
    public static class ObjectInfrastructure
    {
        /// <summary>
        /// </summary>
        /// <param name="llvmWriter">
        /// </param>
        /// <param name="opCodeConstructorInfoPart">
        /// </param>
        public static void WriteCallConstructor(this LlvmWriter llvmWriter, OpCodeConstructorInfoPart opCodeConstructorInfoPart)
        {
            llvmWriter.WriteCallConstructor(opCodeConstructorInfoPart, opCodeConstructorInfoPart.Operand);
        }

        public static void WriteCallConstructor(this LlvmWriter llvmWriter, OpCodePart opCodePart, IConstructor methodBase)
        {
            var writer = llvmWriter.Output;

            writer.WriteLine(String.Empty);
            writer.WriteLine("; Call Constructor");
            var resAlloc = opCodePart.Result;
            opCodePart.Result = null;
            llvmWriter.WriteCall(
                opCodePart,
                methodBase,
                opCodePart.ToCode() == Code.Callvirt,
                true,
                true,
                resAlloc,
                llvmWriter.tryScopes.Count > 0 ? llvmWriter.tryScopes.Peek() : null);
            opCodePart.Result = resAlloc;
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="llvmWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        public static void WriteCallInitObjectMethod(this IType type, LlvmWriter llvmWriter, OpCodePart opCode)
        {
            var writer = llvmWriter.Output;

            var method = new SynthesizedInitMethod(type, llvmWriter);
            writer.WriteLine("; call Init Object method");
            var opCodeNope = OpCodePart.CreateNop;
            llvmWriter.WriteCall(opCodeNope, method, false, true, false, opCode.Result, llvmWriter.tryScopes.Count > 0 ? llvmWriter.tryScopes.Peek() : null);
        }

        /// <summary>
        /// </summary>
        /// <param name="llvmWriter">
        /// </param>
        /// <param name="opCode">
        /// </param>
        public static void WriteInitObject(this LlvmWriter llvmWriter, OpCodePart opCode)
        {
            var declaringType = opCode.Result.Type;
            if (declaringType.IsInterface)
            {
                return;
            }

            var writer = llvmWriter.Output;

            // Init Object From Here
            if (declaringType.HasAnyVirtualMethod())
            {
                var opCodeResult = opCode.Result;

                writer.WriteLine("; set virtual table");

                // initializw virtual table
                if (opCode.HasResult)
                {
                    opCode.Result.Type.UseAsClass = true;
                    llvmWriter.WriteCast(opCode, opCode.Result, llvmWriter.ResolveType("System.Byte").CreatePointer().CreatePointer(), true);
                }
                else
                {
                    declaringType.UseAsClass = true;
                    llvmWriter.WriteCast(opCode, declaringType, "%this", llvmWriter.ResolveType("System.Byte").CreatePointer().CreatePointer(), true);
                }

                writer.WriteLine(String.Empty);

                var virtualTable = declaringType.GetVirtualTable();

                writer.Write(
                    "store i8** getelementptr inbounds ([{0} x i8*]* {1}, i64 0, i64 2), i8*** ",
                    virtualTable.GetVirtualTableSize(),
                    declaringType.GetVirtualTableName());
                if (opCode.HasResult)
                {
                    llvmWriter.WriteResultNumber(opCode.Result);
                }

                writer.WriteLine(String.Empty);

                // restore
                opCode.Result = opCodeResult;
            }

            // init all interfaces
            foreach (var @interface in declaringType.GetInterfaces())
            {
                var opCodeResult = opCode.Result;

                writer.WriteLine("; set virtual interface table");

                llvmWriter.WriteInterfaceAccess(writer, opCode, declaringType, @interface);

                if (opCode.HasResult)
                {
                    opCode.Result.Type.UseAsClass = true;
                    llvmWriter.WriteCast(opCode, opCode.Result, llvmWriter.ResolveType("System.Byte").CreatePointer().CreatePointer(), true);
                }
                else
                {
                    llvmWriter.WriteCast(opCode, @interface, "%this", llvmWriter.ResolveType("System.Byte").CreatePointer().CreatePointer(), true);
                }

                writer.WriteLine(String.Empty);

                var virtualInterfaceTable = declaringType.GetVirtualInterfaceTable(@interface);

                writer.Write(
                    "store i8** getelementptr inbounds ([{0} x i8*]* {1}, i64 0, i64 1), i8*** ",
                    virtualInterfaceTable.GetVirtualTableSize(),
                    declaringType.GetVirtualInterfaceTableName(@interface));
                llvmWriter.WriteResultNumber(opCode.Result);
                writer.WriteLine(String.Empty);

                // restore
                opCode.Result = opCodeResult;
            }
        }

        public static bool HasInterface(this IType classType, IType @interface)
        {
            Debug.Assert(@interface.IsInterface);

            var type = classType;

            while (!type.GetInterfaces().Contains(@interface) || type.BaseType != null && type.BaseType.GetInterfaces().Contains(@interface))
            {
                type = type.BaseType;
                if (type == null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="llvmWriter">
        /// </param>
        public static void WriteInitObjectMethod(this IType type, LlvmWriter llvmWriter)
        {
            var writer = llvmWriter.Output;

            var method = new SynthesizedInitMethod(type, llvmWriter);
            writer.WriteLine("; Init Object method");

            type.UseAsClass = true;

            var opCode = OpCodePart.CreateNop;
            llvmWriter.WriteMethodStart(method);
            llvmWriter.WriteLlvmLoad(opCode, type, new FullyDefinedReference("%.this", llvmWriter.ThisType), true, true);
            writer.WriteLine(String.Empty);
            llvmWriter.WriteInitObject(opCode);
            writer.WriteLine("ret void");
            llvmWriter.WriteMethodEnd(method);
        }

        /// <summary>
        /// </summary>
        /// <param name="llvmWriter">
        /// </param>
        /// <param name="opCodeConstructorInfoPart">
        /// </param>
        /// <param name="declaringType">
        /// </param>
        public static void WriteNew(this LlvmWriter llvmWriter, OpCodeConstructorInfoPart opCodeConstructorInfoPart, IType declaringType)
        {
            if (opCodeConstructorInfoPart.HasResult)
            {
                return;
            }

            var writer = llvmWriter.Output;

            llvmWriter.WriteNewWithoutCallingConstructor(opCodeConstructorInfoPart, declaringType);
            llvmWriter.WriteCallConstructor(opCodeConstructorInfoPart);
        }

        public static void WriteNewWithoutCallingConstructor(this LlvmWriter llvmWriter, OpCodePart opCodePart, IType declaringType)
        {
            if (opCodePart.HasResult)
            {
                return;
            }

            declaringType.UseAsClass = true;

            var writer = llvmWriter.Output;

            writer.WriteLine("; New obj");

            var mallocResult = llvmWriter.WriteSetResultNumber(opCodePart, llvmWriter.ResolveType("System.Byte").CreatePointer());
            var size = declaringType.GetTypeSize();
            writer.WriteLine("call i8* @_Znwj(i32 {0})", size);
            llvmWriter.WriteMemSet(declaringType, mallocResult);
            writer.WriteLine(String.Empty);

            llvmWriter.WriteBitcast(opCodePart, mallocResult, declaringType);
            writer.WriteLine(String.Empty);

            var castResult = opCodePart.Result;

            // llvmWriter.WriteInitObject(writer, opCode, declaringType);
            declaringType.WriteCallInitObjectMethod(llvmWriter, opCodePart);

            // restore result and type
            opCodePart.Result = castResult;

            writer.WriteLine(String.Empty);
            writer.Write("; end of new obj");
        }

        /// <summary>
        /// </summary>
        private class SynthesizedInitMethod : IMethod
        {
            private LlvmWriter writer;

            /// <summary>
            /// </summary>
            /// <param name="type">
            /// </param>
            public SynthesizedInitMethod(IType type, LlvmWriter writer)
            {
                this.Type = type;
                this.writer = writer;
            }

            /// <summary>
            /// </summary>
            public string AssemblyQualifiedName { get; private set; }

            /// <summary>
            /// </summary>
            public CallingConventions CallingConvention
            {
                get
                {
                    return CallingConventions.HasThis;
                }
            }

            /// <summary>
            /// </summary>
            public IType DeclaringType
            {
                get
                {
                    return this.Type;
                }
            }

            /// <summary>
            /// </summary>
            public IEnumerable<IExceptionHandlingClause> ExceptionHandlingClauses
            {
                get
                {
                    return new IExceptionHandlingClause[0];
                }
            }

            /// <summary>
            /// custom field
            /// </summary>
            public bool IsInternalCall { get { return false; } }

            /// <summary>
            /// </summary>
            public string FullName
            {
                get
                {
                    return String.Concat(this.Type.FullName, "..init");
                }
            }

            /// <summary>
            /// </summary>
            public bool IsAbstract { get; private set; }

            /// <summary>
            /// </summary>
            public bool IsConstructor { get; private set; }

            /// <summary>
            /// </summary>
            public bool IsGenericMethod { get; private set; }

            /// <summary>
            /// </summary>
            public bool IsOverride { get; private set; }

            /// <summary>
            /// </summary>
            public bool IsStatic { get; private set; }

            /// <summary>
            /// </summary>
            public bool IsVirtual { get; private set; }

            /// <summary>
            /// </summary>
            public IEnumerable<ILocalVariable> LocalVariables
            {
                get
                {
                    return new ILocalVariable[0];
                }
            }

            /// <summary>
            /// </summary>
            public IModule Module { get; private set; }

            /// <summary>
            /// </summary>
            public string Name
            {
                get
                {
                    return String.Concat(this.Type.Name, "..init");
                }
            }

            /// <summary>
            /// </summary>
            public string Namespace { get; private set; }

            /// <summary>
            /// </summary>
            public IType ReturnType
            {
                get
                {
                    return this.writer.ResolveType("System.Void");
                }
            }

            /// <summary>
            /// </summary>
            public IType Type { get; private set; }

            /// <summary>
            /// </summary>
            /// <param name="obj">
            /// </param>
            /// <returns>
            /// </returns>
            /// <exception cref="NotImplementedException">
            /// </exception>
            public int CompareTo(object obj)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// </summary>
            /// <param name="obj">
            /// </param>
            /// <returns>
            /// </returns>
            public override bool Equals(object obj)
            {
                return this.ToString().Equals(obj.ToString());
            }

            /// <summary>
            /// </summary>
            /// <returns>
            /// </returns>
            public IEnumerable<IType> GetGenericArguments()
            {
                return null;
            }

            /// <summary>
            /// </summary>
            /// <returns>
            /// </returns>
            public override int GetHashCode()
            {
                return this.ToString().GetHashCode();
            }

            /// <summary>
            /// </summary>
            /// <returns>
            /// </returns>
            public byte[] GetILAsByteArray()
            {
                return new byte[0];
            }

            /// <summary>
            /// </summary>
            /// <returns>
            /// </returns>
            public IMethodBody GetMethodBody()
            {
                return new SynthesizedInitMethodBody();
            }

            /// <summary>
            /// </summary>
            /// <returns>
            /// </returns>
            public IEnumerable<IParameter> GetParameters()
            {
                return new IParameter[0];
            }

            /// <summary>
            /// </summary>
            /// <returns>
            /// </returns>
            public override string ToString()
            {
                var result = new StringBuilder();

                // write return type
                result.Append(this.ReturnType);
                result.Append(' ');

                // write Full Name
                result.Append(this.FullName);

                // write Parameter Types
                result.Append('(');
                var index = 0;
                foreach (var parameterType in this.GetParameters())
                {
                    if (index != 0)
                    {
                        result.Append(", ");
                    }

                    result.Append(parameterType);
                    index++;
                }

                result.Append(')');

                return result.ToString();
            }
        }

        /// <summary>
        /// </summary>
        private class SynthesizedInitMethodBody : IMethodBody
        {
            /// <summary>
            /// </summary>
            public IEnumerable<IExceptionHandlingClause> ExceptionHandlingClauses
            {
                get
                {
                    return new IExceptionHandlingClause[0];
                }
            }

            /// <summary>
            /// </summary>
            public IEnumerable<ILocalVariable> LocalVariables
            {
                get
                {
                    return new ILocalVariable[0];
                }
            }

            /// <summary>
            /// </summary>
            /// <returns>
            /// </returns>
            public byte[] GetILAsByteArray()
            {
                return new byte[0];
            }
        }
    }
}