@ECHO OFF

set datetimef=%date:~-4,4%%date:~-10,2%%date:~-7,2%_%time:~0,2%%time:~3,2%

dotnet publish .\autodraw.csproj -r win-x64 -c Release -p:publishsinglefile=true --self-contained false -p:debugsymbols=false -p:debugtype=none -o Builds/Autodraw-win-x64-%datetimef%

dotnet publish .\autodraw.csproj -r win-x86 -c Release -p:publishsinglefile=true --self-contained false -p:debugsymbols=false -p:debugtype=none -o Builds/Autodraw-win-x86-%datetimef%

dotnet publish .\autodraw.csproj -r win-x64 -c Release -p:publishsinglefile=true --self-contained true -p:debugsymbols=false -p:debugtype=none -o Builds/Autodraw-win-%datetimef%
@echo This is a selfcontained build, which contains additional shit like dotnet runtime pre-packaged, necessary for linux as not many people have it pre-installed, not often necessary for Windows however due to its ubandance. This build is more for stability since previous builds were self-contained. > "Builds/Autodraw-win-%datetimef%/.README.txt"

dotnet publish .\autodraw.csproj -r linux-x64 -c Release -p:publishsinglefile=true --self-contained true -p:debugsymbols=false -p:debugtype=none -o Builds/Autodraw-linux-x64-%datetimef%

dotnet publish .\autodraw.csproj -r osx-x64 -c Release -p:publishsinglefile=true --self-contained true -p:debugsymbols=false -p:debugtype=none -o Builds/Autodraw-macos-x64-%datetimef%