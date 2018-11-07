#!/bin/bash

# Build the docker image and tag it for docker hub
docker build -t functions .

# Tag image for docker hub
echo Tagging functions image as $DOCKER_HUB_REPO:$TEAMCITY_BRANCH
docker tag functions $DOCKER_HUB_REPO:$TEAMCITY_BRANCH

# Login to Docker Hub
printf $DOCKER_HUB_PASSWORD | docker login --username $DOCKER_HUB_USERNAME --password-stdin

# Push the image to Docker Hub
echo Pushing $DOCKER_HUB_REPO:$TEAMCITY_BRANCH to Docker Hub
docker push $DOCKER_HUB_REPO:$TEAMCITY_BRANCH