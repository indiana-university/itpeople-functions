#!/bin/bash

# Resolve the current branch name
resolved_branch = ${TEAMCITY_BRANCH/\/refs\/heads\//}

# Build the docker image and tag it for docker hub
docker build -t functions .

# Tag image for docker hub
echo Tagging functions image as $DOCKER_HUB_REPO:$resolved_branch
docker tag functions $DOCKER_HUB_REPO:$resolved_branch

# Login to Docker Hub
printf $DOCKER_HUB_PASSWORD | docker login --username $DOCKER_HUB_USERNAME --password-stdin

# Push the image to Docker Hub
echo Pushing $DOCKER_HUB_REPO:$resolved_branch to Docker Hub
docker push $DOCKER_HUB_REPO:$resolved_branch