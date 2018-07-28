cd CarlBuild
mkdir bin
cd bin
dotnet restore ../../Carl
dotnet publish -c Release -r ubuntu.16.10-x64 -o ../CarlBuild/bin ../../Carl
cp ../../Carl/CommonWords.txt ./
