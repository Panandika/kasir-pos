using System.Collections.Generic;
using ClosedXML.Excel;

namespace Kasir.Utils
{
    public static class ExcelExporter
    {
        /// <summary>
        /// UI-agnostic export: write rows of primitive values to an .xlsx file.
        /// Callers in UI layers are responsible for obtaining <paramref name="filePath"/>
        /// (e.g. via their own SaveFileDialog) and for displaying success messages.
        /// </summary>
        public static void ExportData(string[] headers, IEnumerable<object[]> rows, string filePath, string sheetName = "Sheet1")
        {
            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add(sheetName);

                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cell(1, i + 1).Value = headers[i];
                    ws.Cell(1, i + 1).Style.Font.Bold = true;
                }

                int row = 2;
                foreach (var dataRow in rows)
                {
                    for (int col = 0; col < dataRow.Length; col++)
                    {
                        ws.Cell(row, col + 1).Value = dataRow[col]?.ToString() ?? "";
                    }
                    row++;
                }

                ws.Columns().AdjustToContents();
                workbook.SaveAs(filePath);
            }
        }
    }
}
