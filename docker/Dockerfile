FROM mcr.microsoft.com/dotnet/sdk:5.0 as builder
WORKDIR /app
COPY . .
RUN dotnet restore

RUN dotnet publish src/Cli -c  Release -o out

FROM mcr.microsoft.com/dotnet/sdk:5.0
WORKDIR /app
COPY --from=builder /app/out .
ENTRYPOINT ["dotnet", "Cli.dll"]

