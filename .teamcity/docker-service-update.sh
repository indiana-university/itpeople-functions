#!/bin/bash

# Export the tag name
export DOCKER_TAG=${TEAMCITY_BRANCH/refs\/heads\//}

# Source the Docker client bundle for the environment associated with this build.
source $HOME/.dcd/$DOCKER_UCP_BUNDLE.sh

# Update the service and non-secret environment variables
echo Updating itpeople-functions service from $DOCKER_HUB_REPO:$DOCKER_TAG
docker service update --image $DOCKER_HUB_REPO:$DOCKER_TAG \
    --health-cmd 'curl --fail http://localhost:80/ping || exit 1' \
    --health-interval 2s \
    --health-retries 30 \
    --health-start-period 10s \
    --health-timeout 5s \
    itpeople-functions
