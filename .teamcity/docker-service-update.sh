#!/bin/bash

# Source the Docker client bundle for the environment associated with this build.
source $HOME/.dcd/%DockerUcpBundle%.sh

# Update all service secrets required by the functions
docker secret rm itpeople-functions-OAuthClientId
printf "%OAuthClientId" | docker secret create itpeople-functions-OAuthClientId -
docker secret rm itpeople-functions-OAuthClientId
printf "%OAuthClientId" | docker secret create itpeople-functions-OAuthClientId -
docker secret rm itpeople-functions-JwtSecret
printf "%JwtSecret" | docker secret create itpeople-functions-JwtSecret -
docker secret rm itpeople-functions-DbConnectionString
printf "%DbConnectionString" | docker secret create itpeople-functions-DbConnectionString -

# Update the service and non-secret environment variables
docker service update \
    --env-add OAuthTokenUrl=%OAuthTokenUrl% \
    --env-add OAuthRedirectUrl=%OAuthRedirectUrl% \
    --env-add SPA_HOST=%SPA_Host% \
    --env-add API_HOST=%API_Host% \
    --image %DockerHub.Repo%:%vcsroot.branch% \
    itpeople-functions 