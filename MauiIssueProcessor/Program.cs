using CreateMikLabelModel.DL;

if (await DownloadHelper.DownloadItemsAsync("dotnet", "maui") == -1)
{
    return -1;
}

return 0;
