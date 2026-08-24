using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Xml;
using DesignGenerator.Core;
using DesignGenerator.Util;

namespace DesignGenerator.Localize
{
    public class LocalizeGenerator
    {
        private readonly Dictionary<string, LocalDataInfo> infos = new Dictionary<string, LocalDataInfo>(StringComparer.Ordinal);
        public DiagnosticBag Diagnostics = new DiagnosticBag();

        //true = LOCALNAME 해시로 ID 고정, false = 기존 순번 방식
        public bool UseStableId = true;

        public bool Create(string address, string outputPath, bool changedOnly = false)
        {
            string localDir = Path.Combine(address, "Local");
            if (!Directory.Exists(localDir))
            {
                Diagnostics.Error("TG0001", SourceLocation.InFile(localDir), "Local 폴더가 없습니다.");
                return false;
            }

            ExcelUtils.ExportAllXml(localDir, localDir, changedOnly);

            SetInfos(XmlManager.LoadAllXmlWithName(localDir));
            if (Diagnostics.HasError) return false;

            GenerateInfoData(address);
            return !Diagnostics.HasError;
        }

        private void SetInfos(List<KeyValuePair<string, XmlDocument>> xmlDocs)
        {
            int sequential = 0;

            foreach (var kv in xmlDocs)
            {
                string file = kv.Key;
                XmlDocument doc = kv.Value;

                if (doc.DocumentElement == null)
                    continue;

                XmlNodeList nodes = doc.SelectNodes(doc.DocumentElement.Name + "/record");
                if (nodes == null)
                    continue;

                int recNo = 0;
                foreach (XmlNode node in nodes)
                {
                    recNo++;
                    var loc = SourceLocation.At(file, recNo);

                    string localName = Child(node, "LOCALNAME");
                    if (string.IsNullOrEmpty(localName))
                    {
                        // 빈 행은 건너뜀 (엑셀 표 범위가 넉넉히 잡힌 경우)
                        if (string.IsNullOrEmpty(Child(node, "KO")) &&
                            string.IsNullOrEmpty(Child(node, "EN")) &&
                            string.IsNullOrEmpty(Child(node, "JP"))) continue;

                        Diagnostics.Error("TG3001", loc, "LOCALNAME 이 비어 있는데 번역문이 있습니다.");
                        continue;
                    }

                    if (!CSharpIdentifier.IsValid(localName) || CSharpIdentifier.IsKeyword(localName))
                    {
                        Diagnostics.Error("TG3002", loc,
                            $"LOCALNAME '{localName}' 을(를) 쓸 수 없습니다 — {CSharpIdentifier.Explain(localName)}.",
                            "StringDef 의 enum 멤버 이름이 되므로 영문자/숫자/밑줄만 가능합니다.");
                        continue;
                    }

                    LocalDataInfo prev;
                    if (infos.TryGetValue(localName, out prev))
                    {
                        Diagnostics.Error("TG3003", loc,
                            $"LOCALNAME '{localName}' 이 중복됩니다. (앞선 위치: {prev.SourceFile} #{prev.RecordIndex})");
                        continue;
                    }

                    var info = new LocalDataInfo
                    {
                        IsUse = Child(node, "USE") == "Y",
                        LocalName = localName,
                        Ko = Child(node, "KO"),
                        Jp = Child(node, "JP"),
                        En = Child(node, "EN"),
                        SourceFile = file,
                        RecordIndex = recNo,
                    };

                    //값 결정
                    info.EnumID = UseStableId ? StableId(localName) : sequential;
                    if (info.IsUse) sequential++;      // 사용하는 것만 증가

                    infos.Add(localName, info);
                }
            }

            //해시 충돌 검사(사용 중인 것만)
            if (UseStableId)
            {
                var byId = new Dictionary<int, LocalDataInfo>();
                foreach (var i in infos.Values.Where(x => x.IsUse).OrderBy(x => x.LocalName, StringComparer.Ordinal))
                {
                    LocalDataInfo prev;
                    if (byId.TryGetValue(i.EnumID, out prev))
                    {
                        Diagnostics.Error("TG3004", SourceLocation.At(i.SourceFile, i.RecordIndex),
                          $"'{i.LocalName}' 과 '{prev.LocalName}' 의 ID 가 {i.EnumID} 로 충돌합니다.",
                          "둘 중 하나의 LOCALNAME 을 바꾸세요.");
                    }
                    else
                        byId[i.EnumID] = i;
                }
            }
        }

