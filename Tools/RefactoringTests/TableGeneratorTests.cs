using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml;
using DesignGenerator.Table;
using DesignGenerator.Util;
using ProtoBuf;

internal static class TableGeneratorTests
{
    private static int checks;

    private static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
                Console.Error.WriteLine(current.GetType().FullName + ": " + current.Message);
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        string input = args[0];
        string output = args[1];
        string sources = Path.Combine(output, "generated");
        string bytes = Path.Combine(output, "bytes");
        Directory.CreateDirectory(sources);
        var generator = new TableGenerator();
        Call(generator, "LoadEnums", input);
        foreach (string path in Directory.GetFiles(Path.Combine(input, "Table", "Xml"), "*.xml").OrderBy(x => x, StringComparer.Ordinal))
        {
            var xml = new XmlDocument();
            xml.Load(path);
            Call(generator, "GetTableInfo", xml, Path.GetFileName(path));
        }
        Check(!generator.Diagnostics.HasError, generator.Diagnostics.ToReport());
        int realTables = generator.Tables.Count;
        AddFixtures(generator);
        VerifyNestedKeyGeneration(generator, output);
        foreach (var table in generator.Tables.Values.OrderBy(x => x.TableID))
            Call(generator, "GenerateInfoData", table, sources);

        var timestamps = Directory.GetFiles(sources, "*.cs").ToDictionary(x => x, File.GetLastWriteTimeUtc);
        foreach (var table in generator.Tables.Values.OrderBy(x => x.TableID))
            Call(generator, "GenerateInfoData", table, sources);
        Check(timestamps.All(x => File.GetLastWriteTimeUtc(x.Key) == x.Value), "unchanged generation must not rewrite files");

