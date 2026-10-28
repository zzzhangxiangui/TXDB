using System;
using System.Data;
using System.IO;
using System.Text;

namespace TXDB.Services
{
    /// <summary>
    /// 将当前结果视图按保存对话框选择的格式导出，保留排序和筛选结果。
    /// </summary>
    internal static class ResultExportHelper
    {
        public const string FileFilter = "CSV 文件 (*.csv)|*.csv|TXT 文本文件 (*.txt)|*.txt";

        public static void Export(DataView view, string fileName, int filterIndex)
        {
            if (view == null)
            {
                return;
            }

            char separator = filterIndex == 2 ? '\t' : ',';
            using (StreamWriter writer = new StreamWriter(fileName, false, new UTF8Encoding(true)))
            {
                for (int index = 0; index < view.Table.Columns.Count; index++)
                {
                    if (index > 0)
                    {
                        writer.Write(separator);
                    }

                    writer.Write(EscapeCell(view.Table.Columns[index].ColumnName, separator));
                }

                writer.WriteLine();
                foreach (DataRowView row in view)
                {
                    for (int index = 0; index < view.Table.Columns.Count; index++)
                    {
                        if (index > 0)
                        {
                            writer.Write(separator);
                        }

                        writer.Write(EscapeCell(Convert.ToString(row[index]), separator));
                    }

                    writer.WriteLine();
                }
            }
        }

        private static string EscapeCell(string value, char separator)
        {
            string text = value ?? string.Empty;
            if (text.IndexOf(separator) >= 0 || text.Contains("\"") || text.Contains("\r") || text.Contains("\n"))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }
    }
}
