using DocumentFormat.OpenXml.Office2021.DocumentTasks;
using Newtonsoft.Json.Linq;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace DesignGenerator
{
    internal class TableGenerator
    {
        private Dictionary<int, TableDataInfo> tableDataInfos;
        private Dictionary<int, EnumInfo> enumInfos;

        public Action<string, string, List<string>> OnMgrSet = null;
        public Action<string, string> OnSerializerSet = null;

        public TableGenerator()
        {
            tableDataInfos = new Dictionary<int, TableDataInfo>();
        }

        public void Load(string address, string tableName, string readAllDataPath, string outputScriptPath)
        {
            XmlManager.LoadXML(address + "\\Enum\\enum.xml", null, (enumXml, path) =>
            {
                EnumGenerator enumGenerator = new EnumGenerator();
                enumInfos = enumGenerator.SetEnumInfo(enumXml);
            });

            ExcelUtils.ExportXml($"{address}\\Table\\Tables\\{tableName}.xlsx", $"{address}\\Table\\Xml\\{tableName}.xml");

            XmlDocument xml = XmlManager.LoadXML(address + $"\\Table\\Xml\\{tableName}.xml");
            List<XmlDocument> xmlDatas = XmlManager.LoadAllXML(readAllDataPath);
            foreach (XmlDocument data in xmlDatas)
            {
                GetTableInfo(data);
            }

            XmlNodeList nodes = xml.SelectNodes(xml.DocumentElement.Name + "/verify");
            GetTableInfo(xml);

            GenerateInfoData(tableDataInfos[int.Parse(nodes[0].Attributes["TableId"].Value)], outputScriptPath);
        }

        public async void LoadAll(string address, string readAllDataPath, string outputScriptPath)
        {
            XmlManager.LoadXML(address + "\\Enum\\enum.xml", null, (enumXml, path) =>
            {
                EnumGenerator enumGenerator = new EnumGenerator();
                enumInfos = enumGenerator.SetEnumInfo(enumXml);
            });

            ExcelUtils.ExportAllXml($"{address}\\Table\\Tables", $"{address}\\Table\\Xml").Wait();

            List<XmlDocument> xmlDatas = XmlManager.LoadAllXML(readAllDataPath);
            foreach (XmlDocument data in xmlDatas)
            {
                GetTableInfo(data);
            }

            foreach (var tableDataInfo in tableDataInfos)
            {
                GenerateInfoData(tableDataInfo.Value, outputScriptPath);
                OnSerializerSet?.Invoke(tableDataInfo.Value.TableID.ToString(), tableDataInfo.Value.TableName);

                if (tableDataInfo.Value.IsRefTable())
                {
                    var refTables = tableDataInfo.Value.VarData.Where(x => !string.IsNullOrEmpty(x.RefTable)).Select(x => tableDataInfos[Convert.ToInt32(x.RefTable)].TableName).ToList();
                    OnMgrSet?.Invoke(tableDataInfo.Value.TableID.ToString(), tableDataInfo.Value.TableName, refTables);
                }
                else
                {
                    OnMgrSet?.Invoke(tableDataInfo.Value.TableID.ToString(), tableDataInfo.Value.TableName, null);
                }
            }
        }

        private void GetTableInfo(XmlDocument xml)
        {
            if (xml == null)
                return;

            XmlNodeList recordNodes = xml.SelectNodes(xml.DocumentElement.Name + "/record");
            XmlNodeList verifyNodes = xml.SelectNodes(xml.DocumentElement.Name + "/verify");


            TableDataInfo info = new TableDataInfo();
            info.TableName = xml.DocumentElement.Name;
            info.TableID = int.Parse(verifyNodes[0].Attributes["TableId"].Value);

            JObject jsonData = null;

            foreach (XmlNode node in verifyNodes)
            {
                //if (node.HasChildNodes == false) continue;
                int enumID = node.Attributes["EnumID"].Value == "-" ? 0 : int.Parse(node.Attributes["EnumID"].Value);
                if (node.Attributes["PK"].Value == "Y")
                {
                    info.AddPKData(node.Attributes["ColumnName"].Value, DefineType.ConvertTypeName(node.Attributes["Type"].Value), enumID);
                }

                if (node.Attributes["StringHashIds"].Value != "-")
                {
                    info.AddVarData(
                        node.Attributes["ColumnName"].Value,
                        DefineType.ConvertTypeName(node.Attributes["Type"].Value), enumID, node.Attributes["StringHashIds"].Value);
                }
                else
                {
                    info.AddVarData(node.Attributes["ColumnName"].Value
                        , DefineType.ConvertTypeName(node.Attributes["Type"].Value), enumID);
                }

                if (node.Attributes["IDRule"].Value != "-")
                {
                    jsonData = JObject.Parse(node.Attributes["IDRule"].Value.Replace("&quot;", @""""));
                    if (bool.Parse(jsonData["UseListRule"].ToString()))
                    {
                        info.UseListRule = true;
                    }
                }
            }

            if (jsonData != null)
            {
                JArray array = JArray.Parse(jsonData["PKIds"].ToString());
                for (int i = 0, range = array.Count; i < range; ++i)
                {
                    info.AddIsListRule(array[i].ToString());
                }

                array = JArray.Parse(jsonData["FindPKId"].ToString());
                for (int i = 0, range = array.Count; i < range; ++i)
                {
                    info.AddFindPKListRule(array[i].ToString());
                }
            }

            foreach (XmlNode node in recordNodes)
            {
                if (node.InnerText == "") continue;

                object[] val = new object[info.VarData.Count];
                for (int i = 0; i < info.VarData.Count; ++i)
                {
                    string columName = info.VarData[i].ColumnName;
                    string type = info.VarData[i].Type;

                    if (info.VarData[i].EnumId == 0)
                        val[i] = DefineType.ConvertDataType(type, node[columName].InnerText);
                    else
                    {
                        //Enum 데이터에 해당 값이 있는지를 확인한다.
                        if (enumInfos.TryGetValue(info.VarData[i].EnumId, out var enumInfo))
                        {
                            var enumValue = enumInfo.Values.Find(x => x.EnumName == node[columName].InnerText);
                            if (enumValue != null)
                            {
                                val[i] = DefineType.ConvertDataType(type, enumValue.EnumValue.ToString());
                                continue;
                            }
                            else
                            {
                                val[i] = DefineType.ConvertDataType(type, node[columName].InnerText);
                            }
                        }
                    }
                }
                info.VarObjectData.Add(val);
            }

            if (!tableDataInfos.ContainsKey((int.Parse(verifyNodes[0].Attributes["TableId"].Value))))
            {
                tableDataInfos.Add(int.Parse(verifyNodes[0].Attributes["TableId"].Value), info);
            }
        }

        private void GenerateInfoData(TableDataInfo info, string outputPath)
        {
            if (info == null)
                return;

            CodeCompileUnit unit = new CodeCompileUnit();

            #region InfoClass
            //Add NameSpace 
            CodeNamespace nameSpace = new CodeNamespace("DesignTable");
            nameSpace.Imports.Add(new CodeNamespaceImport("System"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Collections"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            nameSpace.Imports.Add(new CodeNamespaceImport("ProtoBuf"));
            unit.Namespaces.Add(nameSpace);

            //Add Info Class
            CodeTypeDeclaration infoClass = new CodeTypeDeclaration($"{info.TableName}Info");
            infoClass.IsClass = true;
            infoClass.TypeAttributes = System.Reflection.TypeAttributes.Public;
            infoClass.CustomAttributes.Add(new CodeAttributeDeclaration("ProtoContract"));

            int protoNum = 1;
            //Add Var
            foreach (TableVarData data in info.VarData)
            {
                var pro = new CodeMemberField(DefineType.ConverSystemTypeName(data.Type), $"{data.ColumnName}");
                pro.Attributes = MemberAttributes.Public;
                pro.CustomAttributes.Add(new CodeAttributeDeclaration(
                                   "ProtoMember",
                                   new CodeAttributeArgument(new CodePrimitiveExpression(protoNum))));

                infoClass.Members.Add(pro);

                if (data.RefTable != "")
                {
                    var proRef = new CodeMemberField($"{tableDataInfos[int.Parse(data.RefTable)].TableName}Info", $"{data.ColumnName}_ref");
                    proRef.Attributes = MemberAttributes.Public;
                    infoClass.Members.Add(proRef);
                }

                protoNum++;
            }

            //Add Constructor Func
            CodeConstructor constructorFunc = new CodeConstructor();
            constructorFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            constructorFunc.Name = $"{info.TableName}Info";
            infoClass.Members.Add(constructorFunc);

            //Add Constructor Func Params
            CodeConstructor constructorFunc_Params = new CodeConstructor();
            constructorFunc_Params.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            constructorFunc_Params.Name = $"{info.TableName}Info";
            foreach (TableVarData data in info.VarData)
            {
                constructorFunc_Params.Parameters.Add(new CodeParameterDeclarationExpression(DefineType.ConverSystemTypeName(data.Type), $"{data.ColumnName}"));
                constructorFunc_Params.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"this.{data.ColumnName} = {data.ColumnName};"));
            }
            infoClass.Members.Add(constructorFunc_Params);
            nameSpace.Types.Add(infoClass);

            #endregion

            #region InfosClass
            //Add Infos Class
            CodeTypeDeclaration infosClass = new CodeTypeDeclaration($"{info.TableName}Infos");
            infosClass.IsClass = true;
            infosClass.TypeAttributes = System.Reflection.TypeAttributes.Public;
            infosClass.CustomAttributes.Add(new CodeAttributeDeclaration("ProtoContract"));

            //Add Var
            var dataInfoVar = new CodeMemberField($"List<{info.TableName}Info>", $"dataInfo");
            dataInfoVar.Attributes = MemberAttributes.Public;
            dataInfoVar.CustomAttributes.Add(new CodeAttributeDeclaration(
                               "ProtoMember",
                               new CodeAttributeArgument(new CodePrimitiveExpression(1))));
            dataInfoVar.InitExpression = new CodeSnippetExpression($"new List<{info.TableName}Info>()");
            infosClass.Members.Add(dataInfoVar);

            var datasVar = new CodeMemberField($"Dictionary<ArraySegment<byte>, {info.TableName}Info>", $"datas");
            datasVar.Attributes = MemberAttributes.Public;
            datasVar.InitExpression = new CodeSnippetExpression($"new Dictionary<ArraySegment<byte>, {info.TableName}Info>(new DataComparer())");
            infosClass.Members.Add(datasVar);

            if (info.UseListRule)
            {
                var listDataVar = new CodeMemberField($"Dictionary<ArraySegment<byte>, List<{info.TableName}Info>>", "listData");
                listDataVar.Attributes = MemberAttributes.Public;
                listDataVar.InitExpression = new CodeSnippetExpression($"new Dictionary<ArraySegment<byte>, List<{info.TableName}Info>>(new DataComparer())");
                infosClass.Members.Add(listDataVar);
            }

            //Add Insert Func
            CodeMemberMethod insertFunc = new CodeMemberMethod();
            insertFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            insertFunc.Name = $"Insert";
            insertFunc.ReturnType = new CodeTypeReference(typeof(bool));
            foreach (TableVarData data in info.VarData)
            {
                insertFunc.Parameters.Add(new CodeParameterDeclarationExpression(DefineType.ConverSystemTypeName(data.Type), $"{data.ColumnName}"));
                //constructorFunc_Params.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"this.{data.ColumName} = {data.ColumName};"));
            }

            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"foreach ({info.TableName}Info info in dataInfo)"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "{"));

            string insertPkParams = info.GetPkString("info.{0} == {0}", "&&");
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"if({insertPkParams})"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"return false;"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "}"));

            string newInfoParams = info.GetStringVerDatas("{0}", ",");
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"dataInfo.Add(new {info.TableName}Info({newInfoParams}));"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"return true;"));
            infosClass.Members.Add(insertFunc);

            //Initialize Func
            CodeMemberMethod initializeFunc = new CodeMemberMethod();
            initializeFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            initializeFunc.Name = $"Initialize";

            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "foreach (var data in dataInfo)"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "{"));

            string initializePkParams = info.GetPkString("data.{0}", ",");
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"ArraySegment<byte> bytes = GetIdRule({initializePkParams});"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"if (datas.ContainsKey(bytes))"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"continue;"));

            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"datas.Add(bytes, data);"));

            if (info.UseListRule)
            {
                if (!info.IsListIdRule())
                {
                    var listIdRuleString = info.GetListIdRuleString("data.{0}", ",");
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"bytes = GetListIdRule({listIdRuleString});"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"if (listData.ContainsKey(bytes))"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "listData[bytes].Add(data);"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "else"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"listData.Add(bytes, new List<{info.TableName}Info>());"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"listData[bytes].Add(data);"));
                    initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
                }
            }

            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "}"));
            infosClass.Members.Add(initializeFunc);

            //Add Get Func
            CodeMemberMethod getFunc = new CodeMemberMethod();
            getFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            getFunc.Name = "Get";
            getFunc.ReturnType = new CodeTypeReference($"{info.TableName}Info");
            foreach (TablePKData data in info.PKData)
            {
                getFunc.Parameters.Add(new CodeParameterDeclarationExpression(DefineType.ConverSystemTypeName(data.Type), $"{data.ColumnName}"));
            }
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"{info.TableName}Info value = null;"));
            getFunc.Statements.Add(new CodeSnippetStatement("\n"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"if (datas.TryGetValue(GetIdRule({info.GetPkString("{0}", ",")}), out value))"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"return value;"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"return null;"));
            infosClass.Members.Add(getFunc);

            //Add GetIdRule Func
            CodeMemberMethod getIdRuleFunc = new CodeMemberMethod();
            getIdRuleFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            getIdRuleFunc.Name = "GetIdRule";
            getIdRuleFunc.ReturnType = new CodeTypeReference(typeof(ArraySegment<byte>));
            foreach (TablePKData data in info.PKData)
            {
                getIdRuleFunc.Parameters.Add(new CodeParameterDeclarationExpression(DefineType.ConverSystemTypeName(data.Type), $"{data.ColumnName}"));
            }

            getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "ushort total = 0;"));
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "ushort count = 0;"));
            foreach (TablePKData data in info.PKData)
            {
                getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"total += sizeof({data.Type});"));
            }
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement("\n"));
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "if( total == 0 )"));
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return default(System.ArraySegment<byte>);"));
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement("\n"));
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "byte[] bytes = new byte[total];"));
            foreach (TablePKData data in info.PKData)
            {
                getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"Array.Copy(BitConverter.GetBytes({data.ColumnName}), 0, bytes, count, sizeof({data.Type}));"));
                getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"count += sizeof({data.Type});"));
            }
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement("\n"));
            getIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + " return new System.ArraySegment<byte>(bytes);"));
            infosClass.Members.Add(getIdRuleFunc);

            if (info.IsListIdRule())
            {
                //Add GetListById Func
                CodeMemberMethod getListByIdFunc = new CodeMemberMethod();
                getListByIdFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
                getListByIdFunc.Name = "GetListById";
                getListByIdFunc.ReturnType = new CodeTypeReference($"List<{info.TableName}Info>");
                foreach (TablePKData data in info.PKData)
                {
                    if (data.IsListRuleFindPK)
                        getListByIdFunc.Parameters.Add(new CodeParameterDeclarationExpression(DefineType.ConverSystemTypeName(data.Type), $"{data.ColumnName}"));
                }
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"List<{info.TableName}Info> value = null;"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"ArraySegment<byte> bytes = GetListIdRule({info.GetListIdRuleString("{0}", ",")});"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"if (listData.TryGetValue(bytes, out value))"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"return value;"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"return null;"));
                infosClass.Members.Add(getListByIdFunc);

                CodeMemberMethod getListIdRuleFunc = new CodeMemberMethod();
                getListIdRuleFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
                getListIdRuleFunc.Name = "GetListIdRule";
                getListIdRuleFunc.ReturnType = new CodeTypeReference(typeof(ArraySegment<byte>));

                foreach (TablePKData data in info.PKData)
                {
                    if (data.IsListRuleFindPK)
                        getListIdRuleFunc.Parameters.Add(new CodeParameterDeclarationExpression(DefineType.ConverSystemTypeName(data.Type), $"{data.ColumnName}"));
                }
                getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "ushort total = 0;"));
                getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "ushort count = 0;"));

                foreach (TablePKData data in info.PKData)
                {
                    if (data.IsListRuleFindPK)
                        getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"total += sizeof({data.Type});"));
                }
                getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"if (total == 0)"));
                getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"return default(System.ArraySegment<byte>);"));
                getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"\n"));
                getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"byte[] bytes = new byte[total];"));
                foreach (TablePKData data in info.PKData)
                {
                    if (data.IsListRuleFindPK)
                    {
                        getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"Array.Copy(BitConverter.GetBytes({data.ColumnName}), 0, bytes, count, sizeof({data.Type}));"));
                        getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"count += sizeof({data.Type});"));
                    }
                }
                getListIdRuleFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"return new System.ArraySegment<byte>(bytes);"));
                infosClass.Members.Add(getListIdRuleFunc);
            }

            if (info.IsRefTable())
            {
                foreach (TableVarData data in info.VarData)
                {
                    if (!string.IsNullOrEmpty(data.RefTable))
                    {
                        CodeMemberMethod setupRefItemIdFunc = new CodeMemberMethod();
                        setupRefItemIdFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
                        setupRefItemIdFunc.Name = "SetupRef_item_Id";
                        setupRefItemIdFunc.Parameters.Add(new CodeParameterDeclarationExpression($"{tableDataInfos[int.Parse(data.RefTable)].TableName}Infos", "infos"));
                        setupRefItemIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"foreach ({info.TableName}Info data in dataInfo)"));
                        setupRefItemIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "{"));
                        setupRefItemIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"if (data.{data.ColumnName} != -1)"));
                        setupRefItemIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"data.{data.ColumnName}_ref = infos.Get(({data.Type})data.{data.ColumnName});"));
                        setupRefItemIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));

                        infosClass.Members.Add(setupRefItemIdFunc);
                    }
                }
            }

            nameSpace.Types.Add(infosClass);
            #endregion

            ////Test Show
            //CodeDomProvider provider = CodeDomProvider.CreateProvider("CSharp");
            //CodeGeneratorOptions options = new CodeGeneratorOptions();
            //options.BracingStyle = "C";

            //StringBuilder builder = new StringBuilder();
            //using (StringWriter sourceWriter = new StringWriter(builder))
            //{
            //    provider.GenerateCodeFromCompileUnit(unit, sourceWriter, options);
            //}

            //Console.WriteLine(builder.ToString());

            GeneratorUtils.ExportGenerator(unit, outputPath + $"\\{info.TableName}.cs");
        }

        public void ExportAllDataByteFile(string folderPath, string outputPath)
        {
            //System.Reflection.Assembly protobufdll = System.Reflection.Assembly.LoadFrom($"{folderPath}\\protobuf-net.dll");
            string protobufNetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protobuf-net.dll");

            System.Reflection.Assembly protobufdll = System.Reflection.Assembly.LoadFrom(protobufNetPath);

            System.Reflection.Assembly dll1 = System.Reflection.Assembly.LoadFrom($"D:\\Project\\ProjectT\\DesignTable\\Data\\Dll\\DataMgr.dll");
            System.Reflection.Assembly dll2 = System.Reflection.Assembly.LoadFrom($"D:\\Project\\ProjectT\\DesignTable\\Data\\Dll\\LocalData.dll");

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in loadedAssemblies)
            {
                Console.WriteLine(assembly.FullName);
            }

            string dllPath = $"{folderPath}\\DL\\Design.dll";

            //System.Reflection.Assembly dll = System.Reflection.Assembly.LoadFrom($"{folderPath}\\Design.dll");
            
            foreach (var data in tableDataInfos.Values)
            {
                string typeName = $"DesignTable.{data.TableName}Infos";
                System.Type sType = System.Type.GetType(typeName);

                
                sType = dll1.GetType(typeName);
                System.Reflection.MethodInfo addMethod = sType.GetMethod("Insert");
                object inst = System.Activator.CreateInstance(sType);


                foreach (var var in data.VarObjectData)
                {
                    addMethod?.Invoke(inst, var);
                }

                File.WriteAllBytes($"{outputPath}\\{data.TableName}.bytes", DataSerialize(inst));
            }
        }

        public void ExportDataByteFile(string folderPath, string outputPath, string tableName)
        {
            if (string.IsNullOrEmpty(tableName))
            {
                Console.WriteLine($"Table Name Is Null");
                return;
            }

            var tableData = tableDataInfos.Values.FirstOrDefault(x => x.TableName == tableName);
            if (tableData == null)
            {
                Console.WriteLine($"Table Data Is Null");
                return;
            }

            string typeName = $"DesignTable.{tableData.TableName}Infos";
            System.Type sType = System.Type.GetType(typeName);
            System.Reflection.Assembly dll = System.Reflection.Assembly.LoadFile($"{folderPath}\\Design.dll");
            sType = dll.GetType(typeName);
            System.Reflection.MethodInfo addMethod = sType.GetMethod("Insert");
            object inst = System.Activator.CreateInstance(sType);

            foreach (var var in tableData.VarObjectData)
            {
                addMethod?.Invoke(inst, var);
            }

            File.WriteAllBytes($"{outputPath}\\{tableData.TableName}.bytes", DataSerialize(inst));
        }

        private byte[] DataSerialize(object tableInfos)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize((Stream)ms, tableInfos);

                return ms.ToArray();
            }
        }
    }
}
