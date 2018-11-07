#!/bin/bash

# Resolve the tag as the current branch name
export DOCKER_TAG=${TEAMCITY_BRANCH/\/refs\/heads\//}