using System.Windows.Forms;
using ClosedXML.Excel;

namespace Kasir.Utils
{
    public static class ExcelExporter
    {
        public static void ExportDataGridView(DataGridView dgv, string filePath, string sheetName)
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add(sheetName);

                // Headers
                for (int col = 0; col < dgv.Columns.Count; col++)
                {
                    worksheet.Cell(1, col + 1).Value = dgv.Columns[col].HeaderText;
                    worksheet.Cell(1, col + 1).Style.Font.Bold = true;
                }

                // Data rows
                for (int row = 0; row < dgv.Rows.Count; row++)
                {
                    for (int col = 0; col < dgv.Columns.Count; col++)
                    {
                        object value = dgv.Rows[row].Cells[col].Value;
                        if (value != null)
                        {
                            worksheet.Cell(row + 2, col + 1).Value = value.ToString();
                        }
                    }
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                workbook.SaveAs(filePath);
            }
        }

        public static bool ExportWithDialog(DataGridView dgv, string defaultFileName, string sheetName)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Excel Files (*.xlsx)|*.xlsx";
                dlg.FileName = defaultFileName;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    ExportDataGridView(dgv, dlg.FileName, sheetName);
                    MessageBox.Show("Exported to:\n" + dlg.FileName, "Export Complete");
                    return true;
                }
            }
            return false;
        }
    }
}
