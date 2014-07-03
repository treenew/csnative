﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TypeGen.cs" company="">
//   
// </copyright>
// <summary>
//   
// </summary>
// --------------------------------------------------------------------------------------------------------------------
namespace Il2Native.Logic.Gencode
{
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Linq;

    using Il2Native.Logic.CodeParts;

    using PEAssemblyReader;

    /// <summary>
    /// </summary>
    public static class TypeGen
    {
        /// <summary>
        /// </summary>
        private static readonly IDictionary<string, int> SystemTypeSizes = new SortedDictionary<string, int>();

        /// <summary>
        /// </summary>
        private static readonly IDictionary<string, string> SystemTypesToCTypes = new SortedDictionary<string, string>();

        /// <summary>
        /// </summary>
        private static readonly IDictionary<string, int> sizeByType = new SortedDictionary<string, int>();

        /// <summary>
        /// </summary>
        static TypeGen()
        {
            SystemTypesToCTypes["Void"] = "void";
            SystemTypesToCTypes["Byte"] = "i8";
            SystemTypesToCTypes["SByte"] = "i8";
            SystemTypesToCTypes["Char"] = "i16";
            SystemTypesToCTypes["Int16"] = "i16";
            SystemTypesToCTypes["Int32"] = "i32";
            SystemTypesToCTypes["Int64"] = "i64";
            SystemTypesToCTypes["UInt16"] = "i16";
            SystemTypesToCTypes["UInt32"] = "i32";
            SystemTypesToCTypes["UInt64"] = "i64";
            SystemTypesToCTypes["Float"] = "float";
            SystemTypesToCTypes["Single"] = "float";
            SystemTypesToCTypes["Double"] = "double";
            SystemTypesToCTypes["Boolean"] = "i1";
            SystemTypesToCTypes["Byte&"] = "i8*";
            SystemTypesToCTypes["SByte&"] = "i8*";
            SystemTypesToCTypes["Char&"] = "i8*";
            SystemTypesToCTypes["Int16&"] = "i16*";
            SystemTypesToCTypes["Int32&"] = "i32*";
            SystemTypesToCTypes["Int64&"] = "i64*";
            SystemTypesToCTypes["IntPtr"] = "i32*";
            SystemTypesToCTypes["UIntPtr"] = "i32*";
            SystemTypesToCTypes["UInt16&"] = "i16**";
            SystemTypesToCTypes["UInt32&"] = "i32**";
            SystemTypesToCTypes["UInt64&"] = "i64*";
            SystemTypesToCTypes["Float&"] = "float*";
            SystemTypesToCTypes["Single&"] = "float*";
            SystemTypesToCTypes["Double&"] = "double*";
            SystemTypesToCTypes["Boolean&"] = "i1*";

            SystemTypeSizes["Void"] = 0;
            SystemTypeSizes["Byte"] = 1;
            SystemTypeSizes["SByte"] = 1;
            SystemTypeSizes["Char"] = 2;
            SystemTypeSizes["Int16"] = 2;
            SystemTypeSizes["Int32"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Int64"] = 8;
            SystemTypeSizes["UInt16"] = 2;
            SystemTypeSizes["UInt32"] = LlvmWriter.PointerSize;
            SystemTypeSizes["UInt64"] = 8;
            SystemTypeSizes["Float"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Single"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Double"] = 8;
            SystemTypeSizes["Boolean"] = 1;
            SystemTypeSizes["Byte&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["SByte&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Char&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Int16&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Int32&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Int64&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["IntPtr"] = LlvmWriter.PointerSize;
            SystemTypeSizes["UIntPtr"] = LlvmWriter.PointerSize;
            SystemTypeSizes["UInt16&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["UInt32&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["UInt64&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Float&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Single&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Double&"] = LlvmWriter.PointerSize;
            SystemTypeSizes["Boolean&"] = LlvmWriter.PointerSize;
        }

        public static void Clear()
        {
            sizeByType.Clear();
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <returns>
        /// </returns>
        public static int CalculateSize(this IType type)
        {
            if (type.IsInterface)
            {
                // i8** (...)
                return LlvmWriter.PointerSize;
            }

            if (type.IsEnum)
            {
                return type.GetEnumUnderlyingType().GetTypeSize();
            }

            var size = 0;

            // add shift for virtual table
            if (type.IsRootOfVirtualTable())
            {
                size += LlvmWriter.PointerSize;
            }

            if (type.BaseType != null)
            {
                size += type.BaseType.GetTypeSize();
            }

            // add shift for interfaces
            if (type.BaseType == null)
            {
                size += type.GetInterfaces().Count() * LlvmWriter.PointerSize;
            }
            else
            {
                var baseInterfaces = type.BaseType.GetInterfaces();
                size += type.GetInterfaces().Count(i => !baseInterfaces.Contains(i)) * LlvmWriter.PointerSize;
            }

            foreach (var field in IlReader.Fields(type).Where(t => !t.IsStatic).ToList())
            {
                if (field.FieldType.IsStructureType())
                {
                    size += field.FieldType.GetTypeSize();
                }

                var fieldSize = 0;
                if (field.FieldType.IsClass)
                {
                    // pointer size
                    size += LlvmWriter.PointerSize;
                }
                else if (field.FieldType.Namespace == "System" && SystemTypeSizes.TryGetValue(field.FieldType.Name, out fieldSize))
                {
                    size += fieldSize;
                }
                else
                {
                    size += field.FieldType.GetTypeSize();
                }
            }

            sizeByType[type.FullName] = size;

            return size;
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <returns>
        /// </returns>
        public static int GetTypeSize(this IType type)
        {
            // find index
            int size;
            if (!sizeByType.TryGetValue(type.FullName, out size))
            {
                size = type.CalculateSize();
            }

            return size;
        }

        /// <summary>
        /// </summary>
        /// <param name="requiredType">
        /// </param>
        /// <param name="opCodePart">
        /// </param>
        /// <returns>
        /// </returns>
        // TODO: finish converting array data into array class
        public static bool IsClassCastRequired(this IType requiredType, OpCodePart opCodePart)
        {
            if (opCodePart.HasResult && requiredType.TypeNotEquals(opCodePart.Result.Type))
            {
                return requiredType.IsAssignableFrom(opCodePart.Result.Type) 
                    || opCodePart.Result.Type.IsArray && requiredType.FullName == "System.Array";
            }

            return false;
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="doNotConvert">
        /// </param>
        /// <returns>
        /// </returns>
        public static string TypeToCType(this IType type)
        {
            var effectiveType = type;

            if (type.IsArray)
            {
                effectiveType = type.GetElementType();
            }

            if (!type.UseAsClass)
            {
                if (effectiveType.Namespace == "System")
                {
                    string ctype;
                    if (SystemTypesToCTypes.TryGetValue(effectiveType.Name, out ctype))
                    {
                        return ctype;
                    }
                }

                if (type.IsEnum)
                {
                    switch (type.GetEnumUnderlyingType().FullName)
                    {
                        case "System.SByte":
                            return "i8";
                        case "System.Int16":
                            return "i16";
                        case "System.Int32":
                            return "i32";
                        case "System.Int64":
                            return "i64";
                    }
                }

                if (type.IsValueType && type.IsPrimitive)
                {
                    return type.Name.ToLowerInvariant();
                }
            }

            return string.Concat('"', type.FullName, '"');
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="writer">
        /// </param>
        /// <param name="asReference">
        /// </param>
        /// <param name="refChar">
        /// </param>
        public static void WriteTypeModifiers(this IType type, IndentedTextWriter writer, bool asReference)
        {
            var refChar = '*';
            var effectiveType = type;

            var level = 0;
            do
            {
                var isReference = !effectiveType.IsValueType;
                if ((isReference || (!isReference && asReference && level == 0) || effectiveType.IsPointer) && !effectiveType.IsGenericParameter && !effectiveType.IsArray && !effectiveType.IsByRef)
                {
                    writer.Write(refChar);
                }

                if (effectiveType.IsByRef || effectiveType.IsArray)
                {
                    writer.Write(refChar);
                }

                if (effectiveType.HasElementType)
                {
                    effectiveType = effectiveType.GetElementType();
                    level++;
                }
                else
                {
                    break;
                }
            }
            while (effectiveType != null);
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="writer">
        /// </param>
        /// <param name="doNotConvert">
        /// </param>
        public static void WriteTypeName(this IType type, LlvmIndentedTextWriter writer)
        {
            var typeBaseName = type.TypeToCType();

            // clean name
            if (typeBaseName.EndsWith("&"))
            {
                typeBaseName = typeBaseName.Substring(0, typeBaseName.Length - 1);
            }

            var index = typeBaseName.IndexOf('`');
            if (index >= 0)
            {
                var nameWithoutGeneric = typeBaseName.Substring(0, index);
                writer.Write(nameWithoutGeneric);
            }
            else
            {
                writer.Write(typeBaseName);
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="writer">
        /// </param>
        /// <param name="asReference">
        /// </param>
        /// <param name="refChar">
        /// </param>
        public static void WriteTypePrefix(
            this IType type, LlvmIndentedTextWriter writer, bool asReference = false)
        {
            type.WriteTypeWithoutModifiers(writer);
            type.WriteTypeModifiers(writer, asReference);
        }

        /// <summary>
        /// </summary>
        /// <param name="type">
        /// </param>
        /// <param name="writer">
        /// </param>
        /// <param name="doNotIncludeTypePrefixId">
        /// </param>
        public static void WriteTypeWithoutModifiers(this IType type, LlvmIndentedTextWriter writer)
        {
            var effectiveType = type;

            while (effectiveType.HasElementType)
            {
                effectiveType = effectiveType.GetElementType();
            }

            if (type.UseAsClass || !effectiveType.IsPrimitiveType() && !effectiveType.IsVoid() && !effectiveType.IsEnum)
            {
                writer.Write('%');
            }

            // write base name
            effectiveType.WriteTypeName(writer);
        }
    }
}