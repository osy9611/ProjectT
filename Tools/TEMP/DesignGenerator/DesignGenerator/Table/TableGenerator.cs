using System.CodeDom;
using System.Globalization;
using System.Reflection;
using System.Xml;
using Newtonsoft.Json.Linq;
using DesignGenerator.Core;
using DesignGenerator.Enum;
using DesignGenerator.Util;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

namespace DesignGenerator.Table
{
    public sealed class RefLink
    {
        public string ColumnName;
        public string RefTableName;
    }

    public class TableGenerator
    {
        private readonly Dictionary<int, TableDataInfo> tableDataInfos = new Dictionary<int, TableDataInfo>();
        private Dictionary<int, EnumInfo> enumInfos = new Dictionary<int, EnumInfo>();

        public DiagnosticBag Diagnostics = new DiagnosticBag();

        public Action<string, string, List<RefLink>> OnMgrSet = null;
        public Action<string, string> OnSerializerSet = null;

        public IReadOnlyDictionary<int, TableDataInfo> Tables { get { return tableDataInfos; } }

        public bool LoadAll(string address, string readAllDataPath, string outputScriptPath, bool changedOnly = false)
        {
            LoadEnums(address);

            ExcelUtils.ExportAllXml(Path.Combine(address, "Table", "Tables"),
                                    Path.Combine(address, "Table", "Xml"), changedOnly);

            foreach (var kv in XmlManager.LoadAllXmlWithName(readAllDataPath))
                GetTableInfo(kv.Value, kv.Key);

            TableValidator.ValidateSchema(tableDataInfos, enumInfos, Diagnostics);
            foreach (var t in tableDataInfos.Values)
                TableValidator.ValidatePrimaryKeys(t, Diagnostics);

            if (Diagnostics.HasError) return false;

            foreach (var kv in tableDataInfos.OrderBy(x => x.Key))
            {
                var info = kv.Value;
                GenerateInfoData(info, outputScriptPath);
                if (OnSerializerSet != null)
                    OnSerializerSet(info.TableID.ToString(CultureInfo.InvariantCulture), info.TableName);
                if (OnMgrSet != null)
                    OnMgrSet(info.TableID.ToString(CultureInfo.InvariantCulture), info.TableName, BuildRefLinks(info));
            }
            return true;
        }

        public bool Load(string address, string tableName, string readAllDataPath, string outputScriptPath)
        {
            LoadEnums(address);

            ExcelUtils.ExportXml(Path.Combine(address, "Table", "Tables", tableName + ".xlsx"),
                                 Path.Combine(address, "Table", "Xml", tableName + ".xml"));

            // 참조 해석 때문에 전체 verify 가 필요합니다.
            foreach (var kv in XmlManager.LoadAllXmlWithName(readAllDataPath))
                GetTableInfo(kv.Value, kv.Key);

            TableValidator.ValidateSchema(tableDataInfos, enumInfos, Diagnostics);

            var target = tableDataInfos.Values.FirstOrDefault(x => x.TableName == tableName);
            if (target == null)
            {
                Diagnostics.Error("TG2060", SourceLocation.InFile(tableName), "해당 테이블을 찾을 수 없습니다.");
                return false;
            }

            TableValidator.ValidatePrimaryKeys(target, Diagnostics);
            if (Diagnostics.HasError) return false;

            GenerateInfoData(target, outputScriptPath);
            if (OnSerializerSet != null)
                OnSerializerSet(target.TableID.ToString(CultureInfo.InvariantCulture), target.TableName);
            if (OnMgrSet != null)
                OnMgrSet(target.TableID.ToString(CultureInfo.InvariantCulture), target.TableName, BuildRefLinks(target));
            return true;
        }

