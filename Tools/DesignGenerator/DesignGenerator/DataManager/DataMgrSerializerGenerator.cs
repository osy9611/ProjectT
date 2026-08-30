using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DesignGenerator.Util;

namespace DesignGenerator.DataManager
{
    public class DataMgrSerializerGenerator
    {
        private readonly CodeCompileUnit unit;
        private readonly CodeNamespace nameSpace;
        private readonly List<KeyValuePair<string, string>> infoFields = new List<KeyValuePair<string, string>>();

        public DataMgrSerializerGenerator()
        {
            unit = new CodeCompileUnit();
            nameSpace = new CodeNamespace("DesignTable");

            nameSpace.Imports.Add(new CodeNamespaceImport("DesignTable"));
            nameSpace.Imports.Add(new CodeNamespaceImport("ProtoBuf"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Collections"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.IO"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Linq"));
            unit.Namespaces.Add(nameSpace);
        }

        public void SetData(string tableId, string tableName)
        {
            infoFields.Add(new KeyValuePair<string, string>(tableId, tableName));
        }

        public bool Save(string outputPath)
        {
            return GeneratorUtils.ExportGenerator(unit, Path.Combine(outputPath, "DataMessageSerializer.cs"));
        }

        public void Create()
        {
            infoFields.Sort((a, b) => ParseId(a.Key).CompareTo(ParseId(b.Key)));
            CreateDataMgrSerializer();
        }

        private static int ParseId(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private void CreateDataMgrSerializer()
        {
            var cls = new CodeTypeDeclaration("DataMessageSerializer")
            {
                IsClass = true,
                TypeAttributes = TypeAttributes.Public | TypeAttributes.Sealed,
            };

            var serializeFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Serialize",
                ReturnType = new CodeTypeReference(typeof(byte[])),
            };
            serializeFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Object", "tableInfos"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "using (System.IO.MemoryStream stream = new System.IO.MemoryStream())"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "Serializer.Serialize(stream, tableInfos);"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return stream.ToArray();"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
            cls.Members.Add(serializeFunc);

            var deserializeFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Deserialize",
                ReturnType = new CodeTypeReference(typeof(object)),
            };
            deserializeFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Int32", "tableId"));
            deserializeFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Byte[]", "buffer"));

            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "using (System.IO.MemoryStream stream = new System.IO.MemoryStream(buffer))"));
            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "switch (tableId)"));
            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "{"));

            foreach (var f in infoFields)
            {
                deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + $"case {f.Key}:"));
                deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB6 + $"return ProtoBuf.Serializer.Deserialize<{f.Value}Infos>(stream);"));
            }

            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + "default:"));
            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB6 + "throw new System.ArgumentOutOfRangeException(\"tableId\", \"알 수 없는 TableId: \" + tableId);"));
            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "}"));
            deserializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));

            cls.Members.Add(deserializeFunc);
            nameSpace.Types.Add(cls);
        }
    }
}
