using DataAccess;
using Newtonsoft.Json;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var dataPath = @"C:\Users\elipton\Downloads\maui-issues-all.csv";
var csvTable = new DataTableBuilder().ReadCsv(dataPath);

var milestoneMapping = new Dictionary<string, string>()
{
    { "7770503", "6.0.300-rc.1" },
    { "7617522", "6.0.300-preview.14" },
    { "6929376", ".NET 7" },
    { "7665569", "Future" },
    { "7526548", "6.0.200-preview.13" },
    { "6954947", "6.0.100-rc.1" },
    { "7286786", "6.0.200-preview.11" },
    { "7194018", "6.0.101-preview.10" },
    { "7280520", "6.0.200-preview.12" },
    { "7016119", "6.0.101-preview.9" },
    { "6904587", "6.0.100-preview.7" },
};

// These milestones aren't used yet
//{ "", "6.0.300" },
//{ "", "6.0.300-servicing" },
//{ "", "6.0.100-preview.6" },

var mauiGAMilestones = milestoneMapping.Values.Where(m => m.StartsWith("6.0", StringComparison.OrdinalIgnoreCase) && !m.Contains("servicing", StringComparison.OrdinalIgnoreCase)).ToList();
var mauiFutureMilestones = new List<string> { ".NET 7", "Future" };

Console.WriteLine($"Loaded {csvTable.NumRows} rows of data");

var columnNamesByIndex = csvTable.ColumnNames.Zip(Enumerable.Range(0, csvTable.ColumnNames.Count()));

var numberColumnIndex = GetColumnIndex(columnNamesByIndex, "Number");
var titleColumnIndex = GetColumnIndex(columnNamesByIndex, "Title");
var createdAtColumnIndex = GetColumnIndex(columnNamesByIndex, "CreatedAt");
var closedAtColumnIndex = GetColumnIndex(columnNamesByIndex, "ClosedAt");
var milestoneIdColumnIndex = GetColumnIndex(columnNamesByIndex, "MilestoneId");
var stateColumnIndex = GetColumnIndex(columnNamesByIndex, "State");
var labelsColumnIndex = GetColumnIndex(columnNamesByIndex, "Labels");

var strongIssueRows = new List<IssueRow>();

foreach (var csvIssueRow in csvTable.Rows)
{
    var labels = JsonConvert.DeserializeObject<GitHubLabel[]>(csvIssueRow.Values[labelsColumnIndex]);

    milestoneMapping.TryGetValue(csvIssueRow.Values[milestoneIdColumnIndex], out var resolvedMilestoneName);

    var strongIssueRow = new IssueRow()
    {
        Number = Int32.Parse(csvIssueRow.Values[numberColumnIndex]),
        Title = csvIssueRow.Values[titleColumnIndex],
        CreatedAt = DateTimeOffset.Parse(csvIssueRow.Values[createdAtColumnIndex]),
        ClosedAt = csvIssueRow.Values[closedAtColumnIndex].Length > 0 ? DateTimeOffset.Parse(csvIssueRow.Values[closedAtColumnIndex]) : null,
        IsOpen = csvIssueRow.Values[stateColumnIndex] == "open",
        PrimaryArea = labels.FirstOrDefault(l => l.name.StartsWith("area/", StringComparison.Ordinal))?.name,
        IsBug = labels.Any(l => l.name == "t/bug"),
        MilestoneName = resolvedMilestoneName,
    };

    strongIssueRows.Add(strongIssueRow);
}


// PART 1: Get open/closed count for each week

var startDate = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
//startDate = strongIssueRows.Min(x => x.CreatedAt).Date;
startDate = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);


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
openClosedTable.SaveCSV(@"C:\Users\elipton\Downloads\maui-issues-all-openclosed-by-week.csv");


// PART 2: Calculate how much work is GA/Future/Untriaged/Unknown

var openIssues = strongIssueRows.Where(i => i.ClosedAt == null).ToList();

var gaIssueCount = openIssues.Count(i => mauiGAMilestones.Contains(i.MilestoneName, StringComparer.OrdinalIgnoreCase));
var futureIssueCount = openIssues.Count(i => mauiFutureMilestones.Contains(i.MilestoneName, StringComparer.OrdinalIgnoreCase));
var untriagedIssueCount = openIssues.Count(i => string.IsNullOrEmpty(i.MilestoneName));
var unknownIssueCount = openIssues.Count - gaIssueCount - futureIssueCount - untriagedIssueCount;

Console.WriteLine($"Total issues: {strongIssueRows.Count}");
Console.WriteLine($"Open issues: {openIssues.Count}");
Console.WriteLine($"GA issues: {gaIssueCount}");
Console.WriteLine($"Future issues: {futureIssueCount}");
Console.WriteLine($"Untriaged issues: {untriagedIssueCount}");
Console.WriteLine($"Unknown issues: {unknownIssueCount}");
Console.WriteLine("Done");


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
