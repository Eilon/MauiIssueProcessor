using MauiIssueSlicer;
using Microsoft.Office.Interop.Excel;
using System.Globalization;

if (args.Length != 1)
{
    Console.WriteLine($"Usage: dotnet run MauiIssueSlicer c:\\path\\to\\issues.csv");
    return 1;
}

var inputDataPath = args[0];
var csvRows = File.ReadAllLines(inputDataPath);

var outputRoot = Path.GetDirectoryName(Path.GetFullPath(inputDataPath));
var inputFilenameBase = Path.GetFileNameWithoutExtension(inputDataPath);

Console.WriteLine($"Loaded {csvRows.Length - 1} rows of data");

var columnNames = ParseCsvRow(csvRows[0]);
var columnNamesByIndex = columnNames.Zip(Enumerable.Range(0, columnNames.Length));

var numberColumnIndex = GetColumnIndex(columnNamesByIndex, "Number");
var titleColumnIndex = GetColumnIndex(columnNamesByIndex, "Title");
var createdAtColumnIndex = GetColumnIndex(columnNamesByIndex, "CreatedAt");
var closedAtColumnIndex = GetColumnIndex(columnNamesByIndex, "ClosedAt");
var milestoneNameColumnIndex = GetColumnIndex(columnNamesByIndex, "MilestoneName");
var isOpenColumnIndex = GetColumnIndex(columnNamesByIndex, "IsOpen");
var primaryAreaColumnIndex = GetColumnIndex(columnNamesByIndex, "PrimaryArea");
var isBugColumnIndex = GetColumnIndex(columnNamesByIndex, "IsBug");
var labelsColumnIndex = GetColumnIndex(columnNamesByIndex, "Labels");

var strongIssueRows = new List<IssueRow>();

foreach (var csvIssueRowText in csvRows.Skip(1)) // skip the header row
{
    var csvIssueRow = ParseCsvRow(csvIssueRowText);

    var strongIssueRow = new IssueRow()
    {
        Number = Int32.Parse(csvIssueRow[numberColumnIndex]),
        Title = csvIssueRow[titleColumnIndex],
        CreatedAt = DateTimeOffset.Parse(csvIssueRow[createdAtColumnIndex]),
        ClosedAt = csvIssueRow[closedAtColumnIndex].Length > 0 ? DateTimeOffset.Parse(csvIssueRow[closedAtColumnIndex]) : null,
        MilestoneName = csvIssueRow[milestoneNameColumnIndex],
        IsOpen = bool.Parse(csvIssueRow[isOpenColumnIndex]),
        PrimaryArea = csvIssueRow[primaryAreaColumnIndex],
        IsBug = bool.Parse(csvIssueRow[isBugColumnIndex]),
        Labels = csvIssueRow[labelsColumnIndex].Split('|'),
    };

    strongIssueRows.Add(strongIssueRow);
}

var allMilestones = strongIssueRows.Select(i => i.MilestoneName).Distinct().ToList();

// PART 1: Get open/closed count for each week

//var startDate = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
var startDate = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);
//var startDate = strongIssueRows.Min(x => x.CreatedAt).Date; // Start at oldest issue


var daysSinceStartDate = DateTimeOffset.Now - startDate;
var weeks = (int)Math.Ceiling(daysSinceStartDate.TotalDays / 7d);

var weekSpan = new TimeSpan(days: 7, hours: 0, minutes: 0, seconds: 0);

var openClosedByWeek = new List<OpenClosedItem>();

for (int i = 0; i < weeks; i++)
{
    var fromDate = (startDate + i * weekSpan).Date;
    var toDate = (fromDate + weekSpan).Date;

    var issuesOpenedInRange = strongIssueRows.Count(i => i.CreatedAt.Date >= fromDate && i.CreatedAt.Date < toDate);
    var issuesClosedInRange = strongIssueRows.Count(i => i.ClosedAt.HasValue && i.ClosedAt.Value.Date >= fromDate && i.ClosedAt.Value.Date < toDate);

    openClosedByWeek.Add(new OpenClosedItem { Week = fromDate, IssuesOpened = issuesOpenedInRange, IssuesClosed = issuesClosedInRange });
}


// PART 2: Calculate how many BUGS are in GA/Future/Untriaged/Unknown

var openBugs = strongIssueRows.Where(i => i.IsOpen && i.IsBug).ToList();

var untriagedIssueCount = openBugs.Count(i => string.IsNullOrEmpty(i.MilestoneName));

Console.WriteLine($"Total issues: {strongIssueRows.Count}");
Console.WriteLine($"Open BUG issues: {openBugs.Count}");
Console.WriteLine($"Untriaged BUG issues: {untriagedIssueCount}");


// PART 3: Breakdown BUG issues per area in GA milestones and untriaged/unknown

