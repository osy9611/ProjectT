using DocumentFormat.OpenXml.EMMA;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace DesignGenerator
{
    public class DataMgrGenerator
    {
        private CodeCompileUnit unit;
        private CodeNamespace nameSpace;

        private CodeTypeDeclaration enumData;

        //Key : TableId , Value : TableName
        private List<KeyValuePair<string, string>> infoFields;

        //Key : TableName , Value : RefTableName
        private List<KeyValuePair<string, string>> refTableFields;

        //TEST Func
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

            CodeTypeDeclaration dataComparerClass = new CodeTypeDeclaration("DataComparer");
            dataComparerClass.IsClass = true;
            dataComparerClass.TypeAttributes = System.Reflection.TypeAttributes.Public;
            dataComparerClass.BaseTypes.Add(new CodeTypeReference("System.Collections.Generic.IEqualityComparer<ArraySegment<byte>>"));

            //Init Func
            CodeMemberMethod EqualsFunc = new CodeMemberMethod();
            EqualsFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            EqualsFunc.Name = "Equals";
            EqualsFunc.ReturnType = new CodeTypeReference(typeof(bool));
            EqualsFunc.Parameters.Add(new CodeParameterDeclarationExpression(typeof(ArraySegment<byte>), "x"));
            EqualsFunc.Parameters.Add(new CodeParameterDeclarationExpression(typeof(ArraySegment<byte>), "y"));
            EqualsFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "return x.SequenceEqual(y);"));
            dataComparerClass.Members.Add(EqualsFunc);

            CodeMemberMethod GetHashCodeFunc = new CodeMemberMethod();
            GetHashCodeFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            GetHashCodeFunc.Name = "GetHashCode";
            GetHashCodeFunc.ReturnType = new CodeTypeReference(typeof(int));
            GetHashCodeFunc.Parameters.Add(new CodeParameterDeclarationExpression(typeof(ArraySegment<byte>), "obj"));
            GetHashCodeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "if(obj == null) throw new ArgumentNullException(\"obj\");"));
            GetHashCodeFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "return obj.Sum(y => y);"));
            dataComparerClass.Members.Add(GetHashCodeFunc);
            nameSpace.Types.Add(dataComparerClass);

            enumData = new CodeTypeDeclaration("TableId");
            enumData.IsEnum = true;
            enumData.TypeAttributes = System.Reflection.TypeAttributes.Public;

            infoFields = new List<KeyValuePair<string, string>>();
            refTableFields = new List<KeyValuePair<string, string>>();
        }

        public void SetData(string tableId, string tableName, List<string> refTable = null)
        {
            //Enum TableId
            enumData.Members.Add(new CodeMemberField("TableId", $"{tableName} = {tableId}"));

            infoFields.Add(new KeyValuePair<string, string>(tableId, tableName));

            if (refTable == null)
                return;

            foreach (var data in refTable)
            {
                refTableFields.Add(new KeyValuePair<string, string>(tableName, data));
            }
        }

        public void Create()
        {
            nameSpace.Types.Add(enumData);
            CreateDataMgr();
        }

        public void Save(string outputPath)
        {
            GeneratorUtils.ExportGenerator(unit, outputPath + $"\\DataMgr.cs");
        }

        private void CreateDataMgr()
        {
            CodeTypeDeclaration dataMgr = new CodeTypeDeclaration("DataMgr");
            dataMgr.IsClass = true;
            dataMgr.TypeAttributes = System.Reflection.TypeAttributes.Public;

            dataMgr.Members.Add(new CodeMemberField("delegate void", "LoadHandler(byte[] data)"));
            dataMgr.Members.Add(new CodeMemberField("delegate void", "ClearHandler()"));
            dataMgr.Members.Add(new CodeMemberField("Dictionary<int, DataMgr.LoadHandler>", "loadHandlerList = new Dictionary<int, LoadHandler>()"));
            dataMgr.Members.Add(new CodeMemberField("Dictionary<int, DataMgr.ClearHandler>", "clearHandlerList = new Dictionary<int, ClearHandler>()"));
            dataMgr.Members.Add(new CodeMemberField("System.Boolean", "isCallInit = false"));
            dataMgr.Members.Add(new CodeMemberField("DataMessageSerializer", "serializer = new DataMessageSerializer()"));

            foreach (var infoField in infoFields)
            {
                //Private
                string tableFirstLower = char.ToLower(infoField.Value[0]) + infoField.Value.Substring(1);
                CodeMemberField privateField = new CodeMemberField($"{infoField.Value}Infos", $"{tableFirstLower}Infos");
                privateField.Attributes = MemberAttributes.Private;
                dataMgr.Members.Add(privateField);

                //Public
                string tableFirstUpper = char.ToUpper(infoField.Value[0]) + infoField.Value.Substring(1);
                CodeMemberField publicField = new CodeMemberField($"{infoField.Value}Infos", $"{tableFirstUpper}Infos => {tableFirstLower}Infos");
                publicField.Attributes = MemberAttributes.Public;
                dataMgr.Members.Add(publicField);
            }

            //Init Func
            CodeMemberMethod initFunc = new CodeMemberMethod();
            initFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            initFunc.Name = "Init";
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "if (isCallInit)"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return;"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "RegisterLoadHandler();"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "RegisterClearHandler();"));
            initFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "isCallInit = true;"));
            dataMgr.Members.Add(initFunc);

            //Load Func
            CodeMemberMethod loadFunc = new CodeMemberMethod();
            loadFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            loadFunc.Name = "LoadData";
            loadFunc.Parameters.Add(new CodeParameterDeclarationExpression("TableId", "dataType"));
            loadFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Byte[]", "data"));
            loadFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "loadHandlerList[(int)dataType](data);"));
            dataMgr.Members.Add(loadFunc);

            //ClearData Func Array
            CodeMemberMethod clearDataFunc_ArrayParams = new CodeMemberMethod();
            clearDataFunc_ArrayParams.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            clearDataFunc_ArrayParams.Name = "ClearData";
            clearDataFunc_ArrayParams.Parameters.Add(new CodeParameterDeclarationExpression("TableId[]", "dataTypes"));
            clearDataFunc_ArrayParams.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "foreach (int dataType in dataTypes)"));
            clearDataFunc_ArrayParams.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "{ "));
            clearDataFunc_ArrayParams.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "clearHandlerList[dataType]();"));
            clearDataFunc_ArrayParams.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "}"));
            dataMgr.Members.Add(clearDataFunc_ArrayParams);

            //ClearData Func
            CodeMemberMethod clearDataFunc = new CodeMemberMethod();
            clearDataFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            clearDataFunc.Name = "ClearData";
            clearDataFunc.Parameters.Add(new CodeParameterDeclarationExpression("TableId", "dataType"));
            clearDataFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "clearHandlerList[(int)dataType]();"));
            dataMgr.Members.Add(clearDataFunc);

            //ClearDataAll
            CodeMemberMethod clearDataAllFunc = new CodeMemberMethod();
            clearDataAllFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            clearDataAllFunc.Name = "ClearDataAll";
            clearDataAllFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "foreach (DataMgr.ClearHandler clearHandler in clearHandlerList.Values)"));
            clearDataAllFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "{"));
            clearDataAllFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "clearHandler();"));
            clearDataAllFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "}"));
            dataMgr.Members.Add(clearDataAllFunc);

            //RegisterLoadHandler Func
            CodeMemberMethod registerLoadHandlerFunc = new CodeMemberMethod();
            registerLoadHandlerFunc.Attributes = MemberAttributes.Private | MemberAttributes.Final;
            registerLoadHandlerFunc.Name = "RegisterLoadHandler";
            foreach (var infoField in infoFields)
            {
                registerLoadHandlerFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"loadHandlerList.Add({infoField.Key}, new DataMgr.LoadHandler(Load{infoField.Value}Infos));"));
            }
            dataMgr.Members.Add(registerLoadHandlerFunc);

            //RegisterClearHandler Func
            CodeMemberMethod registerClearHandlerFunc = new CodeMemberMethod();
            registerClearHandlerFunc.Attributes = MemberAttributes.Private | MemberAttributes.Final;
            registerClearHandlerFunc.Name = "RegisterClearHandler";
            foreach (var infoField in infoFields)
            {
                registerClearHandlerFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"clearHandlerList.Add({infoField.Key}, ClearData{infoField.Value}Infos);"));
            }
            dataMgr.Members.Add(registerClearHandlerFunc);

            //Load Table Infos Func
            foreach (var infoField in infoFields)
            {
                CodeMemberMethod loadInfoFunc = new CodeMemberMethod();
                loadInfoFunc.Attributes = MemberAttributes.Private | MemberAttributes.Final;

                string tableFirstLower = char.ToLower(infoField.Value[0]) + infoField.Value.Substring(1);
                loadInfoFunc.Name = $"Load{infoField.Value}Infos";
                loadInfoFunc.Parameters.Add(new CodeParameterDeclarationExpression("System.Byte[]", "data"));
                loadInfoFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "using (MemoryStream memoryStream = new MemoryStream(data))"));
                loadInfoFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "{"));
                loadInfoFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"{tableFirstLower}Infos = serializer.Deserialize({infoField.Key},data) as {infoField.Value}Infos;"));
                loadInfoFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"{tableFirstLower}Infos.Initialize();"));
                loadInfoFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + "}"));
                dataMgr.Members.Add(loadInfoFunc);
            }

            //Clear Table Infos Func
            foreach (var infoField in infoFields)
            {
                CodeMemberMethod clearInfoFunc = new CodeMemberMethod();
                clearInfoFunc.Attributes = MemberAttributes.Private | MemberAttributes.Final;

                string tableFirstLower = char.ToLower(infoField.Value[0]) + infoField.Value.Substring(1);
                clearInfoFunc.Name = $"ClearData{infoField.Value}Infos";
                clearInfoFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"if({infoField.Value}Infos != null)"));
                clearInfoFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + $"{tableFirstLower}Infos=null;"));
                dataMgr.Members.Add(clearInfoFunc);
            }

            //SetUpRef Func
            CodeMemberMethod setUpRefFunc = new CodeMemberMethod();
            setUpRefFunc.Attributes = MemberAttributes.Public | MemberAttributes.Final;
            setUpRefFunc.Name = "SetUpRef";
            foreach(var refTable in refTableFields)
            {
                setUpRefFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB2 + $"{refTable.Key}Infos.SetupRef_item_Id({refTable.Value}Infos);"));
            }
            dataMgr.Members.Add(setUpRefFunc);
            nameSpace.Types.Add(dataMgr);
        }
    }
}
