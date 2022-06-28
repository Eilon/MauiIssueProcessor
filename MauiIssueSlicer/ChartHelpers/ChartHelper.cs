using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MauiIssueSlicer
{
    public static class ExcelHelperExtensions
    {
        public static void AddHeaders(this Worksheet worksheet, IEnumerable<ColumnInfo> columns)
        {
            if (worksheet is null)
            {
                throw new ArgumentNullException(nameof(worksheet));
            }
            if (columns is null)
            {
                throw new ArgumentNullException(nameof(columns));
            }
            if (!columns.Any())
            {
                throw new ArgumentException("At least one column must be specified.", nameof(columns));
            }

            var columnIndex = 1;
            foreach (var column in columns)
            { 
                worksheet.Cells[1, columnIndex].Value = column.Name;
                SetHeaderStyle(worksheet.Cells[1, columnIndex]);

                if (column.Width != null)
                {
                    var columnChar = (char)('A' + columnIndex - 1);
                    worksheet.Columns[$"{columnChar}:{columnChar}"].ColumnWidth = column.Width.Value;
                }

                columnIndex++;
            }
        }

        private static void SetHeaderStyle(Microsoft.Office.Interop.Excel.Range headerCell)
        {
            headerCell.Font.Bold = true;
        }
    }

    public class ColumnInfo
    {
        public ColumnInfo(string name, int? width)
        {
            Name = name;
            Width = width;
        }

        public string Name { get; set; }
        public int? Width { get; set; }
    }
}
