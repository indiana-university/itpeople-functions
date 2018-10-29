#!/bin/bash

# kill and remove existing
docker kill funcs
docker rm func

# build and start
docker build -t funcs .
docker run -p 9090:80 -d --name funcs funcs

# open browser
open http://localhost:9090/api/ping