var openIssuesGroupedByArea =
    openBugs
        .GroupBy(i => i.PrimaryArea)
        .ToList();

var issuesByAreaToTriage = new List<AreaTriageSummary>();

for (int i = 0; i < openIssuesGroupedByArea.Count; i++)
{
    var areaGroup = openIssuesGroupedByArea[i];

    issuesByAreaToTriage.Add(
        new AreaTriageSummary
        {
            Area = areaGroup.Key,
            //IssuesForGA = areaGroup.Count(i => mauiGAMilestones.Contains(i.MilestoneName, StringComparer.OrdinalIgnoreCase)),
            IssuesUntriaged = areaGroup.Count(i => string.IsNullOrEmpty(i.MilestoneName)),
        });
}

issuesByAreaToTriage = issuesByAreaToTriage
    .OrderByDescending(a => a.IssuesUntriaged)
    .ThenBy(a => a.Area)
    .ToList();

if (issuesByAreaToTriage.SingleOrDefault(a => string.IsNullOrEmpty(a.Area)) is { } and var emptyArea)
{
    emptyArea.Area = "(no area)";
}


// GENERATE EXCEL OUTPUT

Console.WriteLine("Starting Excel...");
var excelApp = new Microsoft.Office.Interop.Excel.Application();
try
{
    Console.WriteLine("Creating Excel workbook...");
    var excelWorkbook = excelApp.Workbooks.Add();

    Console.WriteLine("Creating Excel worksheet for opened/closed...");
    Worksheet openedClosedWorksheet = excelWorkbook.Sheets.Add();
    openedClosedWorksheet.Name = "OpenedClosedByWeek";
    openedClosedWorksheet.AddHeaders(new[] { new ColumnInfo("WeekStart", 20), new ColumnInfo("Opened", 10), new ColumnInfo("Closed", 10) });

    for (int i = 0; i < openClosedByWeek.Count; i++)
    {
        openedClosedWorksheet.Cells[i + 2, 1].Value = openClosedByWeek[i].Week.ToShortDateString();
        openedClosedWorksheet.Cells[i + 2, 2].Value = openClosedByWeek[i].IssuesOpened.ToString(CultureInfo.InvariantCulture);
        openedClosedWorksheet.Cells[i + 2, 3].Value = openClosedByWeek[i].IssuesClosed.ToString(CultureInfo.InvariantCulture);
    }

    var openClosedChartSourceRange = openedClosedWorksheet.Range[Cell1: "A1", Cell2: "C" + (openClosedByWeek.Count + 1).ToString(CultureInfo.InvariantCulture)];

    ChartObject openedClosedChartObject = openedClosedWorksheet.ChartObjects().Add(300, 40, 800, 400);
    var openedClosedChart = openedClosedChartObject.Chart;
    openedClosedChart.ChartType = XlChartType.xlLineMarkers;
    openedClosedChart.HasTitle = true;
    openedClosedChart.ChartTitle.Text = "Issues Opened and Closed By Week";

    var allOpenedClosedSeries = openedClosedChart.SeriesCollection();
    allOpenedClosedSeries.Add(openClosedChartSourceRange);

    Series openedSeries = allOpenedClosedSeries[1];
    openedSeries.Format.Line.BackColor.RGB = 0xED_7D_31; // blue-ish
    openedSeries.Format.Line.Weight = 2f;

    Trendlines openedSeriesTrendlines = openedSeries.Trendlines();
    var openedLinearTrendline = openedSeriesTrendlines.Add(XlTrendlineType.xlLinear);
    openedLinearTrendline.Format.Line.ForeColor.RGB = 0xED_7D_31; // blue-ish
    openedLinearTrendline.Format.Line.Weight = 3f;
    openedLinearTrendline.Format.Line.DashStyle = Microsoft.Office.Core.MsoLineDashStyle.msoLineDash;

    Series closedSeries = allOpenedClosedSeries[2];
    closedSeries.Format.Line.BackColor.RGB = 0x44_72_C4; // orange-ish
    closedSeries.Format.Line.Weight = 2f;



    Console.WriteLine("Creating Excel worksheet for area triage...");
    Worksheet areaTriageWorksheet = excelWorkbook.Sheets.Add(After: openedClosedWorksheet);
    areaTriageWorksheet.Name = "AreaTriage";

    var areaTriageColumns = new List<ColumnInfo> {
        new ColumnInfo("Area", 30),
        new ColumnInfo("Untriaged", 15),
    };

    var mauiMilestonesWithAtLeastOneIssue =
        allMilestones
            .Where(m => !string.IsNullOrEmpty(m) && openBugs.Any(b => b.MilestoneName == m))
            .OrderBy(m => m.ToLowerInvariant())
            .ToList();

    for (var i = 0; i < mauiMilestonesWithAtLeastOneIssue.Count; i++)
    {
        areaTriageColumns.Add(new ColumnInfo(mauiMilestonesWithAtLeastOneIssue[i], 20));
    }

    areaTriageWorksheet.AddHeaders(areaTriageColumns);

    for (int i = 0; i < issuesByAreaToTriage.Count; i++)
    {
        areaTriageWorksheet.Cells[i + 2, 1].Value = issuesByAreaToTriage[i].Area;
        areaTriageWorksheet.Cells[i + 2, 2].Formula = $"=HYPERLINK(\"https://github.com/dotnet/maui/issues?q=is%3Aopen+is%3Aissue+no:milestone+label:t/bug+label%3A%22{issuesByAreaToTriage[i].Area}%22\", \"{issuesByAreaToTriage[i].IssuesUntriaged.ToString(CultureInfo.InvariantCulture)}\")";

        SetCellColorByValue(areaTriageWorksheet.Cells[i + 2, 2], issuesByAreaToTriage[i].IssuesUntriaged);

        for (var milestoneIndex = 0; milestoneIndex < mauiMilestonesWithAtLeastOneIssue.Count; milestoneIndex++)
        {
            var milestoneName = mauiMilestonesWithAtLeastOneIssue[milestoneIndex];
            var bugsInAreaInMilestone = openIssuesGroupedByArea.SingleOrDefault(a => string.Equals(a.Key, issuesByAreaToTriage[i].Area, StringComparison.OrdinalIgnoreCase))?.Count(b => string.Equals(b.MilestoneName, milestoneName, StringComparison.OrdinalIgnoreCase)) ?? 0;

            areaTriageWorksheet.Cells[i + 2, 3 + milestoneIndex].Formula = $"=HYPERLINK(\"https://github.com/dotnet/maui/issues?q=is%3Aopen+is%3Aissue+milestone%3A%22{milestoneName}%22+label:t/bug+label%3A%22{issuesByAreaToTriage[i].Area}%22\", \"{bugsInAreaInMilestone.ToString(CultureInfo.InvariantCulture)}\")";

            SetCellColorByValue(areaTriageWorksheet.Cells[i + 2, 3 + milestoneIndex], bugsInAreaInMilestone);
        }
    }

    void SetCellColorByValue(Microsoft.Office.Interop.Excel.Range cell, int value)
    {
        if (value >= 10)
        {
            cell.Interior.Color = 0x00_00_ff; // BGR: red
        }
        else if (value >= 1)
        {
            cell.Interior.Color = 0x00_ff_ff; // BGR: yellow
        }
    }


    Console.WriteLine("Creating Excel worksheet for issues created by category...");
    var categoryLabels = new[] { "t/bug", "t/enhancement ☀️", "proposal/open" };


    var startDateForPerCategory = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);


    var daysSinceStartDatePerCategory = DateTimeOffset.Now - startDateForPerCategory;
    var weeksForCategories = (int)Math.Ceiling(daysSinceStartDatePerCategory.TotalDays / 7d);


    var issuesPerCategoryWeek = new List<(DateTime week, Dictionary<string, int> issuesPerCategory)>();

    for (int i = 0; i < weeksForCategories; i++)
    {
        var fromDate = (startDateForPerCategory + i * weekSpan).Date;
        var toDate = (fromDate + weekSpan).Date;

        var issuesOpenedInRange = strongIssueRows.Where(i => i.CreatedAt.Date >= fromDate && i.CreatedAt.Date < toDate);

        var issuesPerCategoryInThisWeek = new Dictionary<string, int>();
        foreach (var issueCategoryLabel in categoryLabels)
        {
            issuesPerCategoryInThisWeek.Add(issueCategoryLabel, issuesOpenedInRange.Count(i => i.Labels.Any(l => string.Equals(l, issueCategoryLabel, StringComparison.OrdinalIgnoreCase))));
        }

        issuesPerCategoryInThisWeek.Add(
            "(none)",
            issuesOpenedInRange
                .Count(i => i
                    .Labels
                    .All(l =>
                        !categoryLabels.Contains(l, StringComparer.OrdinalIgnoreCase))));

        issuesPerCategoryWeek.Add((week: fromDate, issuesPerCategory: issuesPerCategoryInThisWeek));
    }



    Worksheet createdByIssueCategory = excelWorkbook.Sheets.Add(After: areaTriageWorksheet);
    createdByIssueCategory.Name = "CreatedByIssueCategory";
    var createdByIssueCategoryColumns = new List<ColumnInfo> {
        new ColumnInfo("WeekStart", 20),
    };

    var effectiveCategoryLabels = categoryLabels.Concat(new[] { "(none)" }).ToList();

    for (int i = 0; i < effectiveCategoryLabels.Count; i++)
    {
        createdByIssueCategoryColumns.Add(new ColumnInfo(effectiveCategoryLabels[i], 20));
    }

    createdByIssueCategory.AddHeaders(createdByIssueCategoryColumns);


    for (int i = 0; i < issuesPerCategoryWeek.Count; i++)
    {
        createdByIssueCategory.Cells[2 + i, 1].Value = issuesPerCategoryWeek[i].week.ToShortDateString();

        for (int j = 0; j < effectiveCategoryLabels.Count; j++)
        {
            createdByIssueCategory.Cells[2 + i, 2 + j].Value = issuesPerCategoryWeek[i].issuesPerCategory[effectiveCategoryLabels[j]].ToString(CultureInfo.InvariantCulture);
        }
    }

    var createdByIssueCategoryChartSourceRange = createdByIssueCategory.Range[Cell1: "A1", Cell2: ((char)('A' + effectiveCategoryLabels.Count)) + (issuesPerCategoryWeek.Count + 1).ToString(CultureInfo.InvariantCulture)];

    ChartObject createdByIssueCategoryChartObject = createdByIssueCategory.ChartObjects().Add(400, 40, 800, 400);
    var createdByIssueCategoryChart = createdByIssueCategoryChartObject.Chart;
    createdByIssueCategoryChart.ChartType = XlChartType.xlLineMarkers;
    createdByIssueCategoryChart.HasTitle = true;
    createdByIssueCategoryChart.ChartTitle.Text = "Issues Per Category Opened By Week";

    var createdByIssueCategorySeries = createdByIssueCategoryChart.SeriesCollection();
    createdByIssueCategorySeries.Add(createdByIssueCategoryChartSourceRange);

    //Series openedSeries = allOpenedClosedSeries[1];
    //openedSeries.Format.Line.BackColor.RGB = 0xED_7D_31; // blue-ish
    //openedSeries.Format.Line.Weight = 2f;

    //Series closedSeries = allOpenedClosedSeries[2];
    //closedSeries.Format.Line.BackColor.RGB = 0x44_72_C4; // orange-ish
    //closedSeries.Format.Line.Weight = 2f;






    // Delete generic "Sheet1", "Sheet2", etc. that get created in new workbooks (can't delete them early because then you get an error saying there has to be at least 1 active sheet)
    for (int i = 2; i < excelWorkbook.Sheets.Count + 1; i++)
    {
        excelWorkbook.Sheets.Item[i].Delete();
    }


    openedClosedWorksheet.Activate();

    var excelOutput = Path.Combine(outputRoot!, inputFilenameBase, "triage-summary.xlsx");
    if (File.Exists(excelOutput))
    {
        File.Delete(excelOutput);
    }
    Directory.CreateDirectory(Path.GetDirectoryName(excelOutput)!);
    Console.WriteLine($"Saving Excel file to: {excelOutput}");
    excelWorkbook.SaveAs2(Filename: excelOutput);
    excelWorkbook.Close();
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR: Exception during Excel file creation!");
    Console.WriteLine(ex.ToString());
}
finally
{
    Console.WriteLine("Shutting down Excel...");
    excelApp.Quit();
}

