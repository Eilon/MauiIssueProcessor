# MAUI Issue Processor and Slicer

One-time setup:

1. On GitHub create a Personal Access Token (PAT)
   * Go to https://github.com/settings/tokens
   * Click **Generate new token**
   * Give a descriptive name such as `MAUI issue processor` and set a reasonable expiration time
   * Leave all the settings as default (the tool needs only read-only access to public repos, which is the default)
   * Click **Generate token**
   * Save the token to the clipboard (you will never be able to see it again!)
1. Open a command prompt / shell window and navigate to the `MauiIssueProcessor` folder
   * Run the command `dotnet user-secrets set GitHubAccessToken PASTE_THE_TOKEN_VALUE`

Run the issue downloader to download issue metadata to a CSV file:

1. Open a command prompt / shell window and navigate to the `MauiIssueProcessor` folder
1. Run `dotnet run`
   * It takes roughly 30 seconds to download 2500 issues
1. The location of the output file is seen in the output. For example:
   * `Saved CSV to: C:\...\MauiIssueProcessor\bin\Debug\net6.0\dotnet-maui-issues.csv`

Run the slicer to process the CSV file and generate some statistics and other CSV files

1. Open a command prompt / shell window and navigate to the `MauiIssueSlicer` folder
   * Run `dotnet run c:\path\to\file.csv`
   * This will generate two CSV files (the location will be shown), plus some additional statistics will be shown in the output