        private List<RefLink> BuildRefLinks(TableDataInfo info)
        {
            var list = new List<RefLink>();
            foreach (var v in info.VarData)
            {
                if (string.IsNullOrEmpty(v.RefTable)) continue;
                int refId;
                if (!int.TryParse(v.RefTable, NumberStyles.Integer, CultureInfo.InvariantCulture, out refId)) continue;
                TableDataInfo t;
                if (!tableDataInfos.TryGetValue(refId, out t)) continue;
                list.Add(new RefLink { ColumnName = v.ColumnName, RefTableName = t.TableName });
            }
            return list;
        }

        private void LoadEnums(string address)
        {
            string enumXml = Path.Combine(address, "Enum", "enum.xml");
            if (!File.Exists(enumXml))
            {
                Diagnostics.Error("TG0001", SourceLocation.InFile(enumXml),
                    "enum.xml 이 없습니다.", "Enum 단계를 먼저 실행하세요.");
                return;
            }
            var gen = new EnumGenerator();
            enumInfos = gen.SetEnumInfo(XmlManager.LoadXML(enumXml));
        }

        private void GetTableInfo(XmlDocument xml, string fileName)
        {
            if (xml == null || xml.DocumentElement == null) return;

            string root = xml.DocumentElement.Name;
            XmlNodeList verifyNodes = xml.SelectNodes(root + "/verify");
            XmlNodeList recordNodes = xml.SelectNodes(root + "/record");

            if (verifyNodes == null || verifyNodes.Count == 0)
            {
                Diagnostics.Error("TG2061", SourceLocation.InFile(fileName), "verify 노드가 없습니다.");
                return;
            }

            int tableId;
            string tableIdRaw = Attr(verifyNodes[0], "TableId");
            if (!int.TryParse(tableIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out tableId))
            {
                Diagnostics.Error("TG2062", SourceLocation.InFile(fileName), $"TableId 를 읽을 수 없습니다: '{tableIdRaw}'");
                return;
            }
            if (tableDataInfos.ContainsKey(tableId)) return;    // 이미 읽음

            var info = new TableDataInfo { TableName = root, TableID = tableId };

            //verify → 스키마
            JObject jsonData = null;
            bool anyFieldNo = false, allFieldNo = true;
            int positional = 1;

            foreach (XmlNode node in verifyNodes)
            {
                string columnName = Attr(node, "ColumnName");
                if (string.IsNullOrEmpty(columnName))
                    continue;

                var loc = SourceLocation.At(fileName, 0, columnName);

                string typeRaw = Attr(node, "Type");
                string csType;
                try
                {
                    csType = DefineType.ConvertTypeName(typeRaw);
                }
                catch (Exception e)
                {
                    Diagnostics.Error("TG2006", loc, e.Message);
                    continue;
                }

                int enumId = ParseDash(Attr(node, "EnumID"));
                string refTable = Dash(Attr(node, "StringHashIds"));   // K열 = 참조 테이블 ID

                int fieldNo = ParseDash(Attr(node, "FieldNo"));
                if (fieldNo > 0) anyFieldNo = true; else allFieldNo = false;
                if (fieldNo <= 0) fieldNo = positional;
                positional++;

                if (Attr(node, "PK") == "Y")
                    info.AddPKData(columnName, csType, enumId);

                info.AddVarData(columnName, csType, enumId, refTable, fieldNo);

                string idRule = Dash(Attr(node, "IDRule"));
                if (!string.IsNullOrEmpty(idRule))
                {
                    try
                    {
                        jsonData = JObject.Parse(idRule.Replace("&quot;", "\""));
                        if (jsonData["UseListRule"] != null && (bool)jsonData["UseListRule"])
                            info.UseListRule = true;
                    }
                    catch (Exception e)
                    {
                        Diagnostics.Error("TG2010", loc, "ID Rule JSON 파싱 실패: " + e.Message);
                        jsonData = null;
                    }
                }
            }

            info.FieldNoIsPositional = !anyFieldNo;
            if (anyFieldNo && !allFieldNo)
                Diagnostics.Error("TG2041", SourceLocation.InFile(fileName),
                    "FieldNo 가 일부 컬럼에만 지정돼 있습니다. 전부 지정하거나 전부 비우세요.");

            if (jsonData != null)
            {
                ApplyRule(jsonData, "PKIds", info.AddIsListRule);
                ApplyRule(jsonData, "FindPKId", info.AddFindPKListRule);
            }

            //record → 값
            FillRecords(info, recordNodes, fileName);

            tableDataInfos.Add(tableId, info);
        }