        //FNV-1a 32bit를 31bit로 접어 항상 양수. 문자열이 같으면 값도 항상 같음
        public static int StableId(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        private static string Child(XmlNode node, string name)
        {
            //Excel XmlMap은 빈 셀의 엘리먼트를 생략한다. node[name]이 null일 수 있음
            var c = node[name];
            return c == null ? string.Empty : c.InnerText.Trim();
        }

        private void GenerateInfoData(string folderPath)
        {
            var unit = new CodeCompileUnit();
            var ns = new CodeNamespace("DesignLocal");
            ns.Imports.Add(new CodeNamespaceImport("System"));
            ns.Imports.Add(new CodeNamespaceImport("System.Collections"));
            ns.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
            ns.Imports.Add(new CodeNamespaceImport("ProtoBuf"));
            unit.Namespaces.Add(ns);

            var enumData = new CodeTypeDeclaration("StringDef")
            {
                IsEnum = true,
                TypeAttributes = TypeAttributes.Public,
            };

            // 출력 순서를 이름순으로 고정 → git diff 안정, 불필요한 리컴파일 방지
            foreach (var info in infos.Values.Where(x => x.IsUse).OrderBy(x => x.LocalName, StringComparer.Ordinal))
            {
                enumData.Members.Add(new CodeMemberField
                {
                    Name = info.LocalName,
                    InitExpression = new CodePrimitiveExpression(info.EnumID),
                });
            }
            ns.Types.Add(enumData);
            var classData = new CodeTypeDeclaration("LocalData")
            {
                IsClass = true,
                TypeAttributes = TypeAttributes.Public,
            };
            classData.CustomAttributes.Add(new CodeAttributeDeclaration("ProtoContract"));

            var dicVar = new CodeMemberField("Dictionary<StringDef,string>", "localString")
            {
                Attributes = MemberAttributes.Public,
                InitExpression = new CodeSnippetExpression("new Dictionary<StringDef,string>()"),
            };
            dicVar.CustomAttributes.Add(new CodeAttributeDeclaration(
                "ProtoMember", new CodeAttributeArgument(new CodePrimitiveExpression(1))));
            classData.Members.Add(dicVar);

            var loadFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "LoadData",
            };
            loadFunc.Parameters.Add(new CodeParameterDeclarationExpression(
                new CodeTypeReference(typeof(byte[])), "buffer"));
            loadFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "System.IO.MemoryStream stream = new System.IO.MemoryStream(buffer);"));
            loadFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "localString = ProtoBuf.Serializer.Deserialize<LocalData>(stream).localString;"));
            classData.Members.Add(loadFunc);

            var insertFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Insert",
                ReturnType = new CodeTypeReference(typeof(bool)),
            };
            insertFunc.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference(typeof(int)), "type"));
            insertFunc.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference(typeof(string)), "lang"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "if (localString.ContainsKey((StringDef)type))"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB4 + "return false;"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "localString.Add((StringDef)type, lang);"));
            insertFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return true;"));
            classData.Members.Add(insertFunc);

            var getFunc = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = "Get",
                ReturnType = new CodeTypeReference(typeof(string)),
            };
            getFunc.Parameters.Add(new CodeParameterDeclarationExpression(new CodeTypeReference("StringDef"), "id"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "string value;"));
            getFunc.Statements.Add(new CodeSnippetStatement(Tab.TAB3 + "return localString.TryGetValue(id, out value) ? value : string.Empty;"));
            classData.Members.Add(getFunc);

            ns.Types.Add(classData);

            var provider = System.CodeDom.Compiler.CodeDomProvider.CreateProvider("CSharp");
            var options = new System.CodeDom.Compiler.CodeGeneratorOptions { BracingStyle = "C" };
            var builder = new StringBuilder();
            using (var sw = new StringWriter(builder))
                provider.GenerateCodeFromCompileUnit(unit, sw, options);

            DllExporter.ExportStringToDll(builder.ToString(), Path.Combine(folderPath, "Dll", "LocalData.dll"));
        }

        public void ExportDataByteFile(string folderPath)
        {
            string dllPath = Path.Combine(folderPath, "Dll", "Design.dll");
            if (!File.Exists(dllPath))
                throw new FileNotFoundException("Design.dll 을 찾을 수 없습니다.", dllPath);

            Assembly dll = Assembly.LoadFrom(dllPath);
            Type sType = dll.GetType("DesignLocal.LocalData");
            if (sType == null)
                throw new TypeLoadException("DesignLocal.LocalData 가 Design.dll 에 없습니다.");

            MethodInfo addMethod = sType.GetMethod("Insert");
            if (addMethod == null)
                throw new MissingMethodException("DesignLocal.LocalData", "Insert");

            string outDir = Path.Combine(folderPath, "Local");
            Directory.CreateDirectory(outDir);

            foreach (LangaugeType type in System.Enum.GetValues(typeof(LangaugeType)))
            {
                object inst = Activator.CreateInstance(sType);

                foreach (var info in infos.Values.Where(x => x.IsUse).OrderBy(x => x.LocalName, StringComparer.Ordinal))
                {
                    string text = type == LangaugeType.Ko ? info.Ko
                                : type == LangaugeType.Jp ? info.Jp
                                : info.En;

                    var ok = addMethod.Invoke(inst, new object[] { info.EnumID, text ?? string.Empty });
                    if (ok is bool && !(bool)ok)
                        Console.WriteLine($"[WARN] {type} / {info.LocalName}: ID {info.EnumID} 가 이미 있습니다.");
                }

                File.WriteAllBytes(Path.Combine(outDir, type + ".bytes"), Serialize(inst));
            }
        }
        private static byte[] Serialize(object localInfos)
        {
            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, localInfos);
                return ms.ToArray();
            }
        }
    }
}
