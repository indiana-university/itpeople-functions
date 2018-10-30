#!/bin/bash

# kill and remove existing
docker kill funcs
docker rm funcs

# build and start
docker build -t funcs .
docker tag funcs uitsssl/itpeople-functions:sandbox
docker run -p 9090:80 -d --name funcs funcs

# open browser
open http://localhost:9090/api/ping