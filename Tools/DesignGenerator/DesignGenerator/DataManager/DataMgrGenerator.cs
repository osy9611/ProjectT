using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using DesignGenerator.Table;
using DesignGenerator.Util;

namespace DesignGenerator.DataManager
{
    public class DataMgrGenerator
    {
        private readonly CodeCompileUnit unit;
        private readonly CodeNamespace nameSpace;
        private readonly CodeTypeDeclaration enumData;

        private sealed class TableEntry
        {
            public string TableId;
            public string TableName;
            public List<RefLink> Refs = new List<RefLink>();
        }

        private readonly List<TableEntry> tables = new List<TableEntry>();

        public DataMgrGenerator()
        {
            unit = new CodeCompileUnit();
            nameSpace = new CodeNamespace("DesignTable");

            nameSpace.Imports.Add(new CodeNamespaceImport("System"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.IO"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Linq"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            nameSpace.Imports.Add(new CodeNamespaceImport("System.Diagnostics.CodeAnalysis"));
            unit.Namespaces.Add(nameSpace);

            var dataComparerClass = new CodeTypeDeclaration("DataComparer")
            {
                IsClass = true,
                TypeAttributes = TypeAttributes.Public,
            };
            dataComparerClass.BaseTypes.Add(new CodeTypeReference(
                "System.Collections.Generic.IEqualityComparer<ArraySegment<byte>>"));

            var equalsFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Equals",
                ReturnType = new CodeTypeReference(typeof(bool)),
            };
            equalsFunc.Parameters.Add(new CodeParameterDeclarationExpression(typeof(ArraySegment<byte>), "x"));
            equalsFunc.Parameters.Add(new CodeParameterDeclarationExpression(typeof(ArraySegment<byte>), "y"));
            equalsFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return x.SequenceEqual(y);"));
            dataComparerClass.Members.Add(equalsFunc);

            var getHashFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "GetHashCode",
                ReturnType = new CodeTypeReference(typeof(int)),
            };
            getHashFunc.Parameters.Add(new CodeParameterDeclarationExpression(typeof(ArraySegment<byte>), "obj"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "if (obj.Array == null) return 0;"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "unchecked"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "int hash = (int)2166136261;"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "for (int i = obj.Offset; i < obj.Offset + obj.Count; ++i)"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB5 + "hash = (hash ^ obj.Array[i]) * 16777619;"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return hash;"));
            getHashFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
            dataComparerClass.Members.Add(getHashFunc);

            nameSpace.Types.Add(dataComparerClass);

            enumData = new CodeTypeDeclaration("TableId")
            {
                IsEnum = true,
                TypeAttributes = TypeAttributes.Public,
            };
        }

        public void SetData(string tableId, string tableName, List<RefLink> refTables = null)
        {
            var e = new TableEntry { TableId = tableId, TableName = tableName };
            if (refTables != null)
                e.Refs.AddRange(refTables);
            tables.Add(e);
        }

        public void Create()
        {
            tables.Sort((a, b) => ParseId(a.TableId).CompareTo(ParseId(b.TableId)));

            foreach (var t in tables)
            {
                enumData.Members.Add(new CodeMemberField("TableId", $"{t.TableName} = {t.TableId}"));
            }

            nameSpace.Types.Add(enumData);
            CreateDataMgr();
        }

        private static int ParseId(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public bool Save(string outputPath)
        {
            return GeneratorUtils.ExportGenerator(unit, Path.Combine(outputPath, "DataMgr.cs"));
        }

        private void CreateDataMgr()
        {
            var dataMgr = new CodeTypeDeclaration("DataMgr")
            {
                IsClass = true,
                TypeAttributes = TypeAttributes.Public,
            };

            dataMgr.Members.Add(new CodeMemberField("delegate void", "LoadHandler(byte[] data)"));
            dataMgr.Members.Add(new CodeMemberField("delegate void", "ClearHandler()"));
            dataMgr.Members.Add(new CodeMemberField("Dictionary<int, DataMgr.LoadHandler>", "loadHandlerList = new Dictionary<int, LoadHandler>()"));
            dataMgr.Members.Add(new CodeMemberField("Dictionary<int, DataMgr.ClearHandler>", "clearHandlerList = new Dictionary<int, ClearHandler>()"));
            dataMgr.Members.Add(new CodeMemberField("System.Boolean", "isCallInit = false"));
            dataMgr.Members.Add(new CodeMemberField("DataMessageSerializer", "serializer = new DataMessageSerializer()"));

            foreach (var t in tables)
            {
                string lower = Lower(t.TableName);
                string upper = Upper(t.TableName);

                dataMgr.Members.Add(new CodeMemberField($"{t.TableName}Infos", $"{lower}Infos")
                { Attributes = MemberAttributes.Private });

                dataMgr.Members.Add(new CodeMemberField($"{t.TableName}Infos", $"{upper}Infos => {lower}Infos")
                { Attributes = MemberAttributes.Public });
            }

            var initFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Init",
            };
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "if (isCallInit)"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return;"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "RegisterLoadHandler();"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "RegisterClearHandler();"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "isCallInit = true;"));
            dataMgr.Members.Add(initFunc);

            var loadFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "LoadData",
            };
            loadFunc.Parameters.Add(new CodeParameterDeclarationExpression("TableId", "dataType"));
            loadFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Byte[]", "data"));
            loadFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "loadHandlerList[(int)dataType](data);"));
            dataMgr.Members.Add(loadFunc);

