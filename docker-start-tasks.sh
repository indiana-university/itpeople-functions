#!/bin/bash


# kill and remove existing
docker kill tasks
docker rm tasks

# build and start
docker build -f Dockerfile.Tasks -t tasks .
#docker tag funcs uitsssl/itpeople-functions:sandbox
#docker tag funcs registry-test.docker.iu.edu/repositories/dcd/itpeople-functions:sandbox
# docker run -p 7071:80 -d --name tasks tasks

docker run \
-e "AzureWebJobsStorage=" \
-e "AzureWebJobsDashboard=" \
-e "FUNCTIONS_WORKER_RUNTIME=dotnet" \
-e "WEBSITE_SITE_NAME=tasks" \
-e "WEBSITE_INSTANCE_ID=tasks" \
-p 7071:80 -d \
--name tasks tasks

# open browser
open http://localhost:7071/api/ping