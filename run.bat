@echo off
pushd MauiIssueProcessor
dotnet run
popd
pushd MauiIssueSlicer
dotnet run ..\MauiIssueProcessor\dotnet-maui-issues.csv
popd
