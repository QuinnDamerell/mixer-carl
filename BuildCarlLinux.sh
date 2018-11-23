cd CarlBuild
mkdir bin
cd bin
dotnet restore ../../Carl
dotnet publish -c Release -r centos-x64 -o ../CarlBuild/bin ../../Carl
cp ../../Carl/CommonWords.txt ./
