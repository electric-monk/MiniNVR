FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app
EXPOSE 12345

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ThirdParty/bmff
RUN dotnet restore ThirdParty/onvif-discovery
RUN dotnet restore ThirdParty/RtspClientSharp
RUN dotnet build MiniNVR/TestConsole -c Release -o /app/build

FROM base AS final
WORKDIR /app
COPY --from=build /app/build .
LABEL MAINTAINER="Colin D. Munro <colin@mice-software.com>"
ENTRYPOINT ["dotnet", "TestConsole.dll"]