            var clearArray = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "ClearData",
            };
            clearArray.Parameters.Add(new CodeParameterDeclarationExpression("TableId[]", "dataTypes"));
            clearArray.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "foreach (int dataType in dataTypes)"));
            clearArray.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            clearArray.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "clearHandlerList[dataType]();"));
            clearArray.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
            dataMgr.Members.Add(clearArray);

            var clearOne = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "ClearData",
            };
            clearOne.Parameters.Add(new CodeParameterDeclarationExpression("TableId", "dataType"));
            clearOne.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "clearHandlerList[(int)dataType]();"));
            dataMgr.Members.Add(clearOne);

            var clearAll = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "ClearDataAll",
            };
            clearAll.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "foreach (DataMgr.ClearHandler clearHandler in clearHandlerList.Values)"));
            clearAll.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "{"));
            clearAll.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "clearHandler();"));
            clearAll.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "}"));
            dataMgr.Members.Add(clearAll);

            var regLoad = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Private | MemberAttributes.Final,
                Name = "RegisterLoadHandler",
            };
            foreach (var t in tables)
                regLoad.Statements.Add(new CodeSnippetStatement(
                    Tab.TAB3 + $"loadHandlerList.Add({t.TableId}, new DataMgr.LoadHandler(Load{t.TableName}Infos));"));
            dataMgr.Members.Add(regLoad);

            var regClear = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Private | MemberAttributes.Final,
                Name = "RegisterClearHandler",
            };
            foreach (var t in tables)
                regClear.Statements.Add(new CodeSnippetStatement(
                    Tab.TAB3 + $"clearHandlerList.Add({t.TableId}, ClearData{t.TableName}Infos);"));
            dataMgr.Members.Add(regClear);

            foreach (var t in tables)
            {
                string lower = Lower(t.TableName);

                var loadInfo = new CodeMemberMethod
                {
                    Attributes = MemberAttributes.Private | MemberAttributes.Final,
                    Name = $"Load{t.TableName}Infos",
                };
                loadInfo.Parameters.Add(new CodeParameterDeclarationExpression("System.Byte[]", "data"));
                loadInfo.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"{lower}Infos = serializer.Deserialize({t.TableId}, data) as {t.TableName}Infos;"));
                loadInfo.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"if ({lower}Infos != null)"));
                loadInfo.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"{lower}Infos.Initialize();"));
                dataMgr.Members.Add(loadInfo);
            }

            foreach (var t in tables)
            {
                string lower = Lower(t.TableName);

                var clearInfo = new CodeMemberMethod
                {
                    Attributes = MemberAttributes.Private | MemberAttributes.Final,
                    Name = $"ClearData{t.TableName}Infos",
                };
                clearInfo.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"if ({lower}Infos != null)"));
                clearInfo.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + $"{lower}Infos = null;"));
                dataMgr.Members.Add(clearInfo);
            }

            //SetUpRef : 컬럼별 메서드 이름으로
            var setUpRef = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "SetUpRef",
            };
            foreach (var t in tables)
            {
                foreach (var r in t.Refs)
                {
                    string src = Lower(t.TableName);
                    string dst = Lower(r.RefTableName);
                    setUpRef.Statements.Add(new CodeSnippetStatement(
                        Tab.TAB3 + $"if ({src}Infos != null && {dst}Infos != null)"));
                    setUpRef.Statements.Add(new CodeSnippetStatement(
                        Tab.TAB4 + $"{src}Infos.SetupRef_{r.ColumnName}({dst}Infos);"));
                }
            }
            dataMgr.Members.Add(setUpRef);

            nameSpace.Types.Add(dataMgr);
        }
        private static string Lower(string s) { return char.ToLowerInvariant(s[0]) + s.Substring(1); }
        private static string Upper(string s) { return char.ToUpperInvariant(s[0]) + s.Substring(1); }
    }
}
