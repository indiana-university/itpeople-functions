#!/bin/bash

# kill and remove existing
docker kill funcs
docker rm funcs

# build and start
docker build -f Dockerfile.API -t funcs .
#docker tag funcs uitsssl/itpeople-functions:sandbox
#docker tag funcs registry-test.docker.iu.edu/repositories/dcd/itpeople-functions:sandbox
docker run -p 7071:80 -d --name funcs funcs

# open browser
open http://localhost:7071/ping