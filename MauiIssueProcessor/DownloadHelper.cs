// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CreateMikLabelModel.DL.Common;
using System.Diagnostics;

namespace CreateMikLabelModel.DL
{
    public static class DownloadHelper
    {
        public static async Task<int> DownloadItemsAsync(string outputPath, (string owner, string repo)[] repoCombo)
        {
            var stopWatch = Stopwatch.StartNew();

            using (var outputWriter = new StreamWriter(outputPath))
            {
                var outputLinesExcludingHeader = new List<IssueRow>();
                bool completed = false;

                completed = await GraphQLDownloadHelper.DownloadFastUsingGraphQLAsync(outputLinesExcludingHeader, repoCombo, outputWriter);

                if (!completed)
                    return -1;
            }

            stopWatch.Stop();
            Console.WriteLine($"Done writing CSV in {stopWatch.ElapsedMilliseconds}ms");
            return 1;
        }
    }
}
