// See https://aka.ms/new-console-template for more information
using CreateMikLabelModel.DL;

Console.WriteLine("Hello, World!");

if (await DownloadHelper.DownloadItemsAsync("output.tsv", new[] { ("dotnet", "maui") }) == -1)
{
    return -1;
}

return 0;