        private void ApplyRule(JObject json, string key, Action<string> apply)
        {
            var token = json[key];
            if (token == null) return;
            foreach (var t in JArray.Parse(token.ToString())) apply(t.ToString());
        }

        private void FillRecords(TableDataInfo info, XmlNodeList recordNodes, string fileName)
        {
            if (recordNodes == null) return;

            int colCount = info.VarData.Count;
            var colIndex = new Dictionary<string, int>(colCount, StringComparer.Ordinal);
            for (int i = 0; i < colCount; i++) colIndex[info.VarData[i].ColumnName] = i;

            var buffer = new string[colCount];
            int recNo = 0;

            foreach (XmlNode node in recordNodes)
            {
                recNo++;
                if (node.InnerText.Length == 0) continue;

                Array.Clear(buffer, 0, colCount);
                for (XmlNode c = node.FirstChild; c != null; c = c.NextSibling)
                {
                    int idx;
                    if (colIndex.TryGetValue(c.Name, out idx)) buffer[idx] = c.InnerText;
                }

                var val = new object[colCount];
                bool rowOk = true;

                for (int i = 0; i < colCount; i++)
                {
                    TableVarData vd = info.VarData[i];
                    string raw = buffer[i] ?? string.Empty;
                    var loc = SourceLocation.At(fileName, recNo, vd.ColumnName);

                    //enum 이름으로 적힌 값을 숫자로
                    if (vd.EnumId != 0 && raw.Length > 0)
                    {
                        EnumInfo ei;
                        if (enumInfos != null && enumInfos.TryGetValue(vd.EnumId, out ei))
                        {
                            int ev;
                            if (ei.TryGetValue(raw, out ev))
                                raw = ev.ToString(CultureInfo.InvariantCulture);
                        }
                    }

                    object converted;
                    string error;
                    if (DefineType.TryConvertDataType(vd.Type, raw, out converted, out error))
                    {
                        val[i] = converted;
                    }
                    else
                    {
                        Diagnostics.Error("TG2030", loc, error,
                            vd.Type == "bool" ? "엑셀에서 이 컬럼과 이웃 컬럼의 값이 밀리지 않았는지 확인하세요." : null);
                        rowOk = false;
                    }
                }

                if (rowOk)
                    info.VarObjectData.Add(val);
            }
        }

        private static string Attr(XmlNode node, string name)
        {
            if (node == null || node.Attributes == null)
                return string.Empty;

            var a = node.Attributes[name];
            return a == null ? string.Empty : a.Value.Trim();
        }

        private static string Dash(string s)
        {
            return (string.IsNullOrEmpty(s) || s == "-") ? string.Empty : s;
        }

