FROM microsoft/dotnet:2.1-sdk AS installer-env

COPY . /src/dotnet-function-app
RUN cd /src/dotnet-function-app && \
    mkdir -p /home/site/wwwroot && \
    dotnet publish -c Release functions/azfun.fsproj --output /home/site/wwwroot

FROM microsoft/azure-functions-dotnet-core2.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot
EXPOSE 80

COPY --from=installer-env ["/home/site/wwwroot", "/home/site/wwwroot"]