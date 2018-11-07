#!/bin/bash

# Export the tag name
export DOCKER_TAG=${TEAMCITY_BRANCH/\/refs\/heads\//}

# Build the docker image and tag it for docker hub
docker build -t functions .

# Tag image for docker hub
echo Tagging functions image as $DOCKER_HUB_REPO:$DOCKER_TAG
docker tag functions $DOCKER_HUB_REPO:$DOCKER_TAG

# Login to Docker Hub
printf $DOCKER_HUB_PASSWORD | docker login --username $DOCKER_HUB_USERNAME --password-stdin

# Push the image to Docker Hub
echo Pushing $DOCKER_HUB_REPO:$DOCKER_TAG to Docker Hub
docker push $DOCKER_HUB_REPO:$DOCKER_TAG