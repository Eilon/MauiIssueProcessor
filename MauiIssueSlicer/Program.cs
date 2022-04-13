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
    };

    strongIssueRows.Add(strongIssueRow);
}


var mauiGAMilestones =
    strongIssueRows
        .Select(i => i.MilestoneName)
        .Distinct()
        .Where(m => m.StartsWith("6.0", StringComparison.OrdinalIgnoreCase) && !m.Contains("servicing", StringComparison.OrdinalIgnoreCase))
        .ToList();

var mauiFutureMilestones = new List<string> { ".NET 7", "Future" };


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

var gaIssueCount = openBugs.Count(i => mauiGAMilestones.Contains(i.MilestoneName, StringComparer.OrdinalIgnoreCase));
var futureIssueCount = openBugs.Count(i => mauiFutureMilestones.Contains(i.MilestoneName, StringComparer.OrdinalIgnoreCase));
var untriagedIssueCount = openBugs.Count(i => string.IsNullOrEmpty(i.MilestoneName));
var unknownIssueCount = openBugs.Count - gaIssueCount - futureIssueCount - untriagedIssueCount;

Console.WriteLine($"Total issues: {strongIssueRows.Count}");
Console.WriteLine($"Open BUG issues: {openBugs.Count}");
Console.WriteLine($"GA BUG issues: {gaIssueCount}");
Console.WriteLine($"Future BUG issues: {futureIssueCount}");
Console.WriteLine($"Untriaged BUG issues: {untriagedIssueCount}");
Console.WriteLine($"Unknown BUG issues: {unknownIssueCount}");


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
            IssuesForGA = areaGroup.Count(i => mauiGAMilestones.Contains(i.MilestoneName, StringComparer.OrdinalIgnoreCase)),
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
    openedClosedWorksheet.Cells[1, 1].Value = "WeekStart";
    openedClosedWorksheet.Cells[1, 2].Value = "Opened";
    openedClosedWorksheet.Cells[1, 3].Value = "Closed";
    SetHeaderStyle(openedClosedWorksheet.Cells[1, 1]);
    SetHeaderStyle(openedClosedWorksheet.Cells[1, 2]);
    SetHeaderStyle(openedClosedWorksheet.Cells[1, 3]);

    for (int i = 0; i < openClosedByWeek.Count; i++)
    {
        openedClosedWorksheet.Cells[i + 2, 1].Value = openClosedByWeek[i].Week.ToShortDateString();
        openedClosedWorksheet.Cells[i + 2, 2].Value = openClosedByWeek[i].IssuesOpened.ToString(CultureInfo.InvariantCulture);
        openedClosedWorksheet.Cells[i + 2, 3].Value = openClosedByWeek[i].IssuesClosed.ToString(CultureInfo.InvariantCulture);
    }

    openedClosedWorksheet.Columns["A:A"].ColumnWidth = 20;
    openedClosedWorksheet.Columns["B:B"].ColumnWidth = 10;
    openedClosedWorksheet.Columns["C:C"].ColumnWidth = 10;

    var openClosedChartSourceRange = openedClosedWorksheet.Range[Cell1: "A1", Cell2: "C" + (openClosedByWeek.Count() + 1).ToString(CultureInfo.InvariantCulture)];

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
    areaTriageWorksheet.Cells[1, 1].Value = "Area";
    areaTriageWorksheet.Cells[1, 2].Value = "IssuesForGA";
    areaTriageWorksheet.Cells[1, 3].Value = "Untriaged";
    areaTriageWorksheet.Cells[1, 4].Value = "Untriaged link";
    SetHeaderStyle(areaTriageWorksheet.Cells[1, 1]);
    SetHeaderStyle(areaTriageWorksheet.Cells[1, 2]);
    SetHeaderStyle(areaTriageWorksheet.Cells[1, 3]);
    SetHeaderStyle(areaTriageWorksheet.Cells[1, 4]);

    for (int i = 0; i < issuesByAreaToTriage.Count; i++)
    {
        areaTriageWorksheet.Cells[i + 2, 1].Value = issuesByAreaToTriage[i].Area;
        areaTriageWorksheet.Cells[i + 2, 2].Value = issuesByAreaToTriage[i].IssuesForGA.ToString(CultureInfo.InvariantCulture);
        areaTriageWorksheet.Cells[i + 2, 3].Value = issuesByAreaToTriage[i].IssuesUntriaged.ToString(CultureInfo.InvariantCulture);
        areaTriageWorksheet.Cells[i + 2, 4].Formula = $"=HYPERLINK(\"https://github.com/dotnet/maui/issues?q=is%3Aopen+is%3Aissue+no:milestone+label:t/bug+label%3A%22{issuesByAreaToTriage[i].Area}%22\", \"GitHub query: {issuesByAreaToTriage[i].Area}\")";

        if (issuesByAreaToTriage[i].IssuesUntriaged > 5)
        {
            areaTriageWorksheet.Cells[i + 2, 3].Interior.Color = 0x00_00_ff; // BGR: red
        }
        else if (issuesByAreaToTriage[i].IssuesUntriaged > 0)
        {
            areaTriageWorksheet.Cells[i + 2, 3].Interior.Color = 0x00_ff_ff; // BGR: yellow
        }
    }

    areaTriageWorksheet.Columns["A:A"].ColumnWidth = 30;
    areaTriageWorksheet.Columns["B:B"].ColumnWidth = 15;
    areaTriageWorksheet.Columns["C:C"].ColumnWidth = 15;
    areaTriageWorksheet.Columns["D:D"].ColumnWidth = 35;

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

void SetHeaderStyle(Microsoft.Office.Interop.Excel.Range headerCell)
{
    headerCell.Font.Bold = true;
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
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
}

class GitHubLabel
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning disable CS0649 // Field is never assigned to
    public long id;
    public string node_id;
    public string url;
    public string name;
    public string color;
    public string @default;
    public string description;
#pragma warning restore CS0649 // Field is never assigned to
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
}
