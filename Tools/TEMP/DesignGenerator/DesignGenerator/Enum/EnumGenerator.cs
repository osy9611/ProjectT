using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using DesignGenerator.Core;
using DesignGenerator.Util;

namespace DesignGenerator.Enum
{
    public class EnumGenerator
    {
        public Dictionary<int, EnumInfo> enumInfos = new Dictionary<int, EnumInfo>();

        public void Create(string address, string outputPath)
        {
            var result = EnumPipeline.Run(new EnumGenerateOptions
            {
                DataRoot = address,
                OutputRoot = outputPath,
            });

            Console.Write(result.Diagnostics.ToReport());

            if (!result.Succeeded)
                throw new Exception($"Enum 생성 실패 (오류 {result.Diagnostics.ErrorCount}건)");

            enumInfos = ToLegacy(result.Groups);

            Console.WriteLine(result.FileWritten
                ? $"DesignEnum.cs 생성 ({result.Groups.Count}개 enum)"
                : $"DesignEnum.cs 변경 없음 ({result.Groups.Count}개 enum)");
        }

        //TableGenerator가 값 매핑용으로 부르던 것. 시그니처 유지
        public Dictionary<int, EnumInfo> SetEnumInfo(XmlDocument xml)
        {
            var diag = new DiagnosticBag();
            var groups = EnumReader.Read(new XmlDocumentEnumRecordSource(xml, "enum.xml"), diag);
            EnumValidator.Validate(groups, diag);

            if (diag.Items.Count > 0)
                Console.Write(diag.ToReport());

            if (diag.HasError)
                throw new Exception($"Enum 데이터 오류 {diag.ErrorCount}건 — 위 목록을 확인하세요.");

            return ToLegacy(groups);
        }

        private static Dictionary<int, EnumInfo> ToLegacy(IList<EnumGroup> groups)
        {
            var map = new Dictionary<int, EnumInfo>();
            foreach (var g in groups)
            {
                var info = new EnumInfo(g.TypeName, g.Comment, g.GroupId);
                foreach (var m in g.Members)
                    info.AddInfo(m.Name, m.Value.ToString(CultureInfo.InvariantCulture), m.Comment);
                map[g.GroupId] = info;
            }
            return map;
        }
    }

    //이미 메모리에 올라온 XmlDocument를 리더에 물려주기 위한 어뎁터
    internal sealed class XmlDocumentEnumRecordSource : IEnumRecordSource
    {
        private readonly XmlDocument doc;
        private readonly string name;

        public XmlDocumentEnumRecordSource(XmlDocument doc, string name)
        {
            this.doc = doc;
            this.name = name;
        }

        public string SourceName { get { return name; } }

        public IEnumerable<IDictionary<string, string>> ReadRecords()
        {
            if (doc == null || doc.DocumentElement == null)
                yield break;

            XmlNodeList nodes = doc.SelectNodes(doc.DocumentElement.Name + "/record");
            if (nodes == null)
                yield break;

            foreach (XmlNode node in nodes)
            {
                if (!node.HasChildNodes)
                    continue;

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (XmlNode c = node.FirstChild; c != null; c = c.NextSibling)
                    row[c.Name] = c.InnerText;

                yield return row;
            }
        }
    }
}
