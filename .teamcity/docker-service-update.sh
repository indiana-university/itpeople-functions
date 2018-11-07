#!/bin/bash

# Source the tag name
source docker-tag-name.sh

# Source the Docker client bundle for the environment associated with this build.
source $HOME/.dcd/$DOCKER_UCP_BUNDLE.sh

# Update the service and non-secret environment variables
docker service update --image $DOCKER_HUB_REPO:$DOCKER_TAG \
    --health-cmd 'curl --fail localhost:80/api/ping || exit 1' \
    --health-interval 1s \
    --health-retries 120 \
    --health-start-period 10s \
    --health-timeout 5s \
    itpeople-functions
