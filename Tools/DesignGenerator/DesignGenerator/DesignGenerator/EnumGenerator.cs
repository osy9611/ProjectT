using System.CodeDom;
using System.Collections.Generic;
using System.Xml;

namespace DesignGenerator
{
    public class EnumGenerator
    {
        public Dictionary<int, EnumInfo> enumInfos;

        public EnumGenerator()
        {
            enumInfos = new Dictionary<int, EnumInfo>();
        }

        public void Create(string address, string outputPath)
        {
            address = address.Trim();

            ExcelUtils.ExportXml(address + "\\Enum\\enum.xlsx", address + "\\Enum\\enum.xml");

            XmlManager.LoadXML(address + "\\Enum\\enum.xml", outputPath + "\\Automation\\Enum", (xml, path) =>
            {
                enumInfos = SetEnumInfo(xml);
                Save(enumInfos, path);
            });
        }

        public Dictionary<int, EnumInfo> SetEnumInfo(XmlDocument xml)
        {
            XmlNodeList nodes = xml.SelectNodes("Enum/record");
            Dictionary<int, EnumInfo> result = new Dictionary<int, EnumInfo>();

            foreach (XmlNode node in nodes)
            {
                if (node.HasChildNodes == false) continue;
                int groupID = int.Parse(node.SelectSingleNode("GROUP_ID").InnerText);
                if (node.SelectSingleNode("ENUM_VALUE").InnerText == "-")
                {
                    EnumInfo info = new EnumInfo(node.SelectSingleNode("ENUM_NAME").InnerText,
                                        node.SelectSingleNode("COMMENT").InnerText,
                                        groupID);

                    result.Add(groupID, info);
                }
                else
                {
                    groupID = int.Parse(node.SelectSingleNode("GROUP_ID").InnerText);

                    if (result.ContainsKey(groupID))
                    {
                        result[groupID].AddInfo(node.SelectSingleNode("ENUM_NAME").InnerText,
                                                node.SelectSingleNode("ENUM_VALUE").InnerText,
                                                node.SelectSingleNode("COMMENT").InnerText);
                    }
                }
            }
            return result;
        }

        public void Save(Dictionary<int, EnumInfo> datas, string outputPath)
        {
            CodeCompileUnit unit = new CodeCompileUnit();

            //Add Namespace
            CodeNamespace nameSpace = new CodeNamespace("DesignEnum");
            unit.Namespaces.Add(nameSpace);

            //Add Enum
            foreach (var data in datas.Values)
            {
                CodeTypeDeclaration enumData = new CodeTypeDeclaration($"{data.EnumType}");
                enumData.IsEnum = true;
                enumData.TypeAttributes = System.Reflection.TypeAttributes.Public;
                enumData.Comments.Add(new CodeCommentStatement($"ID : {data.GroupID}"));

                foreach (var value in data.Values)
                {
                    enumData.Members.Add(new CodeMemberField(data.EnumType, $"{value.EnumName} = {value.EnumValue}"));
                }
                nameSpace.Types.Add(enumData);
            }

            //test Code
            //CodeDomProvider provider = CodeDomProvider.CreateProvider("CSharp");
            //CodeGeneratorOptions options = new CodeGeneratorOptions();
            //options.BracingStyle = "C";

            //StringBuilder builder = new StringBuilder();
            //using (StringWriter sourceWriter = new StringWriter(builder))
            //{
            //    provider.GenerateCodeFromCompileUnit(unit, sourceWriter, options);
            //}

            //Console.WriteLine(builder.ToString());

            GeneratorUtils.ExportGenerator(unit, outputPath + "\\DesignEnum.cs");
        }
    }
}
