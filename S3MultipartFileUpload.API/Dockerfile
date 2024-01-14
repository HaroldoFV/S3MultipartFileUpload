FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY ["S3MultipartFileUpload.API/S3MultipartFileUpload.API.csproj", "S3MultipartFileUpload.API/"]
RUN dotnet restore "S3MultipartFileUpload.API/S3MultipartFileUpload.API.csproj"
COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "S3MultipartFileUpload.API.dll"]