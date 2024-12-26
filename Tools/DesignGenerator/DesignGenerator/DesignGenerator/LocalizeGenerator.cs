using ProtoBuf;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace DesignGenerator
{
    internal class LocalizeGenerator
    {
        private CodeCompileUnit unit = new CodeCompileUnit();
        private Dictionary<string, LocalDataInfo> infos = new Dictionary<string, LocalDataInfo>();

        public void Create(string address, string outputPath)
        {
            ExcelUtils.ExportAllXml($"{address}\\Local", $"{address}\\Local");

            List<XmlDocument> xmlDocs = XmlManager.LoadAllXML($"{address}\\Local");
            SetInfos(xmlDocs);

            GenerateInfoData(address);
        }

        private void SetInfos(List<XmlDocument> xmlDocs)
        {
            int enumId = 0;
            foreach (var xmlDoc in xmlDocs)
            {
                XmlNodeList nodes = xmlDoc.SelectNodes("Local/record");

                foreach (XmlNode node in nodes)
                {
                    LocalDataInfo info = new LocalDataInfo();
                    info.EnumID = enumId;
                    info.IsUse = node.SelectSingleNode("USE").InnerText == "Y" ? true : false;
                    info.LocalName = node.SelectSingleNode("LOCALNAME").InnerText;
                    info.Ko = node.SelectSingleNode("KO").InnerText;
                    info.Jp = node.SelectSingleNode("JP").InnerText;
                    info.En = node.SelectSingleNode("EN").InnerText;

                    infos.Add(info.LocalName, info);
                    enumId++;
                }
            }
        }

        private void GenerateInfoData(string folderPath)
        {
            CodeNamespace nameSpace = new CodeNamespace("DesignLocal");
            nameSpace.Imports.Add(new CodeNamespaceImport("System"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Collections"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            nameSpace.Imports.Add(new CodeNamespaceImport("ProtoBuf"));
            unit.Namespaces.Add(nameSpace);

            //Add Enum
            CodeTypeDeclaration enumData = new CodeTypeDeclaration($"StringDef");
            enumData.IsEnum = true;
            enumData.TypeAttributes = System.Reflection.TypeAttributes.Public;

            foreach (var info in infos)
            {
                if (!info.Value.IsUse)
                    continue;

                enumData.Members.Add(new CodeMemberField() { Name = info.Value.LocalName, InitExpression = new CodePrimitiveExpression(info.Value.EnumID) });
            }
            nameSpace.Types.Add(enumData);


            //Add Class
            CodeTypeDeclaration classData = new CodeTypeDeclaration($"LocalData");
            classData.IsClass = true;
            classData.TypeAttributes = System.Reflection.TypeAttributes.Public;
            classData.CustomAttributes.Add(new CodeAttributeDeclaration("ProtoContract"));

            var dicVar = new CodeMemberField("Dictionary<StringDef,string>", "localString = new Dictionary<StringDef,string>()");
            dicVar.Attributes = MemberAttributes.Public;
            dicVar.CustomAttributes.Add(new CodeAttributeDeclaration(
                                   "ProtoMember",
                                   new CodeAttributeArgument(new CodePrimitiveExpression(1))));
            classData.Members.Add(dicVar);

            //Add Load Func
            CodeMemberMethod loadFunc = new CodeMemberMethod();
            loadFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            loadFunc.Name = "LoadData";
            loadFunc.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference(typeof(byte[])), "buffer"));
            loadFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "System.IO.MemoryStream stream = new System.IO.MemoryStream(buffer);"));
            loadFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "localString = ProtoBuf.Serializer.Deserialize<LocalData> (stream).localString;"));
            classData.Members.Add(loadFunc);

            //Add insert Func
            CodeMemberMethod insertFunc = new CodeMemberMethod();
            insertFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            insertFunc.Name = "Insert";
            insertFunc.ReturnType = new CodeTypeReference(typeof(bool));
            insertFunc.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference(typeof(int)), "type"));
            insertFunc.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference(typeof(string)), "lang"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "if(!localString.ContainsKey((StringDef)type))"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "localString.Add((StringDef)type,lang);"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return true;"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return false;"));
            classData.Members.Add(insertFunc);

            nameSpace.Types.Add(classData);

            CodeDomProvider provider = CodeDomProvider.CreateProvider("CSharp");
            CodeGeneratorOptions options = new CodeGeneratorOptions();
            options.BracingStyle = "C";

            StringBuilder builder = new StringBuilder();
            using (StringWriter sourceWriter = new StringWriter(builder))
            {
                provider.GenerateCodeFromCompileUnit(unit, sourceWriter, options);
            }

            DllExporter.ExportStringToDll(builder.ToString(), $"{folderPath}\\Dll\\LocalData.dll");
        }

        public void ExportDataByteFile(string folderPath)
        {
            string typeName = "DesignLocal.LocalData";
            System.Type sType = System.Type.GetType(typeName);
            System.Reflection.Assembly dll = System.Reflection.Assembly.LoadFile($"{folderPath}\\Dll\\Design.dll");
            sType = dll.GetType(typeName);

            foreach (LangaugeType type in System.Enum.GetValues(typeof(LangaugeType)))
            {
                System.Reflection.MethodInfo addMethod = sType.GetMethod("Insert");
                object inst = System.Activator.CreateInstance(sType);

                foreach (var info in infos.Values)
                {
                    object[] vals = new object[2];
                    vals[0] = info.EnumID;
                    if (type == LangaugeType.Ko)
                    {
                        vals[1] = info.Ko;
                    }
                    if (type == LangaugeType.Jp)
                    {
                        vals[1] = info.Jp;
                    }
                    if (type == LangaugeType.En)
                    {
                        vals[1] = info.En;
                    }
                    addMethod?.Invoke(inst, vals);
                }

                File.WriteAllBytes($"{folderPath}\\Local\\{type}.bytes", Serialize(inst));
            }
        }

        public byte[] Serialize(object localInfos)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize((Stream)ms, localInfos);

                return ms.ToArray();
            }
        }

    }



    public enum StringDef
    {
        Test = 0
    }

    [ProtoContract]
    public class LocalTest
    {
        [ProtoMember(1)]
        public Dictionary<StringDef, string> localString = new Dictionary<StringDef, string>();

        public void Insert(int type, string lang)
        {
            if (localString.ContainsKey((StringDef)type))
            {
                localString.Add((StringDef)type, lang);
            }
        }

        public void LoadData(byte[] buffer)
        {
            System.IO.MemoryStream stream = new System.IO.MemoryStream(buffer);
            localString = ProtoBuf.Serializer.Deserialize<LocalTest>(stream).localString;
        }

        public byte[] Serialize()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize((Stream)ms, localString);

                return ms.ToArray();
            }
        }
    }
}
