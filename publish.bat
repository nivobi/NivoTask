@echo off
echo Publishing NivoTask -- self-contained win-x86...
dotnet publish src\NivoTask.Api\NivoTask.Api.csproj -p:PublishProfile=win-x86-selfcontained
if %ERRORLEVEL% NEQ 0 (
    echo FAILED. Check errors above.
    exit /b %ERRORLEVEL%
)
echo.
echo Output: publish\win-x86
echo Run:    publish\win-x86\NivoTask.Api.exe
