using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DesignGenerator
{
    public class DataMgrSerializerGenerator
    {
        private CodeCompileUnit unit;
        private CodeNamespace nameSpace;

        //Key : TableId , Value : TableName
        private List<KeyValuePair<string, string>> infoFields;

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

            infoFields = new List<KeyValuePair<string, string>>();
        }

        //Test Func
        public void Show()
        {
            CodeDomProvider provider = CodeDomProvider.CreateProvider("CSharp");
            CodeGeneratorOptions options = new CodeGeneratorOptions();
            options.BracingStyle = "C";

            StringBuilder builder = new StringBuilder();
            using (StringWriter sourceWriter = new StringWriter(builder))
            {
                provider.GenerateCodeFromCompileUnit(unit, sourceWriter, options);
            }

            Console.WriteLine(builder.ToString());
        }

        public void SetData(string tableId, string tableName)
        {
            infoFields.Add(new KeyValuePair<string, string>(tableId, tableName));
        }

        public void Save(string outputPath)
        {
            GeneratorUtils.ExportGenerator(unit, outputPath + $"\\DataMessageSerializer.cs");
        }

        public void Create()
        {
            CreateDataMgrSerializer();
        }

        private void CreateDataMgrSerializer()
        {
            CodeTypeDeclaration dataMgrSerializer = new CodeTypeDeclaration("DataMessageSerializer");
            dataMgrSerializer.IsClass = true;
            dataMgrSerializer.TypeAttributes = System.Reflection.TypeAttributes.Public| System.Reflection.TypeAttributes.Sealed;

            //Serialize Func
            CodeMemberMethod serializeFunc = new CodeMemberMethod();
            serializeFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            serializeFunc.Name = "Serialize";
            serializeFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Object", "tableInfos"));
            serializeFunc.ReturnType = new CodeTypeReference(typeof(byte[]));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "System.IO.MemoryStream stream = new System.IO.MemoryStream();"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "Serializer.Serialize(stream, tableInfos);"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "byte[] buffer = stream.ToArray();\n"));
            serializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "return buffer;"));
            dataMgrSerializer.Members.Add(serializeFunc);

            CodeMemberMethod deSerializeFunc = new CodeMemberMethod();
            deSerializeFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            deSerializeFunc.Name = "Deserialize";
            deSerializeFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Int32", "tableId"));
            deSerializeFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Byte[]", "buffer"));
            deSerializeFunc.ReturnType = new CodeTypeReference(typeof(object));

            deSerializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "System.IO.MemoryStream stream = new System.IO.MemoryStream(buffer);"));
            deSerializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "switch (tableId)"));
            deSerializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "{"));

            foreach(var infoField in infoFields)
            {
                deSerializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"case {infoField.Key}:"));
                deSerializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"return ProtoBuf.Serializer.Deserialize<{infoField.Value}Infos>(stream);"));
            }

            deSerializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "}"));

            deSerializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "return null;"));
            dataMgrSerializer.Members.Add(deSerializeFunc);


            nameSpace.Types.Add(dataMgrSerializer);
        }
    }
}