Console.WriteLine("Done");

return 0;

static string[] ParseCsvRow(string csvRow)
{
    var parts = new List<string?>();

    var index = 0;

    while (index < csvRow.Length)
    {
        if (csvRow[index] == ',')
        {
            parts.Add(null);
        }
        else if (csvRow[index] == '"')
        {
            var nextQuoteIndex = csvRow.IndexOf('"', index + 1);
            parts.Add(csvRow.Substring(index + 1, nextQuoteIndex - index - 1));
            index = nextQuoteIndex + 1; // skip the close quote
        }
        else
        {
            var nextCommaIndex = csvRow.IndexOf(',', index + 1);
            if (nextCommaIndex == -1)
            {
                nextCommaIndex = csvRow.Length;
            }
            parts.Add(csvRow.Substring(index, nextCommaIndex - index));
            index = nextCommaIndex;
        }

        index++; // skip the comma
    }

    return parts.ToArray()!;
}

int GetColumnIndex(IEnumerable<(string First, int Second)> columnNamesByIndex, string columnName)
{
    return columnNamesByIndex.Single(c => c.First == columnName).Second;
}

class OpenClosedItem
{
    public DateTime Week { get; set; }
    public int IssuesOpened { get; set; }
    public int IssuesClosed { get; set; }
}

class AreaTriageSummary
{
    public string? Area { get; set; }
    public int IssuesForGA { get; set; }
    public int IssuesUntriaged { get; set; }
}

class IssueRow
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public long Number;
    public string Title;
    public DateTimeOffset CreatedAt;
    public DateTimeOffset? ClosedAt;
    public string MilestoneName;
    public bool IsOpen;
    public string PrimaryArea;
    public bool IsBug;
    public string[] Labels;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
}
