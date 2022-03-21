using DataAccess;

if (args.Length != 1)
{
    Console.WriteLine($"Usage: dotnet run MauiIssueSlicer c:\\path\\to\\issues.csv");
    return 1;
}

var inputDataPath = args[0];
var csvTable = new DataTableBuilder().ReadCsv(inputDataPath);

var outputRoot = Path.GetDirectoryName(Path.GetFullPath(inputDataPath));
var inputFilenameBase = Path.GetFileNameWithoutExtension(inputDataPath);

Console.WriteLine($"Loaded {csvTable.NumRows} rows of data");

var columnNamesByIndex = csvTable.ColumnNames.Zip(Enumerable.Range(0, csvTable.ColumnNames.Count()));

var numberColumnIndex = GetColumnIndex(columnNamesByIndex, "Number");
var titleColumnIndex = GetColumnIndex(columnNamesByIndex, "Title");
var createdAtColumnIndex = GetColumnIndex(columnNamesByIndex, "CreatedAt");
var closedAtColumnIndex = GetColumnIndex(columnNamesByIndex, "ClosedAt");
var milestoneNameColumnIndex = GetColumnIndex(columnNamesByIndex, "MilestoneName");
var isOpenColumnIndex = GetColumnIndex(columnNamesByIndex, "IsOpen");
var primaryAreaColumnIndex = GetColumnIndex(columnNamesByIndex, "PrimaryArea");
var isBugColumnIndex = GetColumnIndex(columnNamesByIndex, "IsBug");

var strongIssueRows = new List<IssueRow>();

foreach (var csvIssueRow in csvTable.Rows)
{
    var strongIssueRow = new IssueRow()
    {
        Number = Int32.Parse(csvIssueRow.Values[numberColumnIndex]),
        Title = csvIssueRow.Values[titleColumnIndex],
        CreatedAt = DateTimeOffset.Parse(csvIssueRow.Values[createdAtColumnIndex]),
        ClosedAt = csvIssueRow.Values[closedAtColumnIndex].Length > 0 ? DateTimeOffset.Parse(csvIssueRow.Values[closedAtColumnIndex]) : null,
        MilestoneName = csvIssueRow.Values[milestoneNameColumnIndex],
        IsOpen = bool.Parse(csvIssueRow.Values[isOpenColumnIndex]),
        PrimaryArea = csvIssueRow.Values[primaryAreaColumnIndex],
        IsBug = bool.Parse(csvIssueRow.Values[isBugColumnIndex]),
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

var openClosedTable = new DataTableBuilder().FromEnumerable(openClosedByWeek);
var openClosedByWeekFilePath = Path.Combine(outputRoot, inputFilenameBase, "openclosed-by-week.csv");
openClosedTable.SaveCSV(openClosedByWeekFilePath);
Console.WriteLine($"Saved Open Closed By Week CSV to: {openClosedByWeekFilePath}");


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
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
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

var issuesByAreaToTriageTable = new DataTableBuilder().FromEnumerable(issuesByAreaToTriage);
var areaTriageFilePath = Path.Combine(outputRoot, inputFilenameBase, "area-triage.csv");
issuesByAreaToTriageTable.SaveCSV(areaTriageFilePath);
Console.WriteLine($"Saved Area Triage CSV to: {areaTriageFilePath}");

Console.WriteLine("Done");

return 0;


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
    public string Area { get; set; }
    public int IssuesForGA { get; set; }
    public int IssuesUntriaged { get; set; }
}

class IssueRow
{
    public long Number;
    public string Title;
    public DateTimeOffset CreatedAt;
    public DateTimeOffset? ClosedAt;
    public string MilestoneName;
    public bool IsOpen;
    public string PrimaryArea;
    public bool IsBug;
}

class GitHubLabel
{
    public long id;
    public string node_id;
    public string url;
    public string name;
    public string color;
    public string @default;
    public string description;
}
