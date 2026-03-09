# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0.3 AS build
WORKDIR /app

# Copy the root architectural files (Crucial for CPM/Directory.Build)
COPY ["Kariyer.Mail.slnx", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]

# Copy the project file and restore
COPY ["src/Kariyer.Mail.Api/Kariyer.Mail.Api.csproj", "src/Kariyer.Mail.Api/"]
RUN dotnet restore "src/Kariyer.Mail.Api/Kariyer.Mail.Api.csproj"

# Copy the rest of the source code
COPY ["src/Kariyer.Mail.Api/", "src/Kariyer.Mail.Api/"]
WORKDIR "/app/src/Kariyer.Mail.Api"

# Build and Publish
# Note: We use -c Release. If you want Native AOT, add -r linux-x64 --self-contained
RUN dotnet publish "Kariyer.Mail.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .

# Use Non-Root user for security (Enterprise Standard)
USER $APP_UID
ENTRYPOINT ["dotnet", "Kariyer.Mail.Api.dll"]