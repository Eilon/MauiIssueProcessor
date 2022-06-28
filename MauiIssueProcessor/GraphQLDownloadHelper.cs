// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL.Common;
using CreateMikLabelModel.Models;
using GraphQL;
using GraphQL.Client.Http;
using System.Globalization;
using System.Net;
using System.Text;

namespace CreateMikLabelModel.DL
{
    class IssueRow
    {
        public long Number { get; set; }
        public string Title { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public string MilestoneName { get; set; }
        public bool IsOpen { get; set; }
        public string PrimaryArea { get; set; }
        public bool IsBug { get; set; }
        public string Labels { get; set; }
    }

    class GraphQLDownloadHelper
    {
        public const int MaxRetryCount = 25;
        private const int MaxFileChangesPerPR = 100;
        private const string DeletedUser = "ghost";

        public static async Task<bool> DownloadFastUsingGraphQLAsync(
            string owner, string repo)
        {
            var outputLinesExcludingHeader = new List<IssueRow>();

            try
            {
                using (var client = CommonHelper.CreateGraphQLClient())
                {
                    Console.WriteLine($"Downloading Issue records from {owner}/{repo}.");
                    if (!await ProcessGitHubIssueData(
                        client, owner, repo, outputLinesExcludingHeader, GetGitHubIssuePage<IssuesNode>))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{nameof(DownloadFastUsingGraphQLAsync)}:{ex.Message}");
                return false;
            }
            finally
            {
                var orderedIssueRows = outputLinesExcludingHeader
                    .OrderBy(x => x.Number);

                var outputFileName = Path.GetFullPath($"{owner}-{repo}-issues.csv");
                if (File.Exists(outputFileName))
                {
                    File.Delete(outputFileName);
                }
                using var outputWriter = new StreamWriter(outputFileName, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // The UTF8 Identifier (BOM) is required so that emojis will be encoded properly

                var issueRowProps = typeof(IssueRow).GetProperties();
                WriteCsvRow(outputWriter, issueRowProps.Select(p => p.Name).ToArray());

                foreach (var issueRow in orderedIssueRows)
                {
                    WriteCsvRow(outputWriter, issueRowProps.Select(p => Convert.ToString(p.GetValue(issueRow), CultureInfo.InvariantCulture)).ToArray());
                }
                Console.WriteLine($"Saved CSV to: {outputFileName}");
            }
        }

        private static void WriteCsvRow(StreamWriter streamWriter, string[] columns)
        {
            streamWriter.WriteLine(
                string.Join(",",
                    columns
                        .Select(c =>
                            "\"" +
                            c
                                .Replace("\r", "")
                                .Replace("\n", "")
                                .Replace("\"", "'") +
                            "\"")
                        .ToArray()));
        }

        public static async Task<bool> ProcessGitHubIssueData<T>(
            GraphQLHttpClient ghGraphQL, string owner, string repo, List<IssueRow> outputLines,
            Func<GraphQLHttpClient, string, string, string, Task<GitHubListPage<T>>> getPage) where T : IssuesNode
        {
            Console.WriteLine($"Getting all issues for {owner}/{repo}...");
            int backToBackFailureCount = 0;
            var hasNextPage = true;
            string afterID = null;
            var totalProcessed = 0;
            do
            {
                try
                {
                    var issuePage = await getPage(ghGraphQL, owner, repo, afterID);

                    if (issuePage.IsError)
                    {
                        Console.WriteLine("Error encountered in GraphQL query. Stopping.");
                        return false;
                    }

                    var issues = issuePage.Issues.Repository.Issues.Nodes.ToList();

                    totalProcessed += issues.Count;
                    Console.WriteLine($"Processing {totalProcessed}/{issuePage.Issues.Repository.Issues.TotalCount}.");

                    foreach (var issue in issues)
                    {
                        WriteCsvIssue(outputLines, issue);
                    }
                    hasNextPage = issuePage.Issues.Repository.Issues.PageInfo.HasNextPage;
                    afterID = issuePage.Issues.Repository.Issues.PageInfo.EndCursor;
                    backToBackFailureCount = 0; // reset for next round
                }
                catch (GraphQLHttpRequestException gqlHttpEx) when (gqlHttpEx?.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine($"Error encountered in GraphQL query due to HTTP status code {HttpStatusCode.Unauthorized}. Check that the provided auth token is still valid and not expired.");
                    return false;
                }
                catch (Exception cx)
                {
                    Console.WriteLine(cx.Message);
                    Console.WriteLine(string.Join(Environment.NewLine, cx.StackTrace));
                    if (backToBackFailureCount < MaxRetryCount)
                    {
                        backToBackFailureCount++;
                        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                    else
                    {
                        Console.WriteLine($"Retried {MaxRetryCount} consecutive times, skip and move on");
                        hasNextPage = false;
                        // TODO later: investigate different reasons for which this might happen
                    }
                }
            }
            while (hasNextPage);

            return true;
        }

        private static void WriteCsvIssue(List<IssueRow> outputLines, IssuesNode issue)
        {
            var area = issue.Labels.Nodes.FirstOrDefault(l => LabelHelper.IsAreaLabel(l.Name))?.Name;

            outputLines.Add(
                new IssueRow
                {
                    Number = issue.Number,
                    Title = issue.Title.Replace('\"', '\''),
                    CreatedAt = issue.CreatedAt,
                    ClosedAt = issue.ClosedAt,
                    IsOpen = issue.ClosedAt == null,
                    PrimaryArea = area,
                    IsBug = issue.Labels.Nodes.Any(l => l.Name == "t/bug"),
                    Labels = string.Join("|", issue.Labels.Nodes.Select(l => l.Name)),
                    MilestoneName = issue.Milestone?.Title,
                });
        }

        public static async Task<GitHubListPage<T>> GetGitHubIssuePage<T>(GraphQLHttpClient ghGraphQL, string owner, string repo, string afterID)
        {
            var issueRequest = new GraphQLRequest(
                query: @"query ($owner: String!, $name: String!, $afterIssue: String) {
  repository(owner: $owner, name: $name) {
    name
    issues(after: $afterIssue, first: 100, orderBy: {field: CREATED_AT, direction: DESC}) {
      nodes {
        number
        title
        createdAt
        closedAt
        labels(first: 10) {
          nodes {
            name
          },
          totalCount
        }
        milestone
        {
          title
        }
     }
      pageInfo {
        hasNextPage
        endCursor
      }
      totalCount
    }
  }
}
",
                variables: new
                {
                    owner = owner,
                    name = repo,
                    afterIssue = afterID,
                });

            var result = await ghGraphQL.SendQueryAsync<Data<T>>(issueRequest);
            if (result.Errors?.Any() ?? false)
            {
                Console.WriteLine($"GraphQL errors! ({result.Errors.Length})");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"\t{error.Message}");
                }
                return new GitHubListPage<T> { IsError = true, };
            }

            var issueList = new GitHubListPage<T>
            {
                Issues = result.Data,
            };

            return issueList;
        }
    }
}
