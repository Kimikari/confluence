using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Confluence.Tools.Ledger
{
    /// <summary>A test method carrying [Verifies], directly or through its class.</summary>
    public sealed record VerifyingTest(string Assembly, string ClassName, string Method, IReadOnlyList<string> Ids)
    {
        public string Key => ClassName + "." + Method;
    }

    /// <summary>
    /// Reads [Verifies] attributes from test assemblies through System.Reflection.Metadata, so test assemblies are
    /// inspected without loading them or resolving their dependencies.
    /// </summary>
    public static class TestScanner
    {
        private const string VerifiesNamespace = "Confluence.Core.Content";
        private const string VerifiesName = "VerifiesAttribute";

        /// <summary>Expands the patterns and keeps the newest file per assembly name (Debug vs Release builds).</summary>
        public static List<string> ResolveAssemblies(string root, IEnumerable<string> patterns)
        {
            return patterns
                .SelectMany(p => PathGlob.Expand(root, p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.Ordinal).First())
                .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                .ToList();
        }

        public static List<VerifyingTest> Scan(string assemblyPath)
        {
            var tests = new List<VerifyingTest>();
            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return tests;
            var md = pe.GetMetadataReader();
            string assemblyName = md.IsAssembly ? md.GetString(md.GetAssemblyDefinition().Name) : Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var typeHandle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(typeHandle);
                var classIds = VerifiesIds(md, type.GetCustomAttributes());
                string className = FullName(md, type);
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = md.GetMethodDefinition(methodHandle);
                    var attributes = method.GetCustomAttributes();
                    var ids = VerifiesIds(md, attributes);
                    if (classIds.Count > 0 && IsTestMethod(md, attributes)) ids = classIds.Concat(ids).ToList();
                    if (ids.Count == 0) continue;
                    var distinct = ids.Distinct(StringComparer.Ordinal).ToList();
                    tests.Add(new VerifyingTest(assemblyName, className, md.GetString(method.Name), distinct));
                }
            }
            return tests;
        }

        private static List<string> VerifiesIds(MetadataReader md, CustomAttributeHandleCollection attributes)
        {
            var ids = new List<string>();
            foreach (var handle in attributes)
            {
                var attribute = md.GetCustomAttribute(handle);
                if (!AttributeTypeIs(md, attribute, VerifiesNamespace, VerifiesName)) continue;
                var value = attribute.DecodeValue(new StringTypeProvider());
                foreach (var argument in value.FixedArguments)
                {
                    if (argument.Value is ImmutableArray<CustomAttributeTypedArgument<string>> items)
                    {
                        foreach (var item in items)
                            if (item.Value is string id && id.Length > 0) ids.Add(id);
                    }
                    else if (argument.Value is string single && single.Length > 0) ids.Add(single);
                }
            }
            return ids;
        }

        private static bool IsTestMethod(MetadataReader md, CustomAttributeHandleCollection attributes)
        {
            foreach (var handle in attributes)
            {
                var attribute = md.GetCustomAttribute(handle);
                string name = AttributeTypeName(md, attribute);
                if (name == "FactAttribute" || name == "TheoryAttribute") return true;
            }
            return false;
        }

        private static bool AttributeTypeIs(MetadataReader md, CustomAttribute attribute, string ns, string name)
        {
            return AttributeTypeName(md, attribute, out string actualNs) == name && actualNs == ns;
        }

        private static string AttributeTypeName(MetadataReader md, CustomAttribute attribute) => AttributeTypeName(md, attribute, out _);

        private static string AttributeTypeName(MetadataReader md, CustomAttribute attribute, out string ns)
        {
            ns = null;
            EntityHandle parent;
            switch (attribute.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                    parent = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                    break;
                case HandleKind.MethodDefinition:
                    parent = md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                    break;
                default:
                    return null;
            }
            switch (parent.Kind)
            {
                case HandleKind.TypeReference:
                {
                    var reference = md.GetTypeReference((TypeReferenceHandle)parent);
                    ns = md.GetString(reference.Namespace);
                    return md.GetString(reference.Name);
                }
                case HandleKind.TypeDefinition:
                {
                    var definition = md.GetTypeDefinition((TypeDefinitionHandle)parent);
                    ns = md.GetString(definition.Namespace);
                    return md.GetString(definition.Name);
                }
                default:
                    return null;
            }
        }

        /// <summary>Reflection-style full name ("Ns.Outer+Inner") as reported in TRX class names.</summary>
        private static string FullName(MetadataReader md, TypeDefinition type)
        {
            string name = md.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil) return FullName(md, md.GetTypeDefinition(declaring)) + "+" + name;
            string ns = md.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        /// <summary>Decodes attribute argument types as plain names; only string and string[] arguments are needed.</summary>
        private sealed class StringTypeProvider : ICustomAttributeTypeProvider<string>
        {
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
            public string GetSystemType() => "System.Type";
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);
            public string GetTypeFromSerializedName(string name) => name;
            public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
            public bool IsSystemType(string type) => type == "System.Type";
        }
    }
}