        private static int ParseDash(string s)
        {
            s = Dash(s);
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        //코드 생성
        private void GenerateInfoData(TableDataInfo info, string outputPath)
        {
            var unit = new CodeCompileUnit();

            var ns = new CodeNamespace("DesignTable");
            ns.Imports.Add(new CodeNamespaceImport("System"));
            ns.Imports.Add(new CodeNamespaceImport("System.Collections"));
            ns.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            ns.Imports.Add(new CodeNamespaceImport("ProtoBuf"));
            unit.Namespaces.Add(ns);

            #region Info 클래스
            var infoClass = new CodeTypeDeclaration(info.TableName + "Info")
            {
                IsClass = true,
                TypeAttributes = TypeAttributes.Public,
            };
            infoClass.CustomAttributes.Add(new CodeAttributeDeclaration("ProtoContract"));

            foreach (var data in info.VarData)
            {
                var pro = new CodeMemberField(DefineType.ConverSystemTypeName(data.Type), data.ColumnName)
                {
                    Attributes = MemberAttributes.Public,
                };

                pro.CustomAttributes.Add(new CodeAttributeDeclaration(
                   "ProtoMember", new CodeAttributeArgument(new CodePrimitiveExpression(data.FieldNo))));
                infoClass.Members.Add(pro);

                if (!string.IsNullOrEmpty(data.RefTable))
                {
                    int refId = int.Parse(data.RefTable, CultureInfo.InvariantCulture);
                    infoClass.Members.Add(new CodeMemberField(
                        tableDataInfos[refId].TableName + "Info", data.ColumnName + "_ref")
                    {
                        Attributes = MemberAttributes.Public,
                    });
                }
            }

            infoClass.Members.Add(new CodeConstructor
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = info.TableName + "Info",
            });

            var ctorParams = new CodeConstructor
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = info.TableName + "Info",
            };
            foreach (var data in info.VarData)
            {
                ctorParams.Parameters.Add(new CodeParameterDeclarationExpression(
                    DefineType.ConverSystemTypeName(data.Type), data.ColumnName));
                ctorParams.Statements.Add(new CodeSnippetStatement(
                    Tab.TAB3 + $"this.{data.ColumnName} = {data.ColumnName};"));
            }
            infoClass.Members.Add(ctorParams);
            ns.Types.Add(infoClass);
            #endregion

            #region Infos 클래스
            var infosClass = new CodeTypeDeclaration(info.TableName + "Infos")
            {
                IsClass = true,
                TypeAttributes = TypeAttributes.Public,
            };
            infosClass.CustomAttributes.Add(new CodeAttributeDeclaration("ProtoContract"));

            var dataInfoVar = new CodeMemberField($"List<{info.TableName}Info>", "dataInfo")
            {
                Attributes = MemberAttributes.Public,
                InitExpression = new CodeSnippetExpression($"new List<{info.TableName}Info>()"),
            };
            dataInfoVar.CustomAttributes.Add(new CodeAttributeDeclaration(
                "ProtoMember", new CodeAttributeArgument(new CodePrimitiveExpression(1))));
            infosClass.Members.Add(dataInfoVar);

            infosClass.Members.Add(new CodeMemberField(
                $"Dictionary<ArraySegment<byte>, {info.TableName}Info>", "datas")
            {
                Attributes = MemberAttributes.Public,
                InitExpression = new CodeSnippetExpression(
                    $"new Dictionary<ArraySegment<byte>, {info.TableName}Info>(new DataComparer())"),
            });

            if (info.UseListRule)
            {
                infosClass.Members.Add(new CodeMemberField(
                    $"Dictionary<ArraySegment<byte>, List<{info.TableName}Info>>", "listData")
                {
                    Attributes = MemberAttributes.Public,
                    InitExpression = new CodeSnippetExpression(
                        $"new Dictionary<ArraySegment<byte>, List<{info.TableName}Info>>(new DataComparer())"),
                });
            }

            var insertFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Insert",
                ReturnType = new CodeTypeReference(typeof(bool)),
            };
            foreach (var data in info.VarData)
                insertFunc.Parameters.Add(new CodeParameterDeclarationExpression(
                    DefineType.ConverSystemTypeName(data.Type), data.ColumnName));

            string pkArgs = info.GetPkString("{0}", ", ");
            string newInfoParams = info.GetStringVerDatas("{0}", ", ");

