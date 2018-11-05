#!/bin/bash

# Source the Docker client bundle for the environment associated with this build.
source $HOME/.dcd/%DockerUcp.Bundle%.sh

# Update all service secrets required by the functions
docker secret rm itpeople-functions-OAuthClientId
printf "%Functions.OAuthClientId%" | docker secret create itpeople-functions-OAuthClientId -
docker secret rm itpeople-functions-OAuthClientSecret
printf "Functions.%OAuthClientSecret%" | docker secret create itpeople-functions-OAuthClientSecret -
docker secret rm itpeople-functions-JwtSecret
printf "%Functions.JwtSecret%" | docker secret create itpeople-functions-JwtSecret -
docker secret rm itpeople-functions-DbConnectionString
printf "%Functions.DbConnectionString%" | docker secret create itpeople-functions-DbConnectionString -

# Update the service and non-secret environment variables
docker service update \
    --env-add OAuthTokenUrl=%Functions.OAuthTokenUrl% \
    --env-add OAuthRedirectUrl=%Functions.OAuthRedirectUrl% \
    --env-add SPA_HOST=%Functions.SPA_Host% \
    --env-add API_HOST=%Functions.API_Host% \
    --image %DockerHub.Repo%:%vcsroot.branch% \
    itpeople-functions 