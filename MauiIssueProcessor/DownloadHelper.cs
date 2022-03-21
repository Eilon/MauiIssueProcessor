// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace CreateMikLabelModel.DL
{
    public static class DownloadHelper
    {
        public static async Task<int> DownloadItemsAsync(string owner, string repo)
        {
            var stopWatch = Stopwatch.StartNew();

            var completed = await GraphQLDownloadHelper.DownloadFastUsingGraphQLAsync(owner, repo);

            if (!completed)
                return -1;

            stopWatch.Stop();
            Console.WriteLine($"Done writing CSV in {stopWatch.ElapsedMilliseconds}ms");
            return 1;
        }
    }
}