        DllExporter.ExportCSToDll(sources, Path.Combine(output, "DataMgr.dll"));
        generator.ExportAllDataByteFile(output, bytes);
        Assembly assembly = DllExporter.LoadGeneratedAssembly(output, "DataMgr.dll");
        VerifyRuntime(assembly);
        VerifyRoundTrips(generator, assembly, bytes);
        if (args.Length > 2)
            VerifyRoundTrips(generator, assembly, args[2]);
        Console.WriteLine("PASS: {0} assertions; {1} repository tables + {2} synthetic tables; generated code compiled; protobuf round trips{3}",
            checks, realTables, generator.Tables.Count - realTables, args.Length > 2 ? " including peer output" : "");
    }

    private static void AddFixtures(TableGenerator generator)
    {
        var target = NewTable(90001, "A3Target", new[] { "Id", "Text" }, new[] { "int", "string" }, 1);
        target.VarObjectData.Add(new object[] { 1, "target" });
        var text = NewTable(90002, "A3String", new[] { "Id", "Value" }, new[] { "string", "int" }, 1);
        text.VarObjectData.Add(new object[] { "key", 12 });
        var list = NewTable(90003, "A3List", new[] { "Group", "Category", "Id", "Value" }, new[] { "string", "int", "int", "string" }, 3);
        list.UseListRule = true;
        list.AddFindPKListRule("Group");
        list.AddFindPKListRule("Category");
        list.VarObjectData.Add(new object[] { "group", 2, 1, "first" });
        list.VarObjectData.Add(new object[] { "group", 2, 2, "second" });
        list.VarObjectData.Add(new object[] { "group", 3, 1, "other" });
        var refs = NewTable(90005, "A3Refs", new[] { "Id", "Target" }, new[] { "int", "int" }, 1);
        refs.VarData[1].RefTable = "90001";
        refs.VarObjectData.Add(new object[] { 1, 1 });
        refs.VarObjectData.Add(new object[] { 2, -1 });
        var emptyList = NewTable(90006, "A3EmptyList", new[] { "Id" }, new[] { "int" }, 1);
        emptyList.UseListRule = true;
        emptyList.VarObjectData.Add(new object[] { 8 });
        // Exercise the generator's existing zero-PK branch without claiming this is valid input for the schema validator.
        var noKey = NewTable(90007, "A3NoKey", new[] { "Value" }, new[] { "int" }, 0);
        noKey.VarObjectData.Add(new object[] { 7 });
        var tables = (Dictionary<int, TableDataInfo>)generator.Tables;
        foreach (var table in new[] { target, text, list, refs, emptyList, noKey })
            tables.Add(table.TableID, table);
    }

    private static void VerifyNestedKeyGeneration(TableGenerator generator, string output)
    {
        VerifyWideKeyGeneration(generator, output, 7);
        VerifyWideKeyGeneration(generator, output, 9);
    }

    private static void VerifyWideKeyGeneration(TableGenerator generator, string output, int count)
    {
        string folder = Path.Combine(output, "key-repro-" + count);
        Directory.CreateDirectory(folder);
        var nested = NewTable(90008, "A3Wide" + count, Enumerable.Range(1, count).Select(x => "Key" + x).ToArray(), Enumerable.Repeat("int", count).ToArray(), count);
        Call(generator, "GenerateInfoData", nested, folder);
        // The baseline emits an invalid nested generic field. Keep this reproduction separate from runnable fixtures.
        try
        {
            DllExporter.ExportCSToDll(folder, Path.Combine(folder, "Nested.dll"));
        }
        catch (Exception exception)
        {
            Check(exception.Message.Contains("CS1003") && exception.Message.Contains("CS0305"), "known nested generic compilation failure");
            File.WriteAllText(Path.Combine(folder, "diagnostic.txt"), exception.Message);
            Console.WriteLine("KNOWN BASELINE DEFECT: {0}-column PK produces invalid generic field (CS1003/CS0305).", count);
            return;
        }
        throw new Exception("Nested key reproduction changed: update the baseline defect expectation after a separate fix.");
    }

    private static TableDataInfo NewTable(int id, string name, string[] columns, string[] types, int keyCount)
    {
        var table = new TableDataInfo { TableID = id, TableName = name };
        for (int i = 0; i < columns.Length; i++)
        {
            table.AddVarData(columns[i], types[i], -1, "", (i + 1) * 3);
            if (i < keyCount)
                table.AddPKData(columns[i], types[i], -1);
        }
        return table;
    }

    private static void VerifyRuntime(Assembly assembly)
    {
        object target = NewInfos(assembly, "A3Target");
        Check((bool)Call(target, "Insert", 1, "first"), "single PK insert");
        object first = Call(target, "Get", 1);
        Check(!(bool)Call(target, "Insert", 1, "duplicate") && Rows(target).Count == 1 && ReferenceEquals(first, Call(target, "Get", 1)), "duplicate Insert must return false without mutation");
        Check(Call(target, "Get", 99) == null, "missing PK returns null");
        object oldIndex = Field(target, "datas");
        Rows(target).Add(first);
        Throws<InvalidOperationException>(() => Call(target, "Initialize"), "duplicate Initialize");
        Check(ReferenceEquals(oldIndex, Field(target, "datas")), "failed Initialize preserves index");
        Rows(target).RemoveAt(1);
        Call(target, "Initialize");
        Check(!ReferenceEquals(oldIndex, Field(target, "datas")) && ReferenceEquals(first, Call(target, "Get", 1)), "successful Initialize rebuilds index with same objects");

        object strings = NewInfos(assembly, "A3String");
        Throws<ArgumentNullException>(() => Call(strings, "Insert", null, 1), "null string Insert");
        Throws<ArgumentNullException>(() => Call(strings, "Get", new object[] { null }), "null string Get");
        Call(strings, "Insert", "text", 3);
        object textRow = Rows(strings)[0];
        textRow.GetType().GetField("Id").SetValue(textRow, null);
        oldIndex = Field(strings, "datas");
        Throws<InvalidOperationException>(() => Call(strings, "Initialize"), "null string Initialize");
        Check(ReferenceEquals(oldIndex, Field(strings, "datas")), "null Initialize preserves index");

        object lists = NewInfos(assembly, "A3List");
        Call(lists, "Insert", "group", 2, 1, "first");
        Call(lists, "Insert", "group", 2, 2, "second");
        Call(lists, "Insert", "group", 3, 1, "other");
        IList group = (IList)Call(lists, "GetListById", "group", 2);
        Check(group.Count == 2 && ReferenceEquals(group[0], Call(lists, "Get", "group", 2, 1)) && ReferenceEquals(group[1], Call(lists, "Get", "group", 2, 2)), "composite PK/list key and insertion order");
        Check(Call(lists, "GetListById", "missing", 2) == null, "missing list returns null");
        Throws<ArgumentNullException>(() => Call(lists, "GetListById", null, 2), "null list key");
        object oldLists = Field(lists, "listData");
        oldIndex = Field(lists, "datas");
        Check(!(bool)Call(lists, "Insert", "group", 2, 1, "duplicate") && group.Count == 2 && Rows(lists).Count == 3, "duplicate Insert preserves list");
        Rows(lists).Add(Rows(lists)[0]);
        Throws<InvalidOperationException>(() => Call(lists, "Initialize"), "list duplicate Initialize");
        Check(ReferenceEquals(oldLists, Field(lists, "listData")) && ReferenceEquals(oldIndex, Field(lists, "datas")), "failed Initialize preserves both indexes");
        Rows(lists).RemoveAt(3);
        Call(lists, "Initialize");
        group = (IList)Call(lists, "GetListById", "group", 2);
        Check(!ReferenceEquals(oldLists, Field(lists, "listData")) && group.Count == 2 && ReferenceEquals(group[1], Rows(lists)[1]), "Initialize rebuilds list in row order");

        object refs = NewInfos(assembly, "A3Refs");
        Call(refs, "Insert", 1, 1);
        Call(refs, "Insert", 2, -1);
        byte[] beforeRefs = Serialize(refs);
        Call(refs, "SetupRef_Target", target);
        Check(ReferenceEquals(Field(Rows(refs)[0], "Target_ref"), first) && Field(Rows(refs)[1], "Target_ref") == null, "reference link and -1 sentinel");
        Check(beforeRefs.SequenceEqual(Serialize(refs)), "reference fields are not serialized");
        Call(refs, "Insert", 3, 99);
        Throws<InvalidOperationException>(() => Call(refs, "SetupRef_Target", target), "missing reference");

        object empty = NewInfos(assembly, "A3EmptyList");
        Check(empty.GetType().GetField("listData") == null && empty.GetType().GetMethod("GetListById") == null, "list rule without selected keys emits no index");
        object noKey = NewInfos(assembly, "A3NoKey");
        Check((bool)Call(noKey, "Insert", 7) && (bool)Call(noKey, "Insert", 7) && Rows(noKey).Count == 2, "zero PK Insert keeps existing append behavior");
        Throws<InvalidOperationException>(() => Call(noKey, "Initialize"), "zero PK duplicate default key");
        var infoType = assembly.GetType("DesignTable.A3TargetInfo", true);
        Check(infoType.GetField("Id").GetCustomAttribute<ProtoMemberAttribute>().Tag == 3 && infoType.GetField("Text").GetCustomAttribute<ProtoMemberAttribute>().Tag == 6, "explicit non-positional ProtoMember tags");
        Check(target.GetType().GetField("dataInfo").GetCustomAttribute<ProtoMemberAttribute>().Tag == 1, "Infos ProtoMember tag");
        object constructed = Activator.CreateInstance(infoType, 5, "constructor");
        Check((int)Field(constructed, "Id") == 5 && (string)Field(constructed, "Text") == "constructor", "full constructor parameter order");
    }

    private static void VerifyRoundTrips(TableGenerator generator, Assembly assembly, string folder)
    {
        foreach (var table in generator.Tables.Values.OrderBy(x => x.TableID))
        {
            byte[] original = File.ReadAllBytes(Path.Combine(folder, table.TableName + ".bytes"));
            object instance;
            using (var stream = new MemoryStream(original))
                instance = Serializer.NonGeneric.Deserialize(assembly.GetType("DesignTable." + table.TableName + "Infos", true), stream);
            Call(instance, "Initialize");
            Check(Rows(instance).Count == table.VarObjectData.Count && original.SequenceEqual(Serialize(instance)), table.TableName + " protobuf round trip");
            foreach (object row in Rows(instance))
            {
                object[] key = table.PKData.Select(x => Field(row, x.ColumnName)).ToArray();
                Check(ReferenceEquals(Call(instance, "Get", key), row), table.TableName + " lookup after deserialize");
            }
        }
    }

    private static byte[] Serialize(object value)
    {
        using (var stream = new MemoryStream())
        {
            Serializer.NonGeneric.Serialize(stream, value);
            return stream.ToArray();
        }
    }

    private static object NewInfos(Assembly assembly, string name)
    {
        return Activator.CreateInstance(assembly.GetType("DesignTable." + name + "Infos", true));
    }

    private static object Field(object instance, string name)
    {
        return instance.GetType().GetField(name).GetValue(instance);
    }

    private static IList Rows(object instance)
    {
        return (IList)Field(instance, "dataInfo");
    }

    private static object Call(object instance, string name, params object[] args)
    {
        try
        {
            return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(instance, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            Check(true, message);
            return;
        }
        throw new Exception("Expected " + typeof(T).Name + ": " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        checks++;
    }
}
