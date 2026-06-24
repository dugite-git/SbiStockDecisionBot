FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY InvestmentDecisionBot.slnx ./
COPY src/InvestmentDecisionBot.Domain/InvestmentDecisionBot.Domain.csproj src/InvestmentDecisionBot.Domain/
COPY src/InvestmentDecisionBot.Application/InvestmentDecisionBot.Application.csproj src/InvestmentDecisionBot.Application/
COPY src/InvestmentDecisionBot.Infrastructure/InvestmentDecisionBot.Infrastructure.csproj src/InvestmentDecisionBot.Infrastructure/
COPY src/InvestmentDecisionBot.Worker/InvestmentDecisionBot.Worker.csproj src/InvestmentDecisionBot.Worker/
RUN dotnet restore

COPY . .
RUN dotnet publish src/InvestmentDecisionBot.Worker/InvestmentDecisionBot.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data
ENV DATABASE_PATH=/app/data/investment-decision-bot.db
ENTRYPOINT ["dotnet", "InvestmentDecisionBot.Worker.dll"]
