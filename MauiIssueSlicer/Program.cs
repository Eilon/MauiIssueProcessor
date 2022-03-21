using DataAccess;
using Newtonsoft.Json;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

var dataPath = @"C:\Users\elipton\Downloads\export.csv";
var csvTable = new DataTableBuilder().ReadCsv(dataPath);

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


    var strongIssueRow = new IssueRow()
    {
        Number = Int32.Parse(csvIssueRow.Values[numberColumnIndex]),
        Title = csvIssueRow.Values[titleColumnIndex],
        CreatedAt = DateTimeOffset.Parse(csvIssueRow.Values[createdAtColumnIndex]),
        ClosedAt = csvIssueRow.Values[closedAtColumnIndex].Length > 0 ? DateTimeOffset.Parse(csvIssueRow.Values[closedAtColumnIndex]) : null,
        IsOpen = csvIssueRow.Values[stateColumnIndex] == "open",
        PrimaryArea = labels.FirstOrDefault(l => l.name.StartsWith("area/", StringComparison.Ordinal))?.name,
        IsBug = labels.Any(l => l.name == "t/bug"),
        MilestoneName = csvIssueRow.Values[milestoneIdColumnIndex],
    };

    strongIssueRows.Add(strongIssueRow);
}

var startDate = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
var daysSinceStartDate = DateTimeOffset.Now - startDate;

var weeks = (int)Math.Ceiling(daysSinceStartDate.TotalDays / 7d);

Console.WriteLine("Done");


int GetColumnIndex(IEnumerable<(string First, int Second)> columnNamesByIndex, string columnName)
{
    return columnNamesByIndex.Single(c => c.First == columnName).Second;
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
