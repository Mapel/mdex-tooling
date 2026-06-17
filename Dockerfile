FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Mdex.Gateway -c Release -o /app/gateway \
 && dotnet publish src/Mdex.Token   -c Release -o /app/token

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# git is required by the commit_and_push tool, which shells out to `git` in the vault.
RUN apt-get update \
 && apt-get install -y --no-install-recommends git \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/gateway ./
COPY --from=build /app/token   /opt/mdex-token
ENV PATH="/opt/mdex-token:${PATH}"
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["./mdex-gateway"]
