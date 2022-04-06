@echo off
where /q msbuild > nul
IF %ERRORLEVEL% NEQ 0 ( 
   echo ERROR: Couldn't find msbuild. Try running from a Visual Studio Developer Command Prompt
   exit /b 1
)
echo ----------- DOWNLOADING GITHUB ISSUES -----------
pushd MauiIssueProcessor
msbuild /nologo /t:restore,build,run /v:minimal
popd

echo ----------- ANALYZING GITHUB ISSUES -----------
pushd MauiIssueSlicer
msbuild /nologo /t:restore,build,run /v:minimal /p:"RunArguments=..\MauiIssueProcessor\dotnet-maui-issues.csv"
popd
