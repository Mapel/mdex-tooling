FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Mdex.Gateway -c Release -o /app/gateway \
 && dotnet publish src/Mdex.Token   -c Release -o /app/token

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/gateway ./
COPY --from=build /app/token   /opt/mdex-token
ENV PATH="/opt/mdex-token:${PATH}"
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["./mdex-gateway"]
