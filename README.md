# ITPro-Functions

IT Pro Database serverless functions and proxies 

Azure Functions provide a serverless (and cheap!) API alternative to the standard ASP.Net Web API application. 
In serverless architectures, API controllers are eschewed in favor of discrete functions that are triggered by HTTP requests and return an HTTP response. Functions can also be triggered by other events, such as a timer (e.g. cron job) or in response to a message on the Azure Service Bus, and respond with different kinds of outputs, such as a new Service Bus message or an email message.
The functions are ultimately hosted within a web application by the Azure Functions runtime, but a key advange of serverless architectures is that the web app plumbing, scaling, and load-balancing is abstracted away and we don't have to worry about it.

## Prerequisites

1. Install the [.NET Core SDK](https://www.microsoft.com/net/learn/get-started)
2. Install [Node.JS and NPM](https://nodejs.org/en/) 
3. Install [Docker](https://docs.docker.com/install/#supported-platforms)
4. Install the Azure Functions CLI:

On Windows/Linux:
```
npm i -g azure-functions-core-tools@core --unsafe-perm true
```

On Mac:
```
homebrew install azure-functions-core-tools
```

## Authentication and Authorization

Authentication is provided by ESI Middleware's [UAA](https://github.iu.edu/iu-uits-es/uaa) service. UAA provides an OAuth layer for CAS. After singing into CAS, UAA issues an OAuth [JWT](https://jwt.io/) that includes the username and an expiration. You will need to get a Client ID and Client Secret from the UAA team in order to integrate your functions with CAS.

## SSL 

In order to host the Functions app locally you must [create and trust a self-signed SSL certificate](https://www.humankode.com/asp-net-core/develop-locally-with-https-self-signed-certificates-and-asp-net-core). This repo includes a self-signed cert if you just want to use it. The password is `Abcd1234`.  

## Running the code

1. Clone this repo.
2. Add the localhost.pfx to your list of trusted certificates, or create your own.
3. Open the folder in Visual Studio Code.
4. Create a file, `functions/local.settings.json` with these contents:

```
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "",
        "AzureWebJobsDashboard": "",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet",
        "API_HOST": "localhost:7071",
        "SPA_HOST": "localhost:3000",
        "AZURE_FUNCTION_PROXY_BACKEND_URL_DECODE_SLASHES": true,
        "OauthTokenUrl": "https://apps-test.iu.edu/uaa-stg/oauth/token",
        "OauthRedirectUrl": "http://localhost:3000/signin",
        "OauthClientId": "<YOUR UAA CLIENT ID>",
        "OauthClientSecret": "<YOUR UAA CLIENT SECRET>",
        "JwtSecret": "8rjYaJehyxd21bp1JrEsRJ7zstN2eT4jhxWU3UiB",
        "DbConnectionString": "Server=tcp:localhost,1433;User ID=sa;Password=Abcd1234!"
    },
    "Host": {
        "LocalHttpPort": 7071,
        "CORS": "*"
      }
}
```

4. From the command palette exec `build`, then `start`. You should see the terminal light up as the Azure Functions are built and the runtime starts hosting the functions.
5. Verify that the functions are running properly via the `ping` function below. 

## Functions

**Ping** 

A GET endpoint that returns "pong!" if everything is working properly.

*Request*
```
curl https://localhost:7071/api/ping
```

*Response*
```
Pong!
```

## Proxies

The Azure Functions platform recently introduced [Proxies](https://docs.microsoft.com/en-us/azure/azure-functions/functions-proxies) as a way to discretely route and redirect requests to other web/API services.

Proxies play an important role in serving an SPA from a serverless platform. At a minimum, the client will need an `index.html` file and will potentially need other `.js`/`.css` assets. Proxies allow us to transparently route a request for the function webroot (`http://localhost:<port>/`) to some service (such as Azure Storage) that can satisfy a request for the static files.

## Error Handling

This project uses [Railway Oriented Programming](https://fsharpforfunandprofit.com/rop/) to manage execution and handle errors. ROP is a pattern that keeps code clean and ensures all errors are handled and meaningfully reported. The F# implementation of ROP used in this project is [Chessie](https://github.com/fsprojects/Chessie). (It's a train pun.)

## Deploying to Azure

This repo comes with a [Circle CI](https://circleci.com) [configuration](.circleci/config.yml) file that will build, test, package, and deploy the Functions app to Azure via the [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/?view=azure-cli-latest). 

The Azure CLI requires authentication credentials in order to perform the deployment. It's best to use a non-person, [Service Principal](https://docs.microsoft.com/en-us/cli/azure/create-an-azure-service-principal-azure-cli?view=azure-cli-latest) account for this purpose. To create a Service Principal, you will need to get an Azure CLI instance authenticated with the account associated with your Functions App. You can either:  
+ install the Azure CLI locally and sign in with `az login`;  
+ or sign in to [Azure Cloud Shell](https://shell.azure.com/).

Once you have an Azure CLI instance, run:

```
az ad sp create-for-rbac --name USERNAME --password PASSWORD
```

The result should be a block of text similar to:

```
{
  "appId": "38f6...",
  "displayName": "USERNAME",
  "name": "http://USERNAME",
  "password": "PASSWORD",
  "tenant": "1113..."
}
```

Browse to Circle CI. If you don't have a Circle CI account, create one now. It's free for public GitHub repos. Circle CI uses "Contexts" as a store for secrets that need to be available during the build. We'll create a Context now and add the appropriate secrets to it. 

1. Browse to the *Add Projects* section, choose your repo, click *Set Up Project* and then *Start Building*. 
2. In the *Settings* -> *Contexts* section, click *Create Context*.
3. Name the context `azfun-fsharp`. Note: you can name it something different, but you'll need to update the context reference at the bottom of .circleci/config.yml.
4. Add the following environment variables:  
    + `SERVICE_PRINCIPAL_USER`: The Service Principal username url (e.g. http://USERNAME)  
    + `SERVICE_PRINCIPAL_PASSWORD`: The Service Principal password  
    + `SERVICE_PRINCIPAL_TENANT`: The Service Principal tenant  
    + `FUNCTION_APP_test`: The name of your *test* Function App   
    + `FUNCTION_APP_production`: The name of your *production* Function App   
    + `RESOURCE_GROUP`: The name of your Function App Resource Group   

Circle CI should now have the information it needs to build, test, package, and deploy your Function App + SPA.

## Database 

This repo uses SQL Server as a database layer. If you wish to run the functions you'll need to have a running SQL Server database to which the functions can connect. The easiest way to do this is by running SQL Server in a Docker Container. Make sure you have Docker installed and running, then execute:

On Windows:
```
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Abcd1234!" -p 1433:1433 -d microsoft/mssql-server-linux:2017-latest
```

On Mac/Linux:
```
docker run -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=Abcd1234!' -p 1433:1433 -d microsoft/mssql-server-linux:2017-latest
```

This will start the SQL Server. The next step is to apply database migrations. These will get the database schema up to date with the functions.

### Migrations

SQL Server database migrations are managed by [SimpleMigrations](https://github.com/canton7/Simple.Migrations). A command-line tool is provided by the `database` project. To migrate to the latest database schema, execute the following. _Note: You can use the connection string below for local testing._

Windows:
```
cd database
dotnet run "Server=tcp:localhost,1433;User ID=sa;Password=Abcd1234!" up
```

Mac/Linux:
```
cd database
dotnet run 'Server=tcp:localhost,1433;User ID=sa;Password=Abcd1234!' up
```

All command line options:
```
Usage: <executable> Subcommand [-q]

Subcommand can be one of:
   up: Migrate to the latest version
   to <n>: Migrate up/down to the version n
   reapply: Re-apply the current migration
   list: List all available migrations
   baseline <n>: Move the database to version n, without apply migrations

You can issue the command `help` instead of `up` to view the available migration commands.
```

### Interactive SQL Sessions

[SQL Operations Studio](https://docs.microsoft.com/en-us/sql/azure-data-studio/download?view=sql-server-2017) is a cross-platform tool that allows you to query and add data to your database.
