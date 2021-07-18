FROM mono:latest
MAINTAINER Colin D. Munro <colin@mice-software.com>

EXPOSE 12345

ADD . /src
RUN nuget restore /src/ThirdParty/onvif-discovery/OnvifDiscovery.sln
RUN nuget restore /src/ThirdParty/bmff/BMFF.sln
RUN nuget restore /src/MiniNVR/TestConsole.sln
RUN msbuild /p:Configuration=Release /src/MiniNVR/TestConsole.sln
CMD ["mono", "/src/MiniNVR/TestConsole/bin/Release/TestConsole.exe"]
 
