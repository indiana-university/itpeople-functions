#!/bin/bash

# Build the docker image and tag it for docker hub
docker build -t functions .
docker tag functions %DockerHub.Repo%:%vcsroot.branch%

# Login to Docker Hub
docker login --username %DockerHub.Username% --password %DockerHub.Password%

# Push the image to Docker Hub
docker push