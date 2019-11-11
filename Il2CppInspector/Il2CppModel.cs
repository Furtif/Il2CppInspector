﻿/*
    Copyright 2017-2019 Katy Coe - http://www.hearthcode.org - http://www.djkaty.com

    All rights reserved.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Il2CppInspector.Reflection
{
    public class Il2CppModel
    {
        public Il2CppInspector Package { get; }
        public List<Assembly> Assemblies { get; } = new List<Assembly>();

        // List of all types ordered by their TypeDefinitionIndex
        public TypeInfo[] TypesByDefinitionIndex { get; }
        
        // List of all type usages ordered by their type usage index
        public TypeInfo[] TypesByUsageIndex { get; }

        // List of type usages that are initialized via pointers in the image
        public Dictionary<ulong, TypeInfo> TypesByVirtualAddress { get; } = new Dictionary<ulong, TypeInfo>();

        // Every type
        public IEnumerable<TypeInfo> Types => new IEnumerable<TypeInfo>[] { TypesByDefinitionIndex, TypesByUsageIndex, TypesByVirtualAddress.Values }.SelectMany(t => t);

        // List of all methods ordered by their MethodDefinitionIndex
        public MethodBase[] MethodsByDefinitionIndex { get; }

        public Il2CppModel(Il2CppInspector package) {
            Package = package;
            TypesByDefinitionIndex = new TypeInfo[package.TypeDefinitions.Length];
            TypesByUsageIndex = new TypeInfo[package.TypeUsages.Count];
            MethodsByDefinitionIndex = new MethodBase[package.Methods.Length];

            // Create Assembly objects from Il2Cpp package
            for (var image = 0; image < package.Images.Length; image++)
                Assemblies.Add(new Assembly(this, image));
        }

        // Get an assembly by its name
        public Assembly GetAssembly(string name) => Assemblies.FirstOrDefault(a => a.FullName == name);

        // Get a type by its full name
        public TypeInfo GetType(string name) => Types.FirstOrDefault(t => t.FullName == name);

        private TypeInfo getNewTypeUsage(Il2CppType usage, MemberTypes memberType) {
            switch (usage.type) {
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    // Classes defined in the metadata
                    return TypesByDefinitionIndex[usage.datapoint]; // klassIndex

                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    // Everything that requires special handling
                    return new TypeInfo(this, usage, memberType);

                default:
                    // Primitive types
                    return GetTypeFromTypeEnum(usage.type);
            }
        }

        // Get or generate a type from its IL2CPP binary type usage reference
        // (field, return type, generic type parameter etc.)
        public TypeInfo GetTypeFromUsage(int typeUsageIndex, MemberTypes memberType = MemberTypes.All) {

            // Already generated type previously?
            if (TypesByUsageIndex[typeUsageIndex] != null)
                return TypesByUsageIndex[typeUsageIndex];

            var usage = Package.TypeUsages[typeUsageIndex];
            var newUsage = getNewTypeUsage(usage, memberType);

            TypesByUsageIndex[typeUsageIndex] = newUsage;
            return newUsage;
        }

        // Basic primitive types
        public TypeInfo GetTypeFromTypeEnum(Il2CppTypeEnum t) {
            if ((int)t >= Il2CppConstants.FullNameTypeString.Count)
                return null;

            var fqn = Il2CppConstants.FullNameTypeString[(int) t];
            return TypesByDefinitionIndex.First(x => x.FullName == fqn);
        }

        // Type from a virtual address pointer
        // These are always nested types frorm usages within another type
        public TypeInfo GetTypeFromVirtualAddress(ulong ptr) {
            if (TypesByVirtualAddress.ContainsKey(ptr))
                return TypesByVirtualAddress[ptr];

            var type = Package.BinaryImage.ReadMappedObject<Il2CppType>(ptr);
            var newUsage = getNewTypeUsage(type, MemberTypes.NestedType);

            TypesByVirtualAddress.Add(ptr, newUsage);
            return newUsage;
        }

        // The attribute index is an index into AttributeTypeRanges, each of which is a start-end range index into AttributeTypeIndices, each of which is a TypeIndex
        public int GetCustomAttributeIndex(Assembly asm, uint token, int customAttributeIndex) {
            // Prior to v24.1, Type, Field, Parameter, Method, Event, Property, Assembly definitions had their own customAttributeIndex field
            if (Package.Version <= 24.0)
                return customAttributeIndex;

            var image = asm.ImageDefinition;
            var imageRange = image.customAttributeStart..(int)(image.customAttributeStart + image.customAttributeCount);

            // From v24.1 onwards, token was added to Il2CppCustomAttributeTypeRange and each Il2CppImageDefinition noted the CustomAttributeTypeRanges for the image
            var index = Array.FindIndex(Package.AttributeTypeRanges[imageRange], x => x.token == token);
            return index == -1 ? -1 : index + image.customAttributeStart;
        }
    }
}