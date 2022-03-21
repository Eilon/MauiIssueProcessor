// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL.Common;
using CreateMikLabelModel.Models;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace CreateMikLabelModel.DL
{
    class GraphQLDownloadHelper
    {
        public const int MaxRetryCount = 25;
        private const int MaxFileChangesPerPR = 100;
        private const string DeletedUser = "ghost";

        public static async Task<bool> DownloadFastUsingGraphQLAsync(
            Dictionary<(DateTimeOffset, DateTimeOffset?, long, string), string> outputLinesExcludingHeader,
            (string owner, string repo)[] repoCombo,
            StreamWriter outputWriter)
        {
            try
            {
                foreach ((string owner, string repo) repo in repoCombo)
                {
                    using (var client = CommonHelper.CreateGraphQLClient())
                    {
                        Console.WriteLine($"Downloading Issue records from {repo.owner}/{repo.repo}.");
                        if (!await ProcessGitHubIssueData(
                            client, repo.owner, repo.repo, outputLinesExcludingHeader, GetGitHubIssuePage<IssuesNode>))
                        {
                            return false;
                        }
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
                CommonHelper.action(outputLinesExcludingHeader.Values.ToList(), outputWriter);
            }
        }

        public static async Task<bool> ProcessGitHubIssueData<T>(
            GraphQLHttpClient ghGraphQL, string owner, string repo, Dictionary<(DateTimeOffset, DateTimeOffset?, long, string), string> outputLines,
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
                    Console.WriteLine(
                        $"Processing {totalProcessed}/{issuePage.Issues.Repository.Issues.TotalCount}. " +
                        $"Writing {issues.Count} items of interest to output TSV file...");

                    foreach (var issue in issues)
                    {
                        WriteCsvIssue(outputLines, issue, repo);
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

        private static void WriteCsvIssue(Dictionary<(DateTimeOffset, DateTimeOffset?, long, string), string> outputLines, IssuesNode issue
            // TODO: lookup HtmlUrl for transferred files, may be different than repo
            , string repo)
        {
            var area = issue.Labels.Nodes.FirstOrDefault(l => LabelHelper.IsAreaLabel(l.Name))?.Name;
            var createdAt = issue.CreatedAt.UtcDateTime.ToFileTimeUtc();
            var closedAt = issue.ClosedAt?.UtcDateTime.ToFileTimeUtc() ?? 0;
            outputLines.Add(
                (issue.CreatedAt, issue.ClosedAt, issue.Number, repo),
                $"{createdAt},{closedAt},{repo},{issue.Number}\t{issue.Number}\t{area}\t{issue.Title}\t0\t");
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
