using Aspose.Cells;
using Newtonsoft.Json.Linq;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace DesignGenerator
{
    internal class Program
    {
        static void Main(string[] args)
        {
            #region TestMode
            //string[] test = new string[4] { "TableGenerate", "D:\\Project\\ProjectT\\DesignTable\\Data", "D:\\Project\\ProjectT\\Client\\Assets", "All" };
            ////string[] test = new string[3] { "Local", "D:\\Project\\ProjectT\\DesignTable\\Data", "D:\\Project\\ProjectT\\Client\\Assets" };
            //////string[] test = new string[3] { "Enum", "C:\\Users\\Oh\\Desktop\\DesignGenerator\\Data\\Data_Export", "D:\\ProjectT\\Client\\Assets" };

            //if (test.Length == 0)
            //{
            //    Console.WriteLine("Args Is Null");
            //}
            //else
            //{
            //    string type = test[0];
            //    switch (type)
            //    {
            //        case "Enum":
            //            {
            //                string createPath = test[1];
            //                string generatePath = test[2];

            //                EnumGenerator enumGenerator = new EnumGenerator();
            //                enumGenerator.Create(createPath, generatePath);
            //            }
            //            break;
            //        case "TableCreate":
            //            {
            //                string createPath = test[1];
            //                string tableName = test[2];

            //                TableCreator tableCreator = new TableCreator();
            //                if (tableName == "null")
            //                    tableCreator.CreateAll(createPath + "\\table명세서.xlsx", createPath + "\\Table\\Tables");
            //                else
            //                    tableCreator.Create(createPath + "\\table명세서.xlsx", createPath + "\\Table\\Tables", tableName);
            //            }

            //            break;

            //        case "TableGenerate":
            //            {
            //                string folderPath = test[1];
            //                string outputPath = test[2];
            //                string genType = test[3];
            //                string tableName = string.Empty;
            //                TableGenerator tableGen = new TableGenerator();
            //                DataMgrGenerator dataMgrGen = new DataMgrGenerator();
            //                DataMgrSerializerGenerator dataMgrSerializerGen = new DataMgrSerializerGenerator();
            //                tableGen.OnMgrSet += dataMgrGen.SetData;
            //                tableGen.OnSerializerSet += dataMgrSerializerGen.SetData;

            //                if (genType == "One")
            //                {
            //                    tableName = test[4];
            //                    tableGen.Load(folderPath, tableName,
            //                           $"{folderPath}\\Table\\Xml", $"{folderPath}\\Table\\Client");

            //                }
            //                else if (genType == "All")
            //                {
            //                    tableGen.LoadAll(folderPath,
            //                         $"{folderPath}\\Table\\Xml", $"{folderPath}\\Table\\Client");
            //                }

            //                dataMgrGen.Create();
            //                dataMgrGen.Save($"{folderPath}\\Table\\Client");
            //                dataMgrSerializerGen.Create();
            //                dataMgrSerializerGen.Save($"{folderPath}\\Table\\Client");

            //                DllExporter.ExportCSToDll($"{folderPath}\\Table\\Client",
            //                    $"{folderPath}\\Dll\\DataMgr.dll");

            //                //DllExporter.MoveDllFile($"{folderPath}\\Dll\\DataMgr.dll", $"{outputPath}\\Automation\\Dll\\DataMgr.dll");
            //                DllExporter.MergeDll($"{folderPath}\\Dll", $"{outputPath}\\Automation\\Dll\\Design.dll");

            //                if (genType == "One")
            //                    tableGen.ExportDataByteFile($"{outputPath}\\Automation\\Dll", $"{outputPath}\\Automation\\Table", tableName);
            //                else if (genType == "All")
            //                    tableGen.ExportAllDataByteFile($"{outputPath}\\Automation\\Dll", $"{outputPath}\\Automation\\Table");


            //            }
            //            break;

            //        case "Local":
            //            {
            //                string folderPath = test[1];
            //                string outputPath = test[2];

            //                LocalizeGenerator localGen = new LocalizeGenerator();
            //                localGen.Create(folderPath, $"{outputPath}\\Automation");
            //                //DllExporter.MoveDllFile($"{folderPath}\\Dll\\LocalData.dll", $"{outputPath}\\Automation\\Dll\\LocalData.dll");
            //                DllExporter.MergeDll($"{folderPath}\\Dll", $"{outputPath}\\Automation\\Dll\\Design.dll");
            //                localGen.ExportDataByteFile($"{outputPath}\\Automation");
            //            }
            //            break;
            //    }
            //}
            #endregion

            #region RunTimeMode
            if (args.Length == 0)
            {
                Console.WriteLine("Args Is Null");
            }
            else
            {
                string type = args[0];
                switch (type)
                {
                    case "Enum":
                        {
                            string createPath = args[1];
                            string generatePath = args[2];

                            EnumGenerator enumGenerator = new EnumGenerator();
                            enumGenerator.Create(createPath, generatePath);
                        }
                        break;
                    case "TableCreate":
                        {
                            string createPath = args[1];
                            string tableName = args[2];

                            TableCreator tableCreator = new TableCreator();
                            if (tableName == "null")
                                tableCreator.CreateAll(createPath + "\\table명세서.xlsx", createPath + "\\Table\\Tables");
                            else
                                tableCreator.Create(createPath + "\\table명세서.xlsx", createPath + "\\Table\\Tables", tableName);
                        }

                        break;

                    case "TableGenerate":
                        {
                            string folderPath = args[1];
                            string outputPath = args[2];
                            string genType = args[3];
                            string tableName = string.Empty;
                            TableGenerator tableGen = new TableGenerator();
                            DataMgrGenerator dataMgrGen = new DataMgrGenerator();
                            DataMgrSerializerGenerator dataMgrSerializerGen = new DataMgrSerializerGenerator();
                            tableGen.OnMgrSet += dataMgrGen.SetData;
                            tableGen.OnSerializerSet += dataMgrSerializerGen.SetData;

                            if (genType == "One")
                            {
                                tableName = args[4];
                                tableGen.Load(folderPath, tableName,
                                       $"{folderPath}\\Table\\Xml", $"{folderPath}\\Table\\Client");

                            }
                            else if (genType == "All")
                            {
                                tableGen.LoadAll(folderPath,
                                     $"{folderPath}\\Table\\Xml", $"{folderPath}\\Table\\Client");
                            }

                            dataMgrGen.Create();
                            dataMgrGen.Save($"{folderPath}\\Table\\Client");
                            dataMgrSerializerGen.Create();
                            dataMgrSerializerGen.Save($"{folderPath}\\Table\\Client");

                            DllExporter.ExportCSToDll($"{folderPath}\\Table\\Client",
                                $"{folderPath}\\Dll\\DataMgr.dll");

                            DllExporter.MergeDll($"{folderPath}\\Dll", $"{outputPath}\\Automation\\Dll\\Design.dll");

                            if (genType == "One")
                                tableGen.ExportDataByteFile($"{outputPath}\\Automation\\Dll", $"{outputPath}\\Automation\\Table", tableName);
                            else if (genType == "All")
                                tableGen.ExportAllDataByteFile($"{outputPath}\\Automation\\Dll", $"{outputPath}\\Automation\\Table");

                        }
                        break;

                    case "Local":
                        {
                            string folderPath = args[1];
                            string outputPath = args[2];


                            LocalizeGenerator localGen = new LocalizeGenerator();
                            localGen.Create(folderPath, $"{outputPath}\\Automation");
                            DllExporter.MergeDll($"{folderPath}\\Dll", $"{outputPath}\\Automation\\Dll\\Design.dll");
                            localGen.ExportDataByteFile($"{outputPath}\\Automation");
                        }
                        break;
                }
            }
            #endregion
        }
    }
}
