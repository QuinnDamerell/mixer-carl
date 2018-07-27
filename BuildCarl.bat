@echo off
mkdir CarlBuild
cd CarlBuild
mkdir bins
cd bins
dotnet restore ..\Carl\
dotnet publish -c Release -r win10-x86 -o ..\CarlBuild\bins ..\..\Carl\ 
echo _
echo _
echo _
echo _
echo _
echo _
echo Done! Copy the entire Carl folder to where ever you want it an run!
echo _
echo _
echo _
echo _
echo _
pause
