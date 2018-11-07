#!/bin/bash

# Build the docker image and tag it for docker hub
docker build -t functions .
docker tag functions $DOCKER_HUB_REPO:$BRANCH

# Login to Docker Hub
docker login --username $DOCKER_HUB_USERNAME --password $DOCKER_HUB_PASSWORD

# Push the image to Docker Hub
docker push $DOCKER_HUB_REPO:$BRANCH