            if (info.PKData.Count > 0)
            {
                insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"ArraySegment<byte> key = GetIdRule({pkArgs});"));
                insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "if (datas.ContainsKey(key))"));
                insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return false;"));
                insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"{info.TableName}Info newInfo = new {info.TableName}Info({newInfoParams});"));
                insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "dataInfo.Add(newInfo);"));
                insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "datas.Add(key, newInfo);"));
            }
            else
            {
                insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"dataInfo.Add(new {info.TableName}Info({newInfoParams}));"));
            }
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return true;"));
            infosClass.Members.Add(insertFunc);

            //Initialize
            var initializeFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Initialize",
            };
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "foreach (var data in dataInfo)"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"ArraySegment<byte> bytes = GetIdRule({info.GetPkString("data.{0}", ", ")});"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "if (datas.ContainsKey(bytes))"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + "continue;"));
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "datas.Add(bytes, data);"));

            if (info.UseListRule && !info.IsListIdRule())
            {
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"bytes = GetListIdRule({info.GetListIdRuleString("data.{0}", ", ")});"));
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "if (listData.ContainsKey(bytes))"));
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + "listData[bytes].Add(data);"));
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "else"));
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "{"));
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + $"listData.Add(bytes, new List<{info.TableName}Info>());"));
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + "listData[bytes].Add(data);"));
                initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "}"));
            }
            initializeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
            infosClass.Members.Add(initializeFunc);

            //Get
            var getFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Get",
                ReturnType = new CodeTypeReference(info.TableName + "Info"),
            };
            foreach (var data in info.PKData)
                getFunc.Parameters.Add(new CodeParameterDeclarationExpression(
                    DefineType.ConverSystemTypeName(data.Type), data.ColumnName));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"{info.TableName}Info value = null;"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"if (datas.TryGetValue(GetIdRule({pkArgs}), out value))"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return value;"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return null;"));
            infosClass.Members.Add(getFunc);

            //GetIdRule
            infosClass.Members.Add(BuildIdRuleFunc("GetIdRule", info.PKData, info));

            if (info.IsListIdRule())
            {
                var listPk = info.PKData.Where(x => x.IsListRuleFindPK).ToList();

                var getListByIdFunc = new CodeMemberMethod
                {
                    Attributes = MemberAttributes.Public | MemberAttributes.Final,
                    Name = "GetListById",
                    ReturnType = new CodeTypeReference($"List<{info.TableName}Info>"),
                };
                foreach (var d in listPk)
                    getListByIdFunc.Parameters.Add(new CodeParameterDeclarationExpression(
                        DefineType.ConverSystemTypeName(d.Type), d.ColumnName));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"List<{info.TableName}Info> value = null;"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"ArraySegment<byte> bytes = GetListIdRule({info.GetListIdRuleString("{0}", ", ")});"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "if (listData.TryGetValue(bytes, out value))"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return value;"));
                getListByIdFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return null;"));
                infosClass.Members.Add(getListByIdFunc);

                infosClass.Members.Add(BuildIdRuleFunc("GetListIdRule", listPk, info));
            }

            //SetupRef_{ColumnName} 이름을 컬럼별로
            foreach (var data in info.VarData)
            {
                if (string.IsNullOrEmpty(data.RefTable)) continue;
                int refId = int.Parse(data.RefTable, CultureInfo.InvariantCulture);
                string refName = tableDataInfos[refId].TableName;

                var f = new CodeMemberMethod
                {
                    Attributes = MemberAttributes.Public | MemberAttributes.Final,
                    Name = "SetupRef_" + data.ColumnName,
                };
                f.Parameters.Add(new CodeParameterDeclarationExpression(refName + "Infos", "infos"));
                f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"foreach ({info.TableName}Info data in dataInfo)"));
                f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
                f.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"if (data.{data.ColumnName} != -1)"));
                f.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + $"data.{data.ColumnName}_ref = infos.Get(({data.Type})data.{data.ColumnName});"));
                f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
                infosClass.Members.Add(f);
            }

            ns.Types.Add(infosClass);
            #endregion

            GeneratorUtils.ExportGenerator(unit, Path.Combine(outputPath, info.TableName + ".cs"));
        }

        private static CodeMemberMethod BuildIdRuleFunc(string name, List<TablePKData> pks, TableDataInfo info)
        {
            var f = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = name,
                ReturnType = new CodeTypeReference(typeof(ArraySegment<byte>)),
            };
            foreach (var d in pks)
                f.Parameters.Add(new CodeParameterDeclarationExpression(
                    DefineType.ConverSystemTypeName(d.Type), d.ColumnName));

            f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "ushort total = 0;"));
            f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "ushort count = 0;"));

            foreach (var d in pks)
            {
                f.Statements.Add(new CodeSnippetStatement(d.Type == "string"
                    ? Tab.TAB3 + $"total += (ushort)System.Text.Encoding.UTF8.GetByteCount({d.ColumnName});"
                    : Tab.TAB3 + $"total += sizeof({d.Type});"));
            }

            f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "if (total == 0)"));
            f.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return default(System.ArraySegment<byte>);"));
            f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "byte[] bytes = new byte[total];"));

            if (pks.Exists(x => x.Type == "string"))
                f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "byte[] stringBytes;"));

            foreach (var d in pks)
            {
                if (d.Type == "string")
                {
                    f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"stringBytes = System.Text.Encoding.UTF8.GetBytes({d.ColumnName});"));
                    f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "Array.Copy(stringBytes, 0, bytes, count, stringBytes.Length);"));
                    f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "count += (ushort)stringBytes.Length;"));
                }
                else
                {
                    f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"Array.Copy(BitConverter.GetBytes({d.ColumnName}), 0, bytes, count, sizeof({d.Type}));"));
                    f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"count += sizeof({d.Type});"));
                }
            }

            f.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return new System.ArraySegment<byte>(bytes);"));
            return f;
        }

        //.byte 출력
        public void ExportAllDataByteFile(string dllFolder, string outputPath)
        {
            Directory.CreateDirectory(outputPath);
            Assembly dll = LoadDesignDll(dllFolder);

            foreach (var data in tableDataInfos.Values.OrderBy(x => x.TableID))
                ExportOne(dll, data, outputPath);
        }

        public void ExportDataByteFile(string dllFolder, string outputPath, string tableName)
        {
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException("테이블 이름이 비었습니다.");

            var data = tableDataInfos.Values.FirstOrDefault(x => x.TableName == tableName);
            if (data == null)
                throw new InvalidOperationException($"테이블 '{tableName}' 을(를) 찾을 수 없습니다.");

            Directory.CreateDirectory(outputPath);
            ExportOne(LoadDesignDll(dllFolder), data, outputPath);
        }
        private static Assembly LoadDesignDll(string dllFolder)
        {
            string path = Path.Combine(dllFolder, "Design.dll");
            if (!File.Exists(path))
                throw new FileNotFoundException("병합된 Design.dll 을 찾을 수 없습니다.", path);
            return Assembly.LoadFrom(path);
        }

        private static void ExportOne(Assembly dll, TableDataInfo data, string outputPath)
        {
            string typeName = "DesignTable." + data.TableName + "Infos";
            Type sType = dll.GetType(typeName);
            if (sType == null)
                throw new TypeLoadException($"{typeName} 이(가) Design.dll 에 없습니다.");

            MethodInfo addMethod = sType.GetMethod("Insert");
            if (addMethod == null)
                throw new MissingMethodException(typeName, "Insert");

            object inst = Activator.CreateInstance(sType);
            foreach (var row in data.VarObjectData)
                addMethod.Invoke(inst, row);

            File.WriteAllBytes(Path.Combine(outputPath, data.TableName + ".bytes"), Serialize(inst));
        }

        private static byte[] Serialize(object tableInfos)
        {
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, tableInfos);
                return ms.ToArray();
            }
        }
    }
